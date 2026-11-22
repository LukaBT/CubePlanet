using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Silk.NET.Core.Native;
using Silk.NET.Shaderc;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Buffer = Silk.NET.Vulkan.Buffer;
using Image = Silk.NET.Vulkan.Image;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace CubePlanet;

/// <summary>
/// Vulkan backend: instance / device / swapchain, frames in flight, a pooled device-local mesh heap with staged uploads,
/// per-frame uniform + dynamic vertex rings, textures, pipelines, GLSL->SPIR-V compilation (shaderc) with an on-disk cache,
/// cascaded shadow maps and the post-processing chain (HDR scene targets ping-ponged between frames, bloom mips, composite).
/// Depth is reverse-Z (D32 float, 1 = near, 0 = far / infinity) for precision on planet-sized scenes.
/// </summary>
public static unsafe partial class Gpu
{
    public static Vk vk;
    static Instance inst; static PhysicalDevice phys; public static Device dev; static Queue queue; static uint qfam;
    static KhrSurface khrSurf; static KhrSwapchain khrSwap; static SurfaceKHR surface; static SwapchainKHR swap;
    static Image[] swImgs = Array.Empty<Image>(); static ImageView[] swViews = Array.Empty<ImageView>(); static Semaphore[] renderDone = Array.Empty<Semaphore>();
    static Format swFmt; public static Extent2D ext, sceneExt; static float renderScale = 1; static bool needRecreate, curVsync = true; public static string DeviceName = "";
    static PhysicalDeviceMemoryProperties memProps; static bool anisoSupported; static float maxAniso = 1;

    // frames in flight
    public const int FIF = 2;
    static CommandPool pool; static readonly CommandBuffer[] cbs = new CommandBuffer[FIF]; static readonly Fence[] fences = new Fence[FIF]; static readonly Semaphore[] imgAvail = new Semaphore[FIF];
    public static int fi; static uint imgIdx; public static CommandBuffer cb; static bool frameOn, inPass, uploadsPending;

    // passes / targets
    public static SampleCountFlags samples = SampleCountFlags.Count1Bit;
    const Format DEPTH = Format.D32Sfloat, HDR = Format.R16G16B16A16Sfloat, DIST = Format.R16Sfloat;
    static RenderPass sceneRP, shadowRP, postRP, postAddRP, finalRP;
    static GTex msCol, msDist, depthTex; static readonly GTex[] sceneCol = new GTex[FIF], sceneDist = new GTex[FIF]; static readonly Framebuffer[] sceneFB = new Framebuffer[FIF];
    public const int BLM = 6; static GTex bloom; static ImageView[] bloomViews = Array.Empty<ImageView>(); static Framebuffer[] bloomFB = Array.Empty<Framebuffer>(); static Extent2D[] bloomExt = Array.Empty<Extent2D>();
    static Framebuffer[] finalFB = Array.Empty<Framebuffer>();
    // volumetric clouds: half resolution colour (+alpha) and entry distance, composited over the scene in the final pass
    static RenderPass cloudRP; static GTex cloudCol, cloudDist; static Framebuffer cloudFB; static Extent2D cloudExt;
    public const int SMS = 2048, CASC = 3; public static GTex shadow; static readonly ImageView[] shadowLayer = new ImageView[CASC]; static readonly Framebuffer[] shadowFB = new Framebuffer[CASC];
    static Sampler linClamp, blockPlain, blockAniso; static bool anisoOn;

    // descriptors / layout
    static DescriptorSetLayout dsl, dsl1; static PipelineLayout playout; static DescriptorPool dpool;
    static readonly DescriptorSet[] dsets = new DescriptorSet[FIF];
    const int PSETS = 13; static readonly DescriptorSet[,] psets = new DescriptorSet[FIF, PSETS];
    static PipelineCache pcache; static string CacheDir;

