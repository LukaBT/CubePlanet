using static CubePlanet.Cube;
using static CubePlanet.Blocks;

namespace CubePlanet;

public class Feat { public int x, z; public int[] L; public int a, b, c, e, g, h; public bool st, uw; }

/// <summary>Collects blocks as (x, z, y absolute, id) relative to an anchor column; id 255 = carve to air.</summary>
public class Sb
{
    public List<int> L = new();
    public int[] m = { 1000000000, -1000000000, 1000000000, -1000000000, 1000000000, -1000000000 };
    public void Put(int x, int y, int z, int id)
    {
        L.Add(x); L.Add(z); L.Add(y); L.Add(id);
        if (x < m[0]) m[0] = x; if (x > m[1]) m[1] = x;
        if (z < m[2]) m[2] = z; if (z > m[3]) m[3] = z;
        if (y < m[4]) m[4] = y; if (y > m[5]) m[5] = y;
    }
    public void Box(int x0, int y0, int z0, int x1, int y1, int z1, int id)
    {
        for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) for (int z = z0; z <= z1; z++) Put(x, y, z, id);
    }
}

// ---------- biome features: trees, plants & structures (decorate step, per chunk, deterministic) ----------
public static partial class W
{
    static readonly int[] GR = { 1, 19, 20, 21, 22, 8, 76, 77, 78 };
    static readonly int[] FLW = { 46, 47, 48, 49 }, FL2 = { 46, 47, 48, 49, 105, 106, 107 };

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
    static int GyOf(Band B, double h) => (int)Math.Ceiling(R + h - B.inner - .5) - 1;
    static void ColT(Band B, int f, int bx, int bz)
    {
        V3 t = GpA(f, -PI / 4 + (bz + .5) * B.d, -PI / 4 + (bx + .5) * B.d, 1);
        Terrain(t.X, t.Y, t.Z, 0);
    }
    static Feat MkF(Sb S, int px, int pz, bool st)
    {
        if (S.L.Count == 0) return null;
        return new Feat { x = px, z = pz, L = S.L.ToArray(), a = px + S.m[0], b = px + S.m[1], c = pz + S.m[2], e = pz + S.m[3], g = S.m[4], h = S.m[5], st = st };
    }

    static Feat MkPlant(int k, int f, int px, int pz, int gy, int dmax = 0)
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

    static Feat PlantAt(Band B, int f, int qx, int qz)
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

    static readonly Dictionary<int, int[]> PAL = new()
    {
        [1] = new[] { 31, 4, 32, 35, 10 }, [2] = new[] { 31, 4, 32, 35, 10 }, [3] = new[] { 31, 4, 32, 35, 10 },
        [4] = new[] { 32, 28, 32, 35, 10 }, [5] = new[] { 32, 28, 8, 35, 10 }, [7] = new[] { 33, 30, 33, 35, 10 }, [8] = new[] { 11, 39, 11, 39, 7 },
        [14] = new[] { 31, 4, 32, 35, 10 }, [15] = new[] { 91, 90, 91, 35, 10 }, [16] = new[] { 11, 39, 11, 39, 7 }, [17] = new[] { 42, 17, 14, 39, 12 },
        [18] = new[] { 32, 28, 8, 35, 10 }, [19] = new[] { 32, 28, 32, 35, 10 }, [25] = new[] { 93, 92, 41, 93, 82 }, [26] = new[] { 93, 92, 41, 93, 82 }, [29] = new[] { 31, 4, 32, 35, 10 },
    };

