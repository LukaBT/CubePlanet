namespace CubePlanet;

/// <summary>
/// Planet-wide drainage, computed once when a world is created (from the seed, so nothing is saved): where rain would run off the existing
/// terrain, gather, and form rivers and lakes.
/// <list type="number">
/// <item>Height map: the smoothed terrain sampled on a grid over all 6 cube faces (about every 8 blocks; coarser on big planets, at most
/// 512 x 512 per face).</item>
/// <item>Hollows are filled up to their lowest rim (priority flood from the sea): a filled hollow at least a block deep is a lake with a flat
/// surface; after filling, every cell drains downhill to the sea.</item>
/// <item>Each cell drains to the neighbour it was reached from (its way down to the sea).</item>
/// <item>Rain: every cell adds its area (less in deserts) and passes on all it receives, giving each cell its catchment and its flow Q
/// (water units per second).</item>
/// <item>Cells with enough catchment are rivers; width grows with the square root of Q. The water surface is the filled ground 1 block lower,
/// never higher than downstream (rivers only ever run down), so it is level on flat land and drops where the terrain drops: waterfalls.</item>
/// </list>
/// <see cref="Rivers.Apply"/> draws rivers and lakes into the terrain columns from this map; <see cref="Tick"/> keeps disturbed rivers
/// flowing (inflow from upstream, outflow downstream).
/// </summary>
public static class Hydrology
{
    public static int N; public static double CellAng, CellBlocks;
    public static float[] H, Fill, Q, S, W; public static int[] Down; public static byte[] Kind;
    /// <summary>A river or lake is within reach of this cell (terrain columns elsewhere skip all river work).</summary>
    public static bool[] Near;
    public const byte Land = 0, Sea = 1, River = 2, Lake = 3;
    /// <summary>Water units per second of rain per square block, and the catchment (square blocks) that makes a stream.</summary>
    public const double RainPerBlock2 = 4.5e-4, StreamArea = 7700;
    public static bool Built => H != null;

    public static int Id(int f, int i, int j) => (f * N + i) * N + j;
    public static (int f, int i, int j) Split(int id) { int j = id % N, i = id / N % N, f = id / (N * N); return (f, i, j); }
    /// <summary>Unit vector to the centre of a cell (angles beyond the face edge are fine: they point onto the next face).</summary>
    public static V3 Center(int f, double i, double j) => GpA(f, -PI / 4 + (i + .5) * CellAng, -PI / 4 + (j + .5) * CellAng, 1);
    public static V3 Center(int id) { var (f, i, j) = Split(id); return Center(f, i, j); }
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
    static int Nb(int f, int i, int j, int di, int dj)
    {
        int ii = i + di, jj = j + dj;
        if (ii >= 0 && ii < N && jj >= 0 && jj < N) return Id(f, ii, jj);
        return CellOf(Center(f, ii, jj)); // across a cube-face edge
    }
    static readonly int[] DI = { -1, -1, -1, 0, 0, 1, 1, 1 }, DJ = { -1, 0, 1, -1, 1, -1, 0, 1 };

