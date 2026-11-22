namespace CubePlanet;

/// <summary>
/// 128^3 tiling cloud noise (RGBA8), built once and cached on disk:
///   R = Perlin-Worley (billowy base shapes), G/B/A = inverted Worley fBm at 4 / 8 / 16 cells per tile (erosion detail).
/// </summary>
public static class CloudNoise
{
    public const int S = 128;
    const int VERSION = 1;

    public static byte[] Get(string cacheDir)
    {
        string path = Path.Combine(cacheDir, $"cloudnoise_v{VERSION}.bin");
        try { if (File.Exists(path)) { var b = File.ReadAllBytes(path); if (b.Length == S * S * S * 4) return b; } } catch { }
        var d = Build();
        try { File.WriteAllBytes(path, d); } catch { }
        return d;
    }

    static uint Hash(int x, int y, int z, int s)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177 + s * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177u; return h ^ (h >> 16);
        }
    }
    static double H01(int x, int y, int z, int s) => Hash(x, y, z, s) / 4294967296.0;
    static int Wrap(int v, int p) => ((v % p) + p) % p;

    // tiling gradient noise with period p (coordinates in cells), result about -1..1
    static double Perlin(double x, double y, double z, int p)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y), zi = (int)Math.Floor(z);
        double fx = x - xi, fy = y - yi, fz = z - zi;
        double u = fx * fx * fx * (fx * (fx * 6 - 15) + 10), v = fy * fy * fy * (fy * (fy * 6 - 15) + 10), w = fz * fz * fz * (fz * (fz * 6 - 15) + 10);
        double G(int a, int b, int c, double dx, double dy, double dz)
        {
            uint h = Hash(Wrap(a, p), Wrap(b, p), Wrap(c, p), 17) & 15;
            double gu = h < 8 ? dx : dy, gv = h < 4 ? dy : (h == 12 || h == 14) ? dx : dz;
            return ((h & 1) == 0 ? gu : -gu) + ((h & 2) == 0 ? gv : -gv);
        }
        double L(double a, double b, double t) => a + (b - a) * t;
        return L(L(L(G(xi, yi, zi, fx, fy, fz), G(xi + 1, yi, zi, fx - 1, fy, fz), u), L(G(xi, yi + 1, zi, fx, fy - 1, fz), G(xi + 1, yi + 1, zi, fx - 1, fy - 1, fz), u), v),
                 L(L(G(xi, yi, zi + 1, fx, fy, fz - 1), G(xi + 1, yi, zi + 1, fx - 1, fy, fz - 1), u), L(G(xi, yi + 1, zi + 1, fx, fy - 1, fz - 1), G(xi + 1, yi + 1, zi + 1, fx - 1, fy - 1, fz - 1), u), v), w);
    }
    // tiling Worley noise with f cells per tile, returns 1 - distance to the nearest feature point (1 = at a point)
    static double Worley(double x, double y, double z, int f, int seed)
    {
        x *= f; y *= f; z *= f;
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y), zi = (int)Math.Floor(z); double md = 9;
        for (int k = -1; k <= 1; k++)
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    int cx = xi + i, cy = yi + j, cz = zi + k, wx = Wrap(cx, f), wy = Wrap(cy, f), wz = Wrap(cz, f);
                    double px = cx + H01(wx, wy, wz, seed), py = cy + H01(wx, wy, wz, seed + 1), pz = cz + H01(wx, wy, wz, seed + 2);
                    double dx = px - x, dy = py - y, dz = pz - z, d = dx * dx + dy * dy + dz * dz; if (d < md) md = d;
                }
        return 1 - Math.Min(1, Math.Sqrt(md));
    }
    static double WorleyFbm(double x, double y, double z, int f) => Worley(x, y, z, f, 31) * .625 + Worley(x, y, z, f * 2, 47) * .25 + Worley(x, y, z, f * 4, 59) * .125;

    static byte[] Build()
    {
        var o = new byte[S * S * S * 4];
        Parallel.For(0, S, zz =>
        {
            for (int yy = 0; yy < S; yy++)
                for (int xx = 0; xx < S; xx++)
                {
                    double x = (xx + .5) / S, y = (yy + .5) / S, z = (zz + .5) / S;
                    double pn = Perlin(x * 4, y * 4, z * 4, 4) * .5 + Perlin(x * 8, y * 8, z * 8, 8) * .25 + Perlin(x * 16, y * 16, z * 16, 16) * .125;
                    pn = Math.Clamp(pn * .9 + .5, 0, 1);
                    double wb = WorleyFbm(x, y, z, 4);
                    double pw = Math.Clamp(wb + pn * (1 - wb) - .25 * (1 - pn), 0, 1); // remap(perlin, 0, 1, worley, 1), slightly darkened where perlin is low
                    double g = WorleyFbm(x, y, z, 4), b = WorleyFbm(x, y, z, 8), a = WorleyFbm(x, y, z, 16);
                    int i = ((zz * S + yy) * S + xx) * 4;
                    o[i] = (byte)Math.Round(pw * 255); o[i + 1] = (byte)Math.Round(g * 255); o[i + 2] = (byte)Math.Round(b * 255); o[i + 3] = (byte)Math.Round(a * 255);
                }
        });
        return o;
    }
}
