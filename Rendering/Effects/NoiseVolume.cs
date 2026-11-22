namespace CubePlanet;

/// <summary>The 64^3 tiling noise texture the shaders sample (uN): built at startup, does not depend on the seed.</summary>
public static class NoiseVolume
{
    /// <summary>64^3 tiling noise (value noise octaves + worley) histogram-equalised so shader thresholds mean what they say.</summary>
    public static byte[] Build()
    {
        const int S = 64;
        var data = new byte[S * S * S]; var f = new float[S * S * S];
        double Hs(int x, int y, int z, int s)
        {
            int h = M.ToInt32((double)x * 374761393 + (double)y * 668265263 + (double)z * 1274126177 + (double)s * 2147483647);
            h = M.ToInt32((double)(h ^ (int)((uint)h >> 13)) * 1274126177);
            return (uint)(h ^ (int)((uint)h >> 16)) / 4294967296.0;
        }
        double[] Lat(int P) { var a = new double[P * P * P]; for (int z = 0; z < P; z++) for (int y = 0; y < P; y++) for (int x = 0; x < P; x++) a[x + P * (y + P * z)] = Hs(x, y, z, P); return a; }
        var octs = new (int P, double[] L, double w)[] { (4, Lat(4), .5), (8, Lat(8), .3), (16, Lat(16), .2) };
        const int WP = 6; var wx = new double[WP * WP * WP * 3];
        for (int z = 0; z < WP; z++) for (int y = 0; y < WP; y++) for (int x = 0; x < WP; x++) { int i = (x + WP * (y + WP * z)) * 3; wx[i] = Hs(x, y, z, 91); wx[i + 1] = Hs(x, y, z, 92); wx[i + 2] = Hs(x, y, z, 93); }
        for (int z = 0; z < S; z++) for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                {
                    double pn = 0;
                    foreach (var (P, L, w) in octs)
                    {
                        double X = (double)x * P / S, Y = (double)y * P / S, Z = (double)z * P / S;
                        int xi = (int)Math.Floor(X), yi = (int)Math.Floor(Y), zi = (int)Math.Floor(Z);
                        double u = Fade(X - xi), v = Fade(Y - yi), q = Fade(Z - zi);
                        double g(int a, int b, int c) => L[(a % P) + P * ((b % P) + P * (c % P))];
                        pn += w * M.Lerp(q, M.Lerp(v, M.Lerp(u, g(xi, yi, zi), g(xi + 1, yi, zi)), M.Lerp(u, g(xi, yi + 1, zi), g(xi + 1, yi + 1, zi))),
                                            M.Lerp(v, M.Lerp(u, g(xi, yi, zi + 1), g(xi + 1, yi, zi + 1)), M.Lerp(u, g(xi, yi + 1, zi + 1), g(xi + 1, yi + 1, zi + 1))));
                    }
                    double X2 = (double)x * WP / S, Y2 = (double)y * WP / S, Z2 = (double)z * WP / S;
                    int xi2 = (int)Math.Floor(X2), yi2 = (int)Math.Floor(Y2), zi2 = (int)Math.Floor(Z2); double md = 9;
                    for (int k = -1; k <= 1; k++) for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++)
                            {
                                int cx = xi2 + i, cy = yi2 + j, cz = zi2 + k, m = ((cx % WP) + WP) % WP, nn = ((cy % WP) + WP) % WP, oo = ((cz % WP) + WP) % WP, ii = (m + WP * (nn + WP * oo)) * 3;
                                double dx = cx + wx[ii] - X2, dy = cy + wx[ii + 1] - Y2, dz = cz + wx[ii + 2] - Z2, dd = dx * dx + dy * dy + dz * dz;
                                if (dd < md) md = dd;
                            }
                    f[x + S * (y + S * z)] = (float)(pn * .62 + (1 - Math.Min(1, Math.Sqrt(md) * 1.15)) * .38);
                }
        var idx = new int[f.Length]; for (int i = 0; i < idx.Length; i++) idx[i] = i;
        Array.Sort(idx, (a, b) => { int c = f[a].CompareTo(f[b]); return c != 0 ? c : a.CompareTo(b); });
        for (int r = 0; r < idx.Length; r++) data[idx[r]] = (byte)Math.Min(255, (int)Math.Floor((double)r / idx.Length * 256));
        return data;
    }
    static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
}
