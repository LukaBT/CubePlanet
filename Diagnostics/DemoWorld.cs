namespace CubePlanet;

/// <summary>
/// --demoworld NAME [--demosecs S]: creates a saved world (no window) with two hydro plants next to spawn, runs them for S seconds of game
/// time (default 330) and saves it, so it can be opened from the start screen.
/// <list type="bullet">
/// <item>Plant A (x 0..13): a stone reservoir 14 blocks up fed by 3 springs; 3 water pipes down to 3 turbines (on); their water falls into a
/// basin with drains; cables to a bank of 4 batteries (and an oxygen generator).</item>
/// <item>Plant B (x 22..34), joined to A by a cable: a lower pool, a pump (auto) lifting water through a pipe into an upper tank 16 blocks
/// up, and a turbine (auto) taking it back down through another pipe. Spare energy (batteries full) pumps water up; when a machine needs
/// more than the turbines of plant A make, plant B's turbine opens.</item>
/// </list>
/// Everything stands on a stone platform in a cleared space, so the terrain cannot interfere.
/// </summary>
public static class DemoWorld
{
    static int bi, f, x0, y0, z0;
    static BlockPos At(int x, int y, int z) => new(false, bi, f, x0 + x, y0 + y, z0 + z);
    static void Cell(int x, int y, int z, int id, int lvl = 0) => SetCell(bi, f, x0 + x, y0 + y, z0 + z, id, lvl);
    static int Id(string n) => GameAssets.BlockReg.Id(n);
    static void Put(int x, int y, int z, string block, string facing = null)
    {
        int id = Id(block); var p = At(x, y, z); uint w = 0; var ff = DEFS[id]?.State.Find("facing");
        if (facing != null && ff != null) w = ff.With(0, Array.IndexOf(ff.Values, facing));
        p.Set(id, (int)w); BEH[id]?.OnPlace(p, id); BlockActions.NotifyNeighbors(p);
    }
    static BlockEntity E(int x, int y, int z) => BlockStates.entities[At(x, y, z).Key];
    static string ModeOf(int x, int y, int z) => BlockStates.entities.TryGetValue(At(x, y, z).Key, out var e) ? WaterMachines.ModeName(e) : "missing";

