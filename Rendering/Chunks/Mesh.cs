namespace CubePlanet;

/// <summary>A chunk mesh on the GPU (vb = vertex bytes; the indices follow them in the same allocation).</summary>
public class Mesh { public GAlloc a; public uint vb; public int n, nw; public bool i16; public V3 org; }
