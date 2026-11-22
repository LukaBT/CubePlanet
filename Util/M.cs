namespace CubePlanet;

public static class M
{
    public const double PI = Math.PI;
    public static double Dot(V3 a, V3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static V3 Cross(V3 a, V3 b) => new V3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    public static V3 Nrm(V3 a) { double l = a.Len; if (l == 0 || double.IsNaN(l)) l = 1; return new V3(a.X / l, a.Y / l, a.Z / l); }

    /// <summary>Rodrigues rotation of v around unit axis ax by angle an.</summary>
    public static V3 Rot(V3 v, V3 ax, double an)
    {
        double c = Math.Cos(an), s = Math.Sin(an), k = Dot(ax, v) * (1 - c);
        V3 x = Cross(ax, v);
        return new V3(v.X * c + x.X * s + ax.X * k, v.Y * c + x.Y * s + ax.Y * k, v.Z * c + x.Z * s + ax.Z * k);
    }

    // column-major 4x4 matrices (same layout as the OpenGL uniform upload)
    public static double[] Persp(double fy, double a, double n, double f)
    {
        double t = 1 / Math.Tan(fy / 2);
        return new[] { t / a, 0, 0, 0, 0, t, 0, 0, 0, 0, (f + n) / (n - f), -1, 0, 0, 2 * f * n / (n - f), 0 };
    }
    /// <summary>Vulkan projection: Y down, reverse-Z with an infinite far plane (depth = near / view distance, 1 at the near plane, 0 at infinity).</summary>
    public static double[] VkPersp(double fy, double a, double n)
    {
        double t = 1 / Math.Tan(fy / 2);
        return new[] { t / a, 0, 0, 0, 0, -t, 0, 0, 0, 0, 0, -1, 0, 0, n, 0 };
    }
    public static double[] Look(V3 f, V3 u)
    {
        V3 s = Nrm(Cross(f, u)), w = Cross(s, f);
        return new[] { s.X, w.X, -f.X, 0, s.Y, w.Y, -f.Y, 0, s.Z, w.Z, -f.Z, 0, 0, 0, 0, 1 };
    }
    public static float[] Mul(double[] a, double[] b)
    {
        var o = new float[16];
        for (int c = 0; c < 4; c++)
            for (int r = 0; r < 4; r++)
                o[c * 4 + r] = (float)(a[r] * b[c * 4] + a[4 + r] * b[c * 4 + 1] + a[8 + r] * b[c * 4 + 2] + a[12 + r] * b[c * 4 + 3]);
        return o;
    }

    public static double Sm(double a, double b, double x) { x = (x - a) / (b - a); x = x < 0 ? 0 : x > 1 ? 1 : x; return x * x * (3 - 2 * x); }
    public static double Lerp(double t, double a, double b) => a + t * (b - a);
    public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    /// <summary>JavaScript Math.round (halves round towards +infinity).</summary>
    public static double JsRound(double v) => Math.Floor(v + 0.5);

    /// <summary>JavaScript ToInt32 of a double (used so the procedural texture hash matches the original bit for bit).</summary>
    public static int ToInt32(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) return 0;
        d = Math.Truncate(d);
        d %= 4294967296.0;
        if (d < 0) d += 4294967296.0;
        if (d >= 2147483648.0) d -= 4294967296.0;
        return (int)d;
    }
}
