using System.Diagnostics;

namespace CubePlanet;

/// <summary>
/// --benchwater (no window): (1) generated water at rest: water cells around spawn down to cave depth that have air beside or below them
/// (water that would start flowing as soon as it is woken up); (2) cost of the water simulation while a 16x16x4 pool spreads out.
/// </summary>
public static class WaterBench
{
    public static void Run()
    {
        World.InitWorldData(1200, 1337); ChunkGenerator.mainTid = Environment.CurrentManagedThreadId;
        V3 c = new(C.x, C.y, C.z), up = M.Nrm(c);
        V3 g = c; for (int i = 0; i < 400 && GetAt(g.X - up.X * .5, g.Y - up.Y * .5, g.Z - up.Z * .5) == 0; i++) g -= up;
        int bi = BlockAccess.P2b(g.X, g.Y, g.Z); int f = PF, x0 = PX, y0 = PY, z0 = PZ; Band B = bands[bi];
        var (water, loose) = Unsettled(B, bi, f, x0 >> 5, z0 >> 5, 6);
        Console.WriteLine($"BENCHWATER generated water around spawn: {water} cells, {loose} next to air ({100.0 * loose / Math.Max(1, water):0.00} %, waterfall faces not counted)");
        var rv = RiverStats(B, bi, f, x0 >> 5, z0 >> 5, 14);
        Console.WriteLine($"BENCHWATER rivers within {14 * 32} blocks: {rv.cols} river columns, {rv.falls} waterfall columns, nearest {rv.near:0} blocks away");

        // load chunks around the spot like the game does, then let a pool spread on the ground
        for (int dy = -1; dy <= 1; dy++) for (int dz = -2; dz <= 2; dz++) for (int dx = -2; dx <= 2; dx++)
                {
                    int cx = (x0 >> 5) + dx, cy = (y0 >> 5) + dy, cz = (z0 >> 5) + dz;
                    if (cx < 0 || cz < 0 || cy < 0 || cx >= B.fc || cz >= B.fc || cy >= B.rows / CS) continue;
                    var ch = new Chunk { key = ck(bi, f, cx, cy, cz), band = bi, face = f, cx = cx, cy = cy, cz = cz };
                    AddChunk(ch); ChunkGenerator.Ensure(ch); ch.built = true; ch.dirty = false;
                }
        for (int y = 0; y < 4; y++) for (int z = -8; z < 8; z++) for (int x = -8; x < 8; x++) if (Blk(bi, f, x0 + x, y0 + 2 + y, z0 + z) == 0) { SetCell(bi, f, x0 + x, y0 + 2 + y, z0 + z, Water, 8); }
        int ticks = 0; double total = 0, worst = 0; long sets0 = WaterSim.Moves;
        foreach (var ch in chunks.Values) { ch.dirty = ch.relight = false; }
        int dirty = 0;
        while (WaterSim.WAct.Count > 0 && ticks < 600)
        {
            long t0 = Stopwatch.GetTimestamp(); WaterSim.WaterStep(); double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            total += ms; worst = Math.Max(worst, ms); ticks++;
            foreach (var ch in chunks.Values) { if (ch.dirty) dirty++; ch.dirty = ch.relight = false; }
        }
        Console.WriteLine($"BENCHWATER pool: {ticks} ticks until still ({WaterSim.WAct.Count} active left), whole ticks at once: {total / Math.Max(1, ticks):0.00} ms/tick, worst {worst:0.00} ms, " +
                          $"{WaterSim.Moves - sets0} cell changes, {dirty / (double)Math.Max(1, ticks):0.0} chunks to re-mesh per tick");
        // speed, as the game runs it (60 fps): a block dropped from 12 up, and how far a 4-high column spreads in 1 s
        WaterSim.WAct.Clear(); WaterSim.wactQ.Clear();
        int sx = x0 + 20, sz = z0 + 20, gy = y0 + 30; while (gy > y0 - 30 && Blk(bi, f, sx, gy - 1, sz) == 0) gy--;
        for (int y = gy; y < gy + 14; y++) SetCell(bi, f, sx, y, sz, 0);
        SetCell(bi, f, sx, gy + 12, sz, Water, 8); int fall = 0;
        bool Landed() { for (int y = gy - 6; y <= gy; y++) for (int dz = -5; dz <= 5; dz++) for (int dx = -5; dx <= 5; dx++) if (Blk(bi, f, sx + dx, y, sz + dz) == Water) return true; return false; }
        while (fall < 600 && !Landed()) { WaterSim.WaterTick(1 / 60.0); fall++; }
        WaterSim.WAct.Clear(); WaterSim.wactQ.Clear(); SetCell(bi, f, sx, gy, sz, 0);
        // a dry spot (no lake within 12 blocks)
        int px = 0, pz = 0, py = 0;
        foreach (var (ox, oz) in new[] { (-20, -20), (20, -20), (-20, 20), (0, -24), (-24, 0), (24, 0), (0, 24) })
        {
            px = x0 + ox; pz = z0 + oz; py = y0 + 30; while (py > y0 - 30 && Blk(bi, f, px, py - 1, pz) == 0) py--;
            bool wet = false; for (int dz = -12; dz <= 12 && !wet; dz++) for (int dx = -12; dx <= 12 && !wet; dx++) for (int dy = -3; dy <= 6; dy++) if (Blk(bi, f, px + dx, py + dy, pz + dz) == Water) { wet = true; break; }
            if (!wet) break;
        }
        var pre = new HashSet<(int, int, int)>(); for (int dz = -12; dz <= 12; dz++) for (int dx = -12; dx <= 12; dx++) for (int dy = -3; dy <= 6; dy++) if (Blk(bi, f, px + dx, py + dy, pz + dz) == Water) pre.Add((dx, dy, dz));
        for (int y = 0; y < 4; y++) SetCell(bi, f, px, py + y, pz, Water, 8);
        for (int i = 0; i < 60; i++) WaterSim.WaterTick(1 / 60.0);
        int reach = 0; for (int dz = -12; dz <= 12; dz++) for (int dx = -12; dx <= 12; dx++) for (int dy = -3; dy <= 3; dy++) if (Blk(bi, f, px + dx, py + dy, pz + dz) == Water && !pre.Contains((dx, dy, dz))) reach = Math.Max(reach, Math.Abs(dx) + Math.Abs(dz));
        Console.WriteLine($"BENCHWATER speed: falls 12 blocks in {fall / 60.0:0.00} s ({12 * 60.0 / Math.Max(1, fall):0} blocks/s), a 4-high column spreads {reach} blocks in 1 s (water there before: {pre.Count})");
        WaterSim.WAct.Clear(); WaterSim.wactQ.Clear();
        // the same pool again as the game runs it: 60 frames a second, the tick's work spread over frames within the frame budget
        for (int y = 0; y < 4; y++) for (int z = -8; z < 8; z++) for (int x = -8; x < 8; x++) if (Blk(bi, f, x0 + x, y0 + 6 + y, z0 + z) == 0) SetCell(bi, f, x0 + x, y0 + 6 + y, z0 + z, Water, 8);
        int frames = 0, slow = 0; double fworst = 0, ftotal = 0;
        while (WaterSim.WAct.Count > 0 && frames < 60 * 120)
        {
            long t0 = Stopwatch.GetTimestamp(); WaterSim.WaterTick(1 / 60.0); double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            ftotal += ms; fworst = Math.Max(fworst, ms); frames++; if (ms > 3) slow++;
        }
        Console.WriteLine($"BENCHWATER in game (60 fps): {frames / 60.0:0.0} s until still, {ftotal / Math.Max(1, frames):0.00} ms/frame, worst frame {fworst:0.00} ms, {slow} of {frames} frames over 3 ms (budget {WaterSim.FrameBudgetMs} ms)");
    }

