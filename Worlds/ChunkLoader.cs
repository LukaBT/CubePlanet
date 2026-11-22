namespace CubePlanet;

/// <summary>Decides which chunks exist around the camera: background scan, nearest-first creation, dropping far chunks.</summary>
public static class ChunkLoader
{
    // ---------- chunk loader ----------
    internal class Pend { public int bi, f, cx, cy, cz; public long key; public double dd; }
    internal static List<Pend> pending = new(); internal static int pi;
    public static V3 lastScan = new(1e9, 0, 0);
    public static int frameNo;
    public static long chunksVer, meshVer; // bumped when chunks appear/disappear or a mesh is (re)uploaded

    internal static Task<List<Pend>> scanTask;
    /// <summary>Starts a background scan around the camera (no-op while one is running); ApplyScan() picks the result up.</summary>
    public static void Scan()
    {
        if (scanTask != null) return;
        HashSet<long> ec; lock (EditLock) ec = new HashSet<long>(ecount.Keys);
        double cx = C.x, cy = C.y, cz = C.z; int rd = RD;
        scanTask = Task.Run(() =>
        {
            long t0p = System.Diagnostics.Stopwatch.GetTimestamp();
            try { return ScanCore(cx, cy, cz, rd, ec); } finally { Interlocked.Add(ref profScanTicks, System.Diagnostics.Stopwatch.GetTimestamp() - t0p); Interlocked.Increment(ref profScanN); }
        });
    }
    internal static void ApplyScan()
    {
        if (scanTask == null || !scanTask.IsCompleted) return;
        var t = scanTask; scanTask = null; if (t.IsFaulted) return;
        pending = t.Result; pi = 0;
        double far = RD * CS + 2 * CS; var drop = new List<Chunk>();
        foreach (var ch in chunks.Values)
            if (Math.Sqrt((ch.mx - C.x) * (ch.mx - C.x) + (ch.my - C.y) * (ch.my - C.y) + (ch.mz - C.z) * (ch.mz - C.z)) - ch.cr > far) drop.Add(ch);
        foreach (var ch in drop) FreeChunk(ch);
    }
    internal static List<Pend> ScanCore(double Cx, double Cy, double Cz, int RDs, HashSet<long> ec)
    {
        var pending = new List<Pend>();
        var seen = new HashSet<long>(); int half = 16; double range = RDs * CS; int n = (int)Math.Ceiling(range / half);
        for (int i = -n; i <= n; i++) for (int j = -n; j <= n; j++) for (int k = -n; k <= n; k++)
                {
                    int ox = i * half, oy = j * half, oz = k * half; double dd = Math.Sqrt((double)ox * ox + (double)oy * oy + (double)oz * oz);
                    if (dd > range) continue;
                    double px = Cx + ox, py = Cy + oy, pz = Cz + oz;
                    double rr = Math.Sqrt(px * px + py * py + pz * pz);
                    if (rr > R + HMAX + CS || rr < CORE - CS) continue;
                    int bi = P2b(px, py, pz); if (bi < 0) continue;
                    long key = ck(bi, PF, PX >> 5, PY >> 5, PZ >> 5);
                    if (chunkMap.ContainsKey(key) || seen.Contains(key)) continue;
                    seen.Add(key);
                    double rMin = bands[bi].inner + (PY >> 5) * CS;
                    if (rMin > R + HMAX && !ec.Contains(key)) continue;
                    if (rMin + CS < R - DEP && !ec.Contains(key)) continue;
                    // pure stone below the deepest cave can never show a face (unless dug into): don't even create the chunk
                    if (rMin + CS <= R - CAVB - 2 && !ec.Contains(key)) continue;
                    pending.Add(new Pend { bi = bi, f = PF, cx = PX >> 5, cy = PY >> 5, cz = PZ >> 5, key = key, dd = dd });
                }
        pending.Sort((a, b) => a.dd.CompareTo(b.dd));
        return pending;
    }
    public static void LoadStep()
    {
        double dm = Math.Sqrt((C.x - lastScan.X) * (C.x - lastScan.X) + (C.y - lastScan.Y) * (C.y - lastScan.Y) + (C.z - lastScan.Z) * (C.z - lastScan.Z));
        ApplyScan();
        if (dm > 12 || frameNo % 90 == 0)
        {
            if (!C.orbit && Math.Sqrt(C.x * C.x + C.y * C.y + C.z * C.z) < R * 1.5 + 500) { Scan(); CoreScan(); lastScan = new V3(C.x, C.y, C.z); }
        }
        for (int c = 0; c < 64 && pi < pending.Count; pi++)
        {
            var m = pending[pi];
            if (chunks.ContainsKey(m.key)) continue;
            Band B = bands[m.bi];
            if (m.cx >= B.fc || m.cz >= B.fc || m.cy >= B.rows / CS) continue;
            var ch = new Chunk { key = m.key, band = m.bi, face = m.f, cx = m.cx, cy = m.cy, cz = m.cz };
            V3 t = Gp(B, m.f, m.cz * CS + 16, m.cx * CS + 16, B.inner + m.cy * CS + 16);
            ch.mx = t.X; ch.my = t.Y; ch.mz = t.Z;
            double w = CS * B.d * (B.inner + m.cy * CS + CS) * 1.7;
            ch.cr = .5 * Math.Sqrt(2 * w * w + CS * CS) + 2;
            AddChunk(ch); chunksVer++; c++;
        }
    }
}
