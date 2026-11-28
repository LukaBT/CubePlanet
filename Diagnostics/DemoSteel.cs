namespace CubePlanet;

public static partial class DemoWorld
{
    // ---------- --demosteel NAME [--demosecs S]: an automatic steel works, iron ore in, steel plates out ----------
    // One line along x (east), each machine handing its product to the next with an extractor and conveyor belts; coal generators under the
    // floor power everything (machines touching a generator, or each other, share its power):
    //   ore chest -> jaw crusher -> ore concentrator (water from a basin under it, kept full by a spring) -> blast furnace -> oxygen steel
    //   furnace -> plate rolling mill -> plate chest.
    // Beside the blast furnace: a coke oven (north, its hydrogen into a tank on top) and a second jaw crusher turning coral into limestone
    // (south), each with an extractor into the furnace. The oxygen furnace takes its oxygen from two gas tanks filled by an air separation unit.
    // Tailings and slag stay in the machines' output slots (the extractors only take what the next machine uses).
    public static int RunSteel(string name, double secs)
    {
        string folder = WorldSave.FolderFor(name);
        if (Directory.Exists(folder)) { Console.WriteLine($"DEMOWORLD a world folder '{folder}' already exists: not touching it"); return 1; }
        World.InitWorldData(1200, 1337); ChunkGenerator.mainTid = Environment.CurrentManagedThreadId;
        if (!FindSite()) { Console.WriteLine("DEMOWORLD no dry building site near spawn"); return 1; }
        int stone = Id("stone_bricks"); int It(string n) => GameAssets.ItemReg.Id(n);
        for (int y = -4; y <= 12; y++) for (int z = -8; z <= 12; z++) for (int x = -4; x <= 24; x++) Cell(x, y, z, y <= -1 ? stone : 0);
        // power: a coal generator under every block of the line (and under the side machines)
        void Gen(int x, int z) { Cell(x, -1, z, 0); Put(x, -1, z, "coal_generator", "north"); E(x, -1, z).Inv("fuel")[0] = new ItemStack(It("coal"), 64); }
        for (int x = 0; x <= 20; x++) Gen(x, 0);
        Gen(10, -1); Gen(10, 1); Gen(10, 2); Gen(14, -1);
        void Extractor(int x, int z, string facing) => Put(x, 0, z, "extractor", facing);
        void Belts(int xa, int xb) { for (int x = xa; x <= xb; x++) Put(x, 0, 0, "conveyor_belt", "east"); }
        // ore in
        Put(0, 0, 0, "chest", "south"); var ore = E(0, 0, 0).Inv("slots"); ore[0] = new ItemStack(It("iron_ore"), 64); ore[1] = new ItemStack(It("iron_ore"), 64);
        Extractor(1, 0, "east"); Put(2, 0, 0, "jaw_crusher", "south");
        Extractor(3, 0, "east"); Belts(4, 5);
        // concentrator: faces north so its back (south) takes water: a pipe down into a basin, a spring keeps the basin full
        Put(6, 0, 0, "ore_concentrator", "north"); Put(6, 0, 1, "liquid_pipe"); Cell(6, -1, 1, 0); Put(6, -1, 1, "liquid_pipe");
        for (int z = 1; z <= 3; z++) Cell(6, -2, z, Water, 8);
        Cell(6, -2, 4, 0); Put(6, -2, 4, "spring", "north"); E(6, -2, 4).SetNum("rate", 1);
        Extractor(7, 0, "east"); Belts(8, 9);
        // blast furnace, fed coke from the north and limestone from the south
        Put(10, 0, 0, "blast_furnace", "south");
        Put(10, 0, -2, "coke_oven", "south"); var co = E(10, 0, -2).Inv("input"); for (int k = 0; k < 3; k++) co[k] = new ItemStack(It("coal"), 64);
        Put(10, 1, -2, "gas_tank"); Extractor(10, -1, "south");
        Put(10, 0, 2, "jaw_crusher", "south"); var cr = E(10, 0, 2).Inv("input"); for (int k = 0; k < 3; k++) cr[k] = new ItemStack(It("dead_coral_block"), 64);
        Extractor(10, 1, "north");
        Extractor(11, 0, "east"); Belts(12, 13);
        // oxygen steel furnace: limestone in a slot, oxygen from two tanks on a gas pipe that the air separation unit fills
        Put(14, 0, 0, "oxygen_furnace", "south"); E(14, 0, 0).Inv("input")[0] = new ItemStack(It("crushed_limestone"), 64);
        Put(14, 1, 0, "gas_pipe"); Put(14, 1, -1, "gas_pipe"); Put(13, 1, 0, "gas_tank"); Put(15, 1, 0, "gas_tank");
        foreach (var t in new[] { E(13, 1, 0), E(15, 1, 0) }) t.SetNum("filter", Gas.Oxygen);
        Put(14, 0, -1, "air_separator");
        Extractor(15, 0, "east"); Belts(16, 17);
        // plates out
        Put(18, 0, 0, "plate_mill", "south"); Extractor(19, 0, "east"); Put(20, 0, 0, "chest", "south");
        // hotbar
        string[] give = { "multimeter", "jaw_crusher", "ore_concentrator", "coke_oven", "blast_furnace", "air_separator", "oxygen_furnace", "plate_mill", "steel_plate" };
        for (int i = 0; i < give.Length; i++) PlayerInventory.Hotbar[i] = new ItemStack(It(give[i]), give[i] == "multimeter" ? 1 : 64);
        PlayerInventory.Selected = 0; WaterSim.WAct.Clear(); WaterSim.wactQ.Clear();
        int ticks = (int)(secs * 10);
        for (int i = 0; i < ticks; i++) { BlockActions.Tick(.1); WaterSim.WaterStep(); } // water flows too (the basin refills the cell under the pipe)
        int Count(int x, int y, int z, string inv, string item) => E(x, y, z).Inv(inv)?.Where(s => s != null && !s.IsEmpty && s.Item == It(item)).Sum(s => s.Count) ?? 0;
        string report = $"STEELDEMO after {secs:0} s: ore left {Count(0, 0, 0, "slots", "iron_ore")}, crusher batches {E(2, 0, 0).Num("batches"):0}, concentrator {E(6, 0, 0).Num("batches"):0} " +
                        $"(tailings {Count(6, 0, 0, "output", "tailings")}), coke oven {E(10, 0, -2).Num("batches"):0} (hydrogen {E(10, 1, -2).Num("mol"):0} mol), limestone crusher {E(10, 0, 2).Num("batches"):0}, " +
                        $"blast furnace {E(10, 0, 0).Num("batches"):0} (slag {Count(10, 0, 0, "output", "slag")}), oxygen {E(13, 1, 0).Num("mol") + E(15, 1, 0).Num("mol"):0} mol in the tanks, " +
                        $"oxygen furnace {E(14, 0, 0).Num("batches"):0}, plate mill {E(18, 0, 0).Num("batches"):0}, plates in the chest {Count(20, 0, 0, "slots", "steel_plate")}; " +
                        $"status: crusher {E(2, 0, 0).Num("status")}, concentrator {E(6, 0, 0).Num("status")}, blast {E(10, 0, 0).Num("status")}, steel {E(14, 0, 0).Num("status")}, mill {E(18, 0, 0).Num("status")}";
        string Inv(int x, int y, int z, string k) => string.Join(",", E(x, y, z).Inv(k).Select(s => s?.ToString() ?? "-"));
        report += $"\n  blast in [{Inv(10, 0, 0, "input")}] out [{Inv(10, 0, 0, "output")}]; concentrator out [{Inv(6, 0, 0, "output")}]; extractors: {BEH[E(7, 0, 0).BlockId].Describe(E(7, 0, 0))} | {BEH[E(10, 0, -1).BlockId].Describe(E(10, 0, -1))} | {BEH[E(10, 0, 1).BlockId].Describe(E(10, 0, 1))}";
        return Finish(folder, name, report, 10, 7, 10, 0, 0, false);
    }
}
