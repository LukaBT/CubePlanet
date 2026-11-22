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

/// <summary>Frame begin / end, render passes, post-processing, binding and draw helpers.</summary>
public static unsafe partial class Gpu
{
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
}
