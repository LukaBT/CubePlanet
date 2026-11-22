using static CubePlanet.Cube;
using static CubePlanet.Blocks;

namespace CubePlanet;

public static partial class W
{
    // ---------- chunk data generation ----------
    // ore veins: (block id, min depth, max depth below the local surface, veins per 32^3 chunk, vein size, mountains only). Deepslate variant = id + 9.
    static readonly (int id, int dpMin, int dpMax, double tries, int size, bool mtn)[] ORES =
    {
        (58, 4, 200, 3.4, 9, false),   // coal
        (64, 6, 120, 2.2, 8, false),   // copper
        (59, 8, 240, 2.8, 6, false),   // iron
        (60, 30, 280, 1.1, 5, false),  // gold
        (62, 55, 500, 1.3, 5, false),  // redstone
        (63, 22, 320, 1.0, 5, false),  // lapis
        (61, 70, 700, 0.9, 4, false),  // diamond
        (65, 3, 70, 1.6, 2, true),     // emerald (mountains only)
    };
    static void ApplyOres(Chunk ch, byte[] d, Col c, double r0, int f)
    {
        uint st = unchecked((uint)(HH(ch.band * 6 + f, ch.cx, ch.cy * 3 + 1, ch.cz * 5 + 2) * 4294967296.0)) | 1u;
        double Rn() { st ^= st << 13; st ^= st >> 17; st ^= st << 5; return st / 4294967296.0; }
        foreach (var o in ORES)
        {
            int n = (int)o.tries; if (Rn() < o.tries - n) n++;
            for (int t = 0; t < n; t++)
            {
                int x = (int)(Rn() * CS), y = (int)(Rn() * CS), z = (int)(Rn() * CS), i = x + CS * z; double dp = R + c.h[i] - (r0 + y + .5);
                if (dp < o.dpMin || dp > o.dpMax) continue;
                if (o.mtn && c.b[i] != 11 && c.b[i] != 13 && c.b[i] != 20) continue;
                int cx = x, cy = y, cz = z;
                for (int k = 0; k < o.size; k++)
                {
                    int j = cx + CS * (cz + CS * cy), b = d[j];
                    if (b == 2) d[j] = (byte)o.id; else if (b == 66) d[j] = (byte)(o.id + 9);
                    cx += (int)(Rn() * 3) - 1; cy += (int)(Rn() * 3) - 1; cz += (int)(Rn() * 3) - 1;
                    if (cx < 0 || cx >= CS || cy < 0 || cy >= CS || cz < 0 || cz >= CS) break;
                }
            }
        }
    }

    /// <summary>Make sure the chunk's block data exists. If a worker thread is generating it right now this waits for it instead of doing the work twice.</summary>
    public static void Ensure(Chunk ch)
    {
        while (true)
        {
            if (Volatile.Read(ref ch.d) != null) return;
            if (Interlocked.CompareExchange(ref ch.gst, 1, 0) == 0)
            {
                try { Generate(ch); } catch { ch.gst = 0; throw; }
                return;
            }
            Thread.Yield();
        }
    }

