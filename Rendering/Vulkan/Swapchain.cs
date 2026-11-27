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

/// <summary>Swapchain and every size-dependent target (scene, bloom chain, clouds).</summary>
public static unsafe partial class Gpu
{
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
        ext = caps.CurrentExtent.Width != uint.MaxValue ? caps.CurrentExtent : new Extent2D((uint)Math.Max(1, Renderer.VW), (uint)Math.Max(1, Renderer.VH));
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
    public static void Resize() { if (Dx.On) Dx.Resize(); else needRecreate = true; }
}