    public static void Build()
    {
        N = (int)Math.Clamp(Math.Round(PI / 2 * R / 8), 16, 512); CellAng = PI / 2 / N; CellBlocks = PI / 2 * R / N;
        int n = 6 * N * N; H = new float[n]; Fill = new float[n]; Q = new float[n]; S = new float[n]; W = new float[n]; Down = new int[n]; Kind = new byte[n];
        var rain = new float[n];
        // 1. smoothed heights (the terrain without detail finer than a cell)
        Parallel.For(0, 6 * N, row =>
        {
            int f = row / N, i = row % N;
            for (int j = 0; j < N; j++)
            {
                V3 c = Center(f, i, j); Terrain(c.X, c.Y, c.Z, CellBlocks);
                int id = Id(f, i, j); H[id] = (float)gH;
                rain[id] = gB == 8 || gB == 9 || gB == 17 ? .2f : 1f; // deserts, badlands, red canyon: little rain
            }
        });
        // 2. + 3. priority flood from the sea: filled heights, and each land cell's way down (the cell it was reached from)
        var pq = new PriorityQueue<int, float>(); var done = new bool[n]; var order = new List<int>(n);
        for (int id = 0; id < n; id++) { Down[id] = -1; if (H[id] < 0) { Kind[id] = Sea; Fill[id] = H[id]; done[id] = true; pq.Enqueue(id, H[id]); } }
        if (pq.Count == 0) { int lo = 0; for (int id = 1; id < n; id++) if (H[id] < H[lo]) lo = id; Fill[lo] = H[lo]; done[lo] = true; pq.Enqueue(lo, H[lo]); }
        while (pq.Count > 0)
        {
            int c = pq.Dequeue(); if (Kind[c] != Sea) order.Add(c);
            var (f, i, j) = Split(c);
            for (int k = 0; k < 8; k++)
            {
                int m = Nb(f, i, j, DI[k], DJ[k]); if (done[m]) continue;
                done[m] = true; Fill[m] = Math.Max(H[m], Fill[c] + 1e-3f); Down[m] = c; pq.Enqueue(m, Fill[m]);
            }
        }
        // 4. rain gathers downhill: process from the farthest-up cells to the sea
        double area = CellBlocks * CellBlocks;
        for (int id = 0; id < n; id++) Q[id] = (float)(rain[id] * area);
        for (int k = order.Count - 1; k >= 0; k--) { int c = order[k], d = Down[c]; if (d >= 0 && Kind[d] != Sea) Q[d] += Q[c]; }
        // 5. lakes, rivers, water surfaces (from the sea outwards, so downstream is always done first)
        foreach (int c in order)
        {
            double catchment = Q[c]; Q[c] = (float)(catchment * RainPerBlock2);
            int d = Down[c]; double sd = d < 0 || Kind[d] == Sea ? 0 : Kind[d] == Land ? double.NaN : S[d];
            if (Fill[c] - H[c] >= 1) { Kind[c] = Lake; S[c] = (float)Math.Floor(Fill[c]); }
            else if (catchment >= Math.Max(StreamArea, 40 * area)) { Kind[c] = River; S[c] = (float)Math.Max(0, Math.Floor(Fill[c] - 1)); W[c] = (float)Math.Clamp(.5 + .55 * Math.Sqrt(Q[c]), 1, 7); }
            else continue;
            if (!double.IsNaN(sd)) S[c] = (float)Math.Max(S[c], sd); // never below the water it flows into
        }
        // a river leaving a lake starts at the lake's level (and steps down further on, behind a lip), so the lake cannot drain through it;
        // rivers flowing into that river are raised to match (water never stands lower than what it flows into)
        for (int k = order.Count - 1; k >= 0; k--) { int u = order[k], d = Down[u]; if (Kind[u] == Lake && d >= 0 && Kind[d] == River) S[d] = Math.Max(S[d], S[u]); }
        foreach (int c in order) { int d = Down[c]; if (Kind[c] == River && d >= 0 && (Kind[d] == River || Kind[d] == Lake)) S[c] = Math.Max(S[c], S[d]); }
        // cells within reach of a river or lake (the column lookup is warped a few blocks and reaches a few cells around)
        Near = new bool[n]; int reach = 2 + (int)Math.Ceiling((7 + Rivers.Bank + 4) / CellBlocks);
        for (int id = 0; id < n; id++)
        {
            if (Kind[id] != River && Kind[id] != Lake) continue;
            var (f, i, j) = Split(id);
            for (int di = -reach; di <= reach; di++) for (int dj = -reach; dj <= reach; dj++)
                {
                    int ii = i + di, jj = j + dj;
                    Near[ii >= 0 && ii < N && jj >= 0 && jj < N ? Id(f, ii, jj) : CellOf(Center(f, ii, jj))] = true;
                }
        }
    }

