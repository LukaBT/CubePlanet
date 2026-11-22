namespace CubePlanet;

/// <summary>Terrain results of one 32x32 chunk column: height, top / filler block, filler depth, ice, biome.</summary>
public class Col
{
    public float[] h = new float[1024];
    public ushort[] t = new ushort[1024], f = new ushort[1024]; // top / filler block IDs
    public byte[] fd = new byte[1024], ice = new byte[1024], b = new byte[1024]; // filler depth, frozen water, biome
    public double hi = -1e9, lo = 1e9;
}
