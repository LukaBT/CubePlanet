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
        Console.WriteLine($"BENCHWATER generated water around spawn: {water} cells, {loose} next to air ({100.0 * loose / Math.Max(1, water):0.00} %)");

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
                        if (At(x, y - 1, z) == 0 || At(x + 1, y, z) == 0 || At(x - 1, y, z) == 0 || At(x, y, z + 1) == 0 || At(x, y, z - 1) == 0) loose++;
                    }
                }
        return (water, loose);
    }
}
