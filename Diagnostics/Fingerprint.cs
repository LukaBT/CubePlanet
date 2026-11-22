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
        PlayerPhysics.P.on = false; // same camera as --bench (free camera 30 blocks above the spawn point)
        World.InitWorldData(radius, seed);
        ChunkGenerator.mainTid = Environment.CurrentManagedThreadId;
        double cx = Camera.C.x, cy = Camera.C.y, cz = Camera.C.z;

        // the same chunk selection as the loader (ScanCore), without the "already loaded" check
        var list = new List<Chunk>(); var seen = new HashSet<long>();
        int half = 16; double range = rd * World.CS; int n = (int)Math.Ceiling(range / half);
        for (int i = -n; i <= n; i++) for (int j = -n; j <= n; j++) for (int k = -n; k <= n; k++)
                {
                    int ox = i * half, oy = j * half, oz = k * half; double dd = Math.Sqrt((double)ox * ox + (double)oy * oy + (double)oz * oz);
                    if (dd > range) continue;
                    double px = cx + ox, py = cy + oy, pz = cz + oz, rr = Math.Sqrt(px * px + py * py + pz * pz);
                    if (rr > World.R + World.HMAX + World.CS || rr < World.CORE - World.CS) continue;
                    int bi = BlockAccess.P2b(px, py, pz); if (bi < 0) continue;
                    int f = BlockAccess.PF, ccx = BlockAccess.PX >> 5, ccy = BlockAccess.PY >> 5, ccz = BlockAccess.PZ >> 5;
                    long key = BlockAccess.ck(bi, f, ccx, ccy, ccz);
                    if (!seen.Add(key)) continue;
                    double rMin = World.bands[bi].inner + ccy * World.CS;
                    if (rMin > World.R + World.HMAX || rMin + World.CS < World.R - World.DEP || rMin + World.CS <= World.R - World.CAVB - 2) continue;
                    Band B = World.bands[bi];
                    if (ccx >= B.fc || ccz >= B.fc || ccy >= B.rows / World.CS) continue;
                    list.Add(new Chunk { key = key, band = bi, face = f, cx = ccx, cy = ccy, cz = ccz });
                }
        for (int a = -4; a < 4; a++) for (int b = -4; b < 4; b++) for (int c = -4; c < 4; c++)
                    list.Add(new Chunk { key = CoreGrid.cKey(a, b, c), core = true, cc = new[] { a, b, c } });
        list.Sort((p, q) => p.key.CompareTo(q.key));

        Parallel.ForEach(list, ch => { BlockAccess.ResetBlkCache(); if (ch.core) CoreGrid.CoreEnsure(ch); else ChunkGenerator.Ensure(ch); });

        // FNV-1a 64 over (key, block data) in key order
        ulong h = 14695981039346656037UL; long blocks = 0;
        void Add(byte v) { h ^= v; h *= 1099511628211UL; }
        foreach (var ch in list)
        {
            for (int s = 0; s < 64; s += 8) Add((byte)(ch.key >> s));
            foreach (ushort v in ch.d) { Add((byte)v); if (v > 255) Add((byte)(v >> 8)); if (v != 0) blocks++; } // low byte (+ high byte when used): same hash as the old 1-byte data
        }
        Console.WriteLine($"FINGERPRINT radius {World.R} seed {seed} rd {rd}: {list.Count} chunks, {blocks} non-air blocks, hash {h:X16} ({sw.Elapsed.TotalSeconds:0.0} s)");
    }
}
