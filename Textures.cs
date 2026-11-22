namespace CubePlanet;

/// <summary>Procedural 16x16 block tiles (layer = block id) and the 3D noise volume.</summary>
public static partial class Tex
{
    public const int NT = 118;
    static readonly int[][] TC =
    {
        new[]{0,0,0}, new[]{89,161,61}, new[]{125,125,125}, new[]{121,85,58}, new[]{103,82,51}, new[]{50,120,40}, new[]{151,201,240}, new[]{219,207,142}, new[]{240,244,248}, new[]{45,100,200}, new[]{136,126,122}, new[]{214,190,134},
        new[]{190,98,44}, new[]{152,94,67}, new[]{161,83,37}, new[]{186,133,35}, new[]{209,178,161}, new[]{143,61,46}, new[]{77,51,36}, new[]{58,168,38}, new[]{160,164,72}, new[]{92,138,96}, new[]{84,112,58}, new[]{36,82,52}, new[]{34,132,28}, new[]{104,150,44}, new[]{120,164,64}, new[]{214,214,204}, new[]{58,40,24}, new[]{88,70,34}, new[]{112,102,92}, new[]{162,130,78}, new[]{114,84,48}, new[]{170,92,52}, new[]{160,114,80}, new[]{118,118,118}, new[]{100,124,90}, new[]{128,128,130}, new[]{190,225,235}, new[]{224,208,156}, new[]{24,112,42}, new[]{196,168,44}, new[]{178,92,44}, new[]{70,150,55}, new[]{185,165,85}, new[]{50,120,50}, new[]{240,205,40}, new[]{200,30,28}, new[]{70,110,225}, new[]{165,85,205}, new[]{115,82,48}, new[]{45,115,40}, new[]{150,92,42}, new[]{200,40,35}, new[]{140,100,70}, new[]{150,205,90}, new[]{220,125,25}, new[]{95,165,45},
        new[]{40,40,42}, new[]{216,175,147}, new[]{252,238,75}, new[]{92,219,213}, new[]{214,24,24}, new[]{34,68,176}, new[]{224,126,86}, new[]{23,221,98},
        new[]{68,68,78},
        new[]{40,40,42}, new[]{216,175,147}, new[]{252,238,75}, new[]{92,219,213}, new[]{214,24,24}, new[]{34,68,176}, new[]{224,126,86}, new[]{23,221,98},
        new[]{235,100,20},
        new[]{58,150,50},
        new[]{88,112,40},
        new[]{112,76,38},
        new[]{150,190,240},
        new[]{90,150,240},
        new[]{162,168,184},
        new[]{240,230,200},
        new[]{58,58,64},
        new[]{100,96,98},
        new[]{150,56,18},
        new[]{24,16,40},
        new[]{84,170,150},
        new[]{204,234,228},
        new[]{128,122,116},
        new[]{84,50,58},
        new[]{228,180,172},
        new[]{150,118,74},
        new[]{200,164,104},
        new[]{244,168,196},
        new[]{100,146,60},
        new[]{78,128,52},
        new[]{52,150,44},
        new[]{58,96,78},
        new[]{48,96,220},
        new[]{222,100,160},
        new[]{150,60,200},
        new[]{200,40,40},
        new[]{240,200,60},
        new[]{240,150,190},
        new[]{240,130,175},
        new[]{95,110,230},
        new[]{160,110,220},
        new[]{110,170,50},
        new[]{60,120,50},
        new[]{40,150,70},
        new[]{90,120,40},
        new[]{80,130,40},
        new[]{60,100,230},
        new[]{230,110,170},
        new[]{160,70,210},
        new[]{210,50,50},
        new[]{240,210,70}
    };
    public static int[] TileColor(int id) => TC[id];
    static readonly HashSet<int> LOGS = new() { 4, 27, 28, 29, 30, 90, 92 }, PLK = new() { 31, 32, 33, 34, 91, 93 }, LEAFT = new() { 5, 23, 24, 25, 26, 94, 95, 96, 97, 98 }, TERR = new() { 13, 14, 15, 16, 17, 18 }, GRS = new() { 1, 19, 20, 21, 22, 76 };

    /// <summary>JavaScript-compatible integer hash giving [0,1) (bit-exact with the original, including its double rounding).</summary>
    public static double Hash(int x, int y, int i)
    {
        int h = M.ToInt32((double)x * 374761393 + (double)y * 668265263 + (double)i * 2147483647);
        h = M.ToInt32((double)(h ^ (int)((uint)h >> 13)) * 1274126177);
        return (uint)(h ^ (int)((uint)h >> 16)) / 4294967296.0;
    }
    static byte B8(double v) => (byte)Math.Clamp(Math.Round(v, MidpointRounding.ToEven), 0, 255);

