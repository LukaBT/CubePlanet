using System.Numerics;

namespace CubePlanet;

/// <summary>Seeded Perlin noise, fBm and ridged noise (same seed gives the same planet as the original page).</summary>
public static class Noise
{
    // ---------- noise ----------
    public static readonly byte[] PERM = new byte[512];
    public static readonly double[][] OFF = new double[48][];

    public static void SeedNoise(int s)
    {
        uint q = (uint)s; if (q == 0) q = 1;
        double Rnd() { q = unchecked(q * 1664525u + 1013904223u); return q / 4294967296.0; }
        var p = new byte[256]; for (int i = 0; i < 256; i++) p[i] = (byte)i;
        for (int i = 255; i > 0; i--) { int j = (int)Math.Floor(Rnd() * (i + 1)); (p[i], p[j]) = (p[j], p[i]); }
        for (int i = 0; i < 512; i++) PERM[i] = p[i & 255];
        for (int i = 0; i < 48; i++) OFF[i] = new[] { Rnd() * 200, Rnd() * 200, Rnd() * 200 };
    }
    internal static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
    internal static double Grd(int h, double x, double y, double z)
    {
        h &= 15; double u = h < 8 ? x : y, v = h < 4 ? y : (h == 12 || h == 14 ? x : z);
        return ((h & 1) != 0 ? -u : u) + ((h & 2) != 0 ? -v : v);
    }
    public static double Noise3(double x, double y, double z)
    {
        int X = (int)Math.Floor(x), Y = (int)Math.Floor(y), Z = (int)Math.Floor(z);
        x -= X; y -= Y; z -= Z; X &= 255; Y &= 255; Z &= 255;
        double u = Fade(x), v = Fade(y), w = Fade(z);
        int A = PERM[X] + Y, AA = PERM[A] + Z, AB = PERM[A + 1] + Z, B = PERM[X + 1] + Y, BA = PERM[B] + Z, BB = PERM[B + 1] + Z;
        return M.Lerp(w,
            M.Lerp(v, M.Lerp(u, Grd(PERM[AA], x, y, z), Grd(PERM[BA], x - 1, y, z)), M.Lerp(u, Grd(PERM[AB], x, y - 1, z), Grd(PERM[BB], x - 1, y - 1, z))),
            M.Lerp(v, M.Lerp(u, Grd(PERM[AA + 1], x, y, z - 1), Grd(PERM[BA + 1], x - 1, y, z - 1)), M.Lerp(u, Grd(PERM[AB + 1], x, y - 1, z - 1), Grd(PERM[BB + 1], x - 1, y - 1, z - 1))));
    }
    internal static int NOct(int oct, double L, double lw) => lw > 0 ? Math.Max(1, Math.Min(oct, (int)Math.Floor(Math.Log2(Math.Max(L / lw, 1))) + 1)) : oct;
    public static double Fb(double x, double y, double z, double L, int oct, int k, double lw)
    {
        // LOD: octaves finer than the sample spacing are dropped but the normalisation stays that of the full noise,
        // so a distant patch is a true low-pass of the real terrain (same coastlines, same biome thresholds) instead of a stretched version of it
        var o = OFF[k]; int m = NOct(oct, L, lw); double f = 1 / L, a = 1, s = 0;
        for (int i = 0; i < m; i++) { s += a * Noise3(x * f + o[0], y * f + o[1], z * f + o[2]); a *= .5; f *= 2.03; }
        return s / (2 * (1 - Math.Pow(.5, oct)));
    }
    public static double Ridge(double x, double y, double z, double L, int oct, int k, double lw)
    {
        var o = OFF[k]; int m = NOct(oct, L, lw); double f = 1 / L, a = 1, s = 0;
        for (int i = 0; i < m; i++) { double v = 1 - Math.Abs(Noise3(x * f + o[0], y * f + o[1], z * f + o[2])); s += a * v * v; a *= .5; f *= 2.03; }
        for (int i = m; i < oct; i++) { s += a * .63; a *= .5; } // dropped octaves contribute their average
        return s / (2 * (1 - Math.Pow(.5, oct)));
    }
}
