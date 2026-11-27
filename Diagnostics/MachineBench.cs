namespace CubePlanet;

public static partial class DemoWorld
{
    /// <summary>
    /// --benchmachines (no window): builds N copies of a small factory on a stone floor and times the machine tick (energy networks, machines,
    /// conveyors, pumps; 10 a second) and the water simulation. One module (7 x 7): a coal generator, 4 gold cables, a battery, an electric
    /// furnace smelting iron, a lamp; a chest -> extractor -> 3 conveyor belts -> chest line; a pump lifting water from one closed basin through
    /// 6 pipes into another. Prints ms per tick for 64, 256 and 1024 modules.
    /// </summary>
    public static void BenchMachines()
    {
        World.InitWorldData(1200, 1337); ChunkGenerator.mainTid = Environment.CurrentManagedThreadId;
        if (!FindSite()) { Console.WriteLine("BENCHMACHINES no site"); return; }
        int stone = Id("stone_bricks"), coal = GameAssets.ItemReg.Id("coal"), ore = GameAssets.ItemReg.Id("iron_ore"), cob = GameAssets.ItemReg.Id("cobblestone");
        const int Side = 32, Step = 7; int span = Side * Step;
        Band B = bands[bi];
        for (int cy = (y0 - 8) >> 5; cy <= (y0 + 10) >> 5; cy++) for (int cz = (z0 - 4) >> 5; cz <= (z0 + span + 4) >> 5; cz++) for (int cx = (x0 - 4) >> 5; cx <= (x0 + span + 4) >> 5; cx++)
                {
                    if (cx < 0 || cz < 0 || cy < 0 || cx >= B.fc || cz >= B.fc) continue;
                    long k = ck(bi, f, cx, cy, cz); if (!chunks.TryGetValue(k, out var ch)) { ch = new Chunk { key = k, band = bi, face = f, cx = cx, cy = cy, cz = cz }; AddChunk(ch); }
                    ChunkGenerator.Ensure(ch);
                }
        for (int z = -2; z < span + 2; z++) for (int x = -2; x < span + 2; x++) { for (int y = -4; y <= -1; y++) Cell(x, y, z, stone); for (int y = 0; y <= 8; y++) Cell(x, y, z, 0); }
        int built = 0; var sw = new System.Diagnostics.Stopwatch();
        void Module(int mx, int mz)
        {
            int x = mx * Step, z = mz * Step;
            Put(x, 0, z, "coal_generator", "north"); E(x, 0, z).Inv("fuel")[0] = new ItemStack(coal, 64);
            for (int i = 1; i <= 4; i++) Put(x + i, 0, z, "gold_cable");
            Put(x + 5, 0, z, "battery"); Put(x + 4, 0, z + 1, "electric_furnace", "north"); E(x + 4, 0, z + 1).Inv("input")[0] = new ItemStack(ore, 64);
            Put(x + 3, 1, z, "electric_lamp"); if (BlockStates.entities.TryGetValue(At(x + 3, 1, z).Key, out var lp)) lp.SetNum("mode", 1);
            Put(x, 0, z + 3, "chest", "north"); E(x, 0, z + 3).Inv("slots")[0] = new ItemStack(cob, 64);
            Put(x + 1, 0, z + 3, "extractor", "east"); Put(x + 1, 0, z + 2, "gold_cable"); Put(x + 1, 0, z + 1, "gold_cable"); Put(x + 2, 0, z + 1, "gold_cable"); Put(x + 3, 0, z + 1, "gold_cable");
            for (int i = 2; i <= 4; i++) Put(x + i, 0, z + 3, "conveyor_belt", "east");
            Put(x + 5, 0, z + 3, "chest", "north");
            // pump: water basin (x, -1, z+5) -> pump (x, 0, z+5) facing up -> pipes up and over -> into a closed basin at (x+4, -1, z+5)
            Cell(x, -1, z + 5, Water, 8); Cell(x + 4, -1, z + 5, 0);
            Put(x, 0, z + 5, "liquid_pump", "up"); E(x, 0, z + 5).SetNum("mode", 1);
            Put(x, 1, z + 5, "liquid_pipe"); for (int i = 1; i <= 4; i++) Put(x + i, 1, z + 5, "liquid_pipe"); Put(x + 4, 0, z + 5, "liquid_pipe");
            Put(x + 1, 0, z + 5, "gold_cable"); Put(x + 1, 0, z + 4, "gold_cable"); // power from the extractor line
            built++;
        }
        WaterSim.WAct.Clear(); WaterSim.wactQ.Clear();
        foreach (int n in new[] { 64, 256, 1024 })
        {
            int side = (int)Math.Round(Math.Sqrt(n));
            for (int mz = 0; mz < side; mz++) for (int mx = 0; mx < side; mx++) if (mz * Side + mx >= 0 && !(mx < Prev(n) && mz < Prev(n))) Module(mx, mz);
            for (int i = 0; i < 30; i++) { BlockActions.Tick(.1); WaterSim.WaterStep(); } // warm up (networks form, JIT)
            double tick = 0, water = 0, worst = 0; int T = 50;
            for (int i = 0; i < T; i++)
            {
                sw.Restart(); BlockActions.Tick(.1); double a = sw.Elapsed.TotalMilliseconds; sw.Restart(); WaterSim.WaterStep(); double b = sw.Elapsed.TotalMilliseconds;
                tick += a; water += b; worst = Math.Max(worst, a + b);
            }
            int ents = BlockStates.entities.Count, nets = EnergyNet.NetOf.Values.Distinct().Count();
            Console.WriteLine($"BENCHMACHINES {built} modules: {ents} machines, {nets} power networks; machine tick {tick / T:0.00} ms, water {water / T:0.00} ms, worst {worst:0.0} ms (10 ticks a second: {(tick + water) / T * 10 / 10:0.0} % of one core)");
        }
        static int Prev(int n) => n == 64 ? 0 : n == 256 ? 8 : 16;
    }
}
