namespace CubePlanet;

/// <summary>Player hitbox against the voxel world (curved block grid and the Cartesian core).</summary>
public static class Collision
{
    internal static readonly double[] PH = { .001, .6, 1.2, 1.75 };
    internal static readonly V3[] AXV = { new(1, 0, 0), new(0, 1, 0), new(0, 0, 1) };
    internal static readonly int[][] HOF = { new[] { -1, -1 }, new[] { 0, -1 }, new[] { 1, -1 }, new[] { -1, 0 }, new[] { 1, 0 }, new[] { -1, 1 }, new[] { 0, 1 }, new[] { 1, 1 } };
    internal static readonly double[] RCS = Enumerable.Range(0, 12).Select(k => Math.Cos(k * PI / 6)).ToArray(), RSN = Enumerable.Range(0, 12).Select(k => Math.Sin(k * PI / 6)).ToArray();

    // solid = inside one of the block's collision boxes (geometry.collision; full block for cubes, nothing for plants and liquids)
    internal static bool SolidAt(double x, double y, double z)
    {
        int b = LocalAt(x, y, z, out double lx, out double ly, out double lz);
        if (!HASVAR[b]) return Collides(b, lx, ly, lz);
        BlockPos.TryAt(x, y, z, out var p); return Collides(b, p.State, lx, ly, lz); // doors, stairs...: geometry depends on the state
    }

    internal static (V3, V3) CoreFrame(V3 g)
    {
        V3 a = AXV[P.rf]; double d = M.Dot(a, g); V3 e1 = M.Nrm(a - g * d); return (e1, M.Cross(g, e1));
    }
    // unit tangent vectors along the two block-grid directions at p
    public static (V3, V3) GridAx(V3 p, V3 g)
    {
        int f = FaceOf(p.X, p.Y, p.Z); V3 u = U[f], v = V[f]; double du = M.Dot(u, g), dv = M.Dot(v, g);
        return (M.Nrm(u - g * du), M.Nrm(v - g * dv));
    }
    public static bool Pcol(V3 p, double hw)
    {
        V3 g = UpVec(p);
        if (P.core)
        {
            var (e1, e2) = CoreFrame(g); double rr = hw * 1.12; // round hitbox
            for (int k = 0; k < 12; k++)
            {
                double s1 = RCS[k] * rr, s2 = RSN[k] * rr; V3 b = p + e1 * s1 + e2 * s2;
                for (int i = 0; i < 4; i++) { double h = PH[i]; if (SolidAt(b.X + g.X * h, b.Y + g.Y * h, b.Z + g.Z * h)) return true; }
            }
            return false;
        }
        var (a1, a2) = GridAx(p, g);
        for (int k = 0; k < 8; k++)
        {
            double s1 = HOF[k][0] * hw, s2 = HOF[k][1] * hw; V3 b = p + a1 * s1 + a2 * s2;
            for (int i = 0; i < 4; i++) { double h = PH[i]; if (SolidAt(b.X + g.X * h, b.Y + g.Y * h, b.Z + g.Z * h)) return true; }
        }
        return false;
    }
    // move along a unit axis by dist, stopping at (and refining to) the first contact; false if blocked.
    // vert: vertical moves use a hitbox only a hair slimmer than horizontal ones (horizontal moves leave a .01 skin gap, so standing flush
    // against a wall never sticks). It used to be .03 slimmer, which let the player fall into gaps the walking box doesn't fit through
    // (digging down while standing near the block's edge, falling past a ledge) and end up inside the walls, unable to move.
    internal static bool Mv(V3 ax, double dist, bool vert)
    {
        double hw = vert ? HW - .004 : HW; int n = (int)Math.Ceiling(Math.Abs(dist) / .3); if (n == 0) return true; double st = dist / n;
        for (int i = 0; i < n; i++)
        {
            V3 np = P.p + ax * st;
            if (!Pcol(np, hw)) { P.p = np; continue; }
            double lo = 0, hi = st;
            for (int k = 0; k < 7; k++) { double m = (lo + hi) / 2; if (Pcol(P.p + ax * m, hw)) hi = m; else lo = m; }
            if (!vert)
            {
                double c = lo - Math.Sign(st) * .01; V3 qq = P.p + ax * c;
                if (!Pcol(qq, hw)) { P.p = qq; return false; }
            }
            P.p = P.p + ax * lo; return false;
        }
        return true;
    }
    /// <summary>
    /// Safety net: if the player overlaps a block (a chunk finished loading around them, a block edit, round-off on the curved grid),
    /// push them out by the smallest move that frees them: sideways along the block grid first, then straight up.
    /// </summary>
    internal static void Depenetrate(V3 g, V3 tu, V3 tv)
    {
        if (!Pcol(P.p, HW)) return;
        V3[] dirs = { tu, -tu, tv, -tv, M.Nrm(tu + tv), M.Nrm(tu - tv), M.Nrm(tv - tu), M.Nrm(-tu - tv) };
        foreach (double d in new[] { .015, .03, .05, .08, .12, .18, .26, .36, .5, .7, .95, 1.2 })
            foreach (var dir in dirs) { V3 q = P.p + dir * d; if (!Pcol(q, HW)) { P.p = q; return; } }
        // buried deeper (e.g. a tree finished loading around you): climb out on top
        for (double up = .05; up <= 16; up += up < 2 ? .05 : .25) { V3 q = P.p + g * up; if (!Pcol(q, HW)) { P.p = q; P.vu = Math.Max(P.vu, 0); return; } }
    }
}
