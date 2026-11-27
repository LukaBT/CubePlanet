namespace CubePlanet;

/// <summary>Builds the block data of one chunk: terrain, caves, aquifers, ores, features, ground cover, player edits.</summary>
public static class ChunkGenerator
{
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

    internal static void Generate(Chunk ch)
    {
        long t0p = System.Diagnostics.Stopwatch.GetTimestamp();
        if (Environment.CurrentManagedThreadId == mainTid) Interlocked.Increment(ref profSyncGen);
        try { GenerateCore(ch); } finally { Interlocked.Add(ref profGenTicks, System.Diagnostics.Stopwatch.GetTimestamp() - t0p); Interlocked.Increment(ref profGenN); }
    }
    public static int mainTid;
    /// <summary>Time per phase (Stopwatch ticks, all workers) for --genprofile: 0 columns, 1 block fill, 2 cave noise, 3 cave water, 4 ores + oil,
    /// 5 features, 6 ground cover, 7 buried fast path; 8 / 9 = sky / buried chunks counted.</summary>
    public static readonly long[] Prof = new long[10];
    static long Ts() => System.Diagnostics.Stopwatch.GetTimestamp();
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)] internal static void GenerateCore(Chunk ch)
    {
        Band B = bands[ch.band]; int f = ch.face;
        // chunk block data lives on the pinned object heap: flying fast creates ~400 of these 64 KB arrays a second, and on the normal heap
        // every short garbage collection copied the surviving ones around (25 ms pauses); pinned arrays are never moved
        var d = GC.AllocateArray<ushort>(32768, pinned: true);
        bool has; lock (EditLock) has = ecount.ContainsKey(ch.key);
        // a chunk saved with the world is used as saved (then newer edits on top), not generated
        if (WorldSave.TryGetSaved(ch.key, out var savedD)) { Array.Copy(savedD, d, d.Length); goto edits; }
        long q0 = Ts(); Col c = ColsOf(B, f, ch.cx, ch.cz); Interlocked.Add(ref Prof[0], Ts() - q0); q0 = Ts();
        double r0 = B.inner + ch.cy * CS; int salt = ch.cx * 7919 + ch.cz * 104729;
        List<Feat> fl = null;
        if (!has)
        {
            if (r0 > R + Math.Max(c.hi, 0) + 1)
            {
                fl = Feats(ch, B, f); Interlocked.Add(ref Prof[5], Ts() - q0); Interlocked.Increment(ref Prof[8]); q0 = Ts();
                if (fl.Count == 0) { ch.emp = true; Volatile.Write(ref ch.d, d); return; }
            }
            else if (r0 + CS <= R - CAVB && r0 + CS <= R + c.lo)
            {
                // buried below every cave: solid stone turning to deepslate, plus ore veins
                double sMin = R + c.lo - (r0 + CS), sMax = R + c.hi - r0;
                if (sMin >= 72) Array.Fill(d, (ushort)Deepslate);
                else if (sMax < 56) Array.Fill(d, (ushort)Stone);
                else
                    for (int z = 0; z < CS; z++) for (int x = 0; x < CS; x++) for (int y = 0; y < CS; y++)
                            { int i = x + CS * z; double r = r0 + y + .5; d[x + CS * (z + CS * y)] = (ushort)StoneAt(R + c.h[i] - r, i, r, salt); }
                ApplyOres(ch, d, c, r0, f); ch.solid = !ApplyOil(ch, d, c, r0, f); Interlocked.Add(ref Prof[7], Ts() - q0); Interlocked.Increment(ref Prof[9]); Volatile.Write(ref ch.d, d); return;
            }
        }
        double[] NG = null; (double, double)[] aq = null; long caveT = 0;
        // the same blocks as Strata.BaseBlk + Cavable per cell, with the column's values held in locals (BaseBlk keeps the depth in a
        // [ThreadStatic] slot, slow to touch 3 x per cell) and nothing written for air (the array starts zeroed)
        for (int z = 0; z < CS; z++)
            for (int x = 0; x < CS; x++)
            {
                int i = x + CS * z; double hi = c.h[i], rsi = c.rs[i], rni = c.rn[i]; int top = c.t[i], fill = c.f[i], fd = c.fd[i]; bool ice = c.ice[i] != 0;
                double minS = hi < 3 ? 3 : 1.2; bool lowCoast = hi < 16;
                for (int y = 0; y < CS; y++)
                {
                    double r = r0 + y + .5, s = R + hi - r, rel = r - R; int id;
                    if (s <= 0)
                    {   // sea, river, air: never caves; above the sea and the river surface only air remains in this column
                        id = rel < 0 ? (ice && rel > -1 ? Ice : Water) : rel < rsi ? Water : 0; if (id != 0) { d[x + CS * (z + CS * y)] = (ushort)id; continue; }
                        if (rel >= 0) break;
                        continue;
                    }
                    id = s <= 1 ? top : s <= 1 + fd ? (fill == FillBands ? BANDS[(((int)Math.Floor(r - R) % 24) + 24) % 24] : fill) : StoneAt(s, i, r, salt);
                    if (id != 0 && rel > -CAVB && s >= minS && !(rel > rni - 40) && !(lowCoast && rel > -14))
                    {
                        if (NG == null)
                        {
                            long qn = Ts(); NG = new double[729];
                            for (int ky = 0; ky < 9; ky++) for (int kz = 0; kz < 9; kz++) for (int kx = 0; kx < 9; kx++)
                                        NG[(ky * 9 + kz) * 9 + kx] = NodeV(B, f, ch.cx * 8 + kx, ch.cy * 8 + ky, ch.cz * 8 + kz);
                            long qa = Ts(); Interlocked.Add(ref Prof[2], qa - qn);
                            // aquifers: one water level per region, stone barriers between regions (see Aquifers); shared by the whole column
                            aq = c.aq;
                            if (aq == null)
                            {
                                var a2 = new (double, double)[CS * CS];
                                for (int a = 0; a < CS * CS; a++) a2[a] = Aquifers.Column(f, -PI / 4 + (ch.cz * CS + a / CS + .5) * B.d, -PI / 4 + (ch.cx * CS + a % CS + .5) * B.d);
                                c.aq = aq = a2;
                            }
                            long qe = Ts(); Interlocked.Add(ref Prof[3], qe - qa); caveT += qe - qn;
                        }
                        int kx2 = x >> 2, ky2 = y >> 2, kz2 = z >> 2, q = (ky2 * 9 + kz2) * 9 + kx2;
                        if (Tri(NG[q], NG[q + 1], NG[q + 81], NG[q + 82], NG[q + 9], NG[q + 10], NG[q + 90], NG[q + 91], (x & 3) / 4.0, (y & 3) / 4.0, (z & 3) / 4.0) > 0)
                        {
                            id = Aquifers.CaveFill(rel, id, aq[i]); // lava lakes at the bottom, barriers, flooded caves, else air
                        }
                    }
                    if (id != 0) d[x + CS * (z + CS * y)] = (ushort)id;
                }
            }
        Interlocked.Add(ref Prof[1], Ts() - q0 - caveT); q0 = Ts();
        ApplyOres(ch, d, c, r0, f); ApplyOil(ch, d, c, r0, f); Interlocked.Add(ref Prof[4], Ts() - q0); q0 = Ts();
        if (fl == null && r0 + CS > R - 24) fl = Feats(ch, B, f);
        if (fl != null && fl.Count > 0) ApplyFeats(ch, d, fl); Interlocked.Add(ref Prof[5], Ts() - q0); q0 = Ts();
        if (r0 + CS > R && r0 <= R + c.hi + 2) Cover(ch, d, c, B, f); Interlocked.Add(ref Prof[6], Ts() - q0);
        if (r0 + CS > R - 34 && r0 <= R + 2) CoverSea(ch, d, c, B, f);
    edits:
        if (has)
            lock (EditLock)
                for (int z = 0; z < CS; z++) for (int x = 0; x < CS; x++) for (int y = 0; y < CS; y++)
                        {
                            long k = ekOf(ch.band, f, ch.cx * CS + x, ch.cy * CS + y, ch.cz * CS + z);
                            if (edits.TryGetValue(k, out ushort e)) d[x + CS * (z + CS * y)] = e;
                            if (BlockStates.states.TryGetValue(k, out ushort s)) BlockStates.Store(ch, x + CS * (z + CS * y), s); // st is set before d is published
                        }
        Volatile.Write(ref ch.d, d);
    }
}
