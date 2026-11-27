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

/// <summary>Buffers, staging / dynamic rings, the pooled mesh heap, uploads.</summary>
public static unsafe partial class Gpu
{
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
    public static void Free(GAlloc a) { if (Dx.On) { Dx.Free(a); return; } if (a == null || a.freed) return; a.freed = true; pendFree[fi].Add(a); }

    /// <summary>Copies data into a new device-local allocation (recorded in this frame's command buffer, before any render pass).</summary>
    public static GAlloc Upload<T1, T2>(T1[] a, int aCount, T2[] b, int bCount) where T1 : unmanaged where T2 : unmanaged
    {
        if (Dx.On) return Dx.Upload(a, aCount, b, bCount);
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
        if (Dx.On) return;
        if (!uploadsPending) return; uploadsPending = false;
        var mb = new MemoryBarrier { SType = StructureType.MemoryBarrier, SrcAccessMask = AccessFlags.TransferWriteBit, DstAccessMask = AccessFlags.VertexAttributeReadBit | AccessFlags.IndexReadBit };
        vk.CmdPipelineBarrier(cb, PipelineStageFlags.TransferBit, PipelineStageFlags.VertexInputBit, 0, 1, &mb, 0, null, 0, null);
    }
}
