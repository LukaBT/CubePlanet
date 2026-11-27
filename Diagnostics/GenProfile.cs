namespace CubePlanet;

/// <summary>
/// --genprofile [--radius N] [--seed N] (no window): generates and then meshes the chunks around the spawn point on ONE thread (the same
/// selection as --fingerprint, render distance 12) and prints the time of each phase, so generation / meshing speed can be compared
/// between versions (a server will generate chunks for many players).
/// </summary>
public static class GenProfile
{
    public static void Run(double radius, int seed)
    {
        PlayerPhysics.P.on = false; World.InitWorldData(radius, seed); ChunkGenerator.mainTid = -1;
        double cx = Camera.C.x, cy = Camera.C.y, cz = Camera.C.z; int rd = 12;
        var list = new List<Chunk>(); var seen = new HashSet<long>(); int half = 16; double range = rd * World.CS; int n = (int)Math.Ceiling(range / half);
        for (int i = -n; i <= n; i++) for (int j = -n; j <= n; j++) for (int k = -n; k <= n; k++)
                {
                    int ox = i * half, oy = j * half, oz = k * half; if (Math.Sqrt((double)ox * ox + (double)oy * oy + (double)oz * oz) > range) continue;
                    double px = cx + ox, py = cy + oy, pz = cz + oz, rr = Math.Sqrt(px * px + py * py + pz * pz);
                    if (rr > World.R + World.HMAX + World.CS || rr < World.CORE - World.CS) continue;
                    int bi = BlockAccess.P2b(px, py, pz); if (bi < 0) continue;
                    int f = BlockAccess.PF, ccx = BlockAccess.PX >> 5, ccy = BlockAccess.PY >> 5, ccz = BlockAccess.PZ >> 5; long key = BlockAccess.ck(bi, f, ccx, ccy, ccz);
                    if (!seen.Add(key)) continue; double rMin = World.bands[bi].inner + ccy * World.CS;
                    if (rMin > World.R + World.HMAX || rMin + World.CS < World.R - World.DEP || rMin + World.CS <= World.R - World.CAVB - 2) continue;
                    Band B = World.bands[bi]; if (ccx >= B.fc || ccz >= B.fc || ccy >= B.rows / World.CS) continue;
                    list.Add(new Chunk { key = key, band = bi, face = f, cx = ccx, cy = ccy, cz = ccz });
                }
        list.Sort((p, q) => p.key.CompareTo(q.key));
        Array.Clear(ChunkGenerator.Prof); Array.Clear(Mesher.Prof);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var ch in list) { BlockAccess.ResetBlkCache(); World.AddChunk(ch); ChunkGenerator.Ensure(ch); }
        double gen = sw.Elapsed.TotalMilliseconds; double ms(long t) => t * 1000.0 / System.Diagnostics.Stopwatch.Frequency; var P = ChunkGenerator.Prof;
        Console.WriteLine($"GENPROFILE {list.Count} chunks, one thread: generation {gen:0} ms ({gen / list.Count:0.00} ms/chunk; {P[8]} sky, {P[9]} buried)");
        string[] names = { "terrain columns", "block fill", "cave noise", "cave water", "ores + oil", "features (trees, structures)", "ground cover", "buried fast path" };
        for (int i = 0; i < names.Length; i++) Console.WriteLine($"  {names[i],-30} {ms(P[i]),7:0} ms  {ms(P[i]) / gen * 100,5:0.0} %");
        sw.Restart(); int meshed = 0; foreach (var ch in list) { if (ch.emp || ch.d == null) continue; BlockAccess.ResetBlkCache(); Mesher.Build(ch, out _); meshed++; }
        double mesh = sw.Elapsed.TotalMilliseconds; var M2 = Mesher.Prof;
        Console.WriteLine($"  meshing {meshed} chunks: {mesh:0} ms ({mesh / Math.Max(1, meshed):0.00} ms/chunk)");
        string[] mn = { "neighbour data", "skylight", "blocks + faces", "boxes / plants", "pack" };
        for (int i = 0; i < mn.Length; i++) if (M2[i] > 0) Console.WriteLine($"  {"- " + mn[i],-30} {ms(M2[i]),7:0} ms  {ms(M2[i]) / mesh * 100,5:0.0} %");
        Console.WriteLine($"GENPROFILE total {gen + mesh:0} ms ({(gen + mesh) / list.Count:0.00} ms/chunk); this PC: {Environment.ProcessorCount} threads, {Math.Clamp(Environment.ProcessorCount - 2, 1, 14)} chunk workers");
    }
}
