namespace CubePlanet;

/// <summary>
/// Packed chunk mesh, 16 bytes per vertex (was 40):
///   int16x4  position*256 (relative to the chunk origin, +-128 blocks, 1/256 precision), texture layer
///   uint16x2 sky*120 (exact for the /15 light and /8 water-depth steps), bits: u | v&lt;&lt;1 | ao&lt;&lt;2 | material flags&lt;&lt;4 (sway leaves / plant / underwater, emissive, no shadow, ice)
///   snorm8x4 normal
/// Indices are 16-bit whenever the mesh has fewer than 65536 vertices.
/// </summary>
public class MeshData { public byte[] vb; public ushort[] i16; public uint[] i32; public int n; }
public class BuildResult { public MeshData o, w; public int nw; public V3 org; }
