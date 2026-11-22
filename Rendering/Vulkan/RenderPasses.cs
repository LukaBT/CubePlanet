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

/// <summary>Render passes (scene, shadow, post, final, clouds).</summary>
public static unsafe partial class Gpu
{
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
}
