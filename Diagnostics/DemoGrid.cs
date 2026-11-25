namespace CubePlanet;

public static partial class DemoWorld
{
    // ---------- --demogrid NAME [--demosecs S]: a power line from a wind farm to a town, and an LV line beside it for comparison ----------
    // Plant (x 0..28): large, medium and small wind turbines facing north on towers; their cables join a ground cable (z 4) to a transformer
    // LV-MV (x 29, front east). MV: a cable to a wire connector (x 31), wires up to 3 pylons (x 40, 70, 100) and down to a connector (x 109),
    // a transformer back to LV (x 111, front west) and the town (x 112..116): batteries and an oxygen generator.
    // Beside it (z 12): 4 coal generators with 110 blocks of plain LV copper to a battery at x 113: it arrives with big losses (HUD on a cable).
    public static int RunGrid(string name, double secs)
    {
        string folder = WorldSave.FolderFor(name);
        if (Directory.Exists(folder)) { Console.WriteLine($"DEMOWORLD a world folder '{folder}' already exists: not touching it"); return 1; }
        World.InitWorldData(1200, 1337); ChunkGenerator.mainTid = Environment.CurrentManagedThreadId;
        if (!FindSite()) { Console.WriteLine("DEMOWORLD no dry building site near spawn"); return 1; }
        Band Bd = bands[bi];
        for (int cy = (y0 - 6) >> 5; cy <= (y0 + 34) >> 5; cy++) for (int cz = (z0 - 18) >> 5; cz <= (z0 + 22) >> 5; cz++) for (int cx = (x0 - 10) >> 5; cx <= (x0 + 130) >> 5; cx++)
                {
                    if (cx < 0 || cz < 0 || cy < 0 || cx >= Bd.fc || cz >= Bd.fc || cy >= Bd.rows / CS) continue;
                    long k = ck(bi, f, cx, cy, cz);
                    if (!chunks.TryGetValue(k, out var ch)) { ch = new Chunk { key = k, band = bi, face = f, cx = cx, cy = cy, cz = cz }; AddChunk(ch); }
                    ChunkGenerator.Ensure(ch);
                }
        int stone = Id("stone_bricks");
        for (int y = -1; y <= 30; y++) for (int z = -16; z <= 16; z++) for (int x = -6; x <= 122; x++) Cell(x, y, z, y == -1 ? stone : 0);
        void Tower(int x, string block)
        {
            for (int y = 0; y <= 13; y++) Cell(x, y, 0, stone);
            Put(x, 14, 0, block, "north"); Put(x, 14, 1, "shaft", "north"); Put(x, 14, 2, "generator", "north");
            for (int y = 13; y >= 0; y--) Put(x, y, 2, "copper_cable");
            Put(x, 0, 3, "copper_cable");
        }
        Tower(0, "wind_turbine_large"); Tower(14, "wind_turbine_medium"); Tower(24, "wind_turbine");
        for (int x = 0; x <= 28; x++) Put(x, 0, 4, "gold_cable");
        // up to MV, along wires over the pylons, back down to LV
        Put(29, 0, 4, "transformer_lv_mv", "east"); Put(30, 0, 4, "copper_cable"); Put(31, 0, 4, "wire_connector");
        foreach (int x in new[] { 40, 70, 100 }) BlockActions.Place(At(x, 0, 4), Id("pylon"), new V3(0, 0, 1), out _);
        Put(109, 0, 4, "wire_connector"); Put(110, 0, 4, "copper_cable"); Put(111, 0, 4, "transformer_lv_mv", "west");
        int[] anchors = { 31, 40, 70, 100, 109 }; int strung = 0;
        for (int i = 0; i + 1 < anchors.Length; i++)
        {
            BlockPos A(int x) => x == 31 || x == 109 ? At(x, 0, 4) : At(x, 4, 4);
            if (Wires.Connect(A(anchors[i]), A(anchors[i + 1]))) strung++;
        }
        Put(112, 0, 4, "gold_cable"); Put(113, 0, 4, "battery"); Put(114, 0, 4, "battery"); Put(115, 0, 4, "battery"); Put(116, 0, 4, "oxygen_generator", "north");
        // comparison: coal power over 110 blocks of LV copper
        int coal = GameAssets.ItemReg.Id("coal");
        for (int x = 0; x <= 3; x++) { Put(x, 0, 12, "coal_generator", "north"); E(x, 0, 12).Inv("fuel")[0] = new ItemStack(coal, 32); }
        for (int x = 4; x <= 112; x++) Put(x, 0, 12, "copper_cable");
        Put(113, 0, 12, "battery"); Put(114, 0, 12, "battery"); Put(115, 0, 12, "battery");
        string[] give = { "transformer_lv_mv", "transformer_mv_hv", "pylon", "wire_connector", "wire_spool", "copper_cable", "gold_cable", "battery", "wind_turbine_large" };
        for (int i = 0; i < give.Length; i++) PlayerInventory.Hotbar[i] = new ItemStack(GameAssets.ItemReg.Id(give[i]), give[i] == "wire_spool" ? 1 : 64);
        PlayerInventory.Selected = 4;
        Kinetics.Clock = 300; // wind from about WNW: the north-facing turbines catch it
        int ticks = (int)(secs * 10);
        for (int i = 0; i < ticks; i++) BlockActions.Tick(.1);
        double town = E(113, 0, 4).Num("energy") + E(114, 0, 4).Num("energy") + E(115, 0, 4).Num("energy");
        var lvMid = EnergyNet.CableLoad.TryGetValue(At(60, 0, 12).Key, out var l) ? l : default;
        double lvLoss = 0; for (int x = 4; x <= 112; x++) if (EnergyNet.CableLoad.TryGetValue(At(x, 0, 12).Key, out var lx)) lvLoss += lx.Loss;
        double wireLoss = Wires.Links.Sum(w => w.LossNow);
        string report = $"GRIDDEMO after {secs:0} s (wind {Kinetics.WindSpeed:0.0} m/s from {Kinetics.WindFrom}): turbines {E(0, 14, 0).Num("power"):0.0} + {E(14, 14, 0).Num("power"):0.0} + {E(24, 14, 0).Num("power"):0.0} E/s; " +
                        $"up-transformer {E(29, 0, 4).Num("flow"):0.0} E/s (ok {E(29, 0, 4).Num("ok")}), {strung} wires at {Voltage.Name(Wires.Links.FirstOrDefault()?.Level ?? 0)} carrying {Wires.Links.FirstOrDefault()?.Flow:0.0} E/s, losing {wireLoss:0.000} E/s in all; " +
                        $"town batteries {town:0} E; LV coal line: {lvMid.Flow:0.0} E/s in the middle, {lvLoss:0.0} E/s lost on the way, its batteries {E(113, 0, 12).Num("energy") + E(114, 0, 12).Num("energy") + E(115, 0, 12).Num("energy"):0} E";
        return Finish(folder, name, report, 60, -12, 60, 4, 4, false);
    }
}
