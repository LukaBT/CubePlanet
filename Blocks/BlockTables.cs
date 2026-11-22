namespace CubePlanet;

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