    public static int Run(string name, double secs = 200)
    {
        string folder = WorldSave.FolderFor(name);
        if (Directory.Exists(folder)) { Console.WriteLine($"DEMOWORLD a world folder '{folder}' already exists: not touching it"); return 1; }
        World.InitWorldData(1200, 1337); ChunkGenerator.mainTid = Environment.CurrentManagedThreadId;
        // a dry, gently sloped spot near spawn (no water in the building area or around it)
        V3 c = new(C.x, C.y, C.z), up = M.Nrm(c); V3 g = c;
        for (int i = 0; i < 400 && GetAt(g.X - up.X * .5, g.Y - up.Y * .5, g.Z - up.Z * .5) == 0; i++) g -= up;
        bi = BlockAccess.P2b(g.X, g.Y, g.Z); f = PF; int sx = PX, sy = PY, sz = PZ;
        bool found = false;
        foreach (var (ox, oz) in new[] { (0, 0), (64, 0), (-64, 0), (0, 64), (0, -64), (64, 64), (-64, -64), (128, 0), (0, 128) })
        {
            x0 = sx + ox; z0 = sz + oz; var col = ColsOf(bands[bi], f, x0 >> 5, z0 >> 5);
            y0 = (int)Math.Floor(R + col.h[(x0 & 31) + 32 * (z0 & 31)] - bands[bi].inner + .5); // first air cell above the ground
            Load(); bool wet = false;
            for (int y = -4; y <= 32 && !wet; y++) for (int z = -16; z <= 20 && !wet; z++) for (int x = -8; x <= 50; x++) if (Blk(bi, f, x0 + x, y0 + y, z0 + z) == Water) { wet = true; break; }
            if (!wet) { found = true; break; }
        }
        if (!found) { Console.WriteLine("DEMOWORLD no dry building site near spawn"); return 1; }
        Build();
        var report = Simulate(secs);
        // stand between the plants, looking at them
        var eye = At(17, 0, -9); V3 feet = eye.Center - M.Nrm(eye.Center) * .5;
        P.on = true; P.fly = false; P.p = feet; var target = At(17, 9, 5).Center; V3 upv = M.Nrm(feet);
        C.x = feet.X + upv.X * 1.6; C.y = feet.Y + upv.Y * 1.6; C.z = feet.Z + upv.Z * 1.6;
        C.f = M.Nrm(target - new V3(C.x, C.y, C.z)); C.u = upv; C.orbit = false;
        WorldSave.SaveTo(folder, name);
        Console.WriteLine(report);
        Console.WriteLine($"DEMOWORLD saved '{name}' in {folder}");
        // after saving (does not change the saved world): with plant A off and a machine needing energy, plant B's turbine releases the tank
        foreach (int x in new[] { 3, 5, 7 }) E(x, 1, 4).SetNum("mode", 2);
        int Tank() { int n = 0; for (int y = 17; y <= 22; y++) for (int z = 3; z <= 7; z++) for (int x = 26; x <= 30; x++) n += Wl(Wcell(bi, f, x0 + x, y0 + y, z0 + z)); return n; }
        int t0 = Tank(); var gen = E(12, 2, 4); double got = 0;
        for (int i = 0; i < 30; i++) { gen.SetNum("energy", 0); BlockActions.Tick(.1); got += gen.Num("energy"); if ((i & 1) == 1) WaterSim.WaterStep(); }
        Console.WriteLine($"DEMOWORLD release check: oxygen generator got {got:0.0} E in 3 s, upper tank {t0} -> {Tank()} units, plant B turbine {E(23, 0, 5).Num("power"):0.0} E/s (head {E(23, 0, 5).Num("head"):0.0})");
        // the saved world loads back as it was saved
        WorldSave.ResetWorldState(); WorldSave.LoadInto(folder);
        foreach (var ch in chunks.Values) ChunkGenerator.Ensure(ch);
        double bat = new[] { (12, 1, 4), (12, 1, 3), (12, 1, 5), (13, 1, 4) }.Sum(p => BlockStates.entities.TryGetValue(At(p.Item1, p.Item2, p.Item3).Key, out var b) ? b.Num("energy") : -1e9);
        Console.WriteLine($"DEMOWORLD reloaded: {BlockStates.entities.Count} machines, batteries {bat:0} E, upper tank {Tank()} units, turbine A mode {ModeOf(3, 1, 4)}, pump mode {ModeOf(33, 0, 5)}, player on {P.on}");
        return 0;
    }

    static void Load()
    {
        Band B = bands[bi];
        for (int cy = (y0 - 6) >> 5; cy <= (y0 + 34) >> 5; cy++) for (int cz = (z0 - 18) >> 5; cz <= (z0 + 22) >> 5; cz++) for (int cx = (x0 - 10) >> 5; cx <= (x0 + 52) >> 5; cx++)
                {
                    if (cx < 0 || cz < 0 || cy < 0 || cx >= B.fc || cz >= B.fc || cy >= B.rows / CS) continue;
                    long k = ck(bi, f, cx, cy, cz);
                    if (!chunks.TryGetValue(k, out var ch)) { ch = new Chunk { key = k, band = bi, face = f, cx = cx, cy = cy, cz = cz }; AddChunk(ch); }
                    ChunkGenerator.Ensure(ch);
                }
    }

