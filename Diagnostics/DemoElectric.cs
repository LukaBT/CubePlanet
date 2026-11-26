namespace CubePlanet;

public static partial class DemoWorld
{
    // ---------- --demoelectric NAME [--demosecs S]: solar farm, street lamps, electric furnaces, a motor; at sunset ----------
    // Solar farm (z -9..-7): large 3 x 3 panels fixed (x 0) and tracking (x 4), medium 2 x 2 fixed (x 8) and tracking (x 11), small ones at x 14..19
    // (every other one tracking); a cable along z -6 to a battery bank (x 21..23) with 2 coal generators as night backup (x 24, 25).
    // Street (z 2): lamps on cable posts 3 high every 6 blocks (AUTO: lit at night); workshop: 2 electric furnaces with iron ore (x 27, 28);
    // a motor (x 31, 50 %) turning a shaft line into a generator that charges its own battery.
    public static int RunElectric(string name, double secs)
    {
        string folder = WorldSave.FolderFor(name);
        if (Directory.Exists(folder)) { Console.WriteLine($"DEMOWORLD a world folder '{folder}' already exists: not touching it"); return 1; }
        World.InitWorldData(1200, 1337); ChunkGenerator.mainTid = Environment.CurrentManagedThreadId;
        if (!FindSite()) { Console.WriteLine("DEMOWORLD no dry building site near spawn"); return 1; }
        int stone = Id("stone_bricks");
        for (int y = -1; y <= 30; y++) for (int z = -16; z <= 16; z++) for (int x = -6; x <= 44; x++) Cell(x, y, z, y == -1 ? stone : 0);
        // sunset at the site: the sun ~12 degrees up and going down
        V3 up = M.Nrm(At(10, 0, 0).Center); double best = 9, sa = 0;
        for (int i = 0; i < 1440; i++)
        {
            double a = i * Math.PI / 720; Sky.SUNA = a; Sky.SunVec(); double el = M.Dot(Sky.SUN, up);
            Sky.SUNA = a + .01; Sky.SunVec(); bool setting = M.Dot(Sky.SUN, up) < el;
            double d = Math.Abs(el - Math.Sin(12 * Math.PI / 180)); if (setting && d < best) { best = d; sa = a; }
        }
        Sky.SUNA = sa; Sky.SunVec();
        // solar farm
        void Panel(int x, int z, string block, bool track) { BlockActions.Place(At(x, 0, z), Id(block), new V3(0, 0, 1), out _); if (track) E(x, 0, z).SetNum("mode", 1); }
        Panel(0, -9, "solar_panel_large", false); Panel(4, -9, "solar_panel_large", true);
        Panel(8, -8, "solar_panel_medium", false); Panel(11, -8, "solar_panel_medium", true);
        for (int x = 14; x <= 19; x++) Panel(x, -7, "solar_panel_small", (x & 1) == 1);
        for (int x = 0; x <= 20; x++) Put(x, 0, -6, "gold_cable");
        Put(21, 0, -6, "battery"); Put(22, 0, -6, "battery"); Put(23, 0, -6, "battery");
        int coal = GameAssets.ItemReg.Id("coal");
        foreach (int x in new[] { 24, 25 }) { Put(x, 0, -6, "coal_generator", "north"); E(x, 0, -6).Inv("fuel")[0] = new ItemStack(coal, 32); }
        // street with lamps and the workshop, joined to the bank
        for (int z = -5; z <= 2; z++) Put(22, 0, z, "gold_cable");
        for (int x = 0; x <= 30; x++) Put(x, 0, 2, "gold_cable");
        foreach (int x in new[] { 0, 6, 12, 18, 24 }) { for (int y = 1; y <= 2; y++) Put(x, y, 2, "copper_cable"); Put(x, 3, 2, "electric_lamp"); }
        int ore = GameAssets.ItemReg.Id("iron_ore");
        foreach (int x in new[] { 27, 28 }) { Put(x, 0, 1, "electric_furnace", "north"); E(x, 0, 1).Inv("input")[0] = new ItemStack(ore, 64); }
        Put(30, 0, 3, "copper_cable"); Put(31, 0, 3, "electric_motor", "east"); E(31, 0, 3).SetNum("mode", 1);
        for (int x = 32; x <= 35; x++) Put(x, 0, 3, "shaft", "east");
        Put(36, 0, 3, "generator", "east"); Put(36, 1, 3, "copper_cable"); Put(36, 2, 3, "battery");
        string[] give = { "solar_panel_small", "solar_panel_medium", "solar_panel_large", "electric_lamp", "electric_furnace", "electric_motor", "gold_cable", "battery", "iron_ore" };
        for (int i = 0; i < give.Length; i++) PlayerInventory.Hotbar[i] = new ItemStack(GameAssets.ItemReg.Id(give[i]), 64);
        PlayerInventory.Selected = 0;
        int ticks = (int)(secs * 10);
        for (int i = 0; i < ticks; i++) BlockActions.Tick(.1);
        double P(int x, int z) => E(x, 0, z).Num("power");
        double small = 0; for (int x = 14; x <= 19; x++) small += P(x, -7);
        int lit = 0; foreach (int x in new[] { 0, 6, 12, 18, 24 }) if (E(x, 3, 2).Num("on") > 0) lit++;
        string report = $"ELECTRICDEMO after {secs:0} s (sun {Math.Asin(M.Dot(Sky.SUN, up)) * 180 / Math.PI:0} deg up, setting): large fixed {P(0, -9):0.0} vs tracking {P(4, -9):0.0} E/s, " +
                        $"medium fixed {P(8, -8):0.0} vs tracking {P(11, -8):0.0} E/s, 6 small {small:0.0} E/s; lamps lit {lit} of 5; furnaces made {E(27, 0, 1).Inv("output")[0]?.Count ?? 0} + {E(28, 0, 1).Inv("output")[0]?.Count ?? 0} ingots; " +
                        $"motor {E(31, 0, 3).Num("drive"):0.0} E/s rotation, generator {E(36, 0, 3).Num("input"):0.0} E/s; bank {E(21, 0, -6).Num("energy") + E(22, 0, -6).Num("energy") + E(23, 0, -6).Num("energy"):0} E";
        return Finish(folder, name, report, 10, 9, 8, 1, -7, false);
    }
}
