using System.Numerics;

namespace CubePlanet;

/// <summary>Per-chunk-column terrain cache (shared) and the per-thread cave-node / feature caches.</summary>
public static class Columns
{
    // ---------- column / cave caches ----------
    // caches are per thread (no locking); WGEN is bumped when a new world is created so every thread drops its old caches
    public static volatile int WGEN;
    [ThreadStatic] internal static int tlGen;
    [ThreadStatic] internal static Dictionary<long, double> tlNode;
    [ThreadStatic] internal static Dictionary<long, Feat> tlPlc, tlStc;
    internal static void TlSync() { if (tlNode == null || tlGen != WGEN) { tlNode = new(); tlPlc = new(); tlStc = new(); tlGen = WGEN; } }
    // the column cache is shared by all threads (the mesher on the main thread needs the same columns the workers just computed)
    internal static readonly System.Collections.Concurrent.ConcurrentDictionary<long, Col> colCache = new();
    internal static Dictionary<long, double> nodeCache { get { TlSync(); return tlNode; } }
    internal static Dictionary<long, Feat> PLC { get { TlSync(); return tlPlc; } }
    internal static Dictionary<long, Feat> STC { get { TlSync(); return tlStc; } }


    public static Col ColsOf(Band B, int f, int cx, int cz)
    {
        long key = (((long)(B.b * 6 + f) * 1048576) + cx) * 1048576 + cz;
        if (colCache.TryGetValue(key, out var c)) return c;
        if (colCache.Count > 6000) colCache.Clear();
        c = new Col();
        for (int z = 0; z < CS; z++)
            for (int x = 0; x < CS; x++)
            {
                V3 t = GpA(f, -PI / 4 + (cz * CS + z + .5) * B.d, -PI / 4 + (cx * CS + x + .5) * B.d, 1);
                Terrain(t.X, t.Y, t.Z, 0);
                int i = x + CS * z;
                c.h[i] = (float)gH; c.t[i] = (ushort)gT; c.f[i] = (ushort)gF; c.fd[i] = (byte)gFD; c.ice[i] = (byte)gI; c.b[i] = (byte)gB;
                c.rs[i] = (float)Rivers.gRS; c.rn[i] = (float)Rivers.gRN;
                if (!double.IsNaN(Rivers.gRS))
                {   // downstream direction in the block grid (x, z) for drawing the flow
                    double az = -PI / 4 + (cz * CS + z + .5) * B.d, ax = -PI / 4 + (cx * CS + x + .5) * B.d;
                    V3 ex = GpA(f, az, ax + B.d, 1) - t, ez = GpA(f, az + B.d, ax, 1) - t;
                    double fx = M.Dot(Rivers.gRD, ex) / ex.Len, fz = M.Dot(Rivers.gRD, ez) / ez.Len;
                    c.rf[i] = (byte)((((int)Math.Round(Math.Atan2(fz, fx) / (PI / 4)) + 8) & 7) | Rivers.gRW << 3);
                }
                if (gH > c.hi) c.hi = gH; if (gH < c.lo) c.lo = gH;
            }
        colCache[key] = c; return c;
    }
    public static Col ColsPeek(Band B, int f, int cx, int cz) =>
        colCache.TryGetValue((((long)(B.b * 6 + f) * 1048576) + cx) * 1048576 + cz, out var c) ? c : null;
}