    /// <summary>River columns (and waterfall columns: a neighbour column is a lower river stretch) in a (2r+1)^2 block of chunk columns.</summary>
    internal static (int cols, int falls, double near, int fx, int fz) RiverStats(Band B, int bi, int f, int ccx, int ccz, int r)
    {
        int cols = 0, falls = 0, fx = 0, fz = 0; double near = double.PositiveInfinity, drop = 0; // fx, fz: the highest waterfall
        for (int cz = ccz - r; cz <= ccz + r; cz++) for (int cx = ccx - r; cx <= ccx + r; cx++)
            {
                if (cx < 1 || cz < 1 || cx >= B.fc - 1 || cz >= B.fc - 1) continue;
                var c = ColsOf(B, f, cx, cz);
                for (int i = 0; i < 1024; i++)
                {
                    if (float.IsNaN(c.rs[i])) continue; cols++;
                    int x = cx * 32 + (i & 31), z = cz * 32 + (i >> 5); double dd = Math.Sqrt(Math.Pow(x - ccx * 32 - 16, 2) + Math.Pow(z - ccz * 32 - 16, 2)); if (dd < near) near = dd;
                    foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nx = x + dx, nz = z + dz; var nc = ColsOf(B, f, nx >> 5, nz >> 5); float ns = nc.rs[(nx & 31) + 32 * (nz & 31)];
                        if (!float.IsNaN(ns) && ns < c.rs[i]) { falls++; if (c.rs[i] - ns > drop) { drop = c.rs[i] - ns; fx = x; fz = z; } break; }
                    }
                }
            }
        return (cols, falls, near, fx, fz);
    }

    /// <summary>Water cells in a (2r+1)^2 block of chunk columns from sea level down to the cave floor, and how many touch air beside or below.</summary>
    internal static (int water, int loose) Unsettled(Band B, int bi, int f, int ccx, int ccz, int r)
    {
        int cyTop = (int)Math.Floor((R + 2 - B.inner) / CS), cyBot = Math.Max(0, (int)Math.Floor((R - CAVB - B.inner) / CS));
        var data = new Dictionary<long, ushort[]>();
        ushort[] D(int cx, int cy, int cz)
        {
            if (cx < 0 || cz < 0 || cy < 0 || cx >= B.fc || cz >= B.fc || cy >= B.rows / CS) return null;
            long k = ck(bi, f, cx, cy, cz);
            if (!data.TryGetValue(k, out var d)) { var ch = new Chunk { key = k, band = bi, face = f, cx = cx, cy = cy, cz = cz }; ChunkGenerator.Ensure(ch); data[k] = d = ch.d; }
            return d;
        }
        int At(int x, int y, int z) { var d = D(x >> 5, y >> 5, z >> 5); return d == null ? -1 : d[(x & 31) + 32 * ((z & 31) + 32 * (y & 31))]; }
        int water = 0, loose = 0;
        for (int cz = ccz - r; cz <= ccz + r; cz++) for (int cx = ccx - r; cx <= ccx + r; cx++) for (int cy = cyBot; cy <= cyTop; cy++)
                {
                    var d = D(cx, cy, cz); if (d == null) continue;
                    for (int i = 0; i < d.Length; i++)
                    {
                        if (d[i] != Water) continue;
                        int x = cx * 32 + (i & 31), z = cz * 32 + ((i >> 5) & 31), y = cy * 32 + (i >> 10); water++;
                        bool Open(int nx, int ny, int nz)
                        {
                            if (At(nx, ny, nz) != 0) return false;
                            var c0 = ColsOf(B, f, x >> 5, z >> 5); float s0 = c0.rs[(x & 31) + 32 * (z & 31)];
                            var c1 = ColsOf(B, f, nx >> 5, nz >> 5); float s1 = c1.rs[(nx & 31) + 32 * (nz & 31)];
                            return !(!float.IsNaN(s0) && !float.IsNaN(s1) && s1 < s0); // a waterfall face is meant to be open
                        }
                        if (Open(x, y - 1, z) || Open(x + 1, y, z) || Open(x - 1, y, z) || Open(x, y, z + 1) || Open(x, y, z - 1)) loose++;
                    }
                }
        return (water, loose);
    }
}
