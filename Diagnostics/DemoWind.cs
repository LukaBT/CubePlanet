namespace CubePlanet;

public static partial class DemoWorld
{
    // ---------- --demowind NAME [--demosecs S]: a wind farm and a coal plant ----------
    // Wind farm (x 0..16): 3 stone towers; on top (y 14) a wind turbine facing north (blades toward the player, in the z -1 plane), two
    // shafts behind it and a generator; a cable down each tower to a ground cable (z 4) feeding 3 batteries and an oxygen generator (x 20..23).
    // Coal plant (x 30..33): a coal generator with 16 coal, a cable and a battery: it burns while the battery has room.
    public static int RunWind(string name, double secs)
    {
        string folder = WorldSave.FolderFor(name);
        if (Directory.Exists(folder)) { Console.WriteLine($"DEMOWORLD a world folder '{folder}' already exists: not touching it"); return 1; }
        World.InitWorldData(1200, 1337); ChunkGenerator.mainTid = Environment.CurrentManagedThreadId;
        if (!FindSite()) { Console.WriteLine("DEMOWORLD no dry building site near spawn"); return 1; }
        int stone = Id("stone_bricks");
        for (int y = -1; y <= 30; y++) for (int z = -16; z <= 16; z++) for (int x = -6; x <= 44; x++) Cell(x, y, z, y == -1 ? stone : 0);
        int[] towers = { 0, 8, 16 };
        foreach (int x in towers)
        {
            for (int y = 0; y <= 13; y++) { Cell(x, y, 2, stone); Cell(x, y, 3, stone); } // the tower under the shafts and generator
            Put(x, 14, 0, "wind_turbine", "north"); Put(x, 14, 1, "shaft", "north"); Put(x, 14, 2, "shaft", "north"); Put(x, 14, 3, "generator", "south");
            for (int y = 14; y >= 1; y--) Put(x, y, 4, "copper_cable");
        }
        for (int x = 0; x <= 19; x++) Put(x, 0, 4, "copper_cable");
        Put(20, 0, 4, "battery"); Put(21, 0, 4, "battery"); Put(22, 0, 4, "battery"); Put(23, 0, 4, "oxygen_generator", "north");
        // coal plant
        Put(30, 0, 4, "coal_generator", "north"); Put(31, 0, 4, "copper_cable"); Put(32, 0, 4, "battery");
        int coal = GameAssets.ItemReg.Id("coal");
        E(30, 0, 4).Inv("fuel")[0] = new ItemStack(coal, 16);
        // hotbar: the parts to build more
        string[] give = { "wind_turbine", "shaft", "generator", "coal_generator", "copper_cable", "battery", "coal" };
        for (int i = 0; i < give.Length; i++) PlayerInventory.Hotbar[i] = new ItemStack(GameAssets.ItemReg.Id(give[i]), 64);
        PlayerInventory.Selected = 0;
        int ticks = (int)(secs * 10);
        for (int i = 0; i < ticks; i++) BlockActions.Tick(.1);
        string Tower(int x) { var t = E(x, 14, 0); return $"{t.Num("power"):0.0} E/s {t.Num("speed") * 60 / (2 * Math.PI):0} rpm{(t.Num("blocked") > 0 ? " BLOCKED" : "")}"; }
        double windBat = E(20, 0, 4).Num("energy") + E(21, 0, 4).Num("energy") + E(22, 0, 4).Num("energy");
        var cg = E(30, 0, 4);
        string report = $"WINDDEMO after {secs:0} s (wind {Kinetics.Wind(Kinetics.Clock) * 100:0}%): turbines {string.Join(", ", towers.Select(Tower))}; " +
                        $"generators {string.Join(", ", towers.Select(x => $"{E(x, 14, 3).Num("power"):0.0}"))} E/s; wind batteries {windBat:0} E, oxygen generator {E(23, 0, 4).Num("energy"):0} E; " +
                        $"coal generator {cg.Num("power"):0.0} E/s, {cg.Inv("fuel")[0]?.Count ?? 0} coal left + {cg.Num("burn"):0} s burning, its battery {E(32, 0, 4).Num("energy"):0} E";
        return Finish(folder, name, report, 12, -14, 8, 13, 0, false);
    }
}
