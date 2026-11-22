namespace CubePlanet;

/// <summary>
/// Block IDs the code refers to by name, resolved from block_definitions.txt at startup (an unknown name stops the game with a clear error).
/// Field name = id_name in PascalCase. Only blocks that code places or tests need a field; content-only blocks need none.
/// </summary>
public static partial class Blocks
{
    public static int Grass, Stone, Dirt, OakLog, OakLeaves, Ice, Sand, Snow, Water, Gravel, Sandstone, RedSand, Terracotta, OrangeTerracotta, YellowTerracotta;
    public static int WhiteTerracotta, RedTerracotta, BrownTerracotta, JungleGrass, SavannaGrass, TaigaGrass, SwampGrass, SpruceLeaves, JungleLeaves, AcaciaLeaves;
    public static int BirchLeaves, BirchLog, SpruceLog, JungleLog, AcaciaLog, OakPlanks, SprucePlanks, AcaciaPlanks, JunglePlanks, Cobblestone, MossyCobblestone;
    public static int StoneBricks, Glass, CutSandstone, Cactus, HayBale, RedSandstone, TallGrass, DryGrass, Fern, Dandelion, Poppy, Cornflower, Allium, DeadBush;
    public static int Vine, Cocoa, RedMushroom, BrownMushroom, SugarCane, Pumpkin, Melon, CoalOre, IronOre, GoldOre, DiamondOre, RedstoneOre, LapisOre, CopperOre;
    public static int EmeraldOre, Deepslate, DeepslateCoalOre, DeepslateIronOre, DeepslateGoldOre, DeepslateDiamondOre, DeepslateRedstoneOre, DeepslateLapisOre;
    public static int DeepslateCopperOre, DeepslateEmeraldOre, Lava, LushGrass, MossBlock, Podzol, PackedIce, BlueIce, Clay, CoralSand, Basalt, VolcanicAsh;
    public static int MagmaBlock, Obsidian, PrismarineBricks, SeaLantern, DeadCoralBlock, CherryLog, CherryPlanks, PalmLog, PalmPlanks, CherryLeaves, FloweringAzaleaLeaves;
    public static int AzaleaLeaves, PalmLeaves, SnowySpruceLeaves, TubeCoralBlock, BrainCoralBlock, BubbleCoralBlock, FireCoralBlock, HornCoralBlock, PinkPetals;
    public static int PinkTulip, Bluebell, Lavender, Bamboo, SweetBerryBush, Seagrass, Kelp, SeaPickle, TubeCoralFan, BrainCoralFan, BubbleCoralFan, FireCoralFan;
    public static int HornCoralFan;