    // per-frame host-visible rings (staging for uploads + dynamic vertices) and uniform buffers
    sealed class Ring { public Buffer buf; public DeviceMemory mem; public byte* ptr; public ulong size, off; }
    static readonly List<Ring>[] rings = { new(), new() }; static readonly int[] ringCur = new int[FIF];
    static readonly Buffer[] ubo = new Buffer[FIF]; static readonly DeviceMemory[] uboMem = new DeviceMemory[FIF]; static readonly float*[] uboPtr = new float*[FIF];
    public const int UBO_FLOATS = 156;
    static readonly List<GAlloc>[] pendFree = { new(), new() };

    // mesh heap
    const ulong BLK = 64UL << 20; static readonly List<GBlock> blocks = new();
    public static long HeapUsed;

    // textures bound at fixed slots: 1 block array, 2 shadow cascades, 3 noise, 4 font, 5/6 previous frame colour / distance
    public static GTex texBlocks, texNoise, texFont, texCloud, texItems;
    // post-processing pipelines (created by Renderer.InitGpu)
    public static Pipe pBloomPre, pBloomDown, pBloomUp, pComposite, pCloud;
    public static string Cache => CacheDir;

    // ======================================================================= init
    public static bool Integrated;
    /// <summary>pickMsaa is called once the GPU is known (argument: integrated graphics?) and returns the MSAA sample count to use.</summary>
    public static void Init(Window* win, Func<bool, int> pickMsaa, bool vsync)
    {
        vk = Vk.GetApi(); curVsync = vsync;
        CacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CubePlanet", "vkcache");
        try { Directory.CreateDirectory(CacheDir); } catch { }

        // ---- instance
        var appName = (byte*)SilkMarshal.StringToPtr("Cube Planet");
        var ai = new ApplicationInfo { SType = StructureType.ApplicationInfo, PApplicationName = appName, ApplicationVersion = 1, PEngineName = appName, EngineVersion = 1, ApiVersion = Vk.Version11 };
        string[] exts = GLFW.GetRequiredInstanceExtensions();
        var layers = new List<string>();
        if (Environment.GetEnvironmentVariable("CUBEPLANET_VKDEBUG") == "1" && HasLayer("VK_LAYER_KHRONOS_validation")) layers.Add("VK_LAYER_KHRONOS_validation");
        var extPtr = (byte**)SilkMarshal.StringArrayToPtr(exts); var layPtr = (byte**)SilkMarshal.StringArrayToPtr(layers.ToArray());
        var ici = new InstanceCreateInfo { SType = StructureType.InstanceCreateInfo, PApplicationInfo = &ai, EnabledExtensionCount = (uint)exts.Length, PpEnabledExtensionNames = extPtr, EnabledLayerCount = (uint)layers.Count, PpEnabledLayerNames = layPtr };
        Check(vk.CreateInstance(&ici, null, out inst), "vkCreateInstance");
        if (!vk.TryGetInstanceExtension(inst, out khrSurf)) throw new Exception("VK_KHR_surface missing");

        // ---- surface (GLFW)
        Check((Result)GLFW.CreateWindowSurface(new OpenTK.Windowing.GraphicsLibraryFramework.VkHandle(inst.Handle), win, null, out OpenTK.Windowing.GraphicsLibraryFramework.VkHandle sh), "glfwCreateWindowSurface");
        surface = new SurfaceKHR((ulong)sh.Handle);

        // ---- physical device: prefer discrete GPUs that can present to the window
        uint n = 0; vk.EnumeratePhysicalDevices(inst, &n, null); var pds = new PhysicalDevice[n]; fixed (PhysicalDevice* p = pds) vk.EnumeratePhysicalDevices(inst, &n, p);
        int best = -1;
        foreach (var pd in pds)
        {
            uint qn = 0; vk.GetPhysicalDeviceQueueFamilyProperties(pd, &qn, null); var qp = new QueueFamilyProperties[qn]; fixed (QueueFamilyProperties* p = qp) vk.GetPhysicalDeviceQueueFamilyProperties(pd, &qn, p);
            for (uint i = 0; i < qn; i++)
            {
                if ((qp[i].QueueFlags & QueueFlags.GraphicsBit) == 0) continue;
                khrSurf.GetPhysicalDeviceSurfaceSupport(pd, i, surface, out var sup); if (!sup) continue;
                vk.GetPhysicalDeviceProperties(pd, out var pr);
                int score = pr.DeviceType == PhysicalDeviceType.DiscreteGpu ? 3 : pr.DeviceType == PhysicalDeviceType.IntegratedGpu ? 2 : 1;
                if (score > best) { best = score; phys = pd; qfam = i; DeviceName = SilkMarshal.PtrToString((nint)pr.DeviceName); Integrated = pr.DeviceType == PhysicalDeviceType.IntegratedGpu; }
                break;
            }
        }
        if (best < 0) throw new Exception("No Vulkan GPU that can present to this window was found.");
        vk.GetPhysicalDeviceMemoryProperties(phys, out memProps);
        vk.GetPhysicalDeviceProperties(phys, out var props); vk.GetPhysicalDeviceFeatures(phys, out var avail);
        anisoSupported = avail.SamplerAnisotropy; maxAniso = Math.Min(16f, props.Limits.MaxSamplerAnisotropy);
        var lim = props.Limits.FramebufferColorSampleCounts & props.Limits.FramebufferDepthSampleCounts;
        samples = SampleCountFlags.Count1Bit; int msaa = pickMsaa(Integrated);
        foreach (var (want, f) in new[] { (8, SampleCountFlags.Count8Bit), (4, SampleCountFlags.Count4Bit), (2, SampleCountFlags.Count2Bit) })
            if (msaa >= want && (lim & f) != 0) { samples = f; break; }

        // ---- device
        float prio = 1; var qci = new DeviceQueueCreateInfo { SType = StructureType.DeviceQueueCreateInfo, QueueFamilyIndex = qfam, QueueCount = 1, PQueuePriorities = &prio };
        var devExt = (byte**)SilkMarshal.StringArrayToPtr(new[] { KhrSwapchain.ExtensionName });
        var feats = new PhysicalDeviceFeatures { SamplerAnisotropy = anisoSupported };
        var dci = new DeviceCreateInfo { SType = StructureType.DeviceCreateInfo, QueueCreateInfoCount = 1, PQueueCreateInfos = &qci, EnabledExtensionCount = 1, PpEnabledExtensionNames = devExt, PEnabledFeatures = &feats };
        Check(vk.CreateDevice(phys, &dci, null, out dev), "vkCreateDevice");
        vk.GetDeviceQueue(dev, qfam, 0, out queue);
        if (!vk.TryGetDeviceExtension(inst, dev, out khrSwap)) throw new Exception("VK_KHR_swapchain missing");

        // ---- command pool / sync
        var cpi = new CommandPoolCreateInfo { SType = StructureType.CommandPoolCreateInfo, QueueFamilyIndex = qfam, Flags = CommandPoolCreateFlags.ResetCommandBufferBit };
        Check(vk.CreateCommandPool(dev, &cpi, null, out pool), "pool");
        var cai = new CommandBufferAllocateInfo { SType = StructureType.CommandBufferAllocateInfo, CommandPool = pool, Level = CommandBufferLevel.Primary, CommandBufferCount = FIF };
        fixed (CommandBuffer* p = cbs) Check(vk.AllocateCommandBuffers(dev, &cai, p), "cmdbufs");
        for (int i = 0; i < FIF; i++)
        {
            var fci = new FenceCreateInfo { SType = StructureType.FenceCreateInfo, Flags = FenceCreateFlags.SignaledBit }; vk.CreateFence(dev, &fci, null, out fences[i]);
            var sci = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo }; vk.CreateSemaphore(dev, &sci, null, out imgAvail[i]);
        }

