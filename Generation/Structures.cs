namespace CubePlanet;

/// <summary>Structures (villages, ruins, shipwrecks, ...) with their per-biome block palettes.</summary>
public static class Structures
{
    // building palettes per biome: wall, log (corners), roof, floor / foundation, path
    internal static readonly Dictionary<int, int[]> PAL = new()
    {
        [1] = new[] { OakPlanks, OakLog, SprucePlanks, Cobblestone, Gravel }, [2] = new[] { OakPlanks, OakLog, SprucePlanks, Cobblestone, Gravel }, [3] = new[] { OakPlanks, OakLog, SprucePlanks, Cobblestone, Gravel },
        [4] = new[] { SprucePlanks, SpruceLog, SprucePlanks, Cobblestone, Gravel }, [5] = new[] { SprucePlanks, SpruceLog, Snow, Cobblestone, Gravel }, [7] = new[] { AcaciaPlanks, AcaciaLog, AcaciaPlanks, Cobblestone, Gravel }, [8] = new[] { Sandstone, CutSandstone, Sandstone, CutSandstone, Sand },
        [14] = new[] { OakPlanks, OakLog, SprucePlanks, Cobblestone, Gravel }, [15] = new[] { CherryPlanks, CherryLog, CherryPlanks, Cobblestone, Gravel }, [16] = new[] { Sandstone, CutSandstone, Sandstone, CutSandstone, Sand }, [17] = new[] { RedSandstone, RedTerracotta, OrangeTerracotta, CutSandstone, RedSand },
        [18] = new[] { SprucePlanks, SpruceLog, Snow, Cobblestone, Gravel }, [19] = new[] { SprucePlanks, SpruceLog, SprucePlanks, Cobblestone, Gravel }, [25] = new[] { PalmPlanks, PalmLog, HayBale, PalmPlanks, CoralSand }, [26] = new[] { PalmPlanks, PalmLog, HayBale, PalmPlanks, CoralSand }, [29] = new[] { OakPlanks, OakLog, SprucePlanks, Cobblestone, Gravel },
    };

