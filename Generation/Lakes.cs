namespace CubePlanet;

/// <summary>
/// Lakes in the terrain columns, from <see cref="Hydrology"/>'s lake cells, applied to every full-detail terrain column by
/// <see cref="TerrainGen.Terrain"/>. Lake water is plain still water (nothing special happens to it, as with water a player places).
/// Within 1 cell of a lake cell, a column whose ground is below the lake's surface is water up to it (sand at the shallow edge); within
/// 2 cells, the ground stays at least 1 block above the highest lake nearby, so lakes never spill and two lakes at different levels get a
/// shore between them. Results: <see cref="gLS"/> lake surface (NaN = none), <see cref="gLN"/> surface of a lake nearby (no caves above it
/// - 40, see <see cref="Strata.Cavable"/>; no trees where gLS is set).
/// </summary>
public static class Lakes
{
    [ThreadStatic] public static double gLS, gLN;

    /// <summary>Applies lakes to the column at (x, y, z) (a point on the sphere of radius R) whose terrain results are in the g* slots.</summary>
    public static void Apply(double x, double y, double z)
    {
        gLS = gLN = double.NaN;
        if (!Hydrology.Built || gH < -2) return;
        double r = Math.Sqrt(x * x + y * y + z * z); V3 up = new(x / r, y / r, z / r);
        int c0 = Hydrology.CellOf(up, out int f, out double gi, out double gj);
        if (!Hydrology.Near[c0]) return;
        int ci = (int)Math.Round(gi), cj = (int)Math.Round(gj);
        double Lmax = double.NegativeInfinity, Lfill = double.NegativeInfinity;
        for (int di = -2; di <= 2; di++) for (int dj = -2; dj <= 2; dj++)
            {
                int c = Hydrology.CellAt(f, ci + di, cj + dj);
                if (Hydrology.Kind[c] != Hydrology.Lake) continue;
                double L = Hydrology.S[c]; Lmax = Math.Max(Lmax, L);
                if (Math.Max(Math.Abs(di), Math.Abs(dj)) <= 1) Lfill = Math.Max(Lfill, L);
            }
        if (Lmax == double.NegativeInfinity) return;
        gLN = Lmax;
        if (Lfill >= Lmax && gH < Lfill) { gLS = Lfill; if (gH > Lfill - 2) gT = Sand; gI = 0; return; }
        if (gH < Lmax + 1) gH = Lmax + 1; // shore
    }
}
