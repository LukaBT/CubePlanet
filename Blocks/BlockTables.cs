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
    /// <summary>Texture layer of each block for sprites and icons (its "side" texture, else "all").</summary>
    public static readonly int[] TEX = new int[N];
    /// <summary>Texture of each face: TEXF[id * 6 + face], faces in the mesher order +x, -x, +y (top), -y, +z, -z.</summary>
    public static readonly int[] TEXF = new int[N * 6];
    public const byte ModelCube = 0, ModelCross = 1, ModelLiquid = 2, ModelAttached = 3, ModelBoxes = 4;
    public static readonly byte[] MODEL = new byte[N];
    /// <summary>Boxes of "boxes" models (with their face textures).</summary>
    public static readonly BlockDefinition.ModelBox[][] MBOX = new BlockDefinition.ModelBox[N][];
    /// <summary>Collision / selection boxes: null = full block, empty = none. Unknown IDs collide and select like a full block; air has neither.</summary>
    public static readonly BlockBox[][] COLL = new BlockBox[N][], SEL = new BlockBox[N][];
    /// <summary>Definitions by ID (null for unknown IDs).</summary>
    public static readonly BlockDefinition[] DEFS = new BlockDefinition[N];
    /// <summary>Blocks whose look / geometry depends on their state word (rotation, state_overrides): use <see cref="Variant"/>.</summary>
    public static readonly bool[] HASVAR = new bool[N];
    /// <summary>Blocks with collision boxes above y = 1 (fences): they also block the lower part of the cell above. AnyTall: some block has them.</summary>
    public static readonly bool[] TALL = new bool[N]; public static bool AnyTall;
    static readonly BlockVariant[][] VARS = new BlockVariant[N][];
    /// <summary>Behaviour of each block (null = none).</summary>
    public static readonly BlockBehavior[] BEH = new BlockBehavior[N];
    /// <summary>Material flags the mesher writes into every vertex (4th byte of the packed normal), read by the shaders.</summary>
    public static readonly int[] MATF = new int[N];
    public static readonly int[][] LODC = new int[N][];
    public static string[] BN = { "air" };
    /// <summary>Highest block ID + 1.</summary>
    public static int Count = 1;

    public const int MatSwayLeaves = 1, MatSwayPlant = 2, MatSwayWater = 4, MatEmissive = 8, MatNoShadow = 16, MatIce = 32;
    static readonly int[] GrayLod = { 128, 128, 128 };

    /// <summary>Liquids (model "liquid" with a "level" state: water, lava, oil, liquid oxygen): the liquid simulation moves them.</summary>
    public static readonly bool[] LIQ = new bool[N];
    /// <summary>Per liquid (definition key "liquid"): density kg/m^3 [1000], viscosity = it moves every n-th tick [1], boil = units a second that
    /// boil off an open surface [0].</summary>
    public static readonly double[] LiquidDensity = new double[N], LiquidBoil = new double[N]; public static readonly int[] LiquidVisc = new int[N];
    public static bool Wet(int id) => LIQ[id] || UWP[id];
    /// <summary>Colour of a surface block on the distant LOD terrain.</summary>
    public static int[] TileColor(int id) => LODC[id] ?? GrayLod;
    /// <summary>Texture layer + material flags as the mesher's float "layer" vertex component (layer in the low 16 bits, flags above).</summary>
    public static float Lay(int id) => TEX[id] | MATF[id] << 16;
    /// <summary>Same for one face of a cube.</summary>
    public static float Lay(int id, int face) => TEXF[id * 6 + face] | MATF[id] << 16;
    /// <summary>Same for an explicit texture (box faces).</summary>
    public static float LayTex(int id, int tex) => tex | MATF[id] << 16;
    /// <summary>Look and geometry of a block in a given state (cached per state word; thread-safe).</summary>
    public static BlockVariant Variant(int id, int state)
    {
        var a = VARS[id]; if (a == null) return null;
        state &= a.Length - 1;
        return a[state] ??= BlockVariant.Compute(DEFS[id], (uint)state);
    }
    public static BlockBox[] CollisionOf(int id, int state) => HASVAR[id] ? Variant(id, state).Collision : COLL[id];
    public static BlockBox[] SelectionOf(int id, int state) => HASVAR[id] ? Variant(id, state).Selection : SEL[id];
    static bool In(BlockBox[] c, double x, double y, double z) { if (c == null) return true; foreach (var b in c) if (b.Contains(x, y, z)) return true; return false; }
    /// <summary>Is the block-local point (0..1 in each axis) inside the block's collision boxes?</summary>
    public static bool Collides(int id, double x, double y, double z) => In(COLL[id], x, y, z);
    public static bool Collides(int id, int state, double x, double y, double z) => In(CollisionOf(id, state), x, y, z);
    /// <summary>Is the block-local point inside the block's selection boxes?</summary>
    public static bool Selects(int id, double x, double y, double z) => In(SEL[id], x, y, z);
    public static bool Selects(int id, int state, double x, double y, double z) => In(SelectionOf(id, state), x, y, z);

    public static void Build(BlockRegistry reg)
    {
        Count = reg.Size; BN = new string[Count]; BN[0] = "air";
        Array.Fill(OPQ, (byte)1); OPQ[0] = 0; // unknown IDs act like stone (opaque, solid) and show the checkerboard texture
        COLL[0] = SEL[0] = Array.Empty<BlockBox>();
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
            TEX[i] = b.Texture("side");
            for (int fc = 0; fc < 6; fc++) TEXF[i * 6 + fc] = b.Faces[fc];
            MODEL[i] = b.Model switch { "cross" => ModelCross, "liquid" => ModelLiquid, "attached" => ModelAttached, "boxes" => ModelBoxes, _ => ModelCube };
            MBOX[i] = b.Model == "boxes" ? b.Boxes : null;
            COLL[i] = b.Collision; SEL[i] = b.Selection; DEFS[i] = b;
            HASVAR[i] = b.HasVariants;
            TALL[i] = (b.Collision?.Any(c => c.Y1 > 1) ?? false) || b.Overrides.Any(o => o.Collision?.Any(c => c.Y1 > 1) ?? false); AnyTall |= TALL[i];
            BEH[i] = b.Behavior.Length > 0 ? BlockBehavior.Create(b.Behavior, $"{AssetPaths.BlockDefinitions}: block {b.FullName}") : null;
            if (HASVAR[i])
            {   // the default state (word 0) also fills the plain tables, so code that ignores state still sees the default look
                VARS[i] = new BlockVariant[1 << Math.Max(1, b.State.Bits)];
                var v0 = Variant(i, 0); MBOX[i] = b.Model == "boxes" ? v0.Boxes : null; COLL[i] = v0.Collision; SEL[i] = v0.Selection;
                for (int fc = 0; fc < 6; fc++) TEXF[i * 6 + fc] = v0.Faces[fc];
            }
            MATF[i] = (b.Sway == "leaves" ? MatSwayLeaves : b.Sway == "plant" ? MatSwayPlant : b.Sway == "underwater" ? MatSwayWater : 0)
                    | (EMI[i] ? MatEmissive : 0) | (b.CastsShadow ? 0 : MatNoShadow) | (b.ConnectGroup == "ice" ? MatIce : 0);
            LODC[i] = b.LodColor;
            LIQ[i] = b.Model == "liquid" && b.State.Find("level") != null;
            LiquidDensity[i] = Electric.Key(null, i, "liquid", "density", 1000); LiquidVisc[i] = Math.Max(1, (int)Electric.Key(null, i, "liquid", "viscosity", 1)); LiquidBoil[i] = Electric.Key(null, i, "liquid", "boil", 0);
        }
        for (int i = 1; i < Count; i++) BN[i] ??= "unknown_" + i;
        ResolveNames(reg);
    }
}