    /// <summary>A picture of the drainage map: the 6 faces as an unfolded cube, land shaded by height, sea blue, lakes cyan, rivers dark blue
    /// (thicker = more water).</summary>
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
                        case River: { double s = Math.Clamp(Math.Log2(1 + Q[id]) / 7, 0, 1); r = (byte)(40 - 30 * s); g = (byte)(110 - 60 * s); b = (byte)(255 - 40 * s); break; }
                        default: { double t = Math.Clamp(H[id] / 90, 0, 1); r = (byte)(90 + 120 * t); g = (byte)(150 + 60 * t - 100 * t * t); b = (byte)(70 + 140 * t); break; }
                    }
                    int x = at[f].ox * N + j, y = at[f].oy * N + i, o = (y * w + x) * 4; px[o] = r; px[o + 1] = g; px[o + 2] = b; px[o + 3] = 255;
                }
        Png.WriteRgba(path, px, w, h);
    }

    // ---------- disturbed rivers ----------
    // An undisturbed river is not simulated (it is drawn flowing). A drainage cell whose water was changed becomes live and stays live:
    //  - inflow: its own rain and the water of land draining into it at its centre, and the flow Q of every upstream river / lake cell that
    //    is not live at its upstream edge (a live upstream cell's water arrives by itself through the simulation);
    //  - outflow: if the cell downstream is not live, water rising above the natural level near its downstream edge is taken away (it
    //    flows on down the river), but only while the way there is open: a dam stops it, so the water rises into a lake behind it.
    // All of it is taken from / given back to the water cycle's reservoir. Only loaded cells take part.
    public static readonly HashSet<int> Live = new();
    static readonly Dictionary<long, double> carry = new();
    /// <summary>Set while Tick moves water itself (its own changes must not make further cells live).</summary>
    public static bool Quiet;
    public static void Touch(int c) { if (Kind[c] == River || Kind[c] == Lake) Live.Add(c); }

    /// <summary>Block cell of the top water row of drainage cell c, at t (0 = its centre, 1 = the centre of cell 'toward').</summary>
    static WC WaterCellAt(int c, int toward, double t)
    {
        V3 a = Center(c), b = toward >= 0 ? Center(toward) : a, p = M.Nrm(a + (b - a) * t) * (R + S[c] - .5);
        int bi = P2b(p.X, p.Y, p.Z); if (bi < 0) return default;
        return Wcell(bi, PF, PX, PY, PZ);
    }
    /// <summary>The nearest water (or else free) cell around c, up to 3 cells away and 3 rows up: where blocked inflow comes out.</summary>
    static WC OpenNear(WC c)
    {
        if (!c.ok) return c;
        if (c.id == Water || Wfree(c.id)) return c;
        WC free = default;
        for (int r = 1; r <= 3; r++)
            for (int dy = 0; dy <= 3; dy++) for (int dz = -r; dz <= r; dz++) for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                        var n = Wcell(c.b, c.F, c.X + dx, c.Y + dy, c.Z + dz); if (!n.ok) continue;
                        if (n.id == Water) return n;
                        if (!free.ok && Wfree(n.id)) free = n;
                    }
        return free;
    }
    static int Units(long key, double amount)
    {
        double v = (carry.TryGetValue(key, out double cv) ? cv : 0) + amount; int n = (int)v; carry[key] = v - n; return n;
    }

    public static void Tick(double dt)
    {
        if (!Built || Live.Count == 0) return;
        Quiet = true;
        try
        {
            foreach (int c in Live.ToArray())
            {
                var (f, i, j) = Split(c); double own = Q[c];
                // inflow at the upstream edges of upstream river / lake cells that are not live
                for (int k = 0; k < 8; k++)
                {
                    int u = Nb(f, i, j, DI[k], DJ[k]); if (Down[u] != c || (Kind[u] != River && Kind[u] != Lake)) continue;
                    own -= Q[u]; if (Live.Contains(u)) continue;
                    var e = OpenNear(WaterCellAt(c, u, .45)); int n = Units((long)c << 32 | (uint)u, Q[u] * dt);
                    if (e.ok && n > 0) { int put = AddWater(e, n); WaterSim.Reservoir -= put; WaterFlow.AddCell(e.k, e.b, e.F, e.X, e.Y, e.Z, 0, 0, 0, put); }
                }
                // its own rain and the land draining into it, at its centre
                if (own > 0)
                {
                    var m = OpenNear(WaterCellAt(c, -1, 0)); int n = Units((long)c << 32 | 0xFFFFFFFFu, own * dt);
                    if (m.ok && n > 0) { int put = AddWater(m, n); WaterSim.Reservoir -= put; }
                }
                // outflow: excess near the downstream edge, if the cell downstream is not live and the way there is open
                int d = Down[c]; if (d >= 0 && Live.Contains(d)) continue;
                var x = WaterCellAt(c, d, .4); if (!x.ok) continue;
                bool open = true; var step = WaterCellAt(c, d, .5);
                if (!step.ok || (step.id != Water && !Wfree(step.id))) open = false;
                if (!open) continue;
                int took = 0;
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                        for (int dy = 1; dy <= 6; dy++)
                        {
                            var a = Wcell(x.b, x.F, x.X + dx, x.Y + dy, x.Z + dz); if (!a.ok || a.id != Water) continue;
                            int l = Wl(a); if (l == 0) continue;
                            WaterSim.Wput(a, 0); took += l;
                        }
                WaterSim.Reservoir += took;
                if (took > 0) WaterFlow.AddCell(x.k, x.b, x.F, x.X, x.Y, x.Z, 0, 0, 0, took);
            }
        }
        finally { Quiet = false; }
    }
    public static void ClearLive() { Live.Clear(); carry.Clear(); }

    public static string Stats()
    {
        int rivers = 0, lakes = 0, sea = 0; double qmax = 0;
        for (int id = 0; id < Kind.Length; id++) { if (Kind[id] == River) { rivers++; qmax = Math.Max(qmax, Q[id]); } else if (Kind[id] == Lake) lakes++; else if (Kind[id] == Sea) sea++; }
        return $"{6 * N * N} cells of {CellBlocks:0.0} blocks: {rivers} river cells (largest {qmax:0} units/s), {lakes} lake cells, {sea} sea cells";
    }
}
