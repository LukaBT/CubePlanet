namespace CubePlanet;

/// <summary>
/// Per-block lookup tables indexed by block ID (fast arrays for the mesher, generation and physics), built from the block definitions at startup.
/// Sized for every 2-byte ID, so any value found in chunk data can be looked up.
/// </summary>
public static partial class Blocks
{
    const int N = 65536;
    // SPC = special-shaped (plants/vines/cocoa, non-solid); OPQ = hides neighbouring faces; TGR = transparent group (1 leaves, 2 glass, 3 ice, 4+ other groups)
    public static readonly byte[] SPC = new byte[N], OPQ = new byte[N], TGR = new byte[N];
    /// <summary>Underwater plants: special-shaped, non-solid, and they sit inside the water body (water faces next to them are hidden, swimming still works).</summary>
    public static readonly bool[] UWP = new bool[N];
    public static readonly bool[] LOGID = new bool[N], LEAFS = new bool[N];
    /// <summary>Cross sprites placed with a random offset (false: sugar cane, bamboo, seagrass, kelp stand exactly in their cell).</summary>
    public static readonly bool[] JIT = new bool[N];
    /// <summary>Attached models: 1 = vine (planes on supporting sides), 2 = pod (small box on a log).</summary>
    public static readonly byte[] ATT = new byte[N];
    /// <summary>Self-lit blocks (lava, magma, sea lanterns): full light in the mesher, glow in the shader.</summary>
    public static readonly bool[] EMI = new bool[N];
    /// <summary>Texture layer of each block (one texture per block for now; per-face textures come with 3D block models).</summary>
    public static readonly int[] TEX = new int[N];
    /// <summary>Material flags the mesher writes into every vertex (bits 4+ of the packed vertex's second uint16), read by the shaders.</summary>
    public static readonly int[] MATF = new int[N];
    public static readonly int[][] LODC = new int[N][];
    public static string[] BN = { "air" };
    /// <summary>Highest block ID + 1.</summary>
    public static int Count = 1;

    public const int MatSwayLeaves = 1, MatSwayPlant = 2, MatSwayWater = 4, MatEmissive = 8, MatNoShadow = 16, MatIce = 32;
    static readonly int[] GrayLod = { 128, 128, 128 };

    public static bool Wet(int id) => id == Water || UWP[id];
    /// <summary>Colour of a surface block on the distant LOD terrain.</summary>
    public static int[] TileColor(int id) => LODC[id] ?? GrayLod;
    /// <summary>Texture layer + material flags as the mesher's float "layer" vertex component (layer in the low 16 bits, flags above).</summary>
    public static float Lay(int id) => TEX[id] | MATF[id] << 16;

    public static void Build(BlockRegistry reg)
    {
        Count = reg.Size; BN = new string[Count]; BN[0] = "air";
        Array.Fill(OPQ, (byte)1); OPQ[0] = 0; // unknown IDs act like stone (opaque, solid) and show the checkerboard texture
        var groups = new Dictionary<string, byte> { ["leaves"] = 1, ["glass"] = 2, ["ice"] = 3 };
        foreach (var b in reg.All)
        {
            int i = b.Id; BN[i] = b.IdName;
            OPQ[i] = (byte)(b.RenderLayer == "opaque" ? 1 : 0);
            SPC[i] = (byte)(b.Model == "cross" || b.Model == "attached" ? 1 : 0);
            if (b.ConnectGroup.Length > 0)
            {
                if (!groups.TryGetValue(b.ConnectGroup, out byte g)) groups[b.ConnectGroup] = g = (byte)Math.Min(255, groups.Count + 1);
                TGR[i] = g;
            }
            UWP[i] = b.Waterlogged; LOGID[i] = b.HasTag("log"); LEAFS[i] = b.HasTag("leaves");
            JIT[i] = b.RandomOffset; ATT[i] = (byte)(b.Attach == "vine" ? 1 : b.Attach == "pod" ? 2 : 0); EMI[i] = b.Emissive > 0;
            TEX[i] = b.Texture();
            MATF[i] = (b.Sway == "leaves" ? MatSwayLeaves : b.Sway == "plant" ? MatSwayPlant : b.Sway == "underwater" ? MatSwayWater : 0)
                    | (EMI[i] ? MatEmissive : 0) | (b.CastsShadow ? 0 : MatNoShadow) | (b.ConnectGroup == "ice" ? MatIce : 0);
            LODC[i] = b.LodColor;
        }
        for (int i = 1; i < Count; i++) BN[i] ??= "unknown_" + i;
        ResolveNames(reg);
    }
}