    static void Build()
    {
        int stone = Id("stone_bricks"), glass = Id("glass");
        // cleared space on a stone platform
        for (int y = -1; y <= 30; y++) for (int z = -12; z <= 16; z++) for (int x = -4; x <= 44; x++) Cell(x, y, z, y == -1 ? stone : 0);
        // ---------- plant A: reservoir (interior x 2..8, z 2..8, water y 15..17) on 4 pillars
        for (int z = 1; z <= 9; z++) for (int x = 1; x <= 9; x++)
            {
                Cell(x, 14, z, stone);
                if (x == 1 || x == 9 || z == 1 || z == 9) for (int y = 15; y <= 18; y++) Cell(x, y, z, y == 16 && (x + z) % 2 == 0 ? glass : stone);
                else for (int y = 15; y <= 17; y++) Cell(x, y, z, Water, 8);
            }
        foreach (var (px, pz) in new[] { (1, 1), (9, 1), (1, 9), (9, 9) }) for (int y = 0; y <= 13; y++) Cell(px, y, pz, stone);
        Put(1, 17, 5, "spring", "east"); Put(9, 17, 5, "spring", "west"); Put(5, 17, 9, "spring", "north");
        foreach (var sp in new[] { E(1, 17, 5), E(9, 17, 5), E(5, 17, 9) }) sp.SetNum("rate", 16); // 48 units/s: what the 3 turbines take
        // three penstocks (pipes from the reservoir floor down) to three turbines facing down
        foreach (int x in new[] { 3, 5, 7 })
        {
            for (int y = 14; y >= 2; y--) Put(x, y, 4, "water_pipe");
            Put(x, 1, 4, "water_turbine", "down"); E(x, 1, 4).SetNum("mode", 1);
        }
        // tailwater basin under the turbines (interior x 2..8, z 3..5) with drains in its floor
        for (int z = 2; z <= 6; z++) for (int x = 1; x <= 9; x++) if (x == 1 || x == 9 || z == 2 || z == 6) for (int y = 0; y <= 1; y++) Cell(x, y, z, stone);
        for (int x = 2; x <= 8; x++) Put(x, -1, 4, "drain", "up");
        // cables: turbines -> battery bank (4 batteries) + an oxygen generator on top
        Put(4, 1, 4, "copper_cable"); Put(6, 1, 4, "copper_cable"); Put(8, 1, 4, "copper_cable"); Put(9, 1, 4, "copper_cable"); Put(10, 1, 4, "copper_cable"); Put(11, 1, 4, "copper_cable");
        Put(12, 1, 4, "battery"); Put(12, 1, 3, "battery"); Put(12, 1, 5, "battery"); Put(13, 1, 4, "battery");
        Put(12, 2, 4, "oxygen_generator", "north");
        // ---------- plant B: lower pool (interior x 24..32, z 2..8, water y 0..2)
        for (int z = 1; z <= 9; z++) for (int x = 23; x <= 33; x++)
            {
                if (x == 23 || x == 33 || z == 1 || z == 9) for (int y = 0; y <= 3; y++) Cell(x, y, z, stone);
                else for (int y = 0; y <= 2; y++) Cell(x, y, z, Water, 8);
            }
        // upper tank (interior x 26..30, z 3..7, y 17..21), empty, on pillars
        for (int z = 2; z <= 8; z++) for (int x = 25; x <= 31; x++)
            {
                Cell(x, 16, z, stone);
                if (x == 25 || x == 31 || z == 2 || z == 8) for (int y = 17; y <= 22; y++) Cell(x, y, z, (y == 19 || y == 20) && x != 25 && x != 31 && z == 2 ? glass : stone);
            }
        foreach (var (px, pz) in new[] { (25, 2), (31, 2), (25, 8), (31, 8) }) for (int y = 4; y <= 15; y++) Cell(px, y, pz, stone);
        // pump in the pool's east wall (intake: the pool), pipe up and into the tank
        Put(33, 0, 5, "water_pump", "east"); E(33, 0, 5).SetNum("mode", 0);
        for (int y = 0; y <= 17; y++) Put(34, y, 5, "water_pipe");
        Put(33, 17, 5, "water_pipe"); Put(32, 17, 5, "water_pipe"); Put(31, 17, 5, "water_pipe");
        // turbine in the pool's west wall (outlet: the pool), pipe from the tank's bottom
        Put(23, 0, 5, "water_turbine", "east"); E(23, 0, 5).SetNum("mode", 0);
        for (int y = 0; y <= 17; y++) Put(22, y, 5, "water_pipe");
        Put(23, 17, 5, "water_pipe"); Put(24, 17, 5, "water_pipe"); Put(25, 17, 5, "water_pipe");
        // the cable from plant A's batteries to plant B's turbine and pump
        for (int x = 14; x <= 22; x++) Put(x, 1, 4, "copper_cable");
        Put(22, 0, 4, "copper_cable"); Put(23, 0, 4, "copper_cable");
        for (int z = 3; z >= 0; z--) Put(22, 1, z, "copper_cable");
        for (int x = 23; x <= 33; x++) Put(x, 1, 0, "copper_cable");
        for (int z = 1; z <= 5; z++) Put(33, 1, z, "copper_cable");
    }

