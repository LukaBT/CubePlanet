namespace CubePlanet;

/// <summary>
/// Headless "world fingerprint" (no window, no GPU): generates the block data of every chunk the loader would create around the
/// bench spawn point for a fixed radius / seed / render distance, plus the Cartesian core chunks, and prints one hash over all of it.
/// Refactors must keep this hash identical; it is the safety net for the restructure (see PLAN.md, phase 0).
/// Usage: CubePlanet --fingerprint [--radius N] [--seed N] [--fprd CHUNKS]
/// </summary>
public static class Fingerprint
{
    public static void Run(double radius, int seed, int rd)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        W.P.on = false; // same camera as --bench (free camera 30 blocks above the spawn point)
        W.InitWorldData(radius, seed);
        W.mainTid = Environment.CurrentManagedThreadId;
        double cx = W.C.x, cy = W.C.y, cz = W.C.z;

        // the same chunk selection as the loader (ScanCore), without the "already loaded" check
        var list = new List<Chunk>(); var seen = new HashSet<long>();
        int half = 16; double range = rd * W.CS; int n = (int)Math.Ceiling(range / half);
        for (int i = -n; i <= n; i++) for (int j = -n; j <= n; j++) for (int k = -n; k <= n; k++)
                {
                    int ox = i * half, oy = j * half, oz = k * half; double dd = Math.Sqrt((double)ox * ox + (double)oy * oy + (double)oz * oz);
                    if (dd > range) continue;
                    double px = cx + ox, py = cy + oy, pz = cz + oz, rr = Math.Sqrt(px * px + py * py + pz * pz);
                    if (rr > W.R + W.HMAX + W.CS || rr < W.CORE - W.CS) continue;
                    int bi = W.P2b(px, py, pz); if (bi < 0) continue;
                    int f = W.PF, ccx = W.PX >> 5, ccy = W.PY >> 5, ccz = W.PZ >> 5;
                    long key = W.ck(bi, f, ccx, ccy, ccz);
                    if (!seen.Add(key)) continue;
                    double rMin = W.bands[bi].inner + ccy * W.CS;
                    if (rMin > W.R + W.HMAX || rMin + W.CS < W.R - W.DEP || rMin + W.CS <= W.R - W.CAVB - 2) continue;
                    Band B = W.bands[bi];
                    if (ccx >= B.fc || ccz >= B.fc || ccy >= B.rows / W.CS) continue;
                    list.Add(new Chunk { key = key, band = bi, face = f, cx = ccx, cy = ccy, cz = ccz });
                }
        for (int a = -4; a < 4; a++) for (int b = -4; b < 4; b++) for (int c = -4; c < 4; c++)
                    list.Add(new Chunk { key = W.cKey(a, b, c), core = true, cc = new[] { a, b, c } });
        list.Sort((p, q) => p.key.CompareTo(q.key));

        Parallel.ForEach(list, ch => { W.ResetBlkCache(); if (ch.core) W.CoreEnsure(ch); else W.Ensure(ch); });

        // FNV-1a 64 over (key, block data) in key order
        ulong h = 14695981039346656037UL; long blocks = 0;
        void Add(byte v) { h ^= v; h *= 1099511628211UL; }
        foreach (var ch in list)
        {
            for (int s = 0; s < 64; s += 8) Add((byte)(ch.key >> s));
            foreach (byte v in ch.d) { Add(v); if (v != 0) blocks++; }
        }
        Console.WriteLine($"FINGERPRINT radius {W.R} seed {seed} rd {rd}: {list.Count} chunks, {blocks} non-air blocks, hash {h:X16} ({sw.Elapsed.TotalSeconds:0.0} s)");
    }
}
