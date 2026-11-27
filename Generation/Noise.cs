using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
    // gradient h: (+/-u) + (+/-v) with u = x / y and v = y / x / z as in Grd; as coefficients of (x, y, z) (an exact rewrite: the same two terms,
    // the third times 0); 16 entries x 3
    static readonly double[] GT = BuildGT();
    static double[] BuildGT()
    {
        var g = new double[48];
        for (int h = 0; h < 16; h++)
        {
            int ui = h < 8 ? 0 : 1, vi = h < 4 ? 1 : (h == 12 || h == 14 ? 0 : 2);
            g[h * 3 + ui] += (h & 1) != 0 ? -1 : 1; g[h * 3 + vi] += (h & 2) != 0 ? -1 : 1;
        }
        return g;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static double G3(ref double gt, int h, double x, double y, double z)
    {
        ref double g = ref Unsafe.Add(ref gt, (h & 15) * 3);
        return g * x + Unsafe.Add(ref g, 1) * y + Unsafe.Add(ref g, 2) * z;
    }
    /// <summary>Perlin noise (the classic improved noise): the same values as before, about twice as fast (no bounds checks, no Math.Floor,
    /// gradients from a table).</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)] public static double Noise3(double x, double y, double z)
    {
        int X = (int)x, Y = (int)y, Z = (int)z; if (x < X) X--; if (y < Y) Y--; if (z < Z) Z--;
        x -= X; y -= Y; z -= Z; X &= 255; Y &= 255; Z &= 255;
        double u = x * x * x * (x * (x * 6 - 15) + 10), v = y * y * y * (y * (y * 6 - 15) + 10), w = z * z * z * (z * (z * 6 - 15) + 10);
        ref byte P = ref MemoryMarshal.GetArrayDataReference(PERM); ref double gt = ref MemoryMarshal.GetArrayDataReference(GT);
        int A = Unsafe.Add(ref P, X) + Y, AA = Unsafe.Add(ref P, A) + Z, AB = Unsafe.Add(ref P, A + 1) + Z, B = Unsafe.Add(ref P, X + 1) + Y, BA = Unsafe.Add(ref P, B) + Z, BB = Unsafe.Add(ref P, B + 1) + Z;
        double x1 = x - 1, y1 = y - 1, z1 = z - 1;
        double g0 = G3(ref gt, Unsafe.Add(ref P, AA), x, y, z), g1 = G3(ref gt, Unsafe.Add(ref P, BA), x1, y, z);
        double g2 = G3(ref gt, Unsafe.Add(ref P, AB), x, y1, z), g3 = G3(ref gt, Unsafe.Add(ref P, BB), x1, y1, z);
        double g4 = G3(ref gt, Unsafe.Add(ref P, AA + 1), x, y, z1), g5 = G3(ref gt, Unsafe.Add(ref P, BA + 1), x1, y, z1);
        double g6 = G3(ref gt, Unsafe.Add(ref P, AB + 1), x, y1, z1), g7 = G3(ref gt, Unsafe.Add(ref P, BB + 1), x1, y1, z1);
        double a0 = g0 + u * (g1 - g0), a1 = g2 + u * (g3 - g2), a2 = g4 + u * (g5 - g4), a3 = g6 + u * (g7 - g6);
        double b0 = a0 + v * (a1 - a0), b1 = a2 + v * (a3 - a2);
        return b0 + w * (b1 - b0);
    }
    // the normalisation 2 (1 - 0.5^oct) of Fb / Ridge, computed once per octave count (it was a Math.Pow per call)
    static readonly double[] Norm2 = Enumerable.Range(0, 33).Select(o => 2 * (1 - Math.Pow(.5, o))).ToArray();
    internal static int NOct(int oct, double L, double lw) => lw > 0 ? Math.Max(1, Math.Min(oct, (int)Math.Floor(Math.Log2(Math.Max(L / lw, 1))) + 1)) : oct;
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)] public static double Fb(double x, double y, double z, double L, int oct, int k, double lw)
    {
        // LOD: octaves finer than the sample spacing are dropped but the normalisation stays that of the full noise,
        // so a distant patch is a true low-pass of the real terrain (same coastlines, same biome thresholds) instead of a stretched version of it
        var o = OFF[k]; int m = NOct(oct, L, lw); double f = 1 / L, a = 1, s = 0;
        for (int i = 0; i < m; i++) { s += a * Noise3(x * f + o[0], y * f + o[1], z * f + o[2]); a *= .5; f *= 2.03; }
        return s / (oct < Norm2.Length ? Norm2[oct] : 2 * (1 - Math.Pow(.5, oct)));
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)] public static double Ridge(double x, double y, double z, double L, int oct, int k, double lw)
    {
        var o = OFF[k]; int m = NOct(oct, L, lw); double f = 1 / L, a = 1, s = 0;
        for (int i = 0; i < m; i++) { double v = 1 - Math.Abs(Noise3(x * f + o[0], y * f + o[1], z * f + o[2])); s += a * v * v; a *= .5; f *= 2.03; }
        for (int i = m; i < oct; i++) { s += a * .63; a *= .5; } // dropped octaves contribute their average
        return s / (oct < Norm2.Length ? Norm2[oct] : 2 * (1 - Math.Pow(.5, oct)));
    }
}