    static string Simulate(double secs)
    {
        WaterSim.Reservoir = WaterSim.DefaultReservoir; int ticks = (int)(secs * 10);
        var bats = new[] { E(12, 1, 4), E(12, 1, 3), E(12, 1, 5), E(13, 1, 4) };
        int Tank() { int n = 0; for (int y = 17; y <= 22; y++) for (int z = 3; z <= 7; z++) for (int x = 26; x <= 30; x++) n += Wl(Wcell(bi, f, x0 + x, y0 + y, z0 + z)); return n; }
        double fullAt = -1;
        for (int i = 0; i < ticks; i++)
        {
            BlockActions.Tick(.1); WaterFlow.Tick(.1); if ((i & 1) == 1) WaterSim.WaterStep();
            if (fullAt < 0 && bats.All(b => b.Num("energy") > BatteryBehavior.Capacity - 1)) fullAt = i / 10.0;
            if (i % 600 == 599) Console.WriteLine($"DEMOWORLD t {i / 10 + 1} s: batteries {bats.Sum(b => b.Num("energy")):0} E, upper tank {Tank()} units, water moving {WaterSim.WAct.Count} cells");
        }
        var t = E(3, 1, 4); var pump = E(33, 0, 5);
        // water where it does not belong (anywhere in the cleared space outside the reservoir, the tailwater basin, the pool and the tank)
        int stray = 0;
        for (int y = 0; y <= 30; y++) for (int z = -12; z <= 16; z++) for (int x = -4; x <= 44; x++)
                {
                    bool ok = (x >= 2 && x <= 8 && z >= 2 && z <= 8 && y >= 15) || (x >= 2 && x <= 8 && z >= 3 && z <= 5 && y <= 1) || (x >= 24 && x <= 32 && z >= 2 && z <= 8 && y <= 3) || (x >= 26 && x <= 30 && z >= 3 && z <= 7 && y >= 17);
                    if (!ok && Blk(bi, f, x0 + x, y0 + y, z0 + z) == Water) stray++;
                }
        Console.WriteLine($"DEMOWORLD stray water cells: {stray}");
        return $"DEMOWORLD after {secs:0} s: plant A turbines {E(3, 1, 4).Num("power") + E(5, 1, 4).Num("power") + E(7, 1, 4).Num("power"):0.0} E/s (head {t.Num("head"):0.0}), " +
               $"batteries {bats.Sum(b => b.Num("energy")):0} / {bats.Length * BatteryBehavior.Capacity:0} E (full after {(fullAt < 0 ? "-" : fullAt.ToString("0"))} s), " +
               $"pump {pump.Num("flow"):0.0} u/s lifting {pump.Num("lift"):0.0} blocks, upper tank {Tank()} units ({Tank() * (17 - 3) * WaterMachines.K * TurbineBehavior.Efficiency:0} E stored)";
    }
}
