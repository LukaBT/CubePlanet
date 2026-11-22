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
    internal static void GenerateCore(Chunk ch)
    {
        Band B = bands[ch.band]; int f = ch.face;
        var d = new ushort[32768];
        bool has; lock (EditLock) has = ecount.ContainsKey(ch.key);
        // a chunk saved with the world is used as saved (then newer edits on top), not generated
        if (WorldSave.TryGetSaved(ch.key, out var savedD)) { d = (ushort[])savedD.Clone(); goto edits; }
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
                if (sMin >= 72) Array.Fill(d, (ushort)Deepslate);
                else if (sMax < 56) Array.Fill(d, (ushort)Stone);
                else
                    for (int z = 0; z < CS; z++) for (int x = 0; x < CS; x++) for (int y = 0; y < CS; y++)
                            { int i = x + CS * z; double r = r0 + y + .5; d[x + CS * (z + CS * y)] = (ushort)StoneAt(R + c.h[i] - r, i, r, salt); }
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
                            double rel = r - R; id = rel < -(CAVB - 20) ? Lava : rel < aq[(z >> 4) * 2 + (x >> 4)] ? Water : 0; // lava lakes at the bottom, flooded caves, else air
                        }
                    }
                    d[x + CS * (z + CS * y)] = (ushort)id;
                }
            }
        ApplyOres(ch, d, c, r0, f);
        if (fl == null && r0 + CS > R - 24) fl = Feats(ch, B, f);
        if (fl != null && fl.Count > 0) ApplyFeats(ch, d, fl);
        if (r0 + CS > R && r0 <= R + c.hi + 2) Cover(ch, d, c, B, f);
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
