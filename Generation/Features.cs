namespace CubePlanet;

/// <summary>Biome features: trees and plants (decorate step, per chunk, deterministic).</summary>
public static class Features
{
    internal static readonly int[] GR = { 1, 19, 20, 21, 22, 8, 76, 77, 78 };
    internal static readonly int[] FLW = { 46, 47, 48, 49 }, FL2 = { 46, 47, 48, 49, 105, 106, 107 };

    public static void ClearFeatCaches() { WGEN++; colCache.Clear(); }

    public static double HH(int a, int b, int c, int d)
    {
        unchecked
        {
            int h = a * 374761393 ^ b * 668265263 ^ c * 2147483647 ^ d * 1274126177 ^ SEEDV * 1103515245;
            h = (h ^ (int)((uint)h >> 13)) * 1274126177;
            return (uint)(h ^ (int)((uint)h >> 16)) / 4294967296.0;
        }
    }
    internal static int GyOf(Band B, double h) => (int)Math.Ceiling(R + h - B.inner - .5) - 1;
    internal static void ColT(Band B, int f, int bx, int bz)
    {
        V3 t = GpA(f, -PI / 4 + (bz + .5) * B.d, -PI / 4 + (bx + .5) * B.d, 1);
        Terrain(t.X, t.Y, t.Z, 0);
    }
    internal static Feat MkF(Sb S, int px, int pz, bool st)
    {
        if (S.L.Count == 0) return null;
        return new Feat { x = px, z = pz, L = S.L.ToArray(), a = px + S.m[0], b = px + S.m[1], c = pz + S.m[2], e = pz + S.m[3], g = S.m[4], h = S.m[5], st = st };
    }

