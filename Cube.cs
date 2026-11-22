namespace CubePlanet;

public struct Link { public int g; public bool acU, af, al; }

/// <summary>Cube-face axes (same as the original) and the face-to-face seam table.</summary>
public static class Cube
{
    public static readonly V3[] N = { new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1) };
    public static readonly V3[] U = { new(0, 0, -1), new(0, 0, 1), new(1, 0, 0), new(1, 0, 0), new(1, 0, 0), new(-1, 0, 0) };
    public static readonly V3[] V = { new(0, 1, 0), new(0, 1, 0), new(0, 0, -1), new(0, 0, 1), new(0, 1, 0), new(0, 1, 0) };
    public static readonly int[] DX = { 1, -1, 0, 0, 0, 0 }, DY = { 0, 0, 1, -1, 0, 0 }, DZ = { 0, 0, 0, 0, 1, -1 };
    // per face: 4 corners x (x,y,z) offsets of the quad for face direction fc
    public static readonly int[][] CN =
    {
        new[]{1,0,1, 1,0,0, 1,1,0, 1,1,1}, new[]{0,0,0, 0,0,1, 0,1,1, 0,1,0}, new[]{0,1,1, 1,1,1, 1,1,0, 0,1,0},
        new[]{0,0,0, 1,0,0, 1,0,1, 0,0,1}, new[]{0,0,1, 1,0,1, 1,1,1, 0,1,1}, new[]{1,0,0, 0,0,0, 0,1,0, 1,1,0}
    };
    public static readonly int[] CU = { 0, 1, 1, 0 }, CV = { 0, 0, 1, 1 };

    public static readonly Link[] LK = BuildLinks();

    static Link[] BuildLinks()
    {
        var lk = new Link[24];
        for (int f = 0; f < 6; f++)
            for (int s = 0; s < 4; s++)
            {
                V3 ax = s < 2 ? U[f] : V[f];
                double sg = s % 2 != 0 ? -1 : 1;
                V3 dir = ax * sg;
                int g = 0;
                for (int i = 0; i < 6; i++) if (M.Dot(N[i], dir) > .5) { g = i; break; }
                V3 al = s < 2 ? V[f] : U[f];
                double dU = M.Dot(U[g], N[f]), dV = M.Dot(V[g], N[f]);
                bool acU = Math.Abs(dU) > .5;
                double across = acU ? dU : dV;
                V3 other = acU ? V[g] : U[g];
                lk[f * 4 + s] = new Link { g = g, acU = acU, af = across > 0, al = M.Dot(other, al) < 0 };
            }
        return lk;
    }

    public static int FaceOf(double x, double y, double z)
    {
        double ax = Math.Abs(x), ay = Math.Abs(y), az = Math.Abs(z);
        if (ax >= ay && ax >= az) return x > 0 ? 0 : 1;
        if (ay >= az) return y > 0 ? 2 : 3;
        return z > 0 ? 4 : 5;
    }
}

/// <summary>Block ids and their property tables.</summary>
public static class Blocks
{
    // SPC = special-shaped (plants/vines/cocoa, non-solid); OPQ = hides neighbouring faces; TGR = transparent group (1 leaves, 2 glass, 3 ice)
    public static readonly byte[] SPC = new byte[256], OPQ = new byte[256], TGR = new byte[256];
    /// <summary>Underwater plants: special-shaped, non-solid, and they sit inside the water body (water faces next to them are hidden, swimming still works).</summary>
    public static readonly bool[] UWP = new bool[256];
    public static bool Wet(int id) => id == 9 || UWP[id];
    public static readonly bool[] LOGID = new bool[256], LEAFS = new bool[256];
    public static readonly string[] BN =
        ("air grass stone dirt oak_log oak_leaves ice sand snow water gravel sandstone red_sand terracotta orange_terracotta yellow_terracotta white_terracotta red_terracotta brown_terracotta jungle_grass savanna_grass taiga_grass swamp_grass spruce_leaves jungle_leaves acacia_leaves birch_leaves birch_log spruce_log jungle_log acacia_log oak_planks spruce_planks acacia_planks jungle_planks cobblestone mossy_cobblestone stone_bricks glass cut_sandstone cactus hay_bale red_sandstone tall_grass dry_grass fern dandelion poppy cornflower allium dead_bush vine cocoa red_mushroom brown_mushroom sugar_cane pumpkin melon coal_ore iron_ore gold_ore diamond_ore redstone_ore lapis_ore copper_ore emerald_ore deepslate deepslate_coal_ore deepslate_iron_ore deepslate_gold_ore deepslate_diamond_ore deepslate_redstone_ore deepslate_lapis_ore deepslate_copper_ore deepslate_emerald_ore lava lush_grass moss_block podzol packed_ice blue_ice clay coral_sand basalt volcanic_ash magma_block obsidian prismarine_bricks sea_lantern dead_coral_block cherry_log cherry_planks palm_log palm_planks cherry_leaves flowering_azalea_leaves azalea_leaves palm_leaves snowy_spruce_leaves tube_coral_block brain_coral_block bubble_coral_block fire_coral_block horn_coral_block pink_petals pink_tulip bluebell lavender bamboo sweet_berry_bush seagrass kelp sea_pickle tube_coral_fan brain_coral_fan bubble_coral_fan fire_coral_fan horn_coral_fan").Split(' ');

    static Blocks()
    {
        Array.Fill(OPQ, (byte)1);
        foreach (int i in new[] { 0, 9, 5, 23, 24, 25, 26, 38, 6 }) OPQ[i] = 0;
        for (int i = 43; i <= 55; i++) { SPC[i] = 1; OPQ[i] = 0; }
        foreach (int i in new[] { 5, 23, 24, 25, 26 }) { TGR[i] = 1; LEAFS[i] = true; }
        TGR[38] = 2; TGR[6] = 3;
        foreach (int i in new[] { 4, 27, 28, 29, 30 }) LOGID[i] = true;
        // biome blocks: leaves 94-98, land plants 104-109, underwater plants 110-117
        foreach (int i in new[] { 94, 95, 96, 97, 98 }) { OPQ[i] = 0; TGR[i] = 1; LEAFS[i] = true; }
        for (int i = 104; i <= 117; i++) { SPC[i] = 1; OPQ[i] = 0; }
        for (int i = 110; i <= 117; i++) UWP[i] = true;
        LOGID[90] = true; LOGID[92] = true;
    }
}
