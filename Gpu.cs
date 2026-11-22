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

/// <summary>Sub-allocation inside one big device-local buffer (vertex + index data of one mesh).</summary>
public sealed class GAlloc { public GBlock blk; public ulong off, size; public bool freed; }
public sealed class GBlock { public Buffer buf; public DeviceMemory mem; public ulong size; public readonly List<(ulong o, ulong s)> free = new(); }
public sealed class GTex { public Image img; public DeviceMemory mem; public ImageView view; public Sampler smp; }
public sealed class Pipe { public Pipeline p; public string name; }

public enum VL { None, Chunk, ChunkShadow, Lod, LodSea, LodShadow, Pos3, UI }
public enum BlendMode { None, Alpha, Add }
/// <summary>Scene = HDR offscreen pass (2 colour targets: colour + view distance), Shadow = depth only, Post = bloom mip chain, Final = swapchain (composite + UI).</summary>
public enum RP { Scene, Shadow, Post, PostAdd, Final, Cloud }
public sealed class PipeCfg
{
    public VL layout = VL.Chunk; public PrimitiveTopology topo = PrimitiveTopology.TriangleList; public CullModeFlags cull = CullModeFlags.None;
    public bool depthTest = true, depthWrite = true, depthBias, writeDist; public CompareOp cmp = CompareOp.Greater; public BlendMode blend = BlendMode.None;
    public RP pass = RP.Scene;
}