    internal static Feat MkPlant(int k, int f, int px, int pz, int gy, int dmax = 0)
    {
        var S = new Sb(); int q = 0;
        double Rn() => HH(f, px, pz, 100 + q++);
        void L(int x, int y, int z, int id) => S.Put(x, y, z, id);
        void Lay(int cy, int r, int id, double cut, int cx = 0, int cz = 0)
        {
            for (int x = -r; x <= r; x++)
                for (int z = -r; z <= r; z++)
                {
                    if (r > 0 && Math.Abs(x) == r && Math.Abs(z) == r && (r < 2 || Rn() < cut)) continue;
                    L(x + cx, cy, z + cz, id);
                }
        }
        void JungleExtras(bool big, int h)
        {
            int[][] sides = big
                ? new[] { new[] { 2, 0 }, new[] { 2, 1 }, new[] { -1, 0 }, new[] { -1, 1 }, new[] { 0, -1 }, new[] { 1, -1 }, new[] { 0, 2 }, new[] { 1, 2 } }
                : new[] { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 } };
            for (int i = 0; i < (big ? 6 : 3); i++)
            {
                var s = sides[(int)Math.Floor(Rn() * sides.Length)];
                int y = gy + Math.Max(2, h - 5) + (int)Math.Floor(Rn() * 3);
                if (Rn() < .7) L(s[0], y, s[1], 52);
            }
            for (int i = 0; i < (big ? 10 : 4); i++)
            {
                var s = sides[(int)Math.Floor(Rn() * sides.Length)];
                int y = gy + 3 + (int)Math.Floor(Rn() * Math.Max(1, h - 5));
                int len = Math.Min(1 + (int)Math.Floor(Rn() * 4), y - gy);
                for (int j = 0; j < len; j++) L(s[0], y - j, s[1], 51);
            }
            for (int i = 0; i < (big ? 14 : 8); i++)
            {
                int dy = big ? h - 1 + (int)Math.Floor(Rn() * 3) : h - 2 + (int)Math.Floor(Rn() * 3);
                int r = big ? (dy == h - 1 ? 3 : 4) : (dy == h - 2 ? 2 : 3);
                int dd = (int)Math.Floor(Rn() * 4);
                int t = big ? (int)Math.Floor(Rn() * 2) : (int)Math.Floor(Rn() * 3) - 1;
                int cx, cz;
                if (big) { cx = dd == 0 ? r + 1 : dd == 1 ? -r : t; cz = dd == 2 ? r + 1 : dd == 3 ? -r : t; }
                else { cx = dd == 0 ? r + 1 : dd == 1 ? -r - 1 : t; cz = dd == 2 ? r + 1 : dd == 3 ? -r - 1 : t; }
                int len = Math.Min(2 + (int)Math.Floor(Rn() * 4), dy - 1);
                for (int j = 0; j < len; j++) L(cx, gy + dy - j, cz, 51);
            }
        }
        if (k < 2)
        {
            int lg = k != 0 ? 27 : 4, lf = k != 0 ? 26 : 5, h = 4 + (int)Math.Floor(Rn() * 3) + k;
            for (int y = 1; y <= h; y++) L(0, gy + y, 0, lg);
            Lay(gy + h - 2, 2, lf, .5); Lay(gy + h - 1, 2, lf, .5); Lay(gy + h, 1, lf, 0); Lay(gy + h + 1, 1, lf, 0);
        }
        else if (k == 2 || k == 13)
        {
            int lfs = k == 13 ? 98 : 23; // 13 = snow-laden spruce
            int h = 6 + (int)Math.Floor(Rn() * 4);
            for (int y = 1; y <= h + 1; y++) L(0, gy + y, 0, 28);
            for (int dy = h + 1, j = 0; dy >= 3; dy--, j++) Lay(gy + dy, j != 0 ? Math.Min(3, (j + 1) >> 1) : 0, lfs, j % 2 != 0 ? 0 : 1);
        }
        else if (k == 3)
        {
            if (Rn() < .2)
            {
                int h = 13 + (int)Math.Floor(Rn() * 9);
                for (int y = 1; y <= h; y++) { L(0, gy + y, 0, 29); L(1, gy + y, 0, 29); L(0, gy + y, 1, 29); L(1, gy + y, 1, 29); }
                foreach (var (dy, r) in new[] { (h - 1, 3), (h, 4), (h + 1, 4), (h + 2, 3), (h + 3, 1) })
                    for (int x = -r; x <= r + 1; x++) for (int z = -r; z <= r + 1; z++)
                            if ((x - .5) * (x - .5) + (z - .5) * (z - .5) <= r * r + .8) L(x, gy + dy, z, 24);
                JungleExtras(true, h);
            }
            else
            {
                int h = 5 + (int)Math.Floor(Rn() * 5);
                for (int y = 1; y <= h; y++) L(0, gy + y, 0, 29);
                Lay(gy + h - 2, 2, 24, .4); Lay(gy + h - 1, 3, 24, .6); Lay(gy + h, 3, 24, .7); Lay(gy + h + 1, 2, 24, .5); Lay(gy + h + 2, 1, 24, 0);
                JungleExtras(false, h);
            }
        }
        else if (k == 4)
        {
            int h = 4 + (int)Math.Floor(Rn() * 3); bool ax = Rn() < .5; int sg = Rn() < .5 ? 1 : -1, ox = ax ? sg : 0, oz = ax ? 0 : sg;
            for (int y = 1; y <= h; y++) L(0, gy + y, 0, 30);
            L(ox, gy + h + 1, oz, 30); L(ox * 2, gy + h + 2, oz * 2, 30);
            Lay(gy + h + 2, 2, 25, .6, ox * 2, oz * 2); Lay(gy + h + 3, 1, 25, 0, ox * 2, oz * 2);
        }
        else if (k == 5)
        {
            int h = 1 + (int)Math.Floor(Rn() * 3);
            for (int y = 1; y <= h; y++) L(0, gy + y, 0, 40);
        }
        else if (k == 6)
        {
            int r = 1 + (Rn() < .5 ? 1 : 0);
            for (int y = 0; y <= r; y++) for (int x = -r; x <= r; x++) for (int z = -r; z <= r; z++)
                        if (x * x + y * y * 1.6 + z * z <= r * r + .6 && Rn() < .92) L(x, gy + 1 + y, z, Rn() < .6 ? 36 : 35);
        }
        else if (k == 7)
        {
            int h = 8 + (int)Math.Floor(Rn() * 10);
            for (int y = 1; y <= h; y++)
            {
                int r = y < h * .4 ? 1 : 0;
                for (int x = -r; x <= r; x++) for (int z = -r; z <= r; z++) if (Math.Abs(x) + Math.Abs(z) <= r) L(x, gy + y, z, 6);
            }
        }
        else if (k == 8) // big lush oak: tall trunk, huge rounded canopy, hanging vines
        {
            int h = 8 + (int)Math.Floor(Rn() * 4);
            for (int y = 1; y <= h; y++) L(0, gy + y, 0, 4);
            for (int i = 0; i < 2; i++)
            {
                int sg = Rn() < .5 ? 1 : -1; bool ax = Rn() < .5; int by = h - 3 + (int)Math.Floor(Rn() * 2);
                L(ax ? sg : 0, gy + by, ax ? 0 : sg, 4); L(ax ? 2 * sg : 0, gy + by + 1, ax ? 0 : 2 * sg, 4);
            }
            int[] rr = { 2, 3, 4, 4, 3, 2, 1 };
            for (int i = 0; i < rr.Length; i++) Lay(gy + h - 3 + i, rr[i], 5, i < 2 || i > 4 ? .7 : .35);
            for (int i = 0; i < 9; i++)
            {
                int a = (int)Math.Floor(Rn() * 4), t = (int)Math.Floor(Rn() * 5) - 2;
                int vx = a == 0 ? 5 : a == 1 ? -5 : t, vz = a == 2 ? 5 : a == 3 ? -5 : t;
                int vy = gy + h - 1 + (int)Math.Floor(Rn() * 2), len = 2 + (int)Math.Floor(Rn() * 4);
                for (int j = 0; j < len; j++) L(vx, vy - j, vz, 51);
            }
        }
        else if (k == 9) // flowering azalea tree: short trunk, dense canopy of green and blossoming leaves
        {
            int h = 4 + (int)Math.Floor(Rn() * 2);
            for (int y = 1; y <= h; y++) L(0, gy + y, 0, 4);
            int[] rr = { 1, 2, 3, 3, 2, 1 };
            for (int i = 0; i < rr.Length; i++)
            {
                int cy = gy + h - 2 + i, r = rr[i];
                for (int x = -r; x <= r; x++)
                    for (int z = -r; z <= r; z++)
                    {
                        if (Math.Abs(x) == r && Math.Abs(z) == r && (r < 2 || Rn() < .5)) continue;
                        L(x, cy, z, Rn() < .35 ? 95 : 96);
                    }
            }
        }
        else if (k == 10) // cherry tree: leaning forked trunk with a wide, flat blossom canopy
        {
            int h = 4 + (int)Math.Floor(Rn() * 3); bool ax = Rn() < .5; int sg = Rn() < .5 ? 1 : -1, ox = ax ? sg : 0, oz = ax ? 0 : sg;
            for (int y = 1; y <= h; y++) L(0, gy + y, 0, 90);
            L(ox, gy + h, oz, 90); L(ox, gy + h + 1, oz, 90); L(ox * 2, gy + h + 1, oz * 2, 90); L(-ox, gy + h - 1, -oz, 90); L(-ox * 2, gy + h, -oz * 2, 90);
            int[][] lay = { new[] { h - 1, 3 }, new[] { h, 4 }, new[] { h + 1, 4 }, new[] { h + 2, 3 }, new[] { h + 3, 1 } };
            foreach (var ly in lay) Lay(gy + ly[0], ly[1], 94, .7, ox, oz);
        }
        else if (k == 11) // palm: leaning trunk, drooping fronds, coconuts
        {
            int h = 7 + (int)Math.Floor(Rn() * 5); bool ax = Rn() < .5; int sg = Rn() < .5 ? 1 : -1, dx = ax ? sg : 0, dz = ax ? 0 : sg, lean = 1 + (int)Math.Floor(Rn() * 3);
            for (int y = 1; y <= h; y++) { int sh = (int)Math.Floor(lean * Math.Pow((double)y / h, 1.7) + .01); L(dx * sh, gy + y, dz * sh, 92); }
            int tx = dx * lean, tz = dz * lean;
            int[][] dirs = { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 }, new[] { 1, 1 }, new[] { 1, -1 }, new[] { -1, 1 }, new[] { -1, -1 } };
            int[] yo = { 1, 1, 0, -1 };
            foreach (var d in dirs)
            {
                bool diag = d[0] != 0 && d[1] != 0; int len = diag ? 3 : 4;
                for (int i = 1; i <= len; i++)
                {
                    L(tx + d[0] * i, gy + h + yo[i - 1], tz + d[1] * i, 97);
                    if (!diag && (i == 2 || i == 3)) { L(tx + d[0] * i + d[1], gy + h + yo[i - 1] - 1, tz + d[1] * i + d[0], 97); L(tx + d[0] * i - d[1], gy + h + yo[i - 1] - 1, tz + d[1] * i - d[0], 97); }
                }
            }
            L(tx, gy + h + 1, tz, 97); L(tx, gy + h + 2, tz, 97);
            for (int i = 0; i < 3; i++) { var d = dirs[(int)Math.Floor(Rn() * 4)]; L(tx + d[0], gy + h, tz + d[1], 52); }
        }
        else if (k == 12) // giant old-growth spruce: 2x2 trunk, tall layered cone
        {
            int h = 17 + (int)Math.Floor(Rn() * 9);
            for (int y = 1; y <= h; y++) { L(0, gy + y, 0, 28); L(1, gy + y, 0, 28); L(0, gy + y, 1, 28); L(1, gy + y, 1, 28); }
            for (int j = 0, y = h + 3; y >= 5; y--, j++)
            {
                int r = j == 0 ? 0 : Math.Min(5, 1 + (int)(j * .42));
                if (j > 2 && (j & 1) == 1) r = Math.Max(1, r - 1);
                for (int x = -r; x <= r + 1; x++) for (int z = -r; z <= r + 1; z++)
                        if ((x - .5) * (x - .5) + (z - .5) * (z - .5) <= r * r + .8) L(x, gy + y, z, 23);
            }
        }
        else if (k == 14) // bamboo grove cluster
        {
            int n = 3 + (int)Math.Floor(Rn() * 4);
            for (int i = 0; i < n; i++)
            {
                int bx = (int)Math.Floor(Rn() * 3) - 1, bz = (int)Math.Floor(Rn() * 3) - 1, bh = 7 + (int)Math.Floor(Rn() * 9);
                for (int y = 1; y <= bh; y++) L(bx, gy + y, bz, 108);
            }
        }
        else if (k >= 20 && k <= 23) // underwater: coral formations and kelp (never taller than the water above the floor, so nothing breaks the surface)
        {
            if (dmax < 1) return null;
            void Lc(int x, int y, int z, int id) { if (y - gy <= dmax) L(x, y, z, id); }
            int cc = 99 + (int)Math.Floor(Rn() * 5);
            if (k == 20) // coral tree
            {
                int h = Math.Min(dmax, 3 + (int)Math.Floor(Rn() * 4));
                for (int y = 1; y <= h; y++) Lc(0, gy + y, 0, cc);
                for (int i = 0; i < 3; i++)
                {
                    int by = 2 + (int)Math.Floor(Rn() * Math.Max(1, h - 1)), dd = (int)Math.Floor(Rn() * 4), dx = dd == 0 ? 1 : dd == 1 ? -1 : 0, dz = dd == 2 ? 1 : dd == 3 ? -1 : 0;
                    int len = 1 + (int)Math.Floor(Rn() * 2), up = 1 + (int)Math.Floor(Rn() * 3);
                    for (int j = 1; j <= len; j++) Lc(dx * j, gy + by, dz * j, cc);
                    for (int j = 1; j <= up; j++) Lc(dx * len, gy + by + j, dz * len, cc);
                }
            }
            else if (k == 21) // brain coral dome
            {
                int r = 2 + (Rn() < .4 ? 1 : 0);
                for (int y = 0; y <= r; y++) for (int x = -r; x <= r; x++) for (int z = -r; z <= r; z++)
                            if (x * x + y * y * 1.4 + z * z <= r * r + .6 && Rn() < .93) Lc(x, gy + 1 + y, z, cc);
            }
            else if (k == 22) // tube coral cluster
            {
                int n = 4 + (int)Math.Floor(Rn() * 3);
                for (int i = 0; i < n; i++)
                {
                    int bx = (int)Math.Floor(Rn() * 3) - 1, bz = (int)Math.Floor(Rn() * 3) - 1, bh = 2 + (int)Math.Floor(Rn() * 4);
                    for (int y = 1; y <= bh; y++) Lc(bx, gy + y, bz, cc);
                }
            }
            else // kelp stalk
            {
                int h = Math.Min(dmax, 4 + (int)Math.Floor(Rn() * 9));
                for (int y = 1; y <= h; y++) L(0, gy + y, 0, 111);
            }
        }
        return MkF(S, px, pz, false);
    }

    internal static Feat PlantAt(Band B, int f, int qx, int qz)
    {
        int px = qx * 4 + (int)Math.Floor(HH(f, qx, qz, 1) * 4), pz = qz * 4 + (int)Math.Floor(HH(f, qx, qz, 2) * 4);
        if (px < 10 || pz < 10 || px >= B.cols - 10 || pz >= B.cols - 10) return null;
        long key = ((long)(B.b * 6 + f) * 8388608 + qx + 8) * 8388608 + qz + 8;
        if (PLC.TryGetValue(key, out var F)) return F;
        F = null; if (PLC.Count > 30000) PLC.Clear();
        double u = HH(f, qx, qz, 3), v = HH(f, qx, qz, 4);
        ColT(B, f, px, pz); int b = gB; double H = gH; int t = gT;
        double hmin = (b == 16 || b == 25 || b == 26) ? .3 : 1.2; // palms may grow right at the waterline
        if (H >= hmin && H < 40 * HS && gI == 0)
        {
            double p = 0; int k = -1;
            switch (b)
            {
                case 1: p = .03; k = 0; break;
                case 2: p = .8; k = v < .12 ? 1 : 0; break;
                case 3: p = .75; k = v < .15 ? 0 : 1; break;
                case 4: p = .8; k = v < .04 ? 6 : 2; break;
                case 5: p = .08; k = v < .25 ? 7 : 2; break;
                case 6: p = .95; k = 3; break;
                case 7: p = .12; k = 4; break;
                case 8: p = .04; k = 5; break;
                case 9: p = .03; k = 5; break;
                case 10: p = .4; k = 0; break;
                case 14: p = .85; k = v < .25 ? 9 : v < .6 ? 8 : 0; break;
                case 15: p = .5; k = v < .88 ? 10 : 1; break;
                case 16: p = .55; k = 11; break;
                case 17: p = .03; k = 5; break;
                case 18: p = .75; k = v < .55 ? 13 : 2; break;
                case 19: p = .85; k = v < .2 ? 12 : v < .26 ? 6 : 2; break;
                case 25: p = .24; k = 11; break;
                case 26: p = .9; k = v < .65 ? 11 : 3; break;
                case 27: p = .06; k = 6; break;
                case 28: p = .95; k = v < .8 ? 14 : 3; break;
                case 29: p = .015; k = 0; break;
            }
            bool okTop = k == 5 ? (t == 7 || t == 12) : k == 11 ? (t == 7 || t == 82 || Array.IndexOf(GR, t) >= 0) : (k == 6 && b == 27) ? t == 10 : Array.IndexOf(GR, t) >= 0;
            if (k >= 0 && u < p && okTop)
            {
                int gy = GyOf(B, H);
                F = gy < 0 ? null : MkPlant(k, f, px, pz, gy);
                if (F != null && (F.g < 0 || F.h >= B.rows)) F = null;
            }
        }
        else if (H < -2.2 && H > -32 && gI == 0)
        {   // under water: coral reefs and kelp forests
            double p = 0; int k = -1;
            switch (b)
            {
                case 21: p = .5; k = v < .38 ? 20 : v < .68 ? 21 : 22; break;
                case 31: p = .03; k = 21; break;
                case 22: p = .8; k = 23; break;
                case 0: p = .05; k = 23; break;
            }
            if (k >= 0 && u < p && (k == 23 || t == 82 || t == 7 || t == 81))
            {
                int gy = GyOf(B, H), dmax = (int)Math.Floor(-H) - 1;
                F = gy < 0 || dmax < 1 ? null : MkPlant(k, f, px, pz, gy, dmax);
                if (F != null) { F.uw = true; if (F.g < 0 || F.h >= B.rows) F = null; }
            }
        }
        PLC[key] = F; return F;
    }


    internal static List<Feat> FeatsR(Band B, int f, int xa, int za, int xb, int zb, int ya, int yb)
    {
        var o = new List<Feat>();
        bool Ht(Feat F) => F.a <= xb && F.b >= xa && F.c <= zb && F.e >= za && F.g <= yb && F.h >= ya;
        for (int qz = (int)Math.Floor((za - 6) / 4.0); qz <= (int)Math.Floor((zb + 6) / 4.0); qz++)
            for (int qx = (int)Math.Floor((xa - 6) / 4.0); qx <= (int)Math.Floor((xb + 6) / 4.0); qx++)
            { var F = PlantAt(B, f, qx, qz); if (F != null && Ht(F)) o.Add(F); }
        for (int qz = (int)Math.Floor((za - 110) / 96.0); qz <= (int)Math.Floor((zb + 110) / 96.0); qz++)
            for (int qx = (int)Math.Floor((xa - 110) / 96.0); qx <= (int)Math.Floor((xb + 110) / 96.0); qx++)
            { var F = StructAt(B, f, qx, qz); if (F != null && Ht(F)) o.Add(F); }
        return o;
    }
    // trees & structures belong to the band that holds their ground; anything that would cross the band's bottom or top is not generated
    internal static List<Feat> Feats(Chunk ch, Band B, int f)
    {
        int x0 = ch.cx * CS, z0 = ch.cz * CS, y0 = ch.cy * CS;
        return FeatsR(B, f, x0, z0, x0 + CS - 1, z0 + CS - 1, y0, y0 + CS - 1);
    }
    internal static void ApplyFeats(Chunk ch, byte[] d, List<Feat> fl)
    {
        int x0 = ch.cx * CS, z0 = ch.cz * CS, y0 = ch.cy * CS;
        foreach (var F in fl)
        {
            var L = F.L; int fx = F.x - x0, fz = F.z - z0;
            for (int i = 0; i < L.Length; i += 4)
            {
                int x = fx + L[i], z = fz + L[i + 1], y = L[i + 2] - y0;
                if (x < 0 || x >= CS || z < 0 || z >= CS || y < 0 || y >= CS) continue;
                int j = x + CS * (z + CS * y), id = L[i + 3];
                if (F.st) d[j] = (byte)(id == 255 ? 0 : id);
                else { int c = d[j]; if ((id == 51 || id == 52) ? c == 0 : (c == 0 || LEAFS[c] || (F.uw && c == 9))) d[j] = (byte)id; }
            }
        }
    }
}