    static Sb MkStruct(int k, Band B, int f, int px, int pz, int b)
    {
        var S = new Sb(); int q = 0;
        double Rn() => HH(f, px, pz, 200 + q++);
        void L(int x, int y, int z, int id) => S.Put(x, y, z, id);
        void Box(int x0, int y0, int z0, int x1, int y1, int z1, int id) => S.Box(x0, y0, z0, x1, y1, z1, id);
        var gm = new Dictionary<int, (int y, double h)>();
        (int y, double h) G(int x, int z)
        {
            int kk = (x + 2048) * 4096 + z + 2048;
            if (!gm.TryGetValue(kk, out var v)) { ColT(B, f, px + x, pz + z); v = (GyOf(B, gH), gH); gm[kk] = v; }
            return v;
        }
        (int df, int hi, int lo) Flat(int x0, int z0, int x1, int z1, bool sea = false)
        {
            int lo = int.MaxValue, hi = int.MinValue;
            foreach (var (x, z) in new[] { (x0, z0), (x1, z0), (x0, z1), (x1, z1), ((x0 + x1) >> 1, (z0 + z1) >> 1) })
            {
                var g = G(x, z);
                if (!sea && g.h < 1) return (99, 0, 0);
                lo = Math.Min(lo, g.y); hi = Math.Max(hi, g.y);
            }
            return (hi - lo, hi, lo);
        }
        void Found(int x0, int z0, int x1, int z1, int y0, int id)
        {
            for (int x = x0; x <= x1; x++) for (int z = z0; z <= z1; z++) { int g = G(x, z).y; for (int y = g; y < y0; y++) L(x, y, z, id); }
        }
        var pal = PAL.TryGetValue(b, out var pp) ? pp : PAL[1];
        int Wl = pal[0], Lg = pal[1], Rf = pal[2], Fl = pal[3], Pt = pal[4];

        (int, int) House(int x, int z, int w, int d, int y0, int dx, int dz)
        {
            Found(x, z, x + w - 1, z + d - 1, y0, Fl); Box(x, y0 + 1, z, x + w - 1, y0 + 9, z + d - 1, 255); Box(x, y0, z, x + w - 1, y0, z + d - 1, Fl);
            for (int y = 1; y <= 3; y++)
                for (int i = 0; i < w; i++)
                    for (int j = 0; j < d; j++)
                    {
                        if (i != 0 && j != 0 && i < w - 1 && j < d - 1) continue;
                        L(x + i, y0 + y, z + j, (i == 0 || i == w - 1) && (j == 0 || j == d - 1) ? Lg : Wl);
                    }
            int dxc = dz != 0 ? x + (w >> 1) : (dx > 0 ? x + w - 1 : x), dzc = dz != 0 ? (dz > 0 ? z + d - 1 : z) : z + (d >> 1);
            int[][] wins = dz != 0
                ? new[] { new[] { x, z + (d >> 1) }, new[] { x + w - 1, z + (d >> 1) }, new[] { x + (w >> 1), dz > 0 ? z : z + d - 1 } }
                : new[] { new[] { x + (w >> 1), z }, new[] { x + (w >> 1), z + d - 1 }, new[] { dx > 0 ? x : x + w - 1, z + (d >> 1) } };
            foreach (var wn in wins) L(wn[0], y0 + 2, wn[1], 38);
            L(dxc, y0 + 1, dzc, 255); L(dxc, y0 + 2, dzc, 255);
            for (int k2 = 0; ; k2++)
            {
                int zl = z - 1 + k2, zh = z + d - k2; if (zl > zh) break;
                int y = y0 + 4 + k2;
                for (int a = x - 1; a <= x + w; a++)
                {
                    if (k2 == 0 || zl == zh) { for (int c = zl; c <= zh; c++) L(a, y, c, Rf); }
                    else { L(a, y, zl, Rf); L(a, y, zh, Rf); }
                }
                for (int c = Math.Max(zl + 1, z); c <= Math.Min(zh - 1, z + d - 1); c++) { L(x, y, c, Wl); L(x + w - 1, y, c, Wl); }
            }
            return (dxc, dzc);
        }
        void Tower(int x, int z, int y0, int dx, int dz, int hgt, double dmg)
        {
            Found(x, z, x + 4, z + 4, y0, 35); Box(x, y0 + 1, z, x + 4, y0 + hgt + 2, z + 4, 255); Box(x, y0, z, x + 4, y0, z + 4, 35);
            for (int y = 1; y <= hgt; y++)
                for (int i = 0; i < 5; i++)
                    for (int j = 0; j < 5; j++)
                    {
                        if (i != 0 && j != 0 && i < 4 && j < 4) continue;
                        if (dmg != 0 && Rn() < dmg * y / hgt) continue;
                        L(x + i, y0 + y, z + j, (i == 0 || i == 4) && (j == 0 || j == 4) ? (dmg != 0 ? 37 : Lg) : (Rn() < .35 ? 36 : 35));
                    }
            Box(x + 1, y0 + 4, z + 1, x + 3, y0 + 4, z + 3, Wl); L(x + 1, y0 + 4, z + 1, 255); Box(x + 1, y0 + hgt, z + 1, x + 3, y0 + hgt, z + 3, Wl);
            for (int i = 0; i < 5; i++)
                for (int j = 0; j < 5; j++)
                    if ((i == 0 || j == 0 || i == 4 || j == 4) && (i + j) % 2 == 0 && !(dmg != 0 && Rn() < dmg)) L(x + i, y0 + hgt + 1, z + j, 35);
            foreach (var (a, c) in new[] { (2, 0), (2, 4), (0, 2), (4, 2) }) L(x + a, y0 + 6, z + c, 255);
            int dxc = dz != 0 ? x + 2 : (dx > 0 ? x + 4 : x), dzc = dz != 0 ? (dz > 0 ? z + 4 : z) : z + 2;
            L(dxc, y0 + 1, dzc, 255); L(dxc, y0 + 2, dzc, 255);
        }

        if (k == 0) // village
        {
            int wy = G(0, 0).y; Found(-2, -2, 2, 2, wy, Fl); Box(-2, wy + 1, -2, 2, wy + 7, 2, 255); Box(-1, wy, -1, 1, wy, 1, Fl);
            for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++) if (x != 0 || z != 0) L(x, wy + 1, z, Fl);
            L(0, wy, 0, 9);
            foreach (var (a, c) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) }) { L(a, wy + 2, c, Lg); L(a, wy + 3, c, Lg); }
            Box(-1, wy + 4, -1, 1, wy + 4, 1, Rf);
            int NN = 3 + (int)Math.Floor(Rn() * 4); var occ = new List<int[]> { new[] { -2, -2, 2, 2 } }; var doors = new List<int[]>(); int made = 0;
            for (int i = 0; i < NN * 2 && made < NN; i++)
            {
                double ang = ((double)i / NN) * 6.283 + Rn() * .5, rr = 11 + Rn() * 9;
                int hx = (int)M.JsRound(Math.Cos(ang) * rr), hz = (int)M.JsRound(Math.Sin(ang) * rr);
                int kind = made == 2 ? 1 : made == 3 ? 2 : 0;
                int w = kind == 1 ? 5 : kind == 2 ? 7 : 5 + 2 * (int)Math.Floor(Rn() * 2), d = kind == 2 ? 7 : 5, x = hx - (w >> 1), z = hz - (d >> 1);
                if (occ.Any(o => x - 2 <= o[2] && x + w + 1 >= o[0] && z - 2 <= o[3] && z + d + 1 >= o[1])) continue;
                var (df, hi, _) = Flat(x, z, x + w - 1, z + d - 1);
                if (df > (kind == 2 ? 2 : 3)) continue;
                int dx = Math.Abs(hx) > Math.Abs(hz) ? (hx > 0 ? -1 : 1) : 0, dz = dx != 0 ? 0 : (hz > 0 ? -1 : 1);
                int[] dc;
                if (kind == 0) { var h2 = House(x, z, w, d, hi, dx, dz); dc = new[] { h2.Item1, h2.Item2 }; }
                else if (kind == 1) { Tower(x, z, hi, dx, dz, 10, 0); dc = new[] { dz != 0 ? x + 2 : (dx > 0 ? x + 4 : x), dz != 0 ? (dz > 0 ? z + 4 : z) : z + 2 }; }
                else
                {
                    Found(x, z, x + 6, z + 6, hi, 3); Box(x, hi + 1, z, x + 6, hi + 3, z + 6, 255);
                    for (int a2 = 0; a2 < 7; a2++) for (int c = 0; c < 7; c++) L(x + a2, hi, z + c, (a2 == 0 || c == 0 || a2 == 6 || c == 6) ? Lg : (a2 == 3 && c == 3) ? 9 : (a2 % 2 != 0 ? 41 : 3));
                    dc = new[] { x + 3, z + 3 };
                    if (dx != 0 || dz != 0) dc = new[] { dz != 0 ? x + 3 : (dx > 0 ? x + 6 : x), dx != 0 ? z + 3 : (dz > 0 ? z + 6 : z) };
                }
                occ.Add(new[] { x, z, x + w - 1, z + d - 1 }); doors.Add(new[] { dc[0] + dx, dc[1] + dz }); made++;
            }
            foreach (var dr in doors)
            {
                int fx = dr[0], fz = dr[1];
                int sx = Math.Sign(-fx) != 0 ? Math.Sign(-fx) : 1, sz = Math.Sign(-fz) != 0 ? Math.Sign(-fz) : 1;
                int x = fx, z = fz; var tiles = new List<int[]>();
                while (x != 0) { tiles.Add(new[] { x, z }); x += sx; if (Math.Abs(x) <= 2 && Math.Abs(z) <= 2) break; }
                while ((z != 0 && Math.Abs(x) > 2) || (z != 0 && Math.Abs(z) > 2)) { tiles.Add(new[] { x, z }); z += sz; }
                foreach (var tl in tiles)
                {
                    int tx = tl[0], tz = tl[1];
                    if (Math.Abs(tx) <= 2 && Math.Abs(tz) <= 2) continue;
                    if (occ.Any(o => tx >= o[0] && tx <= o[2] && tz >= o[1] && tz <= o[3])) continue;
                    var g = G(tx, tz); if (g.h < 1) continue;
                    L(tx, g.y, tz, Pt); L(tx, g.y + 1, tz, 255); L(tx, g.y + 2, tz, 255);
                }
            }
            if (made < 2) return null;
        }
        else if (k == 1) // desert pyramid
        {
            var (df, hi, _) = Flat(-10, -10, 10, 10); if (df > 6) return null;
            Found(-10, -10, 10, 10, hi, 11); Box(-10, hi + 1, -10, 10, hi + 14, 10, 255);
            for (int j = 0; j <= 10; j++) { int h = 10 - j; for (int x = -h; x <= h; x++) for (int z = -h; z <= h; z++) L(x, hi + j, z, Math.Abs(x) == h || Math.Abs(z) == h ? (j % 3 == 2 ? 14 : 39) : 11); }
            Box(-3, hi + 1, -3, 3, hi + 5, 3, 255); Box(-1, hi + 1, -10, 1, hi + 3, -3, 255); L(0, hi + 1, 0, 15);
            foreach (var (a, c) in new[] { (-3, -3), (3, -3), (-3, 3), (3, 3) }) for (int y = 1; y <= 5; y++) L(a, hi + y, c, 39);
        }
        else if (k == 2) // jungle temple
        {
            var (df, hi, _) = Flat(-7, -7, 7, 7); if (df > 6) return null;
            Found(-7, -7, 7, 7, hi, 36); Box(-7, hi + 1, -7, 7, hi + 12, 7, 255);
            int Mc() => Rn() < .45 ? 36 : 35;
            for (int x = -7; x <= 7; x++) for (int z = -7; z <= 7; z++) L(x, hi, z, Mc());
            foreach (var (h, y1, y2) in new[] { (5, 1, 4), (3, 5, 7), (1, 8, 9) })
                for (int y = y1; y <= y2; y++) for (int x = -h; x <= h; x++) for (int z = -h; z <= h; z++) L(x, hi + y, z, Mc());
            Box(-3, hi + 1, -3, 3, hi + 3, 3, 255); Box(-1, hi + 1, -6, 1, hi + 2, -3, 255); L(0, hi + 1, 0, 15); L(1, hi + 1, 0, 15);
            for (int i = 0; i < 30; i++) { int x = (int)Math.Floor(Rn() * 15) - 7, z = (int)Math.Floor(Rn() * 15) - 7; if (Math.Abs(x) > 5 || Math.Abs(z) > 5) L(x, hi + 1, z, 24); }
        }
        else if (k == 3) // igloo
        {
            var (df, hi, _) = Flat(-4, -4, 4, 4); if (df > 3) return null;
            Found(-4, -4, 4, 4, hi, 3); Box(-4, hi + 1, -4, 4, hi + 6, 4, 255);
            for (int y = 0; y <= 3; y++) for (int x = -3; x <= 3; x++) for (int z = -3; z <= 3; z++)
                    {
                        double d = Math.Sqrt(x * x + y * y + z * z);
                        if (d <= 3.5 && d > 2.4) L(x, hi + y, z, 8); else if (y == 0 && d <= 2.4) L(x, hi, z, 31);
                    }
            L(-3, hi + 1, 0, 6); Box(-1, hi + 1, 3, 1, hi + 3, 5, 8); Box(0, hi + 1, 2, 0, hi + 2, 5, 255); L(0, hi, 4, 8);
        }
        else if (k == 4) // swamp hut
        {
            int y0 = -1000000000;
            foreach (var (x, z) in new[] { (-2, -2), (2, -2), (-2, 2), (2, 2), (0, 0) }) y0 = Math.Max(y0, G(x, z).y);
            y0 += 2;
            foreach (var (x, z) in new[] { (-2, -2), (2, -2), (-2, 2), (2, 2) }) for (int y = G(x, z).y; y < y0; y++) L(x, y, z, 28);
            Box(-3, y0 + 1, -3, 3, y0 + 8, 3, 255); Box(-3, y0, -3, 3, y0, 3, 32);
            for (int y = 1; y <= 3; y++) for (int x = -2; x <= 2; x++) for (int z = -2; z <= 2; z++)
                        if (Math.Abs(x) == 2 || Math.Abs(z) == 2) L(x, y0 + y, z, Math.Abs(x) == 2 && Math.Abs(z) == 2 ? 28 : 32);
            L(-2, y0 + 2, 0, 38); L(2, y0 + 2, 0, 38); L(0, y0 + 2, -2, 38); L(0, y0 + 1, 2, 255); L(0, y0 + 2, 2, 255);
            Box(-3, y0 + 4, -3, 3, y0 + 4, 3, 32); Box(-2, y0 + 5, -2, 2, y0 + 5, 2, 32); Box(-1, y0 + 6, -1, 1, y0 + 6, 1, 32); L(0, y0 + 7, 0, 32); L(-1, y0 + 1, -1, 35);
        }
        else if (k == 5) // watchtower
        {
            double dm = b == 9 || b == 2 || b == 3 || b == 4 || b == 1 || b == 11 ? .45 : 0;
            var (df, hi, _) = Flat(-3, -3, 7, 7); if (df > 3) return null;
            Tower(-2, -2, hi, 0, 1, 9, dm);
        }
        else if (k == 6) // shipwreck
        {
            var (df, hi, lo) = Flat(-8, -3, 8, 3, true);
            if (df > 3 || G(-8, -3).h > -3 || G(8, 3).h > -3 || G(0, 0).h > -3) return null;
            int y0 = lo + 1; Found(-8, -3, 8, 3, y0, 7);
            void Dmg(int x, int y, int z, int id) { if (Rn() > .2) L(x, y, z, id); }
            for (int x = -8; x <= 8; x++)
            {
                double t = Math.Abs(x) / 8.5; int hw = (int)M.JsRound(3 * (1 - Math.Pow(t, 2.2))), hy = 2 + (Math.Abs(x) > 5 ? 2 : Math.Abs(x) > 3 ? 1 : 0);
                for (int z = -hw; z <= hw; z++) { Dmg(x, y0, z, Math.Abs(z) < 1 ? 29 : 32); if (Math.Abs(z) < hw && Rn() < .7) Dmg(x, y0 + 2, z, 31); }
                for (int y = 1; y <= hy; y++) { Dmg(x, y0 + y, hw, 32); if (hw != 0) Dmg(x, y0 + y, -hw, 32); }
            }
            for (int y = 1; y <= 8; y++) if (Rn() < .85) L(0, y0 + y, 0, 28);
            for (int z = -2; z <= 2; z++) if (Rn() < .8) L(0, y0 + 7, z, 28);
        }
        else // sunken ruins: crumbling brick walls and prismarine pillars around a sea lantern
        {
            var (df, _, lo) = Flat(-5, -5, 5, 5, true);
            if (df > 3 || G(0, 0).h > -3.5 || G(-5, -5).h > -3 || G(5, 5).h > -3) return null;
            int y0 = lo + 1; Found(-5, -5, 5, 5, y0, 7);
            void Rb(int x, int y, int z) { double r = Rn(); L(x, y, z, r < .4 ? 36 : r < .7 ? 37 : 87); }
            for (int x = -5; x <= 5; x++)
                for (int z = -5; z <= 5; z++)
                {
                    if (Rn() < .12) continue;
                    if (Math.Abs(x) == 5 || Math.Abs(z) == 5) { int hg = Rn() < .3 ? 0 : 1 + (int)Math.Floor(Rn() * 3); for (int y = 0; y <= hg; y++) Rb(x, y0 + y, z); }
                    else Rb(x, y0, z);
                }
            foreach (var (cx, cz) in new[] { (-5, -5), (5, -5), (-5, 5), (5, 5) })
            {
                int ph = 4 + (int)Math.Floor(Rn() * 4);
                for (int y = 1; y <= ph; y++) if (y < 3 || Rn() > .12) L(cx, y0 + y, cz, 87);
            }
            L(0, y0 + 1, 0, 87); L(0, y0 + 2, 0, 88);
        }
        return S;
    }

    static Feat StructAt(Band B, int f, int qx, int qz)
    {
        int px = qx * 96 + 20 + (int)Math.Floor(HH(f, qx, qz, 11) * 56), pz = qz * 96 + 20 + (int)Math.Floor(HH(f, qx, qz, 12) * 56);
        if (px < 44 || pz < 44 || px >= B.cols - 44 || pz >= B.cols - 44) return null;
        long key = ((long)(B.b * 6 + f) * 8388608 + qx + 8) * 8388608 + qz + 8;
        if (STC.TryGetValue(key, out var F)) return F;
        F = null; if (STC.Count > 3000) STC.Clear();
        ColT(B, f, px, pz); int b = gB; double H = gH, u = HH(f, qx, qz, 13); int k = -1;
        switch (b)
        {
            case 8: k = u < .2 ? 1 : u < .5 ? 0 : -1; break;
            case 9: k = u < .35 ? 5 : -1; break;
            case 6: k = u < .45 ? 2 : -1; break;
            case 5: k = u < .3 ? 3 : u < .5 ? 0 : -1; break;
            case 4: k = u < .3 ? 0 : u < .5 ? 5 : -1; break;
            case 1: k = u < .4 ? 0 : u < .55 ? 5 : -1; break;
            case 2: case 3: k = u < .12 ? 0 : u < .4 ? 5 : -1; break;
            case 7: k = u < .45 ? 0 : -1; break;
            case 10: k = u < .5 ? 4 : -1; break;
            case 0: case 21: case 22: case 24: case 31: k = (H < -4 && H > -26) ? (u < .25 ? 6 : u < .5 ? 7 : -1) : -1; break;
            case 14: k = u < .12 ? 0 : u < .4 ? 5 : -1; break;
            case 15: k = u < .4 ? 0 : u < .55 ? 5 : -1; break;
            case 16: k = u < .55 ? 0 : -1; break;
            case 17: k = u < .22 ? 1 : u < .4 ? 5 : -1; break;
            case 18: k = u < .3 ? 3 : u < .5 ? 0 : -1; break;
            case 19: k = u < .2 ? 0 : u < .4 ? 5 : -1; break;
            case 25: case 26: k = u < .35 ? 0 : -1; break;
            case 28: k = u < .25 ? 2 : -1; break;
            case 29: k = u < .3 ? 0 : -1; break;
            case 11: k = u < .15 && H < 30 * HS ? 5 : -1; break;
        }
        if (k >= 0)
        {
            var S = MkStruct(k, B, f, px, pz, b);
            if (S != null) { F = MkF(S, px, pz, true); if (F != null && (F.g < 0 || F.h >= B.rows)) F = null; }
        }
        STC[key] = F; return F;
    }

    static List<Feat> FeatsR(Band B, int f, int xa, int za, int xb, int zb, int ya, int yb)
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
    static List<Feat> Feats(Chunk ch, Band B, int f)
    {
        int x0 = ch.cx * CS, z0 = ch.cz * CS, y0 = ch.cy * CS;
        return FeatsR(B, f, x0, z0, x0 + CS - 1, z0 + CS - 1, y0, y0 + CS - 1);
    }
    static void ApplyFeats(Chunk ch, byte[] d, List<Feat> fl)
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

    // ground cover: grass, ferns, flowers, mushrooms, dead bushes, sugar cane, pumpkins and melons (after trees/structures, only into free air)
    static void Cover(Chunk ch, byte[] d, Col c, Band B, int f)
    {
        int cx0 = ch.cx * CS, cz0 = ch.cz * CS, cy0 = ch.cy * CS;
        for (int z = 0; z < CS; z++)
            for (int x = 0; x < CS; x++)
            {
                int i = x + CS * z; double h = c.h[i];
                if (h < .3 || c.ice[i] != 0) continue;
                bool hi = h >= 1.2;
                int gy = GyOf(B, h), y = gy + 1 - cy0;
                if (y < 0 || y >= CS) continue;
                int j = x + CS * (z + CS * y);
                if (d[j] != 0) continue;
                int bl = y > 0 ? d[j - CS * CS] : Gen(B, f, cx0 + x, gy, cz0 + z);
                if (bl == 0) continue;
                int bi = c.b[i]; double u = HH(f, cx0 + x, cz0 + z, 60), v = HH(f, cx0 + x, cz0 + z, 61); int id = 0, hg = 1;
                if (bi == 9 || bi == 17) { if (hi && u < .012) id = 50; }
                else if ((bl == 7 || bl == 1 || bl == 3 || bl == 82 || bl == 76) && h < 3 && x > 0 && x < CS - 1 && z > 0 && z < CS - 1 && Math.Min(Math.Min(c.h[i - 1], c.h[i + 1]), Math.Min(c.h[i - CS], c.h[i + CS])) < 0)
                { if (u < .3) { id = 55; hg = 1 + (int)Math.Floor(v * 3); } }
                else if (bl == 7 || bl == 12) { if (hi && (bi == 8 || bi == 17) && u < .012) id = 50; }
                else if (Array.IndexOf(GR, bl) >= 0 && bl != 8 && hi)
                {
                    switch (bi)
                    {
                        case 1: id = u < .42 ? 43 : u < .46 ? FLW[(int)Math.Floor(v * 4)] : u < .4625 ? 56 : 0; break;
                        case 2: case 3: id = u < .28 ? 43 : u < .305 ? FLW[(int)Math.Floor(v * 4)] : u < .315 ? (v < .5 ? 53 : 54) : 0; break;
                        case 4: id = u < .3 ? 45 : u < .4 ? 43 : u < .41 ? (v < .5 ? 53 : 54) : u < .425 ? 109 : 0; break;
                        case 14: id = u < .3 ? 43 : u < .45 ? 45 : u < .52 ? 106 : u < .56 ? FLW[(int)Math.Floor(v * 4)] : u < .565 ? (v < .5 ? 53 : 54) : 0; break;
                        case 15: id = u < .28 ? 104 : u < .46 ? 43 : u < .49 ? 105 : u < .51 ? FLW[(int)Math.Floor(v * 4)] : 0; break;
                        case 16: id = u < .3 ? 43 : u < .33 ? FLW[(int)Math.Floor(v * 4)] : 0; break;
                        case 19: id = u < .35 ? 45 : u < .55 ? 43 : u < .58 ? (v < .5 ? 53 : 54) : u < .6 ? 109 : 0; break;
                        case 26: id = u < .45 ? (v < .6 ? 43 : 45) : u < .47 ? 57 : 0; break;
                        case 28: id = u < .4 ? (v < .6 ? 43 : 45) : 0; break;
                        case 29: id = u < .3 ? 43 : u < .62 ? FL2[(int)Math.Floor(v * 7)] : 0; break;
                        case 6: id = u < .5 ? (v < .6 ? 43 : 45) : u < .505 ? 57 : 0; break;
                        case 7: id = u < .4 ? 44 : 0; break;
                        case 10: id = u < .3 ? 43 : u < .33 ? 48 : u < .35 ? (v < .5 ? 53 : 54) : 0; break;
                    }
                }
                if (id == 55) { for (int k = 0; k < hg && y + k < CS; k++) if (d[j + k * 1024] == 0) d[j + k * 1024] = 55; }
                else if (id != 0) d[j] = (byte)id;
            }
    }

    // underwater ground cover: seagrass, short kelp, sea pickles and coral fans standing in the water above the sea floor
    static void CoverSea(Chunk ch, byte[] d, Col c, Band B, int f)
    {
        int cx0 = ch.cx * CS, cz0 = ch.cz * CS, cy0 = ch.cy * CS;
        for (int z = 0; z < CS; z++)
            for (int x = 0; x < CS; x++)
            {
                int i = x + CS * z; double h = c.h[i];
                if (h > -2.3 || h < -30 || c.ice[i] != 0) continue;
                int bi = c.b[i]; if (bi != 0 && bi != 21 && bi != 22 && bi != 31) continue;
                int gy = GyOf(B, h), y = gy + 1 - cy0;
                if (y < 0 || y >= CS) continue;
                int j = x + CS * (z + CS * y);
                if (d[j] != 9) continue;
                int bl = y > 0 ? d[j - CS * CS] : Gen(B, f, cx0 + x, gy, cz0 + z);
                if (bl == 0 || bl == 9 || bl == 75 || SPC[bl] != 0 || LEAFS[bl]) continue;
                double u = HH(f, cx0 + x, cz0 + z, 62), v = HH(f, cx0 + x, cz0 + z, 63); int id = 0, hg = 1;
                switch (bi)
                {
                    case 21: id = u < .22 ? 110 : u < .40 ? 113 + (int)Math.Floor(v * 5) : u < .43 ? 112 : 0; if (id == 110) hg = 1 + (v < .4 ? 1 : 0); break;
                    case 31: id = u < .25 ? 110 : u < .28 ? 112 : u < .31 ? 113 + (int)Math.Floor(v * 5) : 0; if (id == 110) hg = 1 + (v < .4 ? 1 : 0); break;
                    case 22: id = u < .1 ? 110 : u < .3 ? 111 : 0; if (id == 111) hg = 1 + (int)Math.Floor(v * 3); break;
                    case 0: id = u < .12 ? 110 : 0; break;
                }
                if (id == 0) continue;
                if (-h < hg + 1.3) hg = 1;
                for (int k = 0; k < hg && y + k < CS; k++) if (d[j + k * CS * CS] == 9) d[j + k * CS * CS] = (byte)id; else break;
            }
    }
}
