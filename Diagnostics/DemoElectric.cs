namespace CubePlanet;

public static partial class DemoWorld
{
    // ---------- --demoelectric NAME [--demosecs S]: solar farm, street lamps, electric furnaces, a motor; at sunset ----------
    // Solar farm (z 6..8, on the sunny side of the street): large 3 x 3 panels fixed (x 0) and tracking (x 4), medium 2 x 2 fixed (x 8) and tracking (x 11), small ones at x 14..19
    // (every other one tracking); a cable along z 5 to a battery bank (x 21..23) with 2 coal generators as night backup (x 24, 25).
    // Street (z -6): lamps on cable posts 3 high every 6 blocks (AUTO: lit at night); workshop: 2 electric furnaces with iron ore (x 27, 28);
    // a motor (x 31, 50 %) turning a shaft line into a generator that charges its own battery.
    public static int RunElectric(string name, double secs)
    {
        string folder = WorldSave.FolderFor(name);
        if (Directory.Exists(folder)) { Console.WriteLine($"DEMOWORLD a world folder '{folder}' already exists: not touching it"); return 1; }
        World.InitWorldData(1200, 1337); ChunkGenerator.mainTid = Environment.CurrentManagedThreadId;
        if (!FindSite()) { Console.WriteLine("DEMOWORLD no dry building site near spawn"); return 1; }
        int stone = Id("stone_bricks");
        for (int y = -1; y <= 30; y++) for (int z = -16; z <= 16; z++) for (int x = -6; x <= 44; x++) Cell(x, y, z, y == -1 ? stone : 0);
        // no trees near the solar farm (at a low sun even a tree 40 blocks away shades it): logs and leaves cleared around the site
        Band Bt = bands[bi];
        for (int cy = (y0 - 6) >> 5; cy <= (y0 + 40) >> 5; cy++) for (int cz = (z0 - 50) >> 5; cz <= (z0 + 50) >> 5; cz++) for (int cx = (x0 - 50) >> 5; cx <= (x0 + 60) >> 5; cx++)
                {
                    if (cx < 0 || cz < 0 || cy < 0 || cx >= Bt.fc || cz >= Bt.fc || cy >= Bt.rows / CS) continue;
                    long k = ck(bi, f, cx, cy, cz);
                    if (!chunks.TryGetValue(k, out var ch)) { ch = new Chunk { key = k, band = bi, face = f, cx = cx, cy = cy, cz = cz }; AddChunk(ch); }
                    ChunkGenerator.Ensure(ch);
                }
        for (int y = -5; y <= 40; y++) for (int z = -50; z <= 50; z++) for (int x = -50; x <= 60; x++) { int id = At(x, y, z).Id; if (id > 0 && (LOGID[id] || LEAFS[id])) Cell(x, y, z, 0); }
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
        // the farm stands on a stone deck 10 up, so a low sun clears the hills around
        const int D = 10; for (int z = 3; z <= 10; z++) for (int x = -2; x <= 26; x++) Cell(x, D - 1, z, stone);
        void Panel(int x, int z, string block, bool track) { BlockActions.Place(At(x, D, z), Id(block), new V3(0, 0, 1), out _); if (track) E(x, D, z).SetNum("mode", 1); }
        Panel(0, 6, "solar_panel_large", false); Panel(4, 6, "solar_panel_large", true);
        Panel(8, 6, "solar_panel_medium", false); Panel(11, 6, "solar_panel_medium", true);
        for (int x = 14; x <= 19; x++) Panel(x, 6, "solar_panel_small", (x & 1) == 1);
        for (int x = 0; x <= 20; x++) Put(x, D, 5, "gold_cable");
        Put(21, D, 5, "battery"); Put(22, D, 5, "battery"); Put(23, D, 5, "battery");
        int coal = GameAssets.ItemReg.Id("coal");
        foreach (int x in new[] { 24, 25 }) { Put(x, D, 5, "coal_generator", "north"); E(x, D, 5).Inv("fuel")[0] = new ItemStack(coal, 32); }
        // street with lamps and the workshop, joined to the bank
        for (int z = -5; z <= 4; z++) Put(22, 0, z, "gold_cable");
        for (int y = 1; y <= D; y++) Put(22, y, 4, "gold_cable"); // down from the deck
        for (int x = 0; x <= 30; x++) Put(x, 0, -6, "gold_cable");
        foreach (int x in new[] { 0, 6, 12, 18, 24 }) { for (int y = 1; y <= 2; y++) Put(x, y, -6, "copper_cable"); Put(x, 3, -6, "electric_lamp"); }
        int ore = GameAssets.ItemReg.Id("iron_ore");
        foreach (int x in new[] { 27, 28 }) { Put(x, 0, -7, "electric_furnace", "north"); E(x, 0, -7).Inv("input")[0] = new ItemStack(ore, 64); }
        Put(30, 0, -5, "copper_cable"); Put(31, 0, -5, "electric_motor", "east"); E(31, 0, -5).SetNum("mode", 1);
        for (int x = 32; x <= 35; x++) Put(x, 0, -5, "shaft", "east");
        Put(36, 0, -5, "generator", "east"); Put(36, 1, -5, "copper_cable"); Put(36, 2, -5, "battery");
        string[] give = { "solar_panel_small", "solar_panel_medium", "solar_panel_large", "electric_lamp", "electric_furnace", "electric_motor", "gold_cable", "battery", "iron_ore" };
        for (int i = 0; i < give.Length; i++) PlayerInventory.Hotbar[i] = new ItemStack(GameAssets.ItemReg.Id(give[i]), 64);
        PlayerInventory.Selected = 0;
        int ticks = (int)(secs * 10);
        for (int i = 0; i < ticks; i++) BlockActions.Tick(.1);
        double P(int x, int z) => E(x, D, z).Num("power");
        double small = 0; for (int x = 14; x <= 19; x++) small += P(x, 6);
        int lit = 0; foreach (int x in new[] { 0, 6, 12, 18, 24 }) if (E(x, 3, -6).Num("on") > 0) lit++;
        string report = $"ELECTRICDEMO after {secs:0} s (sun {Math.Asin(M.Dot(Sky.SUN, up)) * 180 / Math.PI:0} deg up, setting): large fixed {P(0, 6):0.0} vs tracking {P(4, 6):0.0} E/s, " +
                        $"medium fixed {P(8, 6):0.0} vs tracking {P(11, 6):0.0} E/s, 6 small {small:0.0} E/s; lamps lit {lit} of 5; furnaces made {E(27, 0, -7).Inv("output")[0]?.Count ?? 0} + {E(28, 0, -7).Inv("output")[0]?.Count ?? 0} ingots; " +
                        $"motor {E(31, 0, -5).Num("drive"):0.0} E/s rotation, generator {E(36, 0, -5).Num("input"):0.0} E/s; bank {E(21, D, 5).Num("energy") + E(22, D, 5).Num("energy") + E(23, D, 5).Num("energy"):0} E";
        return Finish(folder, name, report, 10, -13, 9, D + 1, 6, false);
    }
}
