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

/// <summary>Images, views, samplers, layout transitions and texture uploads.</summary>
public static unsafe partial class Gpu
{
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
}