    // box-model examples (phase 3)
    public static int OakSlab, StoneSlab, CobblestoneSlab, OakStairs, StoneBrickStairs, OakFence;
    // blocks with state and behaviours (phase 4)
    public static int OakDoor, RedBed, Chest, Furnace, OxygenGenerator;
    static void ResolveNames(BlockRegistry r)
    {
        OakSlab = r.Id("base:oak_slab"); StoneSlab = r.Id("base:stone_slab"); CobblestoneSlab = r.Id("base:cobblestone_slab"); OakStairs = r.Id("base:oak_stairs"); StoneBrickStairs = r.Id("base:stone_brick_stairs"); OakFence = r.Id("base:oak_fence");
        OakDoor = r.Id("base:oak_door"); RedBed = r.Id("base:red_bed"); Chest = r.Id("base:chest"); Furnace = r.Id("base:furnace"); OxygenGenerator = r.Id("base:oxygen_generator");
        Grass = r.Id("base:grass"); Stone = r.Id("base:stone"); Dirt = r.Id("base:dirt"); OakLog = r.Id("base:oak_log"); OakLeaves = r.Id("base:oak_leaves");
        Ice = r.Id("base:ice"); Sand = r.Id("base:sand"); Snow = r.Id("base:snow"); Water = r.Id("base:water"); Gravel = r.Id("base:gravel");
        Sandstone = r.Id("base:sandstone"); RedSand = r.Id("base:red_sand"); Terracotta = r.Id("base:terracotta"); OrangeTerracotta = r.Id("base:orange_terracotta");
        YellowTerracotta = r.Id("base:yellow_terracotta"); WhiteTerracotta = r.Id("base:white_terracotta"); RedTerracotta = r.Id("base:red_terracotta");
        BrownTerracotta = r.Id("base:brown_terracotta"); JungleGrass = r.Id("base:jungle_grass"); SavannaGrass = r.Id("base:savanna_grass");
        TaigaGrass = r.Id("base:taiga_grass"); SwampGrass = r.Id("base:swamp_grass"); SpruceLeaves = r.Id("base:spruce_leaves"); JungleLeaves = r.Id("base:jungle_leaves");
        AcaciaLeaves = r.Id("base:acacia_leaves"); BirchLeaves = r.Id("base:birch_leaves"); BirchLog = r.Id("base:birch_log"); SpruceLog = r.Id("base:spruce_log");
        JungleLog = r.Id("base:jungle_log"); AcaciaLog = r.Id("base:acacia_log"); OakPlanks = r.Id("base:oak_planks"); SprucePlanks = r.Id("base:spruce_planks");
        AcaciaPlanks = r.Id("base:acacia_planks"); JunglePlanks = r.Id("base:jungle_planks"); Cobblestone = r.Id("base:cobblestone"); MossyCobblestone = r.Id("base:mossy_cobblestone");
        StoneBricks = r.Id("base:stone_bricks"); Glass = r.Id("base:glass"); CutSandstone = r.Id("base:cut_sandstone"); Cactus = r.Id("base:cactus");
        HayBale = r.Id("base:hay_bale"); RedSandstone = r.Id("base:red_sandstone"); TallGrass = r.Id("base:tall_grass"); DryGrass = r.Id("base:dry_grass");
        Fern = r.Id("base:fern"); Dandelion = r.Id("base:dandelion"); Poppy = r.Id("base:poppy"); Cornflower = r.Id("base:cornflower"); Allium = r.Id("base:allium");
        DeadBush = r.Id("base:dead_bush"); Vine = r.Id("base:vine"); Cocoa = r.Id("base:cocoa"); RedMushroom = r.Id("base:red_mushroom"); BrownMushroom = r.Id("base:brown_mushroom");
        SugarCane = r.Id("base:sugar_cane"); Pumpkin = r.Id("base:pumpkin"); Melon = r.Id("base:melon"); CoalOre = r.Id("base:coal_ore"); IronOre = r.Id("base:iron_ore");
        GoldOre = r.Id("base:gold_ore"); DiamondOre = r.Id("base:diamond_ore"); RedstoneOre = r.Id("base:redstone_ore"); LapisOre = r.Id("base:lapis_ore");
        CopperOre = r.Id("base:copper_ore"); EmeraldOre = r.Id("base:emerald_ore"); Deepslate = r.Id("base:deepslate"); DeepslateCoalOre = r.Id("base:deepslate_coal_ore");
        DeepslateIronOre = r.Id("base:deepslate_iron_ore"); DeepslateGoldOre = r.Id("base:deepslate_gold_ore"); DeepslateDiamondOre = r.Id("base:deepslate_diamond_ore");
        DeepslateRedstoneOre = r.Id("base:deepslate_redstone_ore"); DeepslateLapisOre = r.Id("base:deepslate_lapis_ore"); DeepslateCopperOre = r.Id("base:deepslate_copper_ore");
        DeepslateEmeraldOre = r.Id("base:deepslate_emerald_ore"); Lava = r.Id("base:lava"); LushGrass = r.Id("base:lush_grass"); MossBlock = r.Id("base:moss_block");
        Podzol = r.Id("base:podzol"); PackedIce = r.Id("base:packed_ice"); BlueIce = r.Id("base:blue_ice"); Clay = r.Id("base:clay"); CoralSand = r.Id("base:coral_sand");
        Basalt = r.Id("base:basalt"); VolcanicAsh = r.Id("base:volcanic_ash"); MagmaBlock = r.Id("base:magma_block"); Obsidian = r.Id("base:obsidian");
        PrismarineBricks = r.Id("base:prismarine_bricks"); SeaLantern = r.Id("base:sea_lantern"); DeadCoralBlock = r.Id("base:dead_coral_block");
        CherryLog = r.Id("base:cherry_log"); CherryPlanks = r.Id("base:cherry_planks"); PalmLog = r.Id("base:palm_log"); PalmPlanks = r.Id("base:palm_planks");
        CherryLeaves = r.Id("base:cherry_leaves"); FloweringAzaleaLeaves = r.Id("base:flowering_azalea_leaves"); AzaleaLeaves = r.Id("base:azalea_leaves");
        PalmLeaves = r.Id("base:palm_leaves"); SnowySpruceLeaves = r.Id("base:snowy_spruce_leaves"); TubeCoralBlock = r.Id("base:tube_coral_block");
        BrainCoralBlock = r.Id("base:brain_coral_block"); BubbleCoralBlock = r.Id("base:bubble_coral_block"); FireCoralBlock = r.Id("base:fire_coral_block");
        HornCoralBlock = r.Id("base:horn_coral_block"); PinkPetals = r.Id("base:pink_petals"); PinkTulip = r.Id("base:pink_tulip"); Bluebell = r.Id("base:bluebell");
        Lavender = r.Id("base:lavender"); Bamboo = r.Id("base:bamboo"); SweetBerryBush = r.Id("base:sweet_berry_bush"); Seagrass = r.Id("base:seagrass");
        Kelp = r.Id("base:kelp"); SeaPickle = r.Id("base:sea_pickle"); TubeCoralFan = r.Id("base:tube_coral_fan"); BrainCoralFan = r.Id("base:brain_coral_fan");
        BubbleCoralFan = r.Id("base:bubble_coral_fan"); FireCoralFan = r.Id("base:fire_coral_fan"); HornCoralFan = r.Id("base:horn_coral_fan");
    }
}
