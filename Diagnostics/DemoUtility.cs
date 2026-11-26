namespace CubePlanet;

public static partial class DemoWorld
{
    // ---------- --demoutility NAME [--demosecs S]: the machines that use energy ----------
    // A coal plant (11 generators) and a battery bank with a grid status lamp feed a gold cable along z -11. On it: a charger with a drill;
    // an electrolyzer (water from a basin) filling a hydrogen tank, a fuel cell on the tank powering an oxygen generator; a greenhouse (wheat on
    // farmland, a sprinkler over a water cell, two grow lights); an automatic miner with a chest, an extractor sending what it digs along item
    // pipes to an electric furnace (and a chest for the rest); an elevator tower 12 high. The hotbar has the tools (charged) and blocks.
    public static int RunUtility(string name, double secs)
    {
        string folder = WorldSave.FolderFor(name);
        if (Directory.Exists(folder)) { Console.WriteLine($"DEMOWORLD a world folder '{folder}' already exists: not touching it"); return 1; }
        World.InitWorldData(1200, 1337); ChunkGenerator.mainTid = Environment.CurrentManagedThreadId;
        if (!FindSite()) { Console.WriteLine("DEMOWORLD no dry building site near spawn"); return 1; }
        int stone = Id("stone_bricks");
        for (int y = -3; y <= 30; y++) for (int z = -16; z <= 16; z++) for (int x = -6; x <= 44; x++) Cell(x, y, z, y <= -1 ? stone : 0);
        int coal = GameAssets.ItemReg.Id("coal");
        // power
        for (int x = -5; x <= 5; x++) { Put(x, 0, -12, "coal_generator", "north"); E(x, 0, -12).Inv("fuel")[0] = new ItemStack(coal, 64); }
        for (int x = -5; x <= 40; x++) Put(x, 0, -11, "gold_cable");
        Put(6, 0, -12, "battery"); Put(7, 0, -12, "battery"); Put(8, 0, -12, "battery"); Put(9, 1, -11, "status_lamp");
        void Column(int x, int z0b, int z1b) { for (int z = z0b; z <= z1b; z++) Put(x, 0, z, "gold_cable"); }
        // charger
        Column(2, -10, -9); Put(2, 0, -8, "charger"); E(2, 0, -8).Inv("slot")[0] = new ItemStack(GameAssets.ItemReg.Id("electric_drill"), 1);
        // hydrogen: a water basin under the electrolyzer
        for (int z = -1; z <= 1; z++) for (int x = 11; x <= 13; x++) Cell(x, -1, z, Water, 8);
        Column(12, -10, -2); Put(12, 0, -1, "gold_cable"); Put(12, 0, 0, "electrolyzer", "up"); Put(13, 0, 0, "hydrogen_tank"); Put(13, 1, 0, "fuel_cell"); Put(13, 2, 0, "oxygen_generator", "north");
        // greenhouse
        for (int z = 0; z <= 4; z++) for (int x = 18; x <= 22; x++)
            {
                if (x == 20 && z == 2) { Cell(x, 0, z, Water, 8); Cell(x, -1, z, Water, 8); continue; }
                Cell(x, -1, z, Id("dirt")); Cell(x, 0, z, Id("dirt")); Farming.Till(At(x, 0, z)); Put(x, 1, z, "wheat");
            }
        Put(20, 1, 2, "sprinkler", "up"); Put(20, -2, 2, "spring", "up"); E(20, -2, 2).SetNum("rate", 4); // a spring keeps its water cell full
        Column(20, -10, -1); for (int y = 1; y <= 1; y++) Put(20, 0, -1, "gold_cable"); Put(19, 1, -1, "gold_cable"); Put(20, 1, -1, "gold_cable"); Put(20, 1, 0, "gold_cable"); Put(20, 1, 1, "gold_cable"); // to the sprinkler
        foreach (int x in new[] { 17, 23 }) { Column(x, -10, 2); for (int y = 1; y <= 3; y++) Put(x, y, 2, "gold_cable"); Put(x, 4, 2, "grow_light"); E(x, 4, 2).SetNum("mode", 1); }
        // miner and the item line
        Column(30, -10, 4); Put(30, 0, 5, "miner"); Put(31, 0, 5, "chest", "north");
        Put(32, 0, 5, "extractor", "east"); Put(31, 0, 4, "gold_cable"); Put(32, 0, 4, "gold_cable"); Put(33, 0, 5, "item_pipe"); Put(34, 0, 5, "conveyor_belt", "north"); Put(35, 0, 5, "item_pipe");
        Put(36, 0, 5, "electric_furnace", "north"); Put(35, 0, 6, "chest", "north"); for (int z = -10; z <= 4; z++) Put(36, 0, z, "gold_cable");
        // elevator tower
        for (int z = -7; z <= -5; z++) for (int x = 39; x <= 41; x++) if (!(x == 40 && z == -6)) Cell(x, 11, z, stone);
        Put(40, 0, -6, "elevator"); Put(40, 12, -6, "elevator"); for (int z = -10; z <= -7; z++) Put(40, 0, z, "gold_cable"); for (int y = 1; y <= 12; y++) Put(41, y, -6, "gold_cable"); Put(41, 0, -6, "gold_cable"); Put(41, 0, -7, "gold_cable");
        // hotbar
        var drillItem = new ItemStack(GameAssets.ItemReg.Id("electric_drill"), 1); Tools.SetCharge(drillItem, 4000);
        var sawItem = new ItemStack(GameAssets.ItemReg.Id("chainsaw"), 1); Tools.SetCharge(sawItem, 4000);
        PlayerInventory.Hotbar[0] = drillItem; PlayerInventory.Hotbar[1] = sawItem;
        string[] give = { "multimeter", "miner", "elevator", "item_pipe", "extractor", "electrolyzer", "hydrogen_tank" };
        for (int i = 0; i < give.Length; i++) PlayerInventory.Hotbar[i + 2] = new ItemStack(GameAssets.ItemReg.Id(give[i]), give[i] == "multimeter" ? 1 : 64);
        PlayerInventory.Selected = 0; WaterSim.WAct.Clear(); WaterSim.wactQ.Clear();
        int ticks = (int)(secs * 10);
        for (int i = 0; i < ticks; i++) BlockActions.Tick(.1);
        int Count(ItemStack[] inv) => inv?.Where(s => s != null && !s.IsEmpty).Sum(s => s.Count) ?? 0;
        string report = $"UTILITYDEMO after {secs:0} s: miner {E(30, 0, 5).Num("mined"):0} blocks ({Count(E(31, 0, 5).Inv("slots"))} in its chest, {E(32, 0, 5).Num("moved"):0} sent on, furnace made {E(36, 0, 5).Inv("output")[0]?.Count ?? 0}, overflow chest {Count(E(35, 0, 6).Inv("slots"))}); " +
                        $"hydrogen {E(12, 0, 0).Num("made_kg") * 1000:0} g made, tank {Hydrogen.Bar(E(13, 0, 0)):0.00} bar, oxygen generator {E(13, 2, 0).Num("energy"):0} E; drill in the charger {Tools.Charge(E(2, 0, -8).Inv("slot")[0])} E; " +
                        $"sprinkler watered {E(20, 1, 2).Num("watered"):0}; grow lights lit {GrowLights.Lit.Count}; elevators {E(40, 0, -6).Num("energy"):0} / {E(40, 12, -6).Num("energy"):0} E; status lamp level {E(9, 1, -11).Num("level")}";
        return Finish(folder, name, report, 20, -15, 20, 2, 0, false);
    }
}