    // ---------- background generation ----------
    // Worker threads generate chunk block data (terrain, caves, ores, features) nearest-first from a list the render thread publishes.
    // Everything Generate() touches is thread-safe: result slots and caches are per thread, player edits are read under EditLock, the chunk itself is claimed via Chunk.gst.
    // Meshing jobs come first (their neighbours are already generated, so a finished mesh shows up right away), then generation.
    static readonly object wlock = new(); static Chunk[] wlist = Array.Empty<Chunk>(), mlist = Array.Empty<Chunk>(); static int wnext, mnext; static bool wstarted;
    public sealed class MeshJobResult { public Chunk ch; public BuildResult r; public List<long> wait; public bool failed; }
    public static readonly System.Collections.Concurrent.ConcurrentQueue<MeshJobResult> meshDone = new();
    static void StartWorkers()
    {
        if (wstarted) return; wstarted = true;
        int n = Math.Clamp(Environment.ProcessorCount - 2, 1, 14);
        for (int i = 0; i < n; i++) new Thread(GenWorker) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "chunkwork" + i }.Start();
    }
    public static void SetGenList(Chunk[] list) { StartWorkers(); lock (wlock) { wlist = list; wnext = 0; Monitor.PulseAll(wlock); } }
    public static void SetMeshList(Chunk[] list) { StartWorkers(); lock (wlock) { mlist = list; mnext = 0; Monitor.PulseAll(wlock); } }
    static void GenWorker()
    {
        while (true)
        {
            Chunk c = null; bool mesh = false;
            lock (wlock)
            {
                while (mnext < mlist.Length) { var x = mlist[mnext++]; if (Volatile.Read(ref x.mst) == 0) { c = x; mesh = true; break; } }
                if (c == null)
                    while (wnext < wlist.Length)
                    {
                        var x = wlist[wnext++];
                        if (Volatile.Read(ref x.d) == null && x.gst == 0) { c = x; break; }
                    }
                if (c == null) { Monitor.Wait(wlock, 50); continue; }
            }
            if (mesh)
            {
                if (Interlocked.CompareExchange(ref c.mst, 1, 0) != 0) continue;
                ResetBlkCache();
                long tp = System.Diagnostics.Stopwatch.GetTimestamp();
                try { var r = Build(c, out var wl); meshDone.Enqueue(new MeshJobResult { ch = c, r = r, wait = wl }); }
                catch { meshDone.Enqueue(new MeshJobResult { ch = c, failed = true }); }
                Interlocked.Add(ref profMeshTicks, System.Diagnostics.Stopwatch.GetTimestamp() - tp); Interlocked.Increment(ref profMeshN);
                continue;
            }
            if (Interlocked.CompareExchange(ref c.gst, 1, 0) != 0) continue;
            try { Generate(c); } catch { c.gfail = true; c.gst = 0; }
        }
    }

    public static long profMeshTicks, profMeshN, profGenTicks, profGenN, profSyncGen, profScanTicks, profScanN, profBuildTicks, profBuildN, profRebuild;
    static void Generate(Chunk ch)
    {
        long t0p = System.Diagnostics.Stopwatch.GetTimestamp();
        if (Environment.CurrentManagedThreadId == mainTid) Interlocked.Increment(ref profSyncGen);
        try { GenerateCore(ch); } finally { Interlocked.Add(ref profGenTicks, System.Diagnostics.Stopwatch.GetTimestamp() - t0p); Interlocked.Increment(ref profGenN); }
    }
    public static int mainTid;
    static void GenerateCore(Chunk ch)
    {
        Band B = bands[ch.band]; int f = ch.face;
        var d = new byte[32768];
        bool has; lock (EditLock) has = ecount.ContainsKey(ch.key);
        Col c = ColsOf(B, f, ch.cx, ch.cz);
        double r0 = B.inner + ch.cy * CS; int salt = ch.cx * 7919 + ch.cz * 104729;
        List<Feat> fl = null;
        if (!has)
        {
            if (r0 > R + Math.Max(c.hi, 0) + 1)
            {
                fl = Feats(ch, B, f);
                if (fl.Count == 0) { ch.emp = true; Volatile.Write(ref ch.d, d); return; }
            }
            else if (r0 + CS <= R - CAVB && r0 + CS <= R + c.lo)
            {
                // buried below every cave: solid stone turning to deepslate, plus ore veins
                double sMin = R + c.lo - (r0 + CS), sMax = R + c.hi - r0;
                if (sMin >= 72) Array.Fill(d, (byte)66);
                else if (sMax < 56) Array.Fill(d, (byte)2);
                else
                    for (int z = 0; z < CS; z++) for (int x = 0; x < CS; x++) for (int y = 0; y < CS; y++)
                            { int i = x + CS * z; double r = r0 + y + .5; d[x + CS * (z + CS * y)] = (byte)StoneAt(R + c.h[i] - r, i, r, salt); }
                ApplyOres(ch, d, c, r0, f); ch.solid = true; Volatile.Write(ref ch.d, d); return;
            }
        }
        double[] NG = null, aq = null;
        for (int z = 0; z < CS; z++)
            for (int x = 0; x < CS; x++)
            {
                int i = x + CS * z;
                for (int y = 0; y < CS; y++)
                {
                    double r = r0 + y + .5; int id = BaseBlk(c, i, r, salt);
                    if (id != 0 && Cavable(c, i, r))
                    {
                        if (NG == null)
                        {
                            NG = new double[729];
                            for (int ky = 0; ky < 9; ky++) for (int kz = 0; kz < 9; kz++) for (int kx = 0; kx < 9; kx++)
                                        NG[(ky * 9 + kz) * 9 + kx] = NodeV(B, f, ch.cx * 8 + kx, ch.cy * 8 + ky, ch.cz * 8 + kz);
                            // aquifers: a water level per 16x16 column block; cave air below it is flooded
                            aq = new double[4];
                            for (int a = 0; a < 4; a++)
                            {
                                int bx = ch.cx * CS + (a & 1) * 16 + 8, bz = ch.cz * CS + (a >> 1) * 16 + 8; V3 t = GpA(f, -PI / 4 + bz * B.d, -PI / 4 + bx * B.d, R); var of = OFF[26];
                                aq[a] = Math.Min(-2, -26 + 28 * Noise3(t.X * .014 + of[0], t.Y * .014 + of[1], t.Z * .014 + of[2]));
                            }
                        }
                        int kx2 = x >> 2, ky2 = y >> 2, kz2 = z >> 2, q = (ky2 * 9 + kz2) * 9 + kx2;
                        if (Tri(NG[q], NG[q + 1], NG[q + 81], NG[q + 82], NG[q + 9], NG[q + 10], NG[q + 90], NG[q + 91], (x & 3) / 4.0, (y & 3) / 4.0, (z & 3) / 4.0) > 0)
                        {
                            double rel = r - R; id = rel < -(CAVB - 20) ? 75 : rel < aq[(z >> 4) * 2 + (x >> 4)] ? 9 : 0; // lava lakes at the bottom, flooded caves, else air
                        }
                    }
                    d[x + CS * (z + CS * y)] = (byte)id;
                }
            }
        ApplyOres(ch, d, c, r0, f);
        if (fl == null && r0 + CS > R - 24) fl = Feats(ch, B, f);
        if (fl != null && fl.Count > 0) ApplyFeats(ch, d, fl);
        if (r0 + CS > R && r0 <= R + c.hi + 2) Cover(ch, d, c, B, f);
        if (r0 + CS > R - 34 && r0 <= R + 2) CoverSea(ch, d, c, B, f);
        if (has)
            lock (EditLock)
                for (int z = 0; z < CS; z++) for (int x = 0; x < CS; x++) for (int y = 0; y < CS; y++)
                        {
                            long k = ekOf(ch.band, f, ch.cx * CS + x, ch.cy * CS + y, ch.cz * CS + z);
                            if (edits.TryGetValue(k, out byte e)) d[x + CS * (z + CS * y)] = e;
                        }
        Volatile.Write(ref ch.d, d);
    }

    // ---------- Cartesian core chunks ----------
    public static void CoreScan()
    {
        double range = RD * CS + CS, Hh = 27.7;
        for (int a = -4; a < 4; a++) for (int b = -4; b < 4; b++) for (int c = -4; c < 4; c++)
                {
                    int[] lo = { a * 32, b * 32, c * 32 };
                    double[] md = new double[3];
                    for (int i = 0; i < 3; i++) md[i] = lo[i] > 0 ? lo[i] : lo[i] + 32 < 0 ? -(lo[i] + 32) : 0;
                    if (Math.Sqrt(md[0] * md[0] + md[1] * md[1] + md[2] * md[2]) > CORE + 1) continue;
                    double mx = lo[0] + 16, my = lo[1] + 16, mz = lo[2] + 16;
                    if (Math.Sqrt((mx - C.x) * (mx - C.x) + (my - C.y) * (my - C.y) + (mz - C.z) * (mz - C.z)) - Hh > range) continue;
                    long key = cKey(a, b, c);
                    if (chunks.ContainsKey(key)) continue;
                    AddChunk(new Chunk { key = key, core = true, cc = new[] { a, b, c }, dirty = true, mx = mx, my = my, mz = mz, cr = Hh + 2 });
                }
    }
    public static void CoreEnsure(Chunk ch)
    {
        if (ch.d != null) return;
        int a = ch.cc[0] * 32, b = ch.cc[1] * 32, c = ch.cc[2] * 32; var d = new byte[32768]; int n = 0;
        for (int y = 0; y < 32; y++) for (int z = 0; z < 32; z++) for (int x = 0; x < 32; x++)
                {
                    int id = CoreCell(a + x, b + y, c + z); d[x + 32 * (z + 32 * y)] = (byte)id; if (id != 0) n++;
                }
        ch.d = d; ch.empty = n == 0;
    }
    // is the polar block next to the core surface opaque? (polar stone at that depth unless the player dug there)
    static bool CoreOpqFar(int i, int j, int k)
    {
        double x = i + .5, y = j + .5, z = k + .5;
        if (x * x + y * y + z * z < CORE * CORE) return false;
        if (edits.Count == 0) return true;
        int bi = P2b(x, y, z); if (bi < 0) return false;
        if (!ecount.ContainsKey(ck(bi, PF, PX >> 5, PY >> 5, PZ >> 5))) return true;
        return OPQ[Blk(bi, PF, PX, PY, PZ)] != 0;
    }

    // ---------- chunk loader ----------
    class Pend { public int bi, f, cx, cy, cz; public long key; public double dd; }
    static List<Pend> pending = new(); static int pi;
    public static V3 lastScan = new(1e9, 0, 0);
    public static int frameNo;
    public static long chunksVer, meshVer; // bumped when chunks appear/disappear or a mesh is (re)uploaded

    static Task<List<Pend>> scanTask;
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
    static void ApplyScan()
    {
        if (scanTask == null || !scanTask.IsCompleted) return;
        var t = scanTask; scanTask = null; if (t.IsFaulted) return;
        pending = t.Result; pi = 0;
        double far = RD * CS + 2 * CS; var drop = new List<Chunk>();
        foreach (var ch in chunks.Values)
            if (Math.Sqrt((ch.mx - C.x) * (ch.mx - C.x) + (ch.my - C.y) * (ch.my - C.y) + (ch.mz - C.z) * (ch.mz - C.z)) - ch.cr > far) drop.Add(ch);
        foreach (var ch in drop) FreeChunk(ch);
    }
    static List<Pend> ScanCore(double Cx, double Cy, double Cz, int RDs, HashSet<long> ec)
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
