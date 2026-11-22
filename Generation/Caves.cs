using System.Numerics;

namespace CubePlanet;

/// <summary>Cave density field, sampled on a 4-block lattice and trilinearly interpolated.</summary>
public static class Caves
{
    /// <summary>Cave density (>0 = air): cheese caverns, spaghetti tunnels, a wide tunnel network in some regions, and ravines.</summary>
    internal static double CaveS(double x, double y, double z, double rel)
    {
        var o = OFF[14]; var p = OFF[15]; var q = OFF[16]; var u = OFF[17]; var w = OFF[18]; var mk = OFF[25];
        // cheese caves: big irregular caverns, rare close to the surface
        double c = Noise3(x * .016 + p[2], y * .024 + o[1], z * .016 + p[0]) + .55 * Noise3(x * .045 + o[2], y * .06 + p[1], z * .045 + o[0]);
        double cheese = (c - .58) / .3 - .45 * M.Sm(-30, -5, rel);
        // spaghetti caves: where two noise zero-surfaces cross; thickness varies along the network
        double th = .075 + .06 * (Noise3(x * .008 + q[0], y * .008 + q[1], z * .008 + q[2]) * .5 + .5);
        double a = Noise3(x * .034 + o[0], y * .034 + o[1], z * .034 + o[2]), b = Noise3(x * .034 + p[0], y * .034 + p[1], z * .034 + p[2]);
        double spag = 1 - Math.Max(Math.Abs(a), Math.Abs(b)) / th;
        // wide tunnel network, only in some regions
        double reg = Noise3(x * .006 + mk[0], y * .006 + mk[1], z * .006 + mk[2]);
        double a2 = Noise3(x * .021 + u[0], y * .021 + u[1], z * .021 + u[2]), b2 = Noise3(x * .021 + w[0], y * .021 + w[1], z * .021 + w[2]);
        double wide = (1 - Math.Max(Math.Abs(a2), Math.Abs(b2)) / .11) - M.Sm(.05, -.35, reg) * 1.2;
        // ravines: long vertical slots, narrowing towards the bottom, open to the sky in lowlands, only in some regions
        double rr = Math.Sqrt(x * x + y * y + z * z), sc = rr > 1 ? R / rr : 1;
        double rn = Noise3(x * sc * .011 + q[2], y * sc * .011 + q[0], z * sc * .011 + q[1]);
        double rm = Noise3(x * sc * .004 + mk[2], y * sc * .004 + mk[0], z * sc * .004 + mk[1]);
        double rav = 1 - Math.Abs(rn) / .055 - (1 - M.Sm(-52, -22, rel)) - M.Sm(10, 24, rel) * 1.5 - M.Sm(.25, -.05, rm) * 1.5;
        double tp = Math.Max(0, Math.Min(1.3, (-rel - (CAVB - 30)) / 30 * 1.3));
        return Math.Max(Math.Max(Math.Max(spag, cheese), wide), rav) - tp;
    }

    // cave density is sampled on a 4-block lattice and trilinearly interpolated (>0 = air)
    public static double NodeV(Band B, int f, int gx, int gy, int gz)
    {
        long key = Mix(B.b * 6 + f, gx, gy, gz);
        if (nodeCache.TryGetValue(key, out double v)) return v;
        if (nodeCache.Count > 250000) nodeCache.Clear();
        V3 t = GpA(f, -PI / 4 + (gz * 4 + .5) * B.d, -PI / 4 + (gx * 4 + .5) * B.d, B.inner + gy * 4 + .5);
        v = CaveS(t.X, t.Y, t.Z, t.Len - R);
        nodeCache[key] = v; return v;
    }
    public static double Tri(double a, double b, double c, double d, double e, double f, double g, double h, double x, double y, double z) =>
        ((a * (1 - x) + b * x) * (1 - y) + (c * (1 - x) + d * x) * y) * (1 - z) + ((e * (1 - x) + f * x) * (1 - y) + (g * (1 - x) + h * x) * y) * z;
    public static double CaveAt(Band B, int f, int bx, int by, int bz)
    {
        int gx = bx >> 2, gy = by >> 2, gz = bz >> 2;
        return Tri(NodeV(B, f, gx, gy, gz), NodeV(B, f, gx + 1, gy, gz), NodeV(B, f, gx, gy + 1, gz), NodeV(B, f, gx + 1, gy + 1, gz),
            NodeV(B, f, gx, gy, gz + 1), NodeV(B, f, gx + 1, gy, gz + 1), NodeV(B, f, gx, gy + 1, gz + 1), NodeV(B, f, gx + 1, gy + 1, gz + 1),
            (bx & 3) / 4.0, (by & 3) / 4.0, (bz & 3) / 4.0);
    }
}
