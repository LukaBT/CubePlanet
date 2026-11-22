using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;
using Image = Silk.NET.Vulkan.Image;

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