        // ---- pipeline cache (saved on exit, makes later startups faster)
        byte[] cdata = null; try { string cp = Path.Combine(CacheDir, "pipeline.bin"); if (File.Exists(cp)) cdata = File.ReadAllBytes(cp); } catch { }
        fixed (byte* cd = cdata)
        {
            var pci = new PipelineCacheCreateInfo { SType = StructureType.PipelineCacheCreateInfo, InitialDataSize = (nuint)(cdata?.Length ?? 0), PInitialData = cd };
            if (vk.CreatePipelineCache(dev, &pci, null, out pcache) != Result.Success) { pci.InitialDataSize = 0; pci.PInitialData = null; vk.CreatePipelineCache(dev, &pci, null, out pcache); }
        }

        linClamp = MkSampler(Filter.Linear, Filter.Linear, SamplerMipmapMode.Nearest, SamplerAddressMode.ClampToEdge, 0);
        blockPlain = MkSampler(Filter.Nearest, Filter.Nearest, SamplerMipmapMode.Linear, SamplerAddressMode.Repeat, 4);
        blockAniso = anisoSupported ? MkSampler(Filter.Nearest, Filter.Linear, SamplerMipmapMode.Linear, SamplerAddressMode.Repeat, 4, false, maxAniso) : blockPlain;
        swFmt = PickFormat();
        CreateRenderPasses();
        CreateDescriptorLayout();
        for (int i = 0; i < FIF; i++)
        {
            ubo[i] = MkBuf(UBO_FLOATS * 4, BufferUsageFlags.UniformBufferBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, out uboMem[i]);
            void* up; vk.MapMemory(dev, uboMem[i], 0, UBO_FLOATS * 4, 0, &up); uboPtr[i] = (float*)up; new Span<float>(up, UBO_FLOATS).Clear();
            NewRing(i, 16UL << 20);
        }
        // shadow cascades: one depth array layer each, sampled with hardware comparison (free 2x2 PCF per tap)
        shadow = MkImage(ImageType.Type2D, ImageViewType.Type2DArray, DEPTH, SMS, SMS, 1, CASC, 1, ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit, ImageAspectFlags.DepthBit);
        shadow.smp = MkSampler(Filter.Linear, Filter.Linear, SamplerMipmapMode.Nearest, SamplerAddressMode.ClampToEdge, 0, true);
        for (int l = 0; l < CASC; l++)
        {
            var vci = new ImageViewCreateInfo { SType = StructureType.ImageViewCreateInfo, Image = shadow.img, ViewType = ImageViewType.Type2D, Format = DEPTH, SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.DepthBit, 0, 1, (uint)l, 1) };
            Check(vk.CreateImageView(dev, &vci, null, out shadowLayer[l]), "shadow layer view");
            var att = shadowLayer[l]; var fbi = new FramebufferCreateInfo { SType = StructureType.FramebufferCreateInfo, RenderPass = shadowRP, AttachmentCount = 1, PAttachments = &att, Width = SMS, Height = SMS, Layers = 1 };
            Check(vk.CreateFramebuffer(dev, &fbi, null, out shadowFB[l]), "shadow fb");
        }
        OneShot(c => Transition(c, shadow.img, ImageAspectFlags.DepthBit, ImageLayout.Undefined, ImageLayout.ShaderReadOnlyOptimal, 1, CASC));
        CreateSwapchain();
    }

    static bool HasLayer(string name)
    {
        uint n = 0; vk.EnumerateInstanceLayerProperties(&n, null); var l = new LayerProperties[n];
        fixed (LayerProperties* p = l) vk.EnumerateInstanceLayerProperties(&n, p);
        foreach (var x in l) if (SilkMarshal.PtrToString((nint)x.LayerName) == name) return true;
        return false;
    }
    static void Check(Result r, string what) { if (r != Result.Success) throw new Exception($"{what} failed: {r}"); }


    public static void Shutdown()
    {
        if (dev.Handle == 0) return;
        vk.DeviceWaitIdle(dev);
        try
        {
            nuint sz = 0; vk.GetPipelineCacheData(dev, pcache, &sz, null);
            if (sz > 0) { var d = new byte[(int)sz]; fixed (byte* p = d) vk.GetPipelineCacheData(dev, pcache, &sz, p); File.WriteAllBytes(Path.Combine(CacheDir, "pipeline.bin"), d); }
        }
        catch { }
        DestroySwapchainResources();
        khrSwap.DestroySwapchain(dev, swap, null); khrSurf.DestroySurface(inst, surface, null);
    }
}
