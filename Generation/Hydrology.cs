namespace CubePlanet;

/// <summary>
/// Lakes from the terrain, computed once when a world is created (from the seed, so nothing is saved): the smoothed terrain is sampled on a
/// grid over all 6 cube faces (about every 8 blocks; coarser on big planets, at most 512 x 512 per face), and every hollow is filled up to
/// its lowest rim (priority flood from the sea, as water would fill it). A filled hollow at least a block deep is a lake with a flat surface.
/// Lake water is ordinary still water, exactly like a pool a player builds: no special rules (<see cref="Lakes.Apply"/> draws it).
/// </summary>
public static class Hydrology
{
    public static int N; public static double CellAng, CellBlocks;
    public static float[] H, Fill, S; public static byte[] Kind;
    public const byte Land = 0, Sea = 1, Lake = 3;
    /// <summary>A lake is within reach of this cell (terrain columns elsewhere skip all lake work).</summary>
    public static bool[] Near;
    public static bool Built => H != null;

    public static int Id(int f, int i, int j) => (f * N + i) * N + j;
    public static (int f, int i, int j) Split(int id) { int j = id % N, i = id / N % N, f = id / (N * N); return (f, i, j); }
    /// <summary>Unit vector to the centre of a cell (angles beyond the face edge are fine: they point onto the next face).</summary>
    public static V3 Center(int f, double i, double j) => GpA(f, -PI / 4 + (i + .5) * CellAng, -PI / 4 + (j + .5) * CellAng, 1);
    /// <summary>Cell containing a direction, and the position inside the grid of its face (continuous cell coordinates).</summary>
    public static int CellOf(V3 p, out int f, out double gi, out double gj)
    {
        f = FaceOf(p.X, p.Y, p.Z); V3 n = Cube.N[f], u = Cube.U[f], v = Cube.V[f];
        double pn = M.Dot(p, n);
        gi = (Math.Atan2(M.Dot(p, u), pn) + PI / 4) / CellAng - .5; gj = (Math.Atan2(M.Dot(p, v), pn) + PI / 4) / CellAng - .5;
        int i = Math.Clamp((int)Math.Round(gi), 0, N - 1), j = Math.Clamp((int)Math.Round(gj), 0, N - 1);
        return Id(f, i, j);
    }
    public static int CellOf(V3 p) => CellOf(p, out _, out _, out _);
    /// <summary>Cell (f, i, j), also when i / j lie beyond the face's edge (then it is on the next face).</summary>
    public static int CellAt(int f, int i, int j) => i >= 0 && i < N && j >= 0 && j < N ? Id(f, i, j) : CellOf(Center(f, i, j));
    static readonly int[] DI = { -1, -1, -1, 0, 0, 1, 1, 1 }, DJ = { -1, 0, 1, -1, 1, -1, 0, 1 };

    public static void Build()
    {
        N = (int)Math.Clamp(Math.Round(PI / 2 * R / 8), 16, 512); CellAng = PI / 2 / N; CellBlocks = PI / 2 * R / N;
        int n = 6 * N * N; H = new float[n]; Fill = new float[n]; S = new float[n]; Kind = new byte[n];
        // smoothed heights (the terrain without detail finer than a cell)
        Parallel.For(0, 6 * N, row =>
        {
            int f = row / N, i = row % N;
            for (int j = 0; j < N; j++) { V3 c = Center(f, i, j); Terrain(c.X, c.Y, c.Z, CellBlocks); H[Id(f, i, j)] = (float)gH; }
        });
        // priority flood from the sea: every hollow fills up to its lowest rim
        var pq = new PriorityQueue<int, float>(); var done = new bool[n];
        for (int id = 0; id < n; id++) if (H[id] < 0) { Kind[id] = Sea; Fill[id] = H[id]; done[id] = true; pq.Enqueue(id, H[id]); }
        if (pq.Count == 0) { int lo = 0; for (int id = 1; id < n; id++) if (H[id] < H[lo]) lo = id; Fill[lo] = H[lo]; done[lo] = true; pq.Enqueue(lo, H[lo]); }
        while (pq.Count > 0)
        {
            int c = pq.Dequeue(); var (f, i, j) = Split(c);
            for (int k = 0; k < 8; k++)
            {
                int m = CellAt(f, i + DI[k], j + DJ[k]); if (done[m]) continue;
                done[m] = true; Fill[m] = Math.Max(H[m], Fill[c] + 1e-3f); pq.Enqueue(m, Fill[m]);
                if (Fill[m] - H[m] >= 1) { Kind[m] = Lake; S[m] = (float)Math.Floor(Fill[m]); }
            }
        }
        // cells within reach of a lake (a column looks at the lake cells up to 2 cells around)
        Near = new bool[n];
        for (int id = 0; id < n; id++)
        {
            if (Kind[id] != Lake) continue;
            var (f, i, j) = Split(id);
            for (int di = -3; di <= 3; di++) for (int dj = -3; dj <= 3; dj++) Near[CellAt(f, i + di, j + dj)] = true;
        }
    }

    /// <summary>A picture of the map: the 6 faces as an unfolded cube, land shaded by height, sea blue, lakes cyan.</summary>
    public static void WritePng(string path)
    {
        int w = 4 * N, h = 3 * N; var px = new byte[w * h * 4];
        (int ox, int oy)[] at = { (2, 1), (0, 1), (1, 0), (1, 2), (1, 1), (3, 1) }; // faces +X -X +Y -Y +Z -Z on the cross
        for (int f = 0; f < 6; f++) for (int i = 0; i < N; i++) for (int j = 0; j < N; j++)
                {
                    int id = Id(f, i, j); byte r, g, b;
                    switch (Kind[id])
                    {
                        case Sea: r = 30; g = 60; b = (byte)Math.Clamp(150 + H[id] * 2, 60, 150); break;
                        case Lake: r = 60; g = 200; b = 230; break;
                        default: { double t = Math.Clamp(H[id] / 90, 0, 1); r = (byte)(90 + 120 * t); g = (byte)(150 + 60 * t - 100 * t * t); b = (byte)(70 + 140 * t); break; }
                    }
                    int x = at[f].ox * N + j, y = at[f].oy * N + i, o = (y * w + x) * 4; px[o] = r; px[o + 1] = g; px[o + 2] = b; px[o + 3] = 255;
                }
        Png.WriteRgba(path, px, w, h);
    }

    public static string Stats()
    {
        int lakes = 0, sea = 0;
        for (int id = 0; id < Kind.Length; id++) { if (Kind[id] == Lake) lakes++; else if (Kind[id] == Sea) sea++; }
        return $"{6 * N * N} cells of {CellBlocks:0.0} blocks: {lakes} lake cells, {sea} sea cells";
    }
}
