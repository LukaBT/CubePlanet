namespace CubePlanet;

/// <summary>Terrain results of one 32x32 chunk column: height, top / filler block, filler depth, ice, biome.</summary>
public class Col
{
    public float[] h = new float[1024];
    public byte[] t = new byte[1024], f = new byte[1024], fd = new byte[1024], ice = new byte[1024], b = new byte[1024];
    public double hi = -1e9, lo = 1e9;
}
