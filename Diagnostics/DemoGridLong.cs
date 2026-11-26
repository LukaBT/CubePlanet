namespace CubePlanet;

public static partial class DemoWorld
{
    // ---------- --demogrid2 NAME [--demosecs S]: every voltage level and a very long power line ----------
    // Power plant (on the flat site, x 0..56): 4 large wind turbines facing north (2 generators each) and 8 coal generators feed a diamond
    // LV bus (z 4); substation: transformer LV-MV, diamond (MV), transformer MV-HV, wire connector.
    // HV line: pylons on the real ground every ~36 blocks (stone foundations where there is water), as long as the cube face allows (up to
    // ~1000 blocks), wires between them.
    // Town (at the end, on a small platform): connector, transformer MV-HV (back to MV), a diamond MV bus, transformer LV-MV, a diamond LV bus
    // to 10 batteries and 2 oxygen generators. From the town's MV bus an MV branch line on pylons runs ~160 blocks sideways to a village:
    // connector, transformer LV-MV, 3 batteries.
    // The plant faces +x / +z where the face has room (else it is mirrored).
    public static int RunGridLong(string name, double secs)
    {
        string folder = WorldSave.FolderFor(name);
        if (Directory.Exists(folder)) { Console.WriteLine($"DEMOWORLD a world folder '{folder}' already exists: not touching it"); return 1; }
        World.InitWorldData(1200, 1337); ChunkGenerator.mainTid = Environment.CurrentManagedThreadId;
        if (!FindSite()) { Console.WriteLine("DEMOWORLD no dry building site near spawn"); return 1; }
        Band Bd = bands[bi]; int W = Bd.fc * 32;
        int dx = x0 + 1100 < W - 16 ? 1 : -1, dz = z0 + 220 < W - 16 ? 1 : -1;
        int room = dx > 0 ? W - 24 - x0 : x0 - 24; int lineLen = Math.Clamp(room - 120, 200, 1000);
        // local coordinates (lx along the line, lz sideways), mirrored where the face needs it
        BlockPos L(int lx, int y, int lz) => At(dx * lx, y, dz * lz);
        void C(int lx, int y, int lz, int id) => Cell(dx * lx, y, dz * lz, id);
        string F(string facing) => facing switch
        {
            "east" => dx > 0 ? "east" : "west", "west" => dx > 0 ? "west" : "east",
            "north" => dz > 0 ? "north" : "south", "south" => dz > 0 ? "south" : "north", _ => facing
        };
        void P(int lx, int y, int lz, string block, string facing = null) => Put(dx * lx, y, dz * lz, block, facing == null ? null : F(facing));
        void LoadAround(int lxa, int lxb, int lza, int lzb, int ya, int yb)
        {
            int xa = x0 + Math.Min(dx * lxa, dx * lxb), xb = x0 + Math.Max(dx * lxa, dx * lxb), za = z0 + Math.Min(dz * lza, dz * lzb), zb = z0 + Math.Max(dz * lza, dz * lzb);
            for (int cy = (y0 + ya) >> 5; cy <= (y0 + yb) >> 5; cy++) for (int cz = za >> 5; cz <= zb >> 5; cz++) for (int cx = xa >> 5; cx <= xb >> 5; cx++)
                    {
                        if (cx < 0 || cz < 0 || cy < 0 || cx >= Bd.fc || cz >= Bd.fc || cy >= Bd.rows / CS) continue;
                        long k = ck(bi, f, cx, cy, cz);
                        if (!chunks.TryGetValue(k, out var ch)) { ch = new Chunk { key = k, band = bi, face = f, cx = cx, cy = cy, cz = cz }; AddChunk(ch); }
                        ChunkGenerator.Ensure(ch);
                    }
        }
        // the first free cell above the ground (local y); water gets a stone foundation up to its surface
        int stone = Id("stone_bricks");
        int Ground(int lx, int lz)
        {
            LoadAround(lx - 1, lx + 1, lz - 1, lz + 1, -70, 90);
            int top = int.MinValue;
            for (int y = 85; y >= -65; y--)
            {
                int id = L(lx, y, lz).Id;
                if (id == 0 || (SPC[id] != 0 && id != Water)) continue;
                if (id == Water) { if (top == int.MinValue) top = y; continue; }
                if (top != int.MinValue) { for (int yy = y + 1; yy <= top; yy++) C(lx, yy, lz, stone); return top + 1; }
                return y + 1;
            }
            return top == int.MinValue ? 0 : top + 1;
        }
        void Pad(int lxa, int lxb, int lza, int lzb, int gy) // a platform at gy - 1, air above
        {
            LoadAround(lxa, lxb, lza, lzb, gy - 4, gy + 12);
            for (int lz = lza; lz <= lzb; lz++) for (int lx = lxa; lx <= lxb; lx++) { C(lx, gy - 1, lz, stone); for (int y = gy; y <= gy + 10; y++) C(lx, y, lz, 0); }
        }
        // a pylon on the ground; returns the anchor (its top)
        BlockPos Pylon(int lx, int lz)
        {
            int gy = Ground(lx, lz); LoadAround(lx - 1, lx + 1, lz - 1, lz + 1, gy - 2, gy + 8);
            for (int y = gy; y <= gy + 5; y++) C(lx, y, lz, 0);
            BlockActions.Place(L(lx, gy, lz), Id("pylon"), new V3(0, 0, 1), out _);
            return L(lx, gy + 4, lz);
        }
        int strung = 0, failed = 0; double hvLen = 0, mvLen = 0;
        void String(BlockPos a, BlockPos b, ref double len)
        {
            if (Wires.Connect(a, b)) { strung++; len += Wires.Links[^1].Length; } else failed++;
        }

        // ----- power plant on the flat site -----
        LoadAround(-10, 70, -18, 22, -6, 34);
        for (int y = -1; y <= 30; y++) for (int lz = -16; lz <= 16; lz++) for (int lx = -6; lx <= 70; lx++) C(lx, y, lz, y == -1 ? stone : 0);
        foreach (int lx in new[] { 0, 14, 28, 42 })
        {
            for (int y = 0; y <= 13; y++) C(lx, y, 0, stone);
            P(lx, 14, 0, "wind_turbine_large", "north"); P(lx, 14, 1, "shaft", "north"); P(lx, 14, 2, "generator", "north"); P(lx, 14, 3, "generator", "north");
            for (int y = 13; y >= 1; y--) P(lx, y, 3, "diamond_cable");
            P(lx, 0, 3, "diamond_cable");
        }
        for (int lx = 0; lx <= 50; lx++) P(lx, 0, 4, "diamond_cable");
        int coal = GameAssets.ItemReg.Id("coal");
        for (int lx = 0; lx <= 7; lx++) { P(lx, 0, 8, "coal_generator", "north"); E(dx * lx, 0, dz * 8).Inv("fuel")[0] = new ItemStack(coal, 64); }
        for (int lz = 5; lz <= 8; lz++) P(8, 0, lz, "diamond_cable");
        // plant substation: LV -> MV -> HV
        P(51, 0, 4, "transformer_lv_mv", "east"); P(52, 0, 4, "diamond_cable"); P(53, 0, 4, "transformer_mv_hv", "east"); P(54, 0, 4, "wire_connector");

        // ----- the HV line -----
        var anchors = new List<BlockPos> { L(54, 0, 4) };
        int span = 36, first = 64, last = first + (lineLen / span) * span;
        for (int lx = first; lx <= last; lx += span) anchors.Add(Pylon(lx, 4));
        int xT = last + 12; int gT = Ground(xT + 6, 4); Pad(xT - 2, xT + 18, -2, 10, gT);
        anchors.Add(L(xT, gT, 4)); P(xT, gT, 4, "wire_connector");
        for (int i = 0; i + 1 < anchors.Count; i++) String(anchors[i], anchors[i + 1], ref hvLen);
        // town substation: HV -> MV bus -> LV
        P(xT + 1, gT, 4, "transformer_mv_hv", "west"); P(xT + 2, gT, 4, "diamond_cable"); P(xT + 3, gT, 4, "transformer_lv_mv", "west");
        for (int lx = xT + 4; lx <= xT + 16; lx++) P(lx, gT, 4, "diamond_cable");
        for (int lx = xT + 5; lx <= xT + 14; lx++) P(lx, gT, 5, "battery");
        P(xT + 15, gT, 5, "oxygen_generator", "north"); P(xT + 16, gT, 5, "oxygen_generator", "north");

        // ----- MV branch from the town's MV bus to a village -----
        P(xT + 2, gT, 5, "wire_connector");
        var mv = new List<BlockPos> { L(xT + 2, gT, 5) };
        for (int lz = 20; lz <= 140; lz += 30) mv.Add(Pylon(xT + 2, lz));
        int zV = 165, gV = Ground(xT + 2, zV + 3); Pad(xT - 2, xT + 6, zV - 1, zV + 6, gV);
        mv.Add(L(xT + 2, gV, zV)); P(xT + 2, gV, zV, "wire_connector");
        for (int i = 0; i + 1 < mv.Count; i++) String(mv[i], mv[i + 1], ref mvLen);
        P(xT + 2, gV, zV + 1, "transformer_lv_mv", "north"); P(xT + 2, gV, zV + 2, "diamond_cable");
        P(xT + 1, gV, zV + 3, "battery"); P(xT + 2, gV, zV + 3, "battery"); P(xT + 3, gV, zV + 3, "battery");

        string[] give = { "transformer_lv_mv", "transformer_mv_hv", "pylon", "wire_connector", "wire_spool", "diamond_cable", "gold_cable", "battery", "wind_turbine_large" };
        for (int i = 0; i < give.Length; i++) PlayerInventory.Hotbar[i] = new ItemStack(GameAssets.ItemReg.Id(give[i]), give[i] == "wire_spool" ? 1 : 64);
        PlayerInventory.Selected = 4;
        Kinetics.Clock = 300; // wind from about WNW
        int ticks = (int)(secs * 10);
        for (int i = 0; i < ticks; i++) BlockActions.Tick(.1);

        double Num(int lx, int y, int lz, string n) => BlockStates.entities.TryGetValue(L(lx, y, lz).Key, out var e) ? e.Num(n) : double.NaN;
        double wind = 0; foreach (int lx in new[] { 0, 14, 28, 42 }) wind += Num(lx, 14, 0, "power");
        double coalP = 0; for (int lx = 0; lx <= 7; lx++) coalP += Num(lx, 0, 8, "power");
        double town = 0; for (int lx = xT + 5; lx <= xT + 14; lx++) town += Num(lx, gT, 5, "power");
        double village = Num(xT + 1, gV, zV + 3, "power") + Num(xT + 2, gV, zV + 3, "power") + Num(xT + 3, gV, zV + 3, "power");
        var hvW = Wires.Links.Where(w => w.Level == 64).ToList(); var mvW = Wires.Links.Where(w => w.Level == 8).ToList();
        string report = $"GRIDDEMO2 after {secs:0} s (wind {Kinetics.WindSpeed:0.0} m/s from {Kinetics.WindFrom}): wind {wind:0} E/s + coal {coalP:0} E/s; " +
                        $"plant LV-MV {Num(51, 0, 4, "flow"):0} E/s, MV-HV {Num(53, 0, 4, "flow"):0} E/s; HV line {hvLen:0} blocks, {hvW.Count} wires carrying {hvW.FirstOrDefault()?.Flow:0} E/s, losing {hvW.Sum(w => w.LossNow):0.00} E/s; " +
                        $"town MV-HV {Num(xT + 1, gT, 4, "flow"):0}, LV-MV {Num(xT + 3, gT, 4, "flow"):0} E/s, batteries charging {town:0} E/s; " +
                        $"MV branch {mvLen:0} blocks, {mvW.Count} wires carrying {mvW.FirstOrDefault()?.Flow:0} E/s, village batteries charging {village:0} E/s; {strung} wires strung, {failed} failed";
        int result = Finish(folder, name, report, 50, -8, 80, 4, 4, false);
        // the saved world reloaded with no land loaded at all: the plant, the line, the town and the village still work (GridCache)
        WorldSave.ResetWorldState(); WorldSave.LoadInto(folder, restorePlayer: false);
        foreach (var ch in chunks.Values.ToArray()) FreeChunk(ch);
        for (int i = 0; i < 30; i++) BlockActions.Tick(.1);
        double town2 = 0; for (int lx = xT + 5; lx <= xT + 14; lx++) town2 += Num(lx, gT, 5, "power");
        double village2 = Num(xT + 1, gV, zV + 3, "power") + Num(xT + 2, gV, zV + 3, "power") + Num(xT + 3, gV, zV + 3, "power");
        Console.WriteLine($"GRIDDEMO2 reloaded with no chunks loaded ({chunks.Count} loaded, {GridCache.Count} grid blocks remembered, {Wires.Links.Count} wires): town batteries charging {town2:0} E/s, village {village2:0} E/s");
        return result;
    }
}
