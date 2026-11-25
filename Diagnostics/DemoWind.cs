namespace CubePlanet;

public static partial class DemoWorld
{
    // ---------- --demowind NAME [--demosecs S]: a wind farm and a coal plant ----------
    // Every turbine stands on a stone tower (hub 14 up), with its shaft and generator behind it and a cable down from the generator to a
    // ground cable (z 4) feeding 3 batteries and an oxygen generator (x 10..13, z 5):
    //   large (11 blocks) at x 0 and medium (7) at x 14 facing north, small ones facing north (x 24), east (x 34), south (x 30, z 12) and
    //   west (x 40, z 8). The wind turns slowly, so which turbines make the most changes over time (HUD: wind speed, from, % into the blades).
    // Coal plant (x 20..22, z -8): a coal generator with 16 coal, a cable and a battery: it burns while the battery has room.
    public static int RunWind(string name, double secs)
    {
        string folder = WorldSave.FolderFor(name);
        if (Directory.Exists(folder)) { Console.WriteLine($"DEMOWORLD a world folder '{folder}' already exists: not touching it"); return 1; }
        World.InitWorldData(1200, 1337); ChunkGenerator.mainTid = Environment.CurrentManagedThreadId;
        if (!FindSite()) { Console.WriteLine("DEMOWORLD no dry building site near spawn"); return 1; }
        int stone = Id("stone_bricks");
        for (int y = -1; y <= 30; y++) for (int z = -16; z <= 16; z++) for (int x = -6; x <= 44; x++) Cell(x, y, z, y == -1 ? stone : 0);
        var turbines = new List<(int x, int z, string block, string facing)>();
        void Tower(int x, int z, string block, string facing)
        {
            var (fx, fz) = facing switch { "north" => (0, -1), "south" => (0, 1), "east" => (1, 0), _ => (-1, 0) };
            for (int y = 0; y <= 13; y++) Cell(x, y, z, stone);
            Put(x, 14, z, block, facing); Put(x - fx, 14, z - fz, "shaft", facing); Put(x - 2 * fx, 14, z - 2 * fz, "generator", facing);
            for (int y = 13; y >= 0; y--) Put(x - 2 * fx, y, z - 2 * fz, "copper_cable");
            turbines.Add((x, z, block, facing));
        }
        Tower(0, 0, "wind_turbine_large", "north"); Tower(14, 0, "wind_turbine_medium", "north"); Tower(24, 0, "wind_turbine", "north");
        Tower(34, 0, "wind_turbine", "east"); Tower(30, 12, "wind_turbine", "south"); Tower(40, 8, "wind_turbine", "west");
        // ground cables: a line along z 4 and branches to each tower's cable
        for (int x = 0; x <= 42; x++) Put(x, 0, 4, "copper_cable");
        foreach (int x in new[] { 0, 14, 24 }) Put(x, 0, 3, "copper_cable");
        for (int z = 1; z <= 3; z++) Put(32, 0, z, "copper_cable");
        for (int z = 5; z <= 9; z++) Put(30, 0, z, "copper_cable");
        for (int z = 5; z <= 7; z++) Put(42, 0, z, "copper_cable");
        Put(10, 0, 5, "battery"); Put(11, 0, 5, "battery"); Put(12, 0, 5, "battery"); Put(13, 0, 5, "oxygen_generator", "north");
        // coal plant
        Put(20, 0, -8, "coal_generator", "north"); Put(21, 0, -8, "copper_cable"); Put(22, 0, -8, "battery");
        E(20, 0, -8).Inv("fuel")[0] = new ItemStack(GameAssets.ItemReg.Id("coal"), 16);
        // hotbar: the parts to build more
        string[] give = { "wind_turbine", "wind_turbine_medium", "wind_turbine_large", "shaft", "generator", "coal_generator", "copper_cable", "battery", "coal" };
        for (int i = 0; i < give.Length; i++) PlayerInventory.Hotbar[i] = new ItemStack(GameAssets.ItemReg.Id(give[i]), 64);
        PlayerInventory.Selected = 0;
        Kinetics.Clock = 300; // the wind starts from about west-north-west (north and west facing turbines both catch it), turning slowly
        int ticks = (int)(secs * 10);
        for (int i = 0; i < ticks; i++) BlockActions.Tick(.1);
        string One((int x, int z, string block, string facing) t)
        {
            var e = E(t.x, 14, t.z);
            return $"{t.block.Replace("wind_turbine", "").Trim('_') switch { "" => "small", var s => s }} {t.facing} {e.Num("power"):0.0} E/s {Kinetics.Rpm(e):0} rpm{(e.Num("blocked") > 0 ? " BLOCKED" : "")}";
        }
        double windBat = E(10, 0, 5).Num("energy") + E(11, 0, 5).Num("energy") + E(12, 0, 5).Num("energy");
        var cg = E(20, 0, -8);
        string report = $"WINDDEMO after {secs:0} s (wind {Kinetics.WindSpeed:0.0} m/s from {Kinetics.WindFrom}): {string.Join(", ", turbines.Select(One))}; " +
                        $"wind batteries {windBat:0} E, oxygen generator {E(13, 0, 5).Num("energy"):0} E; " +
                        $"coal generator {cg.Num("power"):0.0} E/s, {cg.Inv("fuel")[0]?.Count ?? 0} coal left + {cg.Num("burn"):0} s burning, its battery {E(22, 0, -8).Num("energy"):0} E";
        return Finish(folder, name, report, 20, -14, 20, 12, 4, false);
    }
}