/// <summary>
/// Vulkan backend: instance / device / swapchain, frames in flight, a pooled device-local mesh heap with staged uploads,
/// per-frame uniform + dynamic vertex rings, textures, pipelines, GLSL->SPIR-V compilation (shaderc) with an on-disk cache,
/// cascaded shadow maps and the post-processing chain (HDR scene targets ping-ponged between frames, bloom mips, composite).
/// Depth is reverse-Z (D32 float, 1 = near, 0 = far / infinity) for precision on planet-sized scenes.
/// </summary>
public static unsafe class Gpu
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
    public static GTex texBlocks, texNoise, texFont, texCloud;
    // post-processing pipelines (created by W.InitGpu)
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

    // ======================================================================= render passes
    static SubpassDependency* Deps()
    {
        // one generous pair for every pass: previous writes/reads of the attachments finish before this pass, and this pass's writes are visible to later shader reads
        var d = (SubpassDependency*)NativeMemory.Alloc((nuint)(sizeof(SubpassDependency) * 2));
        var all = PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.EarlyFragmentTestsBit | PipelineStageFlags.LateFragmentTestsBit | PipelineStageFlags.FragmentShaderBit;
        var acc = AccessFlags.ColorAttachmentWriteBit | AccessFlags.ColorAttachmentReadBit | AccessFlags.DepthStencilAttachmentWriteBit | AccessFlags.DepthStencilAttachmentReadBit | AccessFlags.ShaderReadBit;
        d[0] = new SubpassDependency { SrcSubpass = Vk.SubpassExternal, DstSubpass = 0, SrcStageMask = all, DstStageMask = all, SrcAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit, DstAccessMask = acc };
        d[1] = new SubpassDependency { SrcSubpass = 0, DstSubpass = Vk.SubpassExternal, SrcStageMask = all, DstStageMask = all, SrcAccessMask = AccessFlags.ColorAttachmentWriteBit | AccessFlags.DepthStencilAttachmentWriteBit, DstAccessMask = acc };
        return d;
    }
    static AttachmentDescription Att(Format f, SampleCountFlags s, AttachmentLoadOp load, AttachmentStoreOp store, ImageLayout init, ImageLayout fin) =>
        new() { Format = f, Samples = s, LoadOp = load, StoreOp = store, StencilLoadOp = AttachmentLoadOp.DontCare, StencilStoreOp = AttachmentStoreOp.DontCare, InitialLayout = init, FinalLayout = fin };
    static RenderPass MkRP(AttachmentDescription[] a, AttachmentReference[] col, AttachmentReference* depth, AttachmentReference[] res)
    {
        var deps = Deps();
        fixed (AttachmentDescription* pa = a) fixed (AttachmentReference* pc = col) fixed (AttachmentReference* pr = res)
        {
            var sp = new SubpassDescription { PipelineBindPoint = PipelineBindPoint.Graphics, ColorAttachmentCount = (uint)(col?.Length ?? 0), PColorAttachments = pc, PDepthStencilAttachment = depth, PResolveAttachments = res != null ? pr : null };
            var rpi = new RenderPassCreateInfo { SType = StructureType.RenderPassCreateInfo, AttachmentCount = (uint)a.Length, PAttachments = pa, SubpassCount = 1, PSubpasses = &sp, DependencyCount = 2, PDependencies = deps };
            Check(vk.CreateRenderPass(dev, &rpi, null, out var rp), "render pass"); NativeMemory.Free(deps); return rp;
        }
    }
    static void CreateRenderPasses()
    {
        bool ms = samples != SampleCountFlags.Count1Bit; var C = ImageLayout.ColorAttachmentOptimal; var R = ImageLayout.ShaderReadOnlyOptimal; var U = ImageLayout.Undefined;
        // scene: HDR colour + view distance (+ MSAA resolve), depth
        var dr = new AttachmentReference(2, ImageLayout.DepthStencilAttachmentOptimal);
        if (ms)
            sceneRP = MkRP(new[] {
                Att(HDR, samples, AttachmentLoadOp.Clear, AttachmentStoreOp.DontCare, U, C), Att(DIST, samples, AttachmentLoadOp.Clear, AttachmentStoreOp.DontCare, U, C),
                Att(DEPTH, samples, AttachmentLoadOp.Clear, AttachmentStoreOp.DontCare, U, ImageLayout.DepthStencilAttachmentOptimal),
                Att(HDR, SampleCountFlags.Count1Bit, AttachmentLoadOp.DontCare, AttachmentStoreOp.Store, U, R), Att(DIST, SampleCountFlags.Count1Bit, AttachmentLoadOp.DontCare, AttachmentStoreOp.Store, U, R) },
                new[] { new AttachmentReference(0, C), new AttachmentReference(1, C) }, &dr, new[] { new AttachmentReference(3, C), new AttachmentReference(4, C) });
        else
            sceneRP = MkRP(new[] {
                Att(HDR, samples, AttachmentLoadOp.Clear, AttachmentStoreOp.Store, U, R), Att(DIST, samples, AttachmentLoadOp.Clear, AttachmentStoreOp.Store, U, R),
                Att(DEPTH, samples, AttachmentLoadOp.Clear, AttachmentStoreOp.DontCare, U, ImageLayout.DepthStencilAttachmentOptimal) },
                new[] { new AttachmentReference(0, C), new AttachmentReference(1, C) }, &dr, null);
        var sr = new AttachmentReference(0, ImageLayout.DepthStencilAttachmentOptimal);
        shadowRP = MkRP(new[] { Att(DEPTH, SampleCountFlags.Count1Bit, AttachmentLoadOp.Clear, AttachmentStoreOp.Store, U, R) }, null, &sr, null);
        postRP = MkRP(new[] { Att(HDR, SampleCountFlags.Count1Bit, AttachmentLoadOp.DontCare, AttachmentStoreOp.Store, U, R) }, new[] { new AttachmentReference(0, C) }, null, null);
        postAddRP = MkRP(new[] { Att(HDR, SampleCountFlags.Count1Bit, AttachmentLoadOp.Load, AttachmentStoreOp.Store, R, R) }, new[] { new AttachmentReference(0, C) }, null, null);
        cloudRP = MkRP(new[] { Att(HDR, SampleCountFlags.Count1Bit, AttachmentLoadOp.DontCare, AttachmentStoreOp.Store, U, R), Att(DIST, SampleCountFlags.Count1Bit, AttachmentLoadOp.DontCare, AttachmentStoreOp.Store, U, R) },
            new[] { new AttachmentReference(0, C), new AttachmentReference(1, C) }, null, null);
        finalRP = MkRP(new[] { Att(swFmt, SampleCountFlags.Count1Bit, AttachmentLoadOp.Clear, AttachmentStoreOp.Store, U, ImageLayout.PresentSrcKhr) }, new[] { new AttachmentReference(0, C) }, null, null);
    }
    static Format PickFormat()
    {
        uint n = 0; khrSurf.GetPhysicalDeviceSurfaceFormats(phys, surface, &n, null); var f = new SurfaceFormatKHR[n];
        fixed (SurfaceFormatKHR* p = f) khrSurf.GetPhysicalDeviceSurfaceFormats(phys, surface, &n, p);
        // the shaders tone-map and gamma-encode themselves (like the GL default framebuffer), so use a UNORM swapchain
        foreach (var x in f) if (x.Format == Format.B8G8R8A8Unorm) return x.Format;
        foreach (var x in f) if (x.Format == Format.R8G8B8A8Unorm) return x.Format;
        return f[0].Format;
    }

    // ======================================================================= swapchain + size-dependent targets
    static void CreateSwapchain()
    {
        khrSurf.GetPhysicalDeviceSurfaceCapabilities(phys, surface, out var caps);
        if (caps.CurrentExtent.Width == 0 || caps.CurrentExtent.Height == 0) { ext = new Extent2D(0, 0); return; }
        ext = caps.CurrentExtent.Width != uint.MaxValue ? caps.CurrentExtent : new Extent2D((uint)Math.Max(1, W.VW), (uint)Math.Max(1, W.VH));
        uint n = 0; khrSurf.GetPhysicalDeviceSurfacePresentModes(phys, surface, &n, null); var pm = new PresentModeKHR[n];
        fixed (PresentModeKHR* p = pm) khrSurf.GetPhysicalDeviceSurfacePresentModes(phys, surface, &n, p);
        var mode = PresentModeKHR.FifoKhr;
        if (!curVsync) { if (pm.Contains(PresentModeKHR.MailboxKhr)) mode = PresentModeKHR.MailboxKhr; else if (pm.Contains(PresentModeKHR.ImmediateKhr)) mode = PresentModeKHR.ImmediateKhr; }
        uint count = Math.Max(caps.MinImageCount + 1, 3); if (caps.MaxImageCount > 0) count = Math.Min(count, caps.MaxImageCount);
        var old = swap;
        var sci = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr, Surface = surface, MinImageCount = count, ImageFormat = swFmt, ImageColorSpace = ColorSpaceKHR.SpaceSrgbNonlinearKhr,
            ImageExtent = ext, ImageArrayLayers = 1, ImageUsage = ImageUsageFlags.ColorAttachmentBit | (caps.SupportedUsageFlags & ImageUsageFlags.TransferSrcBit), ImageSharingMode = SharingMode.Exclusive,
            PreTransform = caps.CurrentTransform, CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr, PresentMode = mode, Clipped = true, OldSwapchain = old,
        };
        Check(khrSwap.CreateSwapchain(dev, &sci, null, out swap), "swapchain");
        if (old.Handle != 0) khrSwap.DestroySwapchain(dev, old, null);
        uint ic = 0; khrSwap.GetSwapchainImages(dev, swap, &ic, null); swImgs = new Image[ic]; fixed (Image* p = swImgs) khrSwap.GetSwapchainImages(dev, swap, &ic, p);
        swViews = new ImageView[ic]; finalFB = new Framebuffer[ic]; renderDone = new Semaphore[ic];
        for (int i = 0; i < ic; i++)
        {
            swViews[i] = MkView(swImgs[i], ImageViewType.Type2D, swFmt, ImageAspectFlags.ColorBit, 1, 1);
            var at = swViews[i]; var fbi = new FramebufferCreateInfo { SType = StructureType.FramebufferCreateInfo, RenderPass = finalRP, AttachmentCount = 1, PAttachments = &at, Width = ext.Width, Height = ext.Height, Layers = 1 };
            Check(vk.CreateFramebuffer(dev, &fbi, null, out finalFB[i]), "final framebuffer");
            var sci2 = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo }; vk.CreateSemaphore(dev, &sci2, null, out renderDone[i]);
        }
        // scene targets (colour + distance per frame in flight; the other frame's pair is the "previous frame" for reflections), msaa + depth
        bool ms = samples != SampleCountFlags.Count1Bit;
        uint w = Math.Max(16, (uint)Math.Round(ext.Width * renderScale)), h = Math.Max(16, (uint)Math.Round(ext.Height * renderScale));
        sceneExt = new Extent2D(w, h);
        var tgtUse = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit;
        for (int f = 0; f < FIF; f++)
        {
            sceneCol[f] = MkImage(ImageType.Type2D, ImageViewType.Type2D, HDR, w, h, 1, 1, 1, tgtUse, ImageAspectFlags.ColorBit);
            sceneDist[f] = MkImage(ImageType.Type2D, ImageViewType.Type2D, DIST, w, h, 1, 1, 1, tgtUse, ImageAspectFlags.ColorBit);
        }
        if (ms)
        {
            msCol = MkImage(ImageType.Type2D, ImageViewType.Type2D, HDR, w, h, 1, 1, 1, ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransientAttachmentBit, ImageAspectFlags.ColorBit, samples);
            msDist = MkImage(ImageType.Type2D, ImageViewType.Type2D, DIST, w, h, 1, 1, 1, ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransientAttachmentBit, ImageAspectFlags.ColorBit, samples);
        }
        depthTex = MkImage(ImageType.Type2D, ImageViewType.Type2D, DEPTH, w, h, 1, 1, 1, ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.TransientAttachmentBit, ImageAspectFlags.DepthBit, samples);
        for (int f = 0; f < FIF; f++)
        {
            var at = stackalloc ImageView[5]; uint na;
            if (ms) { at[0] = msCol.view; at[1] = msDist.view; at[2] = depthTex.view; at[3] = sceneCol[f].view; at[4] = sceneDist[f].view; na = 5; }
            else { at[0] = sceneCol[f].view; at[1] = sceneDist[f].view; at[2] = depthTex.view; na = 3; }
            var fbi = new FramebufferCreateInfo { SType = StructureType.FramebufferCreateInfo, RenderPass = sceneRP, AttachmentCount = na, PAttachments = at, Width = w, Height = h, Layers = 1 };
            Check(vk.CreateFramebuffer(dev, &fbi, null, out sceneFB[f]), "scene framebuffer");
        }
        // bloom chain: half resolution, BLM mips
        uint bw = Math.Max(1, w / 2), bh = Math.Max(1, h / 2);
        bloom = MkImage(ImageType.Type2D, ImageViewType.Type2D, HDR, bw, bh, 1, 1, BLM, ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit, ImageAspectFlags.ColorBit);
        bloomViews = new ImageView[BLM]; bloomFB = new Framebuffer[BLM]; bloomExt = new Extent2D[BLM];
        for (int m = 0; m < BLM; m++)
        {
            bloomExt[m] = new Extent2D(Math.Max(1, bw >> m), Math.Max(1, bh >> m));
            var vci = new ImageViewCreateInfo { SType = StructureType.ImageViewCreateInfo, Image = bloom.img, ViewType = ImageViewType.Type2D, Format = HDR, SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, (uint)m, 1, 0, 1) };
            Check(vk.CreateImageView(dev, &vci, null, out bloomViews[m]), "bloom view");
            var at = bloomViews[m]; var fbi = new FramebufferCreateInfo { SType = StructureType.FramebufferCreateInfo, RenderPass = postRP, AttachmentCount = 1, PAttachments = &at, Width = bloomExt[m].Width, Height = bloomExt[m].Height, Layers = 1 };
            Check(vk.CreateFramebuffer(dev, &fbi, null, out bloomFB[m]), "bloom framebuffer");
        }
        cloudExt = new Extent2D(bw, bh);
        cloudCol = MkImage(ImageType.Type2D, ImageViewType.Type2D, HDR, bw, bh, 1, 1, 1, tgtUse, ImageAspectFlags.ColorBit);
        cloudDist = MkImage(ImageType.Type2D, ImageViewType.Type2D, DIST, bw, bh, 1, 1, 1, tgtUse, ImageAspectFlags.ColorBit);
        {
            var at = stackalloc ImageView[2]; at[0] = cloudCol.view; at[1] = cloudDist.view;
            var fbi = new FramebufferCreateInfo { SType = StructureType.FramebufferCreateInfo, RenderPass = cloudRP, AttachmentCount = 2, PAttachments = at, Width = bw, Height = bh, Layers = 1 };
            Check(vk.CreateFramebuffer(dev, &fbi, null, out cloudFB), "cloud framebuffer");
        }
        // start every sampled target cleared and in shader-read layout (the first frame reads the "previous" frame)
        OneShot(c =>
        {
            void Clr(Image img, uint mips, float v)
            {
                var r = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, mips, 0, 1);
                Transition(c, img, ImageAspectFlags.ColorBit, ImageLayout.Undefined, ImageLayout.TransferDstOptimal, mips, 1);
                var cc = new ClearColorValue(v, v, v, 0); vk.CmdClearColorImage(c, img, ImageLayout.TransferDstOptimal, &cc, 1, &r);
                Transition(c, img, ImageAspectFlags.ColorBit, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal, mips, 1);
            }
            for (int f = 0; f < FIF; f++) { Clr(sceneCol[f].img, 1, 0); Clr(sceneDist[f].img, 1, 60000); }
            Clr(bloom.img, BLM, 0); Clr(cloudCol.img, 1, 0); Clr(cloudDist.img, 1, 60000);
        });
        if (dsets[0].Handle != 0 && texBlocks != null) WriteDescriptors();
    }
    static void DestroyTex(ref GTex t) { if (t == null) return; vk.DestroyImageView(dev, t.view, null); vk.DestroyImage(dev, t.img, null); vk.FreeMemory(dev, t.mem, null); t = null; }
    static void DestroySwapchainResources()
    {
        foreach (var f in finalFB) vk.DestroyFramebuffer(dev, f, null);
        foreach (var v in swViews) vk.DestroyImageView(dev, v, null);
        foreach (var s in renderDone) vk.DestroySemaphore(dev, s, null);
        finalFB = Array.Empty<Framebuffer>(); swViews = Array.Empty<ImageView>(); renderDone = Array.Empty<Semaphore>();
        for (int f = 0; f < FIF; f++) { if (sceneFB[f].Handle != 0) vk.DestroyFramebuffer(dev, sceneFB[f], null); sceneFB[f] = default; DestroyTex(ref sceneCol[f]); DestroyTex(ref sceneDist[f]); }
        DestroyTex(ref msCol); DestroyTex(ref msDist); DestroyTex(ref depthTex);
        foreach (var f in bloomFB) vk.DestroyFramebuffer(dev, f, null);
        foreach (var v in bloomViews) vk.DestroyImageView(dev, v, null);
        bloomFB = Array.Empty<Framebuffer>(); bloomViews = Array.Empty<ImageView>(); DestroyTex(ref bloom);
        if (cloudFB.Handle != 0) vk.DestroyFramebuffer(dev, cloudFB, null); cloudFB = default; DestroyTex(ref cloudCol); DestroyTex(ref cloudDist);
    }
    static void Recreate() { vk.DeviceWaitIdle(dev); DestroySwapchainResources(); CreateSwapchain(); needRecreate = false; }
    public static void Resize() => needRecreate = true;

    // ======================================================================= descriptors
    static void CreateDescriptorLayout()
    {
        var all = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit;
        var b = stackalloc DescriptorSetLayoutBinding[8];
        b[0] = new DescriptorSetLayoutBinding { Binding = 0, DescriptorType = DescriptorType.UniformBuffer, DescriptorCount = 1, StageFlags = all };
        for (uint i = 1; i < 8; i++) b[i] = new DescriptorSetLayoutBinding { Binding = i, DescriptorType = DescriptorType.CombinedImageSampler, DescriptorCount = 1, StageFlags = all };
        var li = new DescriptorSetLayoutCreateInfo { SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = 8, PBindings = b };
        Check(vk.CreateDescriptorSetLayout(dev, &li, null, out dsl), "dsl");
        var b1 = stackalloc DescriptorSetLayoutBinding[5];
        for (uint i = 0; i < 5; i++) b1[i] = new DescriptorSetLayoutBinding { Binding = i, DescriptorType = DescriptorType.CombinedImageSampler, DescriptorCount = 1, StageFlags = all };
        var li1 = new DescriptorSetLayoutCreateInfo { SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = 5, PBindings = b1 };
        Check(vk.CreateDescriptorSetLayout(dev, &li1, null, out dsl1), "dsl1");
        var pcr = new PushConstantRange { StageFlags = all, Offset = 0, Size = 32 };
        var ls = stackalloc DescriptorSetLayout[2]; ls[0] = dsl; ls[1] = dsl1;
        var pli = new PipelineLayoutCreateInfo { SType = StructureType.PipelineLayoutCreateInfo, SetLayoutCount = 2, PSetLayouts = ls, PushConstantRangeCount = 1, PPushConstantRanges = &pcr };
        Check(vk.CreatePipelineLayout(dev, &pli, null, out playout), "pipeline layout");
        var ps = stackalloc DescriptorPoolSize[2];
        ps[0] = new DescriptorPoolSize(DescriptorType.UniformBuffer, FIF); ps[1] = new DescriptorPoolSize(DescriptorType.CombinedImageSampler, FIF * 7 + FIF * PSETS * 5);
        var dpi = new DescriptorPoolCreateInfo { SType = StructureType.DescriptorPoolCreateInfo, MaxSets = FIF + FIF * PSETS, PoolSizeCount = 2, PPoolSizes = ps };
        Check(vk.CreateDescriptorPool(dev, &dpi, null, out dpool), "descriptor pool");
        var lays = stackalloc DescriptorSetLayout[FIF]; for (int i = 0; i < FIF; i++) lays[i] = dsl;
        var dai = new DescriptorSetAllocateInfo { SType = StructureType.DescriptorSetAllocateInfo, DescriptorPool = dpool, DescriptorSetCount = FIF, PSetLayouts = lays };
        fixed (DescriptorSet* p = dsets) Check(vk.AllocateDescriptorSets(dev, &dai, p), "descriptor sets");
        var l1 = stackalloc DescriptorSetLayout[FIF * PSETS]; for (int i = 0; i < FIF * PSETS; i++) l1[i] = dsl1;
        var tmp = new DescriptorSet[FIF * PSETS];
        var dai1 = new DescriptorSetAllocateInfo { SType = StructureType.DescriptorSetAllocateInfo, DescriptorPool = dpool, DescriptorSetCount = FIF * PSETS, PSetLayouts = l1 };
        fixed (DescriptorSet* p = tmp) Check(vk.AllocateDescriptorSets(dev, &dai1, p), "post descriptor sets");
        for (int f = 0; f < FIF; f++) for (int i = 0; i < PSETS; i++) psets[f, i] = tmp[f * PSETS + i];
    }
    static void WriteImg(DescriptorSet s, uint binding, Sampler smp, ImageView v)
    {
        var ii = new DescriptorImageInfo(smp, v, ImageLayout.ShaderReadOnlyOptimal);
        var w = new WriteDescriptorSet { SType = StructureType.WriteDescriptorSet, DstSet = s, DstBinding = binding, DescriptorCount = 1, DescriptorType = DescriptorType.CombinedImageSampler, PImageInfo = &ii };
        vk.UpdateDescriptorSets(dev, 1, &w, 0, null);
    }
    /// <summary>(Re)writes every descriptor: call once every texture exists, and after resize / aniso changes (with the GPU idle).</summary>
    public static void WriteDescriptors()
    {
        if (sceneCol[0] == null) return;
        for (int f = 0; f < FIF; f++)
        {
            var bi = new DescriptorBufferInfo(ubo[f], 0, UBO_FLOATS * 4);
            var w = new WriteDescriptorSet { SType = StructureType.WriteDescriptorSet, DstSet = dsets[f], DstBinding = 0, DescriptorCount = 1, DescriptorType = DescriptorType.UniformBuffer, PBufferInfo = &bi };
            vk.UpdateDescriptorSets(dev, 1, &w, 0, null);
            WriteImg(dsets[f], 1, anisoOn ? blockAniso : blockPlain, texBlocks.view);
            WriteImg(dsets[f], 2, shadow.smp, shadow.view);
            WriteImg(dsets[f], 3, texNoise.smp, texNoise.view);
            WriteImg(dsets[f], 4, texFont.smp, texFont.view);
            int pf = (f + 1) % FIF; // previous frame's targets
            WriteImg(dsets[f], 5, linClamp, sceneCol[pf].view);
            WriteImg(dsets[f], 6, linClamp, sceneDist[pf].view);
            WriteImg(dsets[f], 7, texCloud.smp, texCloud.view);
            // post sets (bindings: 0/1 = per-pass inputs, 2 = cloud colour, 3 = cloud distance, 4 = scene distance)
            // 0 = bright-pass from the scene, 1..5 = downsample from mip i-1, 6..10 = upsample into mip 4..0 from the mip below, 11 = composite, 12 = clouds
            for (int i = 0; i < PSETS; i++) { WriteImg(psets[f, i], 2, linClamp, cloudCol.view); WriteImg(psets[f, i], 3, linClamp, cloudDist.view); WriteImg(psets[f, i], 4, linClamp, sceneDist[f].view); }
            WriteImg(psets[f, 0], 0, linClamp, sceneCol[f].view); WriteImg(psets[f, 0], 1, linClamp, sceneCol[f].view);
            for (int m = 1; m < BLM; m++) { WriteImg(psets[f, m], 0, linClamp, bloomViews[m - 1]); WriteImg(psets[f, m], 1, linClamp, bloomViews[m - 1]); }
            for (int k = BLM - 2, i = 0; k >= 0; k--, i++) { WriteImg(psets[f, BLM + i], 0, linClamp, bloomViews[k + 1]); WriteImg(psets[f, BLM + i], 1, linClamp, bloomViews[k + 1]); }
            WriteImg(psets[f, 11], 0, linClamp, sceneCol[f].view); WriteImg(psets[f, 11], 1, linClamp, bloomViews[0]);
            WriteImg(psets[f, 12], 0, linClamp, sceneDist[f].view); WriteImg(psets[f, 12], 1, linClamp, sceneDist[f].view);
        }
    }
    public static bool AnisoSupported => anisoSupported;
    public static void SetAniso(bool on)
    {
        if (on == anisoOn) return; anisoOn = on;
        if (texBlocks == null) return;
        vk.DeviceWaitIdle(dev); WriteDescriptors();
    }

    // ======================================================================= memory helpers
    static uint MemType(uint bits, MemoryPropertyFlags want)
    {
        for (int i = 0; i < memProps.MemoryTypeCount; i++)
            if ((bits & (1u << i)) != 0 && (memProps.MemoryTypes[i].PropertyFlags & want) == want) return (uint)i;
        throw new Exception("no suitable memory type: " + want);
    }
    static Buffer MkBuf(ulong size, BufferUsageFlags u, MemoryPropertyFlags p, out DeviceMemory mem)
    {
        var bci = new BufferCreateInfo { SType = StructureType.BufferCreateInfo, Size = size, Usage = u, SharingMode = SharingMode.Exclusive };
        Check(vk.CreateBuffer(dev, &bci, null, out var b), "buffer");
        vk.GetBufferMemoryRequirements(dev, b, out var req);
        var mai = new MemoryAllocateInfo { SType = StructureType.MemoryAllocateInfo, AllocationSize = req.Size, MemoryTypeIndex = MemType(req.MemoryTypeBits, p) };
        Check(vk.AllocateMemory(dev, &mai, null, out mem), "allocate memory");
        vk.BindBufferMemory(dev, b, mem, 0); return b;
    }
    static GTex MkImage(ImageType t, ImageViewType vt, Format f, uint w, uint h, uint d, uint layers, uint mips, ImageUsageFlags u, ImageAspectFlags asp, SampleCountFlags s = SampleCountFlags.Count1Bit)
    {
        var ici = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo, ImageType = t, Format = f, Extent = new Extent3D(w, h, d), MipLevels = mips, ArrayLayers = layers, Samples = s,
            Tiling = ImageTiling.Optimal, Usage = u, SharingMode = SharingMode.Exclusive, InitialLayout = ImageLayout.Undefined,
        };
        Check(vk.CreateImage(dev, &ici, null, out var img), "image");
        vk.GetImageMemoryRequirements(dev, img, out var req);
        uint mt; MemoryPropertyFlags lazy = MemoryPropertyFlags.DeviceLocalBit | MemoryPropertyFlags.LazilyAllocatedBit;
        try { mt = (u & ImageUsageFlags.TransientAttachmentBit) != 0 ? MemType(req.MemoryTypeBits, lazy) : MemType(req.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit); }
        catch { mt = MemType(req.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit); }
        var mai = new MemoryAllocateInfo { SType = StructureType.MemoryAllocateInfo, AllocationSize = req.Size, MemoryTypeIndex = mt };
        Check(vk.AllocateMemory(dev, &mai, null, out var mem), "image memory");
        vk.BindImageMemory(dev, img, mem, 0);
        return new GTex { img = img, mem = mem, view = MkView(img, vt, f, asp, mips, layers) };
    }
    static ImageView MkView(Image img, ImageViewType vt, Format f, ImageAspectFlags asp, uint mips, uint layers)
    {
        var vci = new ImageViewCreateInfo { SType = StructureType.ImageViewCreateInfo, Image = img, ViewType = vt, Format = f, SubresourceRange = new ImageSubresourceRange(asp, 0, mips, 0, layers) };
        Check(vk.CreateImageView(dev, &vci, null, out var v), "image view"); return v;
    }
    static Sampler MkSampler(Filter mag, Filter min, SamplerMipmapMode mm, SamplerAddressMode am, float maxLod, bool compare = false, float aniso = 1)
    {
        var si = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo, MagFilter = mag, MinFilter = min, MipmapMode = mm, AddressModeU = am, AddressModeV = am, AddressModeW = am,
            MaxLod = maxLod, CompareEnable = compare, CompareOp = CompareOp.LessOrEqual, BorderColor = BorderColor.FloatOpaqueWhite,
            AnisotropyEnable = aniso > 1, MaxAnisotropy = aniso,
        };
        Check(vk.CreateSampler(dev, &si, null, out var s), "sampler"); return s;
    }
    static void Transition(CommandBuffer c, Image img, ImageAspectFlags asp, ImageLayout from, ImageLayout to, uint mips, uint layers)
    {
        var b = new ImageMemoryBarrier
        {
            SType = StructureType.ImageMemoryBarrier, OldLayout = from, NewLayout = to, SrcQueueFamilyIndex = Vk.QueueFamilyIgnored, DstQueueFamilyIndex = Vk.QueueFamilyIgnored, Image = img,
            SubresourceRange = new ImageSubresourceRange(asp, 0, mips, 0, layers),
            SrcAccessMask = from == ImageLayout.TransferDstOptimal ? AccessFlags.TransferWriteBit : 0,
            DstAccessMask = to == ImageLayout.TransferDstOptimal ? AccessFlags.TransferWriteBit : AccessFlags.ShaderReadBit,
        };
        vk.CmdPipelineBarrier(c, from == ImageLayout.TransferDstOptimal ? PipelineStageFlags.TransferBit : PipelineStageFlags.TopOfPipeBit,
            to == ImageLayout.TransferDstOptimal ? PipelineStageFlags.TransferBit : PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.VertexShaderBit, 0, 0, null, 0, null, 1, &b);
    }
    static void OneShot(Action<CommandBuffer> rec)
    {
        var cai = new CommandBufferAllocateInfo { SType = StructureType.CommandBufferAllocateInfo, CommandPool = pool, Level = CommandBufferLevel.Primary, CommandBufferCount = 1 };
        vk.AllocateCommandBuffers(dev, &cai, out var c);
        var bi = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit };
        vk.BeginCommandBuffer(c, &bi); rec(c); vk.EndCommandBuffer(c);
        var si = new SubmitInfo { SType = StructureType.SubmitInfo, CommandBufferCount = 1, PCommandBuffers = &c };
        vk.QueueSubmit(queue, 1, &si, default); vk.QueueWaitIdle(queue); vk.FreeCommandBuffers(dev, pool, 1, &c);
    }

    /// <summary>Creates a sampled texture and uploads every mip level (mips[level] holds all layers / slices of that level, tightly packed).</summary>
    public static GTex MakeTexture(ImageType t, ImageViewType vt, Format f, int bpp, uint w, uint h, uint d, uint layers, byte[][] mips, Filter mag, Filter min, SamplerMipmapMode mm, SamplerAddressMode am)
    {
        var tex = MkImage(t, vt, f, w, h, d, layers, (uint)mips.Length, ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit, ImageAspectFlags.ColorBit);
        tex.smp = MkSampler(mag, min, mm, am, mips.Length - 1);
        ulong total = 0; foreach (var m in mips) total += (ulong)m.Length;
        var sb = MkBuf(Math.Max(total, 4), BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, out var sm);
        void* p; vk.MapMemory(dev, sm, 0, total, 0, &p); ulong o = 0; foreach (var m in mips) { Marshal.Copy(m, 0, (nint)((byte*)p + o), m.Length); o += (ulong)m.Length; } vk.UnmapMemory(dev, sm);
        OneShot(c =>
        {
            Transition(c, tex.img, ImageAspectFlags.ColorBit, ImageLayout.Undefined, ImageLayout.TransferDstOptimal, (uint)mips.Length, layers);
            ulong off = 0;
            for (int l = 0; l < mips.Length; l++)
            {
                uint mw = Math.Max(1, w >> l), mh = Math.Max(1, h >> l), md = Math.Max(1, d >> l);
                var r = new BufferImageCopy { BufferOffset = off, ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, (uint)l, 0, layers), ImageExtent = new Extent3D(mw, mh, md) };
                vk.CmdCopyBufferToImage(c, sb, tex.img, ImageLayout.TransferDstOptimal, 1, &r); off += (ulong)mips[l].Length;
            }
            Transition(c, tex.img, ImageAspectFlags.ColorBit, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal, (uint)mips.Length, layers);
        });
        vk.DestroyBuffer(dev, sb, null); vk.FreeMemory(dev, sm, null);
        return tex;
    }

    // ======================================================================= rings (staging + dynamic vertices)
    static Ring NewRing(int f, ulong size)
    {
        var r = new Ring { size = size };
        r.buf = MkBuf(size, BufferUsageFlags.TransferSrcBit | BufferUsageFlags.VertexBufferBit | BufferUsageFlags.IndexBufferBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, out r.mem);
        void* p; vk.MapMemory(dev, r.mem, 0, size, 0, &p); r.ptr = (byte*)p; rings[f].Add(r); return r;
    }
    static (Ring r, ulong off) RingAlloc(ulong size)
    {
        size = (size + 15) & ~15UL; var list = rings[fi];
        while (ringCur[fi] < list.Count)
        {
            var r = list[ringCur[fi]];
            if (r.off + size <= r.size) { ulong o = r.off; r.off += size; return (r, o); }
            ringCur[fi]++;
        }
        var nr = NewRing(fi, Math.Max(16UL << 20, size)); ringCur[fi] = list.Count - 1; nr.off = size; return (nr, 0);
    }

    // ======================================================================= mesh heap
    static GAlloc HeapAlloc(ulong size)
    {
        size = (size + 15) & ~15UL;
        foreach (var b in blocks)
        {
            var fl = b.free;
            for (int i = 0; i < fl.Count; i++)
            {
                var (o, s) = fl[i]; if (s < size) continue;
                if (s == size) fl.RemoveAt(i); else fl[i] = (o + size, s - size);
                HeapUsed += (long)size; return new GAlloc { blk = b, off = o, size = size };
            }
        }
        ulong bs = Math.Max(BLK, size);
        var nb = new GBlock { size = bs };
        nb.buf = MkBuf(bs, BufferUsageFlags.VertexBufferBit | BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit, MemoryPropertyFlags.DeviceLocalBit, out nb.mem);
        if (bs > size) nb.free.Add((size, bs - size));
        blocks.Add(nb); HeapUsed += (long)size;
        return new GAlloc { blk = nb, off = 0, size = size };
    }
    static void HeapFreeNow(GAlloc a)
    {
        var fl = a.blk.free; HeapUsed -= (long)a.size;
        int lo = 0, hi = fl.Count; while (lo < hi) { int m = (lo + hi) >> 1; if (fl[m].o < a.off) lo = m + 1; else hi = m; }
        ulong o = a.off, s = a.size;
        if (lo < fl.Count && o + s == fl[lo].o) { s += fl[lo].s; fl.RemoveAt(lo); }
        if (lo > 0 && fl[lo - 1].o + fl[lo - 1].s == o) { o = fl[lo - 1].o; s += fl[lo - 1].s; fl.RemoveAt(lo - 1); lo--; }
        fl.Insert(lo, (o, s));
    }
    /// <summary>Frees a mesh allocation once the GPU can no longer be using it.</summary>
    public static void Free(GAlloc a) { if (a == null || a.freed) return; a.freed = true; pendFree[fi].Add(a); }

    /// <summary>Copies data into a new device-local allocation (recorded in this frame's command buffer, before any render pass).</summary>
    public static GAlloc Upload<T1, T2>(T1[] a, int aCount, T2[] b, int bCount) where T1 : unmanaged where T2 : unmanaged
    {
        ulong sa = (ulong)(aCount * sizeof(T1)), sb = (ulong)(bCount * sizeof(T2)), size = sa + sb;
        if (size == 0) return HeapAlloc(16);
        var g = HeapAlloc(size);
        if (!frameOn) { OneShotUpload(g, a, aCount, b, bCount); return g; }
        var (r, o) = RingAlloc(size);
        fixed (T1* pa = a) System.Buffer.MemoryCopy(pa, r.ptr + o, sa, sa);
        if (sb > 0) fixed (T2* pb = b) System.Buffer.MemoryCopy(pb, r.ptr + o + sa, sb, sb);
        var reg = new BufferCopy(o, g.off, size); vk.CmdCopyBuffer(cb, r.buf, g.blk.buf, 1, &reg); uploadsPending = true;
        return g;
    }
    static void OneShotUpload<T1, T2>(GAlloc g, T1[] a, int aCount, T2[] b, int bCount) where T1 : unmanaged where T2 : unmanaged
    {
        ulong sa = (ulong)(aCount * sizeof(T1)), sb = (ulong)(bCount * sizeof(T2)), size = sa + sb;
        var sbuf = MkBuf(size, BufferUsageFlags.TransferSrcBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, out var sm);
        void* p; vk.MapMemory(dev, sm, 0, size, 0, &p);
        fixed (T1* pa = a) System.Buffer.MemoryCopy(pa, p, sa, sa);
        if (sb > 0) fixed (T2* pb = b) System.Buffer.MemoryCopy(pb, (byte*)p + sa, sb, sb);
        vk.UnmapMemory(dev, sm);
        OneShot(c => { var reg = new BufferCopy(0, g.off, size); vk.CmdCopyBuffer(c, sbuf, g.blk.buf, 1, &reg); });
        vk.DestroyBuffer(dev, sbuf, null); vk.FreeMemory(dev, sm, null);
    }
    /// <summary>Makes this frame's uploads visible to vertex fetch. Called before the first render pass of the frame.</summary>
    public static void FlushUploads()
    {
        if (!uploadsPending) return; uploadsPending = false;
        var mb = new MemoryBarrier { SType = StructureType.MemoryBarrier, SrcAccessMask = AccessFlags.TransferWriteBit, DstAccessMask = AccessFlags.VertexAttributeReadBit | AccessFlags.IndexReadBit };
        vk.CmdPipelineBarrier(cb, PipelineStageFlags.TransferBit, PipelineStageFlags.VertexInputBit, 0, 1, &mb, 0, null, 0, null);
    }

    // ======================================================================= frame
    /// <summary>Render resolution as a fraction of the window (0.3 .. 1). Changing it rebuilds the size-dependent targets.</summary>
    public static void SetRenderScale(double s)
    {
        float v = (float)Math.Clamp(s, .25, 1);
        if (Math.Abs(v - renderScale) > .001) { renderScale = v; needRecreate = true; }
    }
    public static bool BeginFrame(bool vsync)
    {
        if (vsync != curVsync) { curVsync = vsync; needRecreate = true; }
        if (needRecreate || ext.Width == 0) { Recreate(); if (ext.Width == 0) return false; }
        var f = fences[fi]; vk.WaitForFences(dev, 1, &f, true, ulong.MaxValue);
        foreach (var a in pendFree[fi]) HeapFreeNow(a); pendFree[fi].Clear();
        foreach (var r in rings[fi]) r.off = 0; ringCur[fi] = 0;
        uint idx = 0; var res = khrSwap.AcquireNextImage(dev, swap, ulong.MaxValue, imgAvail[fi], default, &idx);
        if (res == Result.ErrorOutOfDateKhr) { needRecreate = true; return false; }
        if (res != Result.Success && res != Result.SuboptimalKhr) throw new Exception("vkAcquireNextImageKHR: " + res);
        imgIdx = idx; vk.ResetFences(dev, 1, &f);
        cb = cbs[fi]; vk.ResetCommandBuffer(cb, 0);
        var bi = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit };
        vk.BeginCommandBuffer(cb, &bi); frameOn = true; ResetBinds();
        return true;
    }
    static void ResetBinds() { boundPipe = null; boundVB = default; boundVBOff = ulong.MaxValue; boundIB = default; boundIBOff = ulong.MaxValue; }
    static void BindSet() { var s = dsets[fi]; vk.CmdBindDescriptorSets(cb, PipelineBindPoint.Graphics, playout, 0, 1, &s, 0, null); }
    static void BindPostSet(int i) { var s = psets[fi, i]; vk.CmdBindDescriptorSets(cb, PipelineBindPoint.Graphics, playout, 1, 1, &s, 0, null); }
    static void BeginPass(RenderPass rp, Framebuffer fb, Extent2D e, ClearValue* cv, uint ncv)
    {
        EndPass();
        var rbi = new RenderPassBeginInfo { SType = StructureType.RenderPassBeginInfo, RenderPass = rp, Framebuffer = fb, RenderArea = new Rect2D(new Offset2D(0, 0), e), ClearValueCount = ncv, PClearValues = cv };
        vk.CmdBeginRenderPass(cb, &rbi, SubpassContents.Inline); inPass = true;
        var vp = new Viewport(0, 0, e.Width, e.Height, 0, 1); var sc = new Rect2D(new Offset2D(0, 0), e);
        vk.CmdSetViewport(cb, 0, 1, &vp); vk.CmdSetScissor(cb, 0, 1, &sc); BindSet(); boundPipe = null;
    }
    public static void BeginShadow(int layer)
    {
        FlushUploads();
        var cv = new ClearValue { DepthStencil = new ClearDepthStencilValue(1, 0) };
        BeginPass(shadowRP, shadowFB[layer], new Extent2D(SMS, SMS), &cv, 1);
    }
    public static void EndPass() { if (inPass) { vk.CmdEndRenderPass(cb); inPass = false; } }
    /// <summary>Opens the HDR scene pass (colour cleared to transparent black: alpha 0 marks open sky for god rays).</summary>
    public static void BeginScene()
    {
        FlushUploads();
        var cv = stackalloc ClearValue[5];
        cv[0] = new ClearValue { Color = new ClearColorValue(0, 0, 0, 0) }; cv[1] = new ClearValue { Color = new ClearColorValue(60000, 0, 0, 0) };
        cv[2] = new ClearValue { DepthStencil = new ClearDepthStencilValue(0, 0) }; cv[3] = cv[0]; cv[4] = cv[1];
        BeginPass(sceneRP, sceneFB[fi], sceneExt, cv, 5);
    }
    /// <summary>Ends the scene, runs the bloom chain if enabled, opens the swapchain pass and draws the composite (FXAA, bloom, god rays).</summary>
    public static void PostProcess(bool doBloom, bool doClouds)
    {
        EndPass();
        if (doClouds)
        {
            BeginPass(cloudRP, cloudFB, cloudExt, null, 0);
            Bind(pCloud); BindPostSet(12); Push(1f / ext.Width, 1f / ext.Height, 0, 0); DrawFull();
        }
        if (doBloom)
        {
            for (int m = 0; m < BLM; m++)
            {
                BeginPass(postRP, bloomFB[m], bloomExt[m], null, 0);
                Bind(m == 0 ? pBloomPre : pBloomDown); BindPostSet(m);
                Extent2D src = m == 0 ? sceneExt : bloomExt[m - 1];
                Push(1f / src.Width, 1f / src.Height, 0, m); DrawFull();
            }
            for (int k = BLM - 2, i = 0; k >= 0; k--, i++)
            {
                BeginPass(postAddRP, bloomFB[k], bloomExt[k], null, 0);
                Bind(pBloomUp); BindPostSet(BLM + i);
                Push(1f / bloomExt[k + 1].Width, 1f / bloomExt[k + 1].Height, 0, k); DrawFull();
            }
        }
        BeginFinal(0, 0, 0);
        Bind(pComposite); BindPostSet(11); Push(1f / sceneExt.Width, 1f / sceneExt.Height, 0, 0); DrawFull();
    }
    /// <summary>Swapchain pass (the start screen draws only this, the game draws its composite into it first). The UI goes on top.</summary>
    public static void BeginFinal(float r, float g, float b)
    {
        FlushUploads();
        var cv = new ClearValue { Color = new ClearColorValue(r, g, b, 1) };
        BeginPass(finalRP, finalFB[imgIdx], ext, &cv, 1);
    }
    public static void EndFrame()
    {
        if (!frameOn) return;
        EndPass();
        Buffer shotBuf = default; DeviceMemory shotMem = default; string shot = ShotPath; ShotPath = null;
        if (shot != null)
        {
            shotBuf = MkBuf((ulong)ext.Width * ext.Height * 4, BufferUsageFlags.TransferDstBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit, out shotMem);
            var b = new ImageMemoryBarrier { SType = StructureType.ImageMemoryBarrier, OldLayout = ImageLayout.PresentSrcKhr, NewLayout = ImageLayout.TransferSrcOptimal, SrcQueueFamilyIndex = Vk.QueueFamilyIgnored, DstQueueFamilyIndex = Vk.QueueFamilyIgnored, Image = swImgs[imgIdx], SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1), SrcAccessMask = AccessFlags.ColorAttachmentWriteBit, DstAccessMask = AccessFlags.TransferReadBit };
            vk.CmdPipelineBarrier(cb, PipelineStageFlags.ColorAttachmentOutputBit, PipelineStageFlags.TransferBit, 0, 0, null, 0, null, 1, &b);
            var r = new BufferImageCopy { ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1), ImageExtent = new Extent3D(ext.Width, ext.Height, 1) };
            vk.CmdCopyImageToBuffer(cb, swImgs[imgIdx], ImageLayout.TransferSrcOptimal, shotBuf, 1, &r);
            b.OldLayout = ImageLayout.TransferSrcOptimal; b.NewLayout = ImageLayout.PresentSrcKhr; b.SrcAccessMask = AccessFlags.TransferReadBit; b.DstAccessMask = 0;
            vk.CmdPipelineBarrier(cb, PipelineStageFlags.TransferBit, PipelineStageFlags.BottomOfPipeBit, 0, 0, null, 0, null, 1, &b);
        }
        vk.EndCommandBuffer(cb); frameOn = false;
        var wait = imgAvail[fi]; var sig = renderDone[imgIdx]; var c = cb; PipelineStageFlags ws = PipelineStageFlags.ColorAttachmentOutputBit;
        var si = new SubmitInfo { SType = StructureType.SubmitInfo, WaitSemaphoreCount = 1, PWaitSemaphores = &wait, PWaitDstStageMask = &ws, CommandBufferCount = 1, PCommandBuffers = &c, SignalSemaphoreCount = 1, PSignalSemaphores = &sig };
        Check(vk.QueueSubmit(queue, 1, &si, fences[fi]), "vkQueueSubmit");
        var sw = swap; uint ii = imgIdx;
        var pi = new PresentInfoKHR { SType = StructureType.PresentInfoKhr, WaitSemaphoreCount = 1, PWaitSemaphores = &sig, SwapchainCount = 1, PSwapchains = &sw, PImageIndices = &ii };
        var res = khrSwap.QueuePresent(queue, &pi);
        if (res == Result.ErrorOutOfDateKhr || res == Result.SuboptimalKhr) needRecreate = true;
        if (shot != null)
        {
            var f = fences[fi]; vk.WaitForFences(dev, 1, &f, true, ulong.MaxValue);
            void* p; vk.MapMemory(dev, shotMem, 0, (ulong)ext.Width * ext.Height * 4, 0, &p);
            var px = new byte[ext.Width * ext.Height * 4]; Marshal.Copy((nint)p, px, 0, px.Length); vk.UnmapMemory(dev, shotMem);
            vk.DestroyBuffer(dev, shotBuf, null); vk.FreeMemory(dev, shotMem, null);
            Png.Write(shot, px, (int)ext.Width, (int)ext.Height, swFmt == Format.B8G8R8A8Unorm, false);
        }
        fi = (fi + 1) % FIF;
    }
    /// <summary>Set to a .png path to save the next presented frame (used by --bench --shot).</summary>
    public static string ShotPath;

    // ======================================================================= uniforms + draw helpers
    public static void WriteUbo(float[] u) { fixed (float* p = u) System.Buffer.MemoryCopy(p, uboPtr[fi], UBO_FLOATS * 4, Math.Min(u.Length, UBO_FLOATS) * 4); }
    public static void UboSet(int i, float v) => uboPtr[fi][i] = v;
    static Pipe boundPipe; static Buffer boundVB, boundIB; static ulong boundVBOff, boundIBOff; static IndexType boundIT;
    public static void Bind(Pipe p) { if (boundPipe == p) return; boundPipe = p; vk.CmdBindPipeline(cb, PipelineBindPoint.Graphics, p.p); }
    public static void Push(float x, float y, float z, float wt) { var d = stackalloc float[4] { x, y, z, wt }; vk.CmdPushConstants(cb, playout, ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, 0, 16, d); }
    public static void PushK(float r, float g, float b, float a) { var d = stackalloc float[4] { r, g, b, a }; vk.CmdPushConstants(cb, playout, ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit, 16, 16, d); }
    public static void BindVB(Buffer b, ulong off) { if (b.Handle == boundVB.Handle && off == boundVBOff) return; boundVB = b; boundVBOff = off; vk.CmdBindVertexBuffers(cb, 0, 1, &b, &off); }
    public static void BindIB(Buffer b, ulong off, IndexType t) { if (b.Handle == boundIB.Handle && off == boundIBOff && t == boundIT) return; boundIB = b; boundIBOff = off; boundIT = t; vk.CmdBindIndexBuffer(cb, b, off, t); }
    /// <summary>Mesh stored as [vertices][indices] in one allocation.</summary>
    public static void DrawMesh(GAlloc a, uint vbytes, uint count, uint first, IndexType it)
    {
        BindVB(a.blk.buf, a.off); BindIB(a.blk.buf, a.off + vbytes, it); vk.CmdDrawIndexed(cb, count, 1, first, 0, 0);
    }
    /// <summary>Vertices from one allocation, indices from another (shared) one.</summary>
    public static void DrawShared(GAlloc v, GAlloc ix, IndexType t, uint count, uint first = 0)
    {
        BindVB(v.blk.buf, v.off); BindIB(ix.blk.buf, ix.off, t); vk.CmdDrawIndexed(cb, count, 1, first, 0, 0);
    }
    public static void DrawFull() => vk.CmdDraw(cb, 3, 1, 0, 0);
    /// <summary>Per-frame vertex data (host-visible ring).</summary>
    public static void DrawDynamic(ReadOnlySpan<float> data, uint vcount)
    {
        if (vcount == 0) return;
        var (r, o) = RingAlloc((ulong)data.Length * 4); data.CopyTo(new Span<float>(r.ptr + o, data.Length));
        BindVB(r.buf, o); vk.CmdDraw(cb, vcount, 1, 0, 0);
    }
    public static void DrawDynamicIndexed(float[] data, int floats, GAlloc ix, uint count, uint first)
    {
        var (r, o) = RingAlloc((ulong)floats * 4); fixed (float* p = data) System.Buffer.MemoryCopy(p, r.ptr + o, floats * 4, floats * 4);
        BindVB(r.buf, o); BindIB(ix.blk.buf, ix.off, IndexType.Uint16); vk.CmdDrawIndexed(cb, count, 1, first, 0, 0);
    }

    // ======================================================================= shaders / pipelines
    // All loose GL-style uniforms live in one std140 block (written once per frame); per-draw values are push constants.
    const string HEADER = """
#version 450
layout(std140,set=0,binding=0) uniform Frame{
 mat4 uVP; mat4 uLV;
 vec3 uS; float uR; vec3 uC; float uAT; vec3 uWind; float uTime; vec3 uWdir; float uFog;
 vec3 uF; float uTan; vec3 uRt; float uAsp; vec3 uUp; float uShT; vec2 uScr; float uRc0; float uRc1;
 float uWA,uWL,uWK,uWD,uWTm,uWR0,uSea,uWR,uWF,uWC,uOvc,uStorm,uFlash,uSnow,uWet,uCov,uShOn,uCsOn,uWnd,uD,uNear,uRW,uCN,uCF;
 mat4 uPVP; mat4 uLV1; mat4 uLV2;
 vec3 uPC; float uSsr; vec2 uSunUV; float uRayK; float uBloomK;
 float uFxaa; float uShN; float uRS; float uPad1;
 vec3 uWindW; float uCloudQ; vec3 uWindD; float uCloudOn;
};
layout(push_constant) uniform PC{ vec3 uO; float uWt; vec4 uK; };
layout(set=0,binding=1) uniform sampler2DArray uT;
layout(set=0,binding=2) uniform sampler2DArrayShadow uSh;
layout(set=0,binding=3) uniform sampler3D uN;
layout(set=0,binding=4) uniform sampler2D uFont;
layout(set=0,binding=5) uniform sampler2D uHist;
layout(set=0,binding=6) uniform sampler2D uHistD;
layout(set=1,binding=0) uniform sampler2D uSrc;
layout(set=1,binding=1) uniform sampler2D uSrc2;
layout(set=1,binding=2) uniform sampler2D uSrc3;
layout(set=1,binding=3) uniform sampler2D uSrc4;
layout(set=1,binding=4) uniform sampler2D uSrc5;
layout(set=0,binding=7) uniform sampler3D uCloudN;

""";
    static readonly Regex LooseUniform = new(@"uniform\s+(?!\w+\s*\{)[^;{]*;", RegexOptions.Compiled);
    static Shaderc sc; static Compiler* scComp; static CompileOptions* scOpt;

    static byte[] Spirv(string src, ShaderKind kind, string name)
    {
        string body = LooseUniform.Replace(src, "").Replace("gl_VertexID", "gl_VertexIndex");
        if (kind == ShaderKind.FragmentShader) body = body.Replace("out vec4 o;", "layout(location=0) out vec4 o;");
        string full = HEADER + body;
        string hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(full + (int)kind)));
        string path = Path.Combine(CacheDir, hash + ".spv");
        try { if (File.Exists(path)) return File.ReadAllBytes(path); } catch { }
        if (sc == null)
        {
            sc = Shaderc.GetApi(); scComp = sc.CompilerInitialize(); scOpt = sc.CompileOptionsInitialize();
            sc.CompileOptionsSetOptimizationLevel(scOpt, OptimizationLevel.Performance);
            sc.CompileOptionsSetAutoMapLocations(scOpt, true);
            sc.CompileOptionsSetTargetEnv(scOpt, TargetEnv.Vulkan, (uint)EnvVersion.Vulkan10);
        }
        var bytes = Encoding.UTF8.GetBytes(full);
        CompilationResult* res;
        fixed (byte* p = bytes) res = sc.CompileIntoSpv(scComp, p, (nuint)bytes.Length, kind, name, "main", scOpt);
        if (sc.ResultGetCompilationStatus(res) != CompilationStatus.Success)
        {
            string err = SilkMarshal.PtrToString((nint)sc.ResultGetErrorMessage(res)); sc.ResultRelease(res);
            throw new Exception($"shader {name} compile error:\n{err}");
        }
        var outb = new byte[(int)sc.ResultGetLength(res)]; Marshal.Copy((nint)sc.ResultGetBytes(res), outb, 0, outb.Length); sc.ResultRelease(res);
        try { File.WriteAllBytes(path, outb); } catch { }
        return outb;
    }
    static ShaderModule Module(byte[] code)
    {
        fixed (byte* p = code)
        {
            var ci = new ShaderModuleCreateInfo { SType = StructureType.ShaderModuleCreateInfo, CodeSize = (nuint)code.Length, PCode = (uint*)p };
            Check(vk.CreateShaderModule(dev, &ci, null, out var m), "shader module"); return m;
        }
    }

    public static Pipe MakePipe(string name, string vs, string fs, PipeCfg c)
    {
        var vm = Module(Spirv(vs, ShaderKind.VertexShader, name + ".vert")); var fm = Module(Spirv(fs, ShaderKind.FragmentShader, name + ".frag"));
        var main = (byte*)SilkMarshal.StringToPtr("main");
        var st = stackalloc PipelineShaderStageCreateInfo[2];
        st[0] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.VertexBit, Module = vm, PName = main };
        st[1] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.FragmentBit, Module = fm, PName = main };
        (uint loc, Format f, uint off)[] attrs; uint stride;
        switch (c.layout)
        {
            case VL.Chunk: stride = 16; attrs = new[] { (0u, Format.R16G16B16A16Sint, 0u), (1u, Format.R16G16Uint, 8u), (2u, Format.R8G8B8A8SNorm, 12u) }; break;
            case VL.ChunkShadow: stride = 16; attrs = new[] { (0u, Format.R16G16B16A16Sint, 0u), (1u, Format.R16G16Uint, 8u) }; break;
            case VL.Lod: stride = 48; attrs = new[] { (0u, Format.R32G32B32Sfloat, 0u), (1u, Format.R32G32Sfloat, 12u), (2u, Format.R32G32B32Sfloat, 20u), (3u, Format.R32Sfloat, 32u), (4u, Format.R32G32B32Sfloat, 36u) }; break;
            case VL.LodSea: stride = 48; attrs = new[] { (0u, Format.R32G32B32Sfloat, 0u), (3u, Format.R32Sfloat, 32u) }; break;
            case VL.LodShadow: stride = 48; attrs = new[] { (0u, Format.R32G32B32Sfloat, 0u) }; break;
            case VL.Pos3: stride = 12; attrs = new[] { (0u, Format.R32G32B32Sfloat, 0u) }; break;
            case VL.UI: stride = 40; attrs = new[] { (0u, Format.R32G32Sfloat, 0u), (1u, Format.R32G32Sfloat, 8u), (2u, Format.R32G32B32A32Sfloat, 16u), (3u, Format.R32G32Sfloat, 32u) }; break;
            default: stride = 0; attrs = Array.Empty<(uint, Format, uint)>(); break;
        }
        var bind = new VertexInputBindingDescription(0, stride, VertexInputRate.Vertex);
        var ad = stackalloc VertexInputAttributeDescription[Math.Max(1, attrs.Length)];
        for (int i = 0; i < attrs.Length; i++) ad[i] = new VertexInputAttributeDescription(attrs[i].loc, 0, attrs[i].f, attrs[i].off);
        var vin = new PipelineVertexInputStateCreateInfo { SType = StructureType.PipelineVertexInputStateCreateInfo, VertexBindingDescriptionCount = attrs.Length > 0 ? 1u : 0u, PVertexBindingDescriptions = &bind, VertexAttributeDescriptionCount = (uint)attrs.Length, PVertexAttributeDescriptions = ad };
        var ia = new PipelineInputAssemblyStateCreateInfo { SType = StructureType.PipelineInputAssemblyStateCreateInfo, Topology = c.topo };
        var vps = new PipelineViewportStateCreateInfo { SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, ScissorCount = 1 };
        bool shadowPass = c.pass == RP.Shadow, scene = c.pass == RP.Scene;
        // Vulkan measures winding in Y-down framebuffer space: with the Y-flipped main projection GL CCW faces stay CCW, the unflipped shadow pass sees them CW
        var rs = new PipelineRasterizationStateCreateInfo
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo, PolygonMode = PolygonMode.Fill, CullMode = c.cull, FrontFace = shadowPass ? FrontFace.Clockwise : FrontFace.CounterClockwise, LineWidth = 1,
            DepthBiasEnable = c.depthBias, DepthBiasConstantFactor = shadowPass ? 1.5f : -3, DepthBiasSlopeFactor = shadowPass ? 1.5f : -3,
        };
        var msi = new PipelineMultisampleStateCreateInfo { SType = StructureType.PipelineMultisampleStateCreateInfo, RasterizationSamples = scene ? samples : SampleCountFlags.Count1Bit };
        bool hasDepth = scene || shadowPass;
        var ds = new PipelineDepthStencilStateCreateInfo { SType = StructureType.PipelineDepthStencilStateCreateInfo, DepthTestEnable = hasDepth && c.depthTest, DepthWriteEnable = hasDepth && c.depthWrite, DepthCompareOp = c.cmp };
        var cba = stackalloc PipelineColorBlendAttachmentState[2];
        cba[0] = new PipelineColorBlendAttachmentState { ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit, BlendEnable = c.blend != BlendMode.None };
        // alpha channel of the scene target = how much of the sky is covered (the god-ray pass only marches through open sky)
        if (c.blend == BlendMode.Alpha) { cba[0].SrcColorBlendFactor = BlendFactor.SrcAlpha; cba[0].DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha; cba[0].SrcAlphaBlendFactor = BlendFactor.One; cba[0].DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha; cba[0].ColorBlendOp = cba[0].AlphaBlendOp = BlendOp.Add; }
        if (c.blend == BlendMode.Add) { cba[0].SrcColorBlendFactor = BlendFactor.One; cba[0].DstColorBlendFactor = BlendFactor.One; cba[0].SrcAlphaBlendFactor = BlendFactor.Zero; cba[0].DstAlphaBlendFactor = BlendFactor.One; cba[0].ColorBlendOp = cba[0].AlphaBlendOp = BlendOp.Add; }
        cba[1] = new PipelineColorBlendAttachmentState { ColorWriteMask = c.writeDist ? ColorComponentFlags.RBit : 0, BlendEnable = false };
        if (c.pass == RP.Cloud) cba[1].ColorWriteMask = ColorComponentFlags.RBit;
        var cbs2 = new PipelineColorBlendStateCreateInfo { SType = StructureType.PipelineColorBlendStateCreateInfo, AttachmentCount = shadowPass ? 0u : (scene || c.pass == RP.Cloud) ? 2u : 1u, PAttachments = cba };
        var dyn = stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor };
        var dsi = new PipelineDynamicStateCreateInfo { SType = StructureType.PipelineDynamicStateCreateInfo, DynamicStateCount = 2, PDynamicStates = dyn };
        var rp = c.pass switch { RP.Shadow => shadowRP, RP.Post => postRP, RP.PostAdd => postAddRP, RP.Final => finalRP, RP.Cloud => cloudRP, _ => sceneRP };
        var gpi = new GraphicsPipelineCreateInfo
        {
            SType = StructureType.GraphicsPipelineCreateInfo, StageCount = 2, PStages = st, PVertexInputState = &vin, PInputAssemblyState = &ia, PViewportState = &vps,
            PRasterizationState = &rs, PMultisampleState = &msi, PDepthStencilState = &ds, PColorBlendState = &cbs2, PDynamicState = &dsi, Layout = playout,
            RenderPass = rp, Subpass = 0,
        };
        Check(vk.CreateGraphicsPipelines(dev, pcache, 1, &gpi, null, out var pl), "pipeline " + name);
        vk.DestroyShaderModule(dev, vm, null); vk.DestroyShaderModule(dev, fm, null); SilkMarshal.Free((nint)main);
        return new Pipe { p = pl, name = name };
    }

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
