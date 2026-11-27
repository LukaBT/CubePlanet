namespace CubePlanet;

/// <summary>Terrain results of one 32x32 chunk column: height, top / filler block, filler depth, ice, biome.</summary>
public class Col
{
    public float[] h = new float[1024];
    public ushort[] t = new ushort[1024], f = new ushort[1024]; // top / filler block IDs
    public byte[] fd = new byte[1024], ice = new byte[1024], b = new byte[1024]; // filler depth, frozen water, biome
    public double hi = -1e9, lo = 1e9;
    /// <summary>Lakes: water surface (relative to sea level, NaN = none) and the surface of a lake nearby (no caves above it - 40).</summary>
    public float[] rs = new float[1024], rn = new float[1024];
    /// <summary>Cave water of each column (<see cref="Aquifers.Column"/>): computed the first time a chunk of this column has caves, then shared
    /// by every chunk above and below (it used to be recomputed for each: a quarter of all generation time).</summary>
    public (double level, double barrierTop)[] aq;
}
