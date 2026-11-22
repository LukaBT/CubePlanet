using System.Diagnostics;

namespace CubePlanet;

/// <summary>--benchedit: headless cost of block edits (set block + re-mesh every chunk it marks dirty, as the main thread does in the frame of the edit).</summary>
public static class EditBench
{
    public static void Run()
    {
        World.InitWorldData(1200, 1337); ChunkGenerator.mainTid = Environment.CurrentManagedThreadId;
        V3 c = new(C.x, C.y, C.z), up = M.Nrm(c);
        V3 g = c; for (int i = 0; i < 400 && GetAt(g.X - up.X * .5, g.Y - up.Y * .5, g.Z - up.Z * .5) == 0; i++) g -= up;
        int bi = BlockAccess.P2b(g.X, g.Y, g.Z); int f = PF, x0 = PX, y0 = PY, z0 = PZ; Band B = bands[bi];
        // load the chunks around the spot like the game does
        for (int dy = -2; dy <= 2; dy++) for (int dz = -2; dz <= 2; dz++) for (int dx = -2; dx <= 2; dx++)
                {
                    int cx = (x0 >> 5) + dx, cy = (y0 >> 5) + dy, cz = (z0 >> 5) + dz;
                    if (cx < 0 || cz < 0 || cy < 0 || cx >= B.fc || cz >= B.fc || cy >= B.rows / CS) continue;
                    var ch = new Chunk { key = ck(bi, f, cx, cy, cz), band = bi, face = f, cx = cx, cy = cy, cz = cz };
                    AddChunk(ch); ChunkGenerator.Ensure(ch); ch.built = true; ch.dirty = false;
                }
        var rnd = new Random(1); var sw = new Stopwatch(); long setT = 0, buildT = 0; int builds = 0, relit = 0, n = 200; double worst = 0;
        foreach (var ch in chunks.Values) Mesher.Build(ch, out _); // warm up
        for (int e = 0; e < n; e++)
        {
            int x = x0 + rnd.Next(-6, 7), z = z0 + rnd.Next(-6, 7), y = y0 + rnd.Next(-1, 3);
            long t0 = Stopwatch.GetTimestamp();
            SetCell(bi, f, x, y, z, Blk(bi, f, x, y, z) == 0 ? Stone : 0);
            long t1 = Stopwatch.GetTimestamp(); setT += t1 - t0;
            foreach (var ch in chunks.Values.Where(h => h.dirty).ToList()) { Mesher.Build(ch, out _); ch.dirty = false; ch.relight = false; builds++; }
            foreach (var ch in chunks.Values) if (ch.relight) { ch.relight = false; relit++; } // background re-light (worker threads)
            long t2 = Stopwatch.GetTimestamp(); buildT += t2 - t1; worst = Math.Max(worst, (t2 - t0) * 1000.0 / Stopwatch.Frequency);
        }
        double ms(long t) => t * 1000.0 / Stopwatch.Frequency;
        Console.WriteLine($"BENCHEDIT {n} edits: set {ms(setT) / n:0.000} ms/edit, {builds / (double)n:0.0} chunk meshes/edit at {ms(buildT) / Math.Max(1, builds):0.00} ms each, " +
                          $"{ms(setT + buildT) / n:0.00} ms/edit total, worst {worst:0.00} ms; {relit / (double)n:0.0} chunks/edit re-lit in the background");
        // autosave cost once every loaded chunk (5x5x5 around the spot) has been edited
        foreach (var ch in chunks.Values.ToList()) SetCell(bi, f, ch.cx * 32 + 5, ch.cy * 32 + 5, ch.cz * 32 + 5, Stone);
        string dir = Path.Combine(Path.GetTempPath(), "cubeplanet_benchedit_world"); if (Directory.Exists(dir)) Directory.Delete(dir, true);
        WorldSave.Current = dir; WorldSave.CurrentName = "bench"; WorldSave.Tick(61); WorldSave.WaitPending(); // first save: one-time JIT warm-up
        long s0 = Stopwatch.GetTimestamp(); WorldSave.Tick(61); long s1 = Stopwatch.GetTimestamp(); WorldSave.WaitPending(); long s2 = Stopwatch.GetTimestamp();
        WorldSave.Current = null;
        Console.WriteLine($"BENCHEDIT autosave of {ecount.Count(kv => kv.Value > 0)} edited chunks: {ms(s1 - s0):0.0} ms on the main thread, {ms(s2 - s1):0.0} ms more in the background, " +
                          $"{Directory.GetFiles(Path.Combine(dir, "regions")).Length} region files");
        Directory.Delete(dir, true);
    }
}
