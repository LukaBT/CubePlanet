namespace CubePlanet;

/// <summary>
/// Rivers and lakes in the terrain columns, drawn from the planet's drainage map (<see cref="Hydrology"/>), applied to every full-detail
/// terrain column by <see cref="TerrainGen.Terrain"/>:
/// <list type="bullet">
/// <item>River: each river cell's water runs in a straight segment from its centre to the centre of the cell it drains into (slightly
/// meandering: the column position is warped a few blocks). Within the half width (from the river's flow) the column is the river bed
/// (1 .. 3 deep, gravel) under water up to the cell's surface; the surface of the half nearer the downstream cell is that cell's, so a step
/// in the surface is a straight line across the river: a waterfall where the terrain drops.</item>
/// <item>Banks: the ground near a river blends down to 1 block above the water and never lower (a levee where it would be lower); right
/// next to the water it also holds the higher surface of a waterfall.</item>
/// <item>Lake: in a lake cell (and the ring around it) every column whose ground is below the lake's surface is water up to it; columns
/// close to a lake are kept at least 1 block above its surface, so lakes never spill.</item>
/// </list>
/// Results (thread-static like the rest of the terrain): <see cref="gRS"/> water surface (NaN = none), <see cref="gRN"/> surface of water
/// nearby (no caves above it - 8), <see cref="gRD"/> downstream direction (world), <see cref="gRW"/> flow strength 0..7 for drawing,
/// <see cref="gRC"/> the drainage cell (-1 = none) whose water this is.
/// </summary>
public static class Rivers
{
    public const double Bank = 6;
    [ThreadStatic] public static double gRS, gRN; [ThreadStatic] public static V3 gRD; [ThreadStatic] public static int gRW, gRC;

    static double Nz(double x, double y, double z, int k, double f) { var o = OFF[k]; return Noise3(x * f + o[0], y * f + o[1], z * f + o[2]); }

    /// <summary>Flow strength 0..7 drawn for a flow of q units per second.</summary>
    public static int Strength(double q) => Math.Clamp((int)Math.Round(1 + Math.Log2(1 + q) * 1.1), 1, 7);
    static int CellAt(int f, int ii, int jj) => ii >= 0 && ii < Hydrology.N && jj >= 0 && jj < Hydrology.N ? Hydrology.Id(f, ii, jj) : Hydrology.CellOf(Hydrology.Center(f, ii, jj));

    /// <summary>Carves the column at (x, y, z) (a point on the sphere of radius R) whose terrain results are in the g* slots.</summary>
    public static void Apply(double x, double y, double z)
    {
        gRS = gRN = double.NaN; gRW = 0; gRC = -1;
        if (!Hydrology.Built || gH < -2) return;
        { double rr = Math.Sqrt(x * x + y * y + z * z); if (!Hydrology.Near[Hydrology.CellOf(new V3(x / rr, y / rr, z / rr))]) return; } // no water anywhere near
        // a little meander: the column is looked up a few blocks away (smoothly varying)
        double wx = Nz(x, y, z, 44, 1 / 40.0) * 3, wy = Nz(x, y, z, 45, 1 / 40.0) * 3, wz = Nz(x, y, z, 46, 1 / 40.0) * 3;
        V3 P = new(x + wx, y + wy, z + wz); P = P * (R / P.Len); V3 up = P * (1 / R);
        Hydrology.CellOf(up, out int f, out double gi, out double gj);
        int ci = (int)Math.Round(gi), cj = (int)Math.Round(gj); double cb = Hydrology.CellBlocks;

        // ---- rivers: nearest segment (cell centre -> downstream cell centre) among the cells around
        double best = double.PositiveInfinity, bd = 0, bt = 0, bw = 0; int bc = -1, bdn = -1;
        int reach = 1 + (int)Math.Ceiling((7 + Bank) / cb);
        for (int di = -reach; di <= reach; di++) for (int dj = -reach; dj <= reach; dj++)
            {
                int c = CellAt(f, ci + di, cj + dj);
                if (Hydrology.Kind[c] != Hydrology.River) continue;
                int dn = Hydrology.Down[c]; if (dn < 0) continue;
                V3 a = Hydrology.Center(c) * R, b = Hydrology.Center(dn) * R, ab = b - a; double l2 = M.Dot(ab, ab);
                double t = l2 > 0 ? Math.Clamp(M.Dot(P - a, ab) / l2, 0, 1) : 0; double d = (P - (a + ab * t)).Len;
                double w = Hydrology.W[c] + (Hydrology.Kind[dn] == Hydrology.River ? (Hydrology.W[dn] - Hydrology.W[c]) * t : 0);
                if (d - w < best) { best = d - w; bd = d; bt = t; bw = w; bc = c; bdn = dn; }
            }
        if (bc >= 0 && best <= Bank)
        {
            double sUp = Hydrology.S[bc], sDn = Hydrology.Kind[bdn] == Hydrology.Sea ? 0 : Hydrology.S[bdn];
            double S = bt < .5 ? sUp : sDn;
            {
                gRN = S; // (no "skip where the ground is far below": banks must always hold the water, or a river at a cliff edge leaks)
                if (bd <= bw)
                {
                    double depth = 1 + 2 * (1 - (bd / bw) * (bd / bw)) * Math.Min(1, bw / 2);
                    gH = S - depth; gRS = S; gT = gF = Gravel; gFD = 2; gI = 0; gRC = bc;
                    gRD = M.Nrm(Hydrology.Center(bdn) - Hydrology.Center(bc)); gRW = Strength(Hydrology.Q[bc]);
                    return;
                }
                double lev = bd - bw < 2.5 ? Math.Max(sUp, sDn) : S, k = (bd - bw) / Bank;
                gH = gH > S + 1 ? S + 1 + (gH - S - 1) * k * k : S + 1;
                gH = Math.Max(gH, lev + 1);
            }
        }

        // ---- lakes: lake cells here or around (within 1 cell: may fill, within 2: shore). Only the highest lake nearby fills a column (two
        // lakes at different levels next to each other get a shore between them), every other column near a lake stays above the highest
        double Lmax = double.NegativeInfinity, Lfill = double.NegativeInfinity; int lc = -1;
        for (int di = -2; di <= 2; di++) for (int dj = -2; dj <= 2; dj++)
            {
                int c = CellAt(f, ci + di, cj + dj);
                if (Hydrology.Kind[c] != Hydrology.Lake) continue;
                double L = Hydrology.S[c]; Lmax = Math.Max(Lmax, L);
                if (Math.Max(Math.Abs(di), Math.Abs(dj)) <= 1 && L > Lfill) { Lfill = L; lc = c; }
            }
        if (lc >= 0 && Lfill >= Lmax && gH < Lfill) { gRS = Lfill; gRN = Lfill; gRC = lc; if (gH > Lfill - 2) gT = Sand; gI = 0; return; }
        if (Lmax > double.NegativeInfinity)
        {
            if (gH < Lmax + 1) gH = Lmax + 1; // shore
            gRN = double.IsNaN(gRN) ? Lmax : Math.Max(gRN, Lmax);
        }
    }
}