    internal static Sb MkStruct(int k, Band B, int f, int px, int pz, int b)
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
            Found(x, z, x + w - 1, z + d - 1, y0, Fl); Box(x, y0 + 1, z, x + w - 1, y0 + 9, z + d - 1, Carve); Box(x, y0, z, x + w - 1, y0, z + d - 1, Fl);
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
            foreach (var wn in wins) L(wn[0], y0 + 2, wn[1], Glass);
            L(dxc, y0 + 1, dzc, Carve); L(dxc, y0 + 2, dzc, Carve);
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
            Found(x, z, x + 4, z + 4, y0, Cobblestone); Box(x, y0 + 1, z, x + 4, y0 + hgt + 2, z + 4, Carve); Box(x, y0, z, x + 4, y0, z + 4, Cobblestone);
            for (int y = 1; y <= hgt; y++)
                for (int i = 0; i < 5; i++)
                    for (int j = 0; j < 5; j++)
                    {
                        if (i != 0 && j != 0 && i < 4 && j < 4) continue;
                        if (dmg != 0 && Rn() < dmg * y / hgt) continue;
                        L(x + i, y0 + y, z + j, (i == 0 || i == 4) && (j == 0 || j == 4) ? (dmg != 0 ? StoneBricks : Lg) : (Rn() < .35 ? MossyCobblestone : Cobblestone));
                    }
            Box(x + 1, y0 + 4, z + 1, x + 3, y0 + 4, z + 3, Wl); L(x + 1, y0 + 4, z + 1, Carve); Box(x + 1, y0 + hgt, z + 1, x + 3, y0 + hgt, z + 3, Wl);
            for (int i = 0; i < 5; i++)
                for (int j = 0; j < 5; j++)
                    if ((i == 0 || j == 0 || i == 4 || j == 4) && (i + j) % 2 == 0 && !(dmg != 0 && Rn() < dmg)) L(x + i, y0 + hgt + 1, z + j, Cobblestone);
            foreach (var (a, c) in new[] { (2, 0), (2, 4), (0, 2), (4, 2) }) L(x + a, y0 + 6, z + c, Carve);
            int dxc = dz != 0 ? x + 2 : (dx > 0 ? x + 4 : x), dzc = dz != 0 ? (dz > 0 ? z + 4 : z) : z + 2;
            L(dxc, y0 + 1, dzc, Carve); L(dxc, y0 + 2, dzc, Carve);
        }

        if (k == 0) // village
        {
            int wy = G(0, 0).y; Found(-2, -2, 2, 2, wy, Fl); Box(-2, wy + 1, -2, 2, wy + 7, 2, Carve); Box(-1, wy, -1, 1, wy, 1, Fl);
            for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++) if (x != 0 || z != 0) L(x, wy + 1, z, Fl);
            L(0, wy, 0, Water);
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
                    Found(x, z, x + 6, z + 6, hi, Dirt); Box(x, hi + 1, z, x + 6, hi + 3, z + 6, Carve);
                    for (int a2 = 0; a2 < 7; a2++) for (int c = 0; c < 7; c++) L(x + a2, hi, z + c, (a2 == 0 || c == 0 || a2 == 6 || c == 6) ? Lg : (a2 == 3 && c == 3) ? Water : (a2 % 2 != 0 ? HayBale : Dirt));
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
                    L(tx, g.y, tz, Pt); L(tx, g.y + 1, tz, Carve); L(tx, g.y + 2, tz, Carve);
                }
            }
            if (made < 2) return null;
        }
        else if (k == 1) // desert pyramid
        {
            var (df, hi, _) = Flat(-10, -10, 10, 10); if (df > 6) return null;
            Found(-10, -10, 10, 10, hi, Sandstone); Box(-10, hi + 1, -10, 10, hi + 14, 10, Carve);
            for (int j = 0; j <= 10; j++) { int h = 10 - j; for (int x = -h; x <= h; x++) for (int z = -h; z <= h; z++) L(x, hi + j, z, Math.Abs(x) == h || Math.Abs(z) == h ? (j % 3 == 2 ? OrangeTerracotta : CutSandstone) : Sandstone); }
            Box(-3, hi + 1, -3, 3, hi + 5, 3, Carve); Box(-1, hi + 1, -10, 1, hi + 3, -3, Carve); L(0, hi + 1, 0, YellowTerracotta);
            foreach (var (a, c) in new[] { (-3, -3), (3, -3), (-3, 3), (3, 3) }) for (int y = 1; y <= 5; y++) L(a, hi + y, c, CutSandstone);
        }
        else if (k == 2) // jungle temple
        {
            var (df, hi, _) = Flat(-7, -7, 7, 7); if (df > 6) return null;
            Found(-7, -7, 7, 7, hi, MossyCobblestone); Box(-7, hi + 1, -7, 7, hi + 12, 7, Carve);
            int Mc() => Rn() < .45 ? MossyCobblestone : Cobblestone;
            for (int x = -7; x <= 7; x++) for (int z = -7; z <= 7; z++) L(x, hi, z, Mc());
            foreach (var (h, y1, y2) in new[] { (5, 1, 4), (3, 5, 7), (1, 8, 9) })
                for (int y = y1; y <= y2; y++) for (int x = -h; x <= h; x++) for (int z = -h; z <= h; z++) L(x, hi + y, z, Mc());
            Box(-3, hi + 1, -3, 3, hi + 3, 3, Carve); Box(-1, hi + 1, -6, 1, hi + 2, -3, Carve); L(0, hi + 1, 0, YellowTerracotta); L(1, hi + 1, 0, YellowTerracotta);
            for (int i = 0; i < 30; i++) { int x = (int)Math.Floor(Rn() * 15) - 7, z = (int)Math.Floor(Rn() * 15) - 7; if (Math.Abs(x) > 5 || Math.Abs(z) > 5) L(x, hi + 1, z, JungleLeaves); }
        }
        else if (k == 3) // igloo
        {
            var (df, hi, _) = Flat(-4, -4, 4, 4); if (df > 3) return null;
            Found(-4, -4, 4, 4, hi, Dirt); Box(-4, hi + 1, -4, 4, hi + 6, 4, Carve);
            for (int y = 0; y <= 3; y++) for (int x = -3; x <= 3; x++) for (int z = -3; z <= 3; z++)
                    {
                        double d = Math.Sqrt(x * x + y * y + z * z);
                        if (d <= 3.5 && d > 2.4) L(x, hi + y, z, Snow); else if (y == 0 && d <= 2.4) L(x, hi, z, OakPlanks);
                    }
            L(-3, hi + 1, 0, Ice); Box(-1, hi + 1, 3, 1, hi + 3, 5, Snow); Box(0, hi + 1, 2, 0, hi + 2, 5, Carve); L(0, hi, 4, Snow);
        }
        else if (k == 4) // swamp hut
        {
            int y0 = -1000000000;
            foreach (var (x, z) in new[] { (-2, -2), (2, -2), (-2, 2), (2, 2), (0, 0) }) y0 = Math.Max(y0, G(x, z).y);
            y0 += 2;
            foreach (var (x, z) in new[] { (-2, -2), (2, -2), (-2, 2), (2, 2) }) for (int y = G(x, z).y; y < y0; y++) L(x, y, z, SpruceLog);
            Box(-3, y0 + 1, -3, 3, y0 + 8, 3, Carve); Box(-3, y0, -3, 3, y0, 3, SprucePlanks);
            for (int y = 1; y <= 3; y++) for (int x = -2; x <= 2; x++) for (int z = -2; z <= 2; z++)
                        if (Math.Abs(x) == 2 || Math.Abs(z) == 2) L(x, y0 + y, z, Math.Abs(x) == 2 && Math.Abs(z) == 2 ? SpruceLog : SprucePlanks);
            L(-2, y0 + 2, 0, Glass); L(2, y0 + 2, 0, Glass); L(0, y0 + 2, -2, Glass); L(0, y0 + 1, 2, Carve); L(0, y0 + 2, 2, Carve);
            Box(-3, y0 + 4, -3, 3, y0 + 4, 3, SprucePlanks); Box(-2, y0 + 5, -2, 2, y0 + 5, 2, SprucePlanks); Box(-1, y0 + 6, -1, 1, y0 + 6, 1, SprucePlanks); L(0, y0 + 7, 0, SprucePlanks); L(-1, y0 + 1, -1, Cobblestone);
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
            int y0 = lo + 1; Found(-8, -3, 8, 3, y0, Sand);
            void Dmg(int x, int y, int z, int id) { if (Rn() > .2) L(x, y, z, id); }
            for (int x = -8; x <= 8; x++)
            {
                double t = Math.Abs(x) / 8.5; int hw = (int)M.JsRound(3 * (1 - Math.Pow(t, 2.2))), hy = 2 + (Math.Abs(x) > 5 ? 2 : Math.Abs(x) > 3 ? 1 : 0);
                for (int z = -hw; z <= hw; z++) { Dmg(x, y0, z, Math.Abs(z) < 1 ? JungleLog : SprucePlanks); if (Math.Abs(z) < hw && Rn() < .7) Dmg(x, y0 + 2, z, OakPlanks); }
                for (int y = 1; y <= hy; y++) { Dmg(x, y0 + y, hw, SprucePlanks); if (hw != 0) Dmg(x, y0 + y, -hw, SprucePlanks); }
            }
            for (int y = 1; y <= 8; y++) if (Rn() < .85) L(0, y0 + y, 0, SpruceLog);
            for (int z = -2; z <= 2; z++) if (Rn() < .8) L(0, y0 + 7, z, SpruceLog);
        }
        else // sunken ruins: crumbling brick walls and prismarine pillars around a sea lantern
        {
            var (df, _, lo) = Flat(-5, -5, 5, 5, true);
            if (df > 3 || G(0, 0).h > -3.5 || G(-5, -5).h > -3 || G(5, 5).h > -3) return null;
            int y0 = lo + 1; Found(-5, -5, 5, 5, y0, Sand);
            void Rb(int x, int y, int z) { double r = Rn(); L(x, y, z, r < .4 ? MossyCobblestone : r < .7 ? StoneBricks : PrismarineBricks); }
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
                for (int y = 1; y <= ph; y++) if (y < 3 || Rn() > .12) L(cx, y0 + y, cz, PrismarineBricks);
            }
            L(0, y0 + 1, 0, PrismarineBricks); L(0, y0 + 2, 0, SeaLantern);
        }
        return S;
    }

    internal static Feat StructAt(Band B, int f, int qx, int qz)
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
}
