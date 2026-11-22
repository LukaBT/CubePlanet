using System.Numerics;

namespace CubePlanet;

/// <summary>The Cartesian core: inside radius CORE the world is a plain grid of unit cubes in 32^3 chunks (no polar bands).</summary>
public static class CoreGrid
{
    // ---------- Cartesian core: inside radius CORE the world is a plain grid of unit cubes (32^3 chunks), no polar bands ----------
    // a cell is stone when its centre is within CORE+.9 (so every point with r<CORE lies in a solid cell); player edits live in coreEd
    internal static readonly double CR2 = (CORE + .9) * (CORE + .9);
    public static readonly Dictionary<int, byte> coreEd = new();
    public static int cEk(int i, int j, int k) => ((i + 128) * 256 + (j + 128)) * 256 + (k + 128);
    public static long cKey(int a, int b, int c) => -(1 + (((a + 4) * 8 + (b + 4)) * 8 + (c + 4)));
    public static int CoreCell(int i, int j, int k)
    {
        if (i < -128 || i > 127 || j < -128 || j > 127 || k < -128 || k > 127) return 0;
        if (coreEd.TryGetValue(cEk(i, j, k), out byte e)) return e;
        double x = i + .5, y = j + .5, z = k + .5;
        return x * x + y * y + z * z < CR2 ? 66 : 0;
    }
    public static int CoreAt(double x, double y, double z) => CoreCell((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z));
    internal static readonly int[][] C7 = { new[] { 0, 0, 0 }, new[] { 1, 0, 0 }, new[] { -1, 0, 0 }, new[] { 0, 1, 0 }, new[] { 0, -1, 0 }, new[] { 0, 0, 1 }, new[] { 0, 0, -1 } };
    public static void CoreSet(double x, double y, double z, int id)
    {
        int i = (int)Math.Floor(x), j = (int)Math.Floor(y), k = (int)Math.Floor(z);
        if (CoreCell(i, j, k) == id) return;
        coreEd[cEk(i, j, k)] = (byte)id;
        foreach (var o in C7)
        {
            int a = i + o[0], b = j + o[1], c = k + o[2];
            if (chunks.TryGetValue(cKey(a >> 5, b >> 5, c >> 5), out var ch))
            {
                ch.dirty = true;
                if (ch.d != null && o[0] == 0 && o[1] == 0 && o[2] == 0) { ch.d[(i & 31) + 32 * ((k & 31) + 32 * (j & 31))] = (byte)id; ch.empty = false; }
            }
        }
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
    internal static bool CoreOpqFar(int i, int j, int k)
    {
        double x = i + .5, y = j + .5, z = k + .5;
        if (x * x + y * y + z * z < CORE * CORE) return false;
        if (edits.Count == 0) return true;
        int bi = P2b(x, y, z); if (bi < 0) return false;
        if (!ecount.ContainsKey(ck(bi, PF, PX >> 5, PY >> 5, PZ >> 5))) return true;
        return OPQ[Blk(bi, PF, PX, PY, PZ)] != 0;
    }
}