    public static byte[] BuildAtlas()
    {
        var img = new byte[16 * 16 * NT * 4];
        for (int i = 1; i < NT; i++)
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    int o = ((i * 16 + y) * 16 + x) * 4;
                    double n = (Hash(x, y, i) - .5) * (i == 8 || i == 9 ? 10 : i == 10 ? 60 : TERR.Contains(i) ? 9 : 36);
                    double[] c = { TC[i][0] + n, TC[i][1] + n, TC[i][2] + n }; double a = 255;
                    void Mul(double k) { c[0] *= k; c[1] *= k; c[2] *= k; }
                    if (i == 9) a = 150;
                    if ((i == 11 || i == 39) && y % 4 == 0) Mul(.93);
                    if (LOGS.Contains(i) && x % 4 < 1) Mul(.75);
                    if (i == 27 && Hash(x, y, 99) < .14) c = new double[] { 50, 50, 45 };
                    if (LEAFT.Contains(i) && Hash(x, y, 9) < .25) a = 0;
                    if (GRS.Contains(i) && y < 3) c = new[] { c[0] * .9, c[1] * 1.05, c[2] * .9 };
                    if (PLK.Contains(i)) { if (y % 4 == 3) Mul(.8); if ((x + (y >> 2) * 5) % 8 == 0) Mul(.88); }
                    if (i == 35 || i == 36) { double q = Hash(x >> 2, y >> 2, i) - .5; c = new[] { c[0] + q * 60, c[1] + q * 60, c[2] + q * 60 }; if (x % 4 == 0 || y % 4 == 0) Mul(.8); }
                    if (i == 37 && (y % 8 == 0 || x % 8 == ((y >> 3) & 1) * 4)) Mul(.72);
                    if (i == 38)
                    {
                        bool e = x == 0 || x == 15 || y == 0 || y == 15, e2 = x == 1 || x == 14 || y == 1 || y == 14;
                        bool g = ((x == y || x == y + 1) && x > 2 && x < 8) || ((x == y + 5 || x == y + 6) && x > 8 && x < 13);
                        if (e) { c = new double[] { 150, 185, 200 }; a = 255; }
                        else if (e2) { c = new double[] { 200, 228, 236 }; a = 70; }
                        else if (g) { c = new double[] { 240, 250, 254 }; a = 120; }
                        else { c = new double[] { 170, 215, 225 }; a = 22; }
                    }
                    if (i == 6)
                    {
                        if (Hash(x, y, 66) > .92) c = new double[] { 236, 246, 255 };
                        if ((x * 3 + y * 2 + ((y >> 2) * 7)) % 17 == 0) c = new[] { Math.Min(255, c[0] * 1.1 + 14), Math.Min(255, c[1] * 1.1 + 14), Math.Min(255, c[2] * 1.1 + 14) };
                        if (Hash(x >> 1, y >> 1, 67) < .12) Mul(.9);
                    }
                    if (i == 40 && (x % 5 == 0 || y % 5 == 0)) c = new double[] { 18, 84, 32 };
                    if (i == 41 && x % 4 == 0) Mul(.8);
                    img[o] = B8(c[0]); img[o + 1] = B8(c[1]); img[o + 2] = B8(c[2]); img[o + 3] = B8(a);
                }
        PaintNew(img);
        PaintBiomes(img);
        for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) { int o = (y * 16 + x) * 4; img[o] = 255; img[o + 1] = 0; img[o + 2] = 255; img[o + 3] = 255; }
        return img;
    }

    // NOTE: texture row 0 is the BOTTOM of a block face, so y below = height above the ground
    static void PaintNew(byte[] img)
    {
        void Px(int i, int x, int y, double[] c, double a = 255)
        {
            if (x < 0 || x > 15 || y < 0 || y > 15) return;
            int o = ((i * 16 + y) * 16 + x) * 4; img[o] = B8(c[0]); img[o + 1] = B8(c[1]); img[o + 2] = B8(c[2]); img[o + 3] = B8(a);
        }
        void Clr(int i) { for (int k = 0; k < 256; k++) img[(i * 256 + k) * 4 + 3] = 0; }
        double[] Sh(double[] c, double k) => new[] { Math.Clamp(c[0] * k, 0, 255), Math.Clamp(c[1] * k, 0, 255), Math.Clamp(c[2] * k, 0, 255) };
        void Ln(int i, int x0, int y0, int x1, int y1, double[] c)
        {
            int n = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)); if (n == 0) n = 1;
            for (int k = 0; k <= n; k++) Px(i, (int)M.JsRound(x0 + (x1 - x0) * (double)k / n), (int)M.JsRound(y0 + (y1 - y0) * (double)k / n), c);
        }
        void Blades(int i, double[] bs, int[] ht)
        {
            Clr(i);
            for (int x = 0; x < 16; x++)
            {
                if (Hash(x, 1, i) < .2) continue;
                int h = ht[0] + (int)Math.Floor(Hash(x, 2, i) * ht[1]);
                for (int y = 0; y < h; y++) Px(i, x, y, Sh(bs, .7 + .5 * y / h + (Hash(x, y, i) - .5) * .25));
            }
        }
        Blades(43, new double[] { 70, 150, 55 }, new[] { 5, 10 }); Blades(44, new double[] { 185, 165, 85 }, new[] { 4, 9 });
        { // fern
            Clr(45); var g = new double[] { 50, 120, 50 }; Ln(45, 7, 0, 7, 13, Sh(g, .7)); Ln(45, 8, 0, 8, 12, Sh(g, .7));
            for (int y = 2; y <= 14; y++)
            {
                int hw = (int)M.JsRound(6 * Math.Sin((y - 1) / 14.0 * Math.PI));
                for (int k = 1; k <= hw; k++)
                    if ((k + y) % 2 == 0 || k < 3) { var c = Sh(g, .8 + Hash(k, y, 45) * .5); int yy = Math.Min(15, y + (k >> 2)); Px(45, 7 - k, yy, c); Px(45, 8 + k, yy, c); }
            }
        }
        void Flower(int i, double[] head, double[] center)
        {
            Clr(i); var g = new double[] { 55, 130, 45 };
            for (int y = 0; y < 9; y++) { Px(i, 7, y, Sh(g, .8 + Hash(7, y, i) * .3)); Px(i, 8, y, Sh(g, .7 + Hash(8, y, i) * .3)); }
            Px(i, 5, 2, g); Px(i, 6, 3, g); Px(i, 9, 4, g); Px(i, 10, 5, g);
            for (int y = 9; y <= 14; y++) for (int x = 5; x <= 10; x++)
                {
                    if ((x == 5 || x == 10) && (y == 9 || y == 14)) continue;
                    bool mid = x >= 7 && x <= 8 && y >= 11 && y <= 12;
                    Px(i, x, y, Sh(mid ? center : head, .85 + Hash(x, y, i) * .3));
                }
        }
        Flower(46, new double[] { 240, 205, 40 }, new double[] { 220, 130, 20 }); Flower(47, new double[] { 200, 30, 28 }, new double[] { 25, 25, 25 });
        Flower(48, new double[] { 70, 110, 225 }, new double[] { 190, 200, 255 }); Flower(49, new double[] { 165, 85, 205 }, new double[] { 220, 160, 240 });
        { // dead bush
            Clr(50); var b = new double[] { 115, 82, 48 };
            Ln(50, 7, 0, 7, 7, b); Ln(50, 8, 0, 8, 6, b); Ln(50, 7, 4, 3, 9, b); Ln(50, 3, 9, 2, 12, b); Ln(50, 8, 3, 12, 8, b); Ln(50, 12, 8, 13, 12, b); Ln(50, 7, 6, 6, 11, b); Ln(50, 8, 5, 9, 10, b);
        }
        { // vine
            Clr(51);
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
                    if (Hash(x, y, 51) < .3 + Hash(x, 0, 5151) * .55) Px(51, x, y, Sh(new double[] { 45, 115, 40 }, .7 + Hash(x, y, 52) * .5));
        }
        for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
            {
                double k = .85 + Hash(x, y, 52) * .25;
                Px(52, x, y, Sh(y >= 13 ? new double[] { 70, 110, 40 } : new double[] { 155, 92, 42 }, x % 4 == 0 && y < 13 ? k * .75 : k));
            }
        { // red mushroom
            Clr(53);
            for (int y = 0; y < 5; y++) { Px(53, 7, y, new double[] { 225, 215, 195 }); Px(53, 8, y, new double[] { 215, 205, 185 }); }
            foreach (var r in new[] { new[] { 5, 4, 11 }, new[] { 6, 3, 12 }, new[] { 7, 4, 11 }, new[] { 8, 6, 9 } })
                for (int x = r[1]; x <= r[2]; x++) Px(53, x, r[0], Hash(x, r[0], 53) < .2 ? new double[] { 235, 235, 225 } : Sh(new double[] { 200, 40, 35 }, .85 + Hash(x, r[0], 3) * .3));
        }
        { // brown mushroom
            Clr(54);
            for (int y = 0; y < 4; y++) { Px(54, 7, y, new double[] { 190, 170, 140 }); Px(54, 8, y, new double[] { 180, 160, 130 }); }
            foreach (var r in new[] { new[] { 4, 4, 11 }, new[] { 5, 3, 12 }, new[] { 6, 4, 11 } })
                for (int x = r[1]; x <= r[2]; x++) Px(54, x, r[0], Sh(new double[] { 140, 100, 70 }, .85 + Hash(x, r[0], 4) * .3));
        }
        { // sugar cane
            Clr(55);
            foreach (var xs in new[] { new[] { 1, 2 }, new[] { 7, 8 }, new[] { 12, 13 } })
                for (int y = 0; y < 16; y++) foreach (int x in xs) Px(55, x, y, Sh(y % 5 == 0 ? new double[] { 110, 160, 60 } : new double[] { 150, 205, 90 }, .9 + Hash(x, y, 55) * .2));
            foreach (var p in new[] { new[] { 3, 11 }, new[] { 4, 12 }, new[] { 5, 12 }, new[] { 9, 8 }, new[] { 10, 9 }, new[] { 11, 9 } }) Px(55, p[0], p[1], new double[] { 130, 185, 75 });
        }
        for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
            {
                Px(56, x, y, Sh(new double[] { 220, 125, 25 }, (x % 4 == 0 ? .78 : 1) * (.92 + Hash(x, y, 56) * .16)));
                Px(57, x, y, Sh(x % 4 < 2 ? new double[] { 60, 125, 40 } : new double[] { 110, 175, 55 }, .92 + Hash(x, y, 57) * .16));
            }
        // ---- ores (stone / deepslate background with blobs), deepslate, lava ----
        double[] ST = { 125, 125, 125 }, DS = { 68, 68, 78 };
        double[][] OC = { new double[] { 28, 28, 30 }, new double[] { 216, 175, 147 }, new double[] { 252, 238, 75 }, new double[] { 92, 219, 213 }, new double[] { 214, 24, 24 }, new double[] { 34, 68, 176 }, new double[] { 224, 126, 86 }, new double[] { 23, 221, 98 } };
        void Ore(int i, int seedId, double[] bc, double[] oc, bool deep)
        {
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
                {
                    double n = (Hash(x, y, i) - .5) * (deep ? 26 : 34), k = deep && y % 5 == 0 ? .86 : 1;
                    var c = new[] { (bc[0] + n) * k, (bc[1] + n) * k, (bc[2] + n) * k }; bool ore = false; double dm = 9;
                    for (int b = 0; b < 6; b++)
                    {
                        double cx = 1.5 + Hash(b, 1, seedId) * 13, cy = 1.5 + Hash(b, 2, seedId) * 13, rr = 1.2 + Hash(b, 3, seedId) * 1.1, d2 = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                        if (d2 < rr * rr) { ore = true; dm = Math.Min(dm, d2 / (rr * rr)); }
                    }
                    if (ore) { double k2 = (.82 + .36 * Hash(x, y, i + 200)) * (dm > .6 ? .82 : 1); c = new[] { oc[0] * k2, oc[1] * k2, oc[2] * k2 }; }
                    Px(i, x, y, c);
                }
        }
        for (int o = 0; o < 8; o++) { Ore(58 + o, 58 + o, ST, OC[o], false); Ore(67 + o, 58 + o, DS, OC[o], true); }
        for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
            {
                double n = (Hash(x, y, 66) - .5) * 26, k = y % 5 == 0 ? .84 : 1, f = Hash(x, y, 166) < .07 ? 12 : 0;
                Px(66, x, y, new[] { (DS[0] + n) * k + f, (DS[1] + n) * k + f, (DS[2] + n) * k + f });
                double h = Hash(x >> 1, y >> 1, 75);
                Px(75, x, y, h > .72 ? new double[] { 255, 205, 60 } : h < .2 ? new double[] { 150, 42, 10 } : Sh(new double[] { 235, 96, 18 }, .88 + Hash(x, y, 76) * .26));
            }
    }

    /// <summary>64^3 tiling noise (value noise octaves + worley) histogram-equalised so shader thresholds mean what they say.</summary>
    public static byte[] BuildNoise3D()
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
