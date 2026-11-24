using System.Numerics;

namespace CubePlanet;

/// <summary>Height, surface blocks and biome of a terrain column (results in the [ThreadStatic] g* slots).</summary>
public static class TerrainGen
{
    // ---------- terrain ----------
    // results: height above sea level, top block, filler block, filler thickness, frozen-water flag, biome
    // per-thread result slots (Terrain() / BaseBlk() are called from the chunk worker threads as well)
    [ThreadStatic] public static double gH, gS;
    [ThreadStatic] public static int gT, gF, gFD, gI, gB;
    // terracotta strata of badlands / red canyon walls, one entry per block of height (repeats every 24)
    internal static readonly int[] BANDS = { Terracotta, OrangeTerracotta, Terracotta, Terracotta, YellowTerracotta, Terracotta, WhiteTerracotta, Terracotta, RedTerracotta, Terracotta, OrangeTerracotta, Terracotta, BrownTerracotta, Terracotta, YellowTerracotta, WhiteTerracotta, Terracotta, OrangeTerracotta, RedTerracotta, Terracotta, Terracotta, OrangeTerracotta, Terracotta, WhiteTerracotta };
    /// <summary>Filler value meaning "terracotta bands by height" (BaseBlk picks the band); 0 is never a real filler.</summary>
    public const int FillBands = 0;
    public static readonly string[] BNM = { "Ocean", "Plains", "Forest", "Birch Forest", "Taiga", "Snowy Plains", "Jungle", "Savanna", "Desert", "Badlands", "Swamp", "Mountains", "Beach", "Snowy Peaks",
        "Lush Forest", "Cherry Grove", "Oasis", "Red Canyon", "Snowy Taiga", "Old Growth Taiga", "Glacier Peaks", "Coral Reef", "Kelp Forest", "Frozen Ocean", "Deep Ocean", "Tropical Beach", "Tropical Island", "Stony Shore", "Bamboo Jungle", "Flower Meadow", "Volcano", "Warm Ocean" };

    // noise slots used by the biome code (each slot has its own random offset): 27 lush/cherry, 28 oasis, 29 volcano, 30 canyon region, 31 islands, 32 sand/clay/island detail,
    // 33 ice / ash detail, 34 icebergs, 35 sea ridges, 36 trenches, 37 canyon lines, 38 canyon plateau, 39 meadow, 40 bamboo, 41 old growth, 42 coral reef
    public static void Terrain(double ux, double uy, double uz, double lw)
    {
        double x = ux * R, y = uy * R, z = uz * R, lat = Math.Abs(uy), Lc = LCN, bs = BSC, ls = LSC, sl = Math.Sqrt(LSC), dw = Math.Min(1, (LSC - 1) / 2);
        double l = Fb(x, y, z, Lc, 4, 0, lw) * 2.1 + .1 * Fb(x, y, z, 130 * ls, 3, 1, lw) + .02; // continents: >0 land
        double tmp = 1 - lat * lat * 1.15 + Fb(x, y, z, 700 * bs, 2, 5, lw) * .45, wet = Fb(x, y, z, 520 * bs, 3, 6, lw) * 1.6;
        double H, mtn = 0, ds = 0, rock = 0, bd = 0, oas = 0, cm = 0, cliff = 1, isl = 0, icb = 0, vol = 0, crat = 0; bool sw = false;
        if (l < 0)
        {
            double e = 1 - Math.Exp(l * 4.2); H = -OD * e + Fb(x, y, z, 160 * ls, 3, 2, lw) * 6 * e + dw * Fb(x, y, z, 30, 3, 44, lw) * 2 * e;
            if (e > .04)
            {   // submerged ridges / sand bars and deep trenches
                H += M.Sm(.62, .86, Ridge(x, y, z, 120 * ls, 3, 35, lw)) * (3 + 10 * e) * Math.Max(1, HS);
                H -= M.Sm(.82, .96, Ridge(x, y, z, 230 * ls, 2, 36, lw)) * 15 * Math.Max(1, HS) * e * e;
            }
            if (H > -.3) H = -.3;
            if (H < -OD * 1.75) H = -OD * 1.75;
            if (tmp > .5)
            {   // tropical islands and atolls
                isl = M.Sm(.42, .58, Fb(x, y, z, 90, 2, 31, lw)) * M.Sm(.5, .66, tmp) * M.Sm(-17, -7, H) * M.Sm(-.05, .15, Fb(x, y, z, 500 * bs, 2, 47, lw)); // island groups only in some regions
                if (isl > 0) H += isl * (4.2 + Fb(x, y, z, 16, 2, 32, lw) * 2.6 - H);
            }
            else if (tmp < .1)
            {   // icebergs
                icb = M.Sm(.40, .56, Fb(x, y, z, 42, 2, 34, lw)) * M.Sm(-16, -3, H) * M.Sm(-.05, .15, Fb(x, y, z, 500 * bs, 2, 47, lw));
                if (icb > 0) H += icb * (3.5 + Math.Abs(Fb(x, y, z, 13, 2, 33, lw)) * 22 - H);
            }
        }
        else
        {
            double er = Fb(x, y, z, 520 * ls, 3, 19, lw), rug = M.Sm(-.5, .45, -er); // erosion: low = rugged, high = worn down and flat
            double wp0 = 70 * ls, wx = Fb(x, y, z, 210 * ls, 2, 20, lw) * wp0, wy = Fb(x, y, z, 210 * ls, 2, 21, lw) * wp0, wz = Fb(x, y, z, 210 * ls, 2, 22, lw) * wp0; // domain warp
            double hill = Fb(x + wx * .4, y + wy * .4, z + wz * .4, 75 * ls, 4, 2, lw) * 14 * HS * (.55 + .9 * rug);
            mtn = M.Sm(.05, .55, Fb(x, y, z, 480 * ls, 2, 3, lw) * 1.6 + (l - .35) * 1.2) * (.3 + .7 * rug);
            double peaks = Math.Pow(Ridge(x + wx, y + wy, z + wz, 190 * ls, 5, 4, lw), 1.5);
            double hk = Math.Max(1, HS); // continental uplift grows with the height scale, otherwise the big hills drown the land in lakes
            H = l * 16 * hk + hill * Math.Min(1, l * 5) + mtn * (peaks * 1.3 + .08) * MH;
            if (dw > 0) H += dw * (Fb(x, y, z, 30, 3, 44, lw) * 3 * Math.Min(1, l * 5) + mtn * Ridge(x, y, z, 55, 3, 45, lw) * 10 * HS); // keeps small-scale roughness on huge worlds
            // valleys cut into the highlands; broad terraced plateaus on worn-down uplands
            double vv = Math.Abs(Fb(x + wx, y + wy, z + wz, 320 * ls, 2, 23, lw));
            H -= (1 - M.Sm(0, .09, vv)) * (.3 + .7 * rug) * M.Sm(8, 40, H) * 14 * HS;
            double pt = M.Sm(.25, .55, Fb(x, y, z, 380 * ls, 2, 24, lw)) * (1 - rug) * M.Sm(10, 40, H);
            H += (Math.Floor(H / 8) * 8 + 4 - H) * pt * .6;
            // hot & dry = desert (rolling dunes); part of it becomes badlands (terraced mesas) or red canyon country
            ds = M.Sm(.66, .8, tmp) * (1 - M.Sm(-.25, -.1, wet));
            if (ds > 0) { double d1 = 1 - Math.Abs(Fb(x, y, z, 34 * sl, 2, 7, lw) * 1.7); H = H * (1 - ds * .35) + d1 * d1 * 6 * ds * M.Sm(1, 5, H) * (1 - mtn); }
            bd = ds > .3 ? M.Sm(.02, .2, Fb(x, y, z, 330 * bs, 2, 4, lw)) : 0;
            if (bd > 0) { double mz = M.Sm(-.1, .4, Fb(x, y, z, 90 * ls, 3, 5, lw)); H = H * (1 - bd) + Math.Floor((H + mz * 22 * HS) / 4) * 4 * bd; }
            // red canyon: terraced plateaus cut by winding slot canyons whose walls show the banded strata
            if (ds > .3 && bd < .6 && l > .12)
            {
                double cmv = M.Sm(.10, .28, Fb(x, y, z, 420 * bs, 2, 30, lw)) * M.Sm(.3, .6, ds) * (1 - bd) * (1 - mtn);
                if (cmv > 0)
                {
                    double cvl = Math.Abs(Fb(x + wx * .7, y + wy * .7, z + wz * .7, 170 * sl, 2, 37, lw));
                    cliff = M.Sm(.03, .10, cvl);
                    double plat = Math.Floor((22 * HS + 6 * Fb(x, y, z, 90 * sl, 2, 38, lw)) / 3) * 3;
                    double Hc = 2.4 + Math.Floor(cliff * 6) / 6 * (plat - 2.4);
                    H = H * (1 - cmv) + Hc * cmv; cm = cmv;
                }
            }
            // oasis: a small pool with a ring of grass inside the dunes
            if (ds > .55 && bd < .3 && cm < .25)
            {
                oas = M.Sm(.42, .56, Fb(x, y, z, 56, 2, 28, lw)) * M.Sm(.55, .75, ds);
                if (oas > 0) H += oas * (.8 - 2.8 * M.Sm(.55, .9, oas) - H);
            }
            // swamp: warm, wet, low land with shallow ponds
            sw = tmp > .5 && tmp < .95 && wet > .42 && mtn < .1 && ds < .2;
            if (sw && H < 8) H = Math.Min(H, 1.5) * .5 + Fb(x, y, z, 20, 2, 7, lw) * 1.8 - .1;
            // rivers: zero-crossings of warped noise, carved only in lowlands
            double wp = 45 * ls;
            double rv = Math.Abs(Fb(x + Fb(x, y, z, 150 * ls, 1, 8, lw) * wp, y + Fb(x, y, z, 150 * ls, 1, 9, lw) * wp, z + Fb(x, y, z, 150 * ls, 1, 10, lw) * wp, 300 * ls, 2, 11, lw));
            double low = 1 - M.Sm(8 * hk, 22 * hk, H), bb = M.Sm(0, .075, rv);
            if (low > 0 && bb < 1) H = H + ((-4 + bb * (H + 4)) - H) * low;
            rock = Fb(x, y, z, 40, 2, 12, lw);
        }
        {   // volcanoes: steep basalt cones with lava lakes in the crater (on land and rising out of the sea)
            double vn = Fb(x, y, z, 320 * sl, 2, 29, lw);
            if (vn > .34)
            {   // rare: only inside "volcanic regions" so they do not dot the whole planet
                vol = M.Sm(.34, .56, vn) * M.Sm(.08, .22, Fb(x, y, z, 900 * bs, 2, 46, lw));
                if (vol > 0) { crat = M.Sm(.72, .90, vol); H += (Math.Pow(vol, 1.3) * 50 - crat * 16) * HS; }
            }
        }
        if (H > HMAX - 6) H = HMAX - 6;
        double tm = tmp - Math.Max(H, 0) * .005 / HS, snowL = 46 * HS + Fb(x, y, z, 60, 2, 13, lw) * 7;
        gI = tm < .1 ? 1 : 0; gFD = 3; int bi = 1;
        if (oas > .1 && H < 8)
        {
            bi = 16;
            if (H < -.1) { gT = Sand; gF = Sand; gFD = 3; } else { gT = Grass; gF = Dirt; }
        }
        else if (H < 0)
        {
            double dep = -H;
            if (l >= 0)
            {   // river / lake / pond
                bi = 0;
                if (H > -6) { gT = Sand; gF = Sand; gFD = 2; } else if (H > -15) { gT = Gravel; gF = Sand; gFD = 2; } else { gT = Dirt; gF = Dirt; gFD = 2; }
            }
            else
            {
                double ck = Fb(x, y, z, 24, 2, 32, lw);   // clay patches
                if (tm < .1)
                {
                    bi = 23;
                    if (icb > .1) { gT = PackedIce; gF = PackedIce; gFD = 26; }
                    else if (dep < 7) { gT = Gravel; gF = Gravel; gFD = 2; } else { gT = Dirt; gF = Dirt; gFD = 2; }
                }
                else if (vol > .08) { bi = 0; gT = Basalt; gF = Basalt; gFD = 4; }
                else if (tm >= .56)
                {   // warm sea: pale sand, lagoons and coral reefs
                    bool reef = dep > 2.2 && dep < 20 && Fb(x, y, z, 70 * sl, 2, 42, lw) > .08;
                    bi = reef ? 21 : 31;
                    if (dep < 12) { gT = ck > .5 ? Clay : CoralSand; gF = Sand; gFD = 2; }
                    else if (dep < 20) { gT = Sand; gF = Sand; gFD = 2; }
                    else { gT = Gravel; gF = Dirt; gFD = 2; }
                }
                else if (dep > 17) { bi = 24; gT = ck > .38 ? Clay : (dep > 30 ? Stone : Gravel); gF = Dirt; gFD = 2; }
                else if (tm < .3) { bi = 22; gT = dep < 8 ? Gravel : (ck > .42 ? Clay : Dirt); gF = dep < 8 ? Gravel : Dirt; gFD = 2; }
                else
                {
                    bi = 0;
                    if (dep < 6) { gT = Sand; gF = Sand; gFD = 2; } else if (dep < 15) { gT = Gravel; gF = Sand; gFD = 2; } else { gT = Dirt; gF = Dirt; gFD = 2; }
                    if (ck > .5 && dep > 3) gT = Clay;
                }
            }
        }
        else if (icb > .1)
        {   // iceberg above the water
            bi = 23; double sn = Fb(x, y, z, 9, 2, 33, lw);
            gT = H > 6 ? (sn > .15 ? BlueIce : PackedIce) : (sn > .2 ? PackedIce : Snow); gF = PackedIce; gFD = 26;
        }
        else if (vol > .14)
        {
            bi = 30;
            if (crat > .45 && H > 10) { gT = Lava; gF = Basalt; gFD = 8; }
            else if (crat > .08) { gT = Obsidian; gF = Basalt; gFD = 8; }
            else if (vol > .45 || H > 24) { gT = Basalt; gF = Basalt; gFD = 8; }
            else { gT = Fb(x, y, z, 7, 2, 33, lw) > .1 ? Basalt : VolcanicAsh; gF = VolcanicAsh; gFD = 4; }
        }
        else if (isl > .1)
        {
            if (H <= 2.4) { bi = 25; gT = Fb(x, y, z, 30, 2, 32, lw) > .1 ? CoralSand : Sand; gF = Sand; gFD = 3; }
            else { bi = 26; gT = JungleGrass; gF = Dirt; }
        }
        else if (H > snowL || (tm < .06 && mtn > .3 && H > 12))
        {
            if (tm < .12) { bi = 20; double gl = Fb(x, y, z, 38, 2, 33, lw); gT = gl > .30 ? BlueIce : gl > .02 ? PackedIce : Snow; gF = PackedIce; gFD = 9; }
            else { bi = 13; gT = Snow; gF = Stone; gFD = 2; }
        }
        else if (mtn > .5 && H > 10 * HS) { bi = 11; gT = rock > .35 ? Gravel : Stone; gF = Stone; gFD = 2; }
        else if (tm < .18)
        {
            if (wet > -.15 && tm > -.04) { bi = 18; gT = Snow; gF = Dirt; }
            else { bi = 5; gT = (tm < -.1 && Fb(x, y, z, 50, 2, 33, lw) > .28) ? PackedIce : Snow; gF = Dirt; }
        }
        else if (cm > .45) { bi = 17; gT = (cliff > .99 || H < 4.3) ? RedSand : BANDS[(((int)Math.Floor(H) % 24) + 24) % 24]; gF = FillBands; gFD = 64; }
        else if (ds > .45)
        {
            if (bd > .5) { bi = 9; gT = H < 4 ? RedSand : BANDS[(((int)Math.Floor(H) % 24) + 24) % 24]; gF = FillBands; gFD = 64; }
            else { bi = 8; gT = Sand; gF = Sandstone; gFD = 5; }
        }
        else if (sw) { bi = 10; gT = SwampGrass; gF = Dirt; }
        else if (H <= (tmp > .62 ? 3.2 : 2.6))
        {
            if (tmp > .62) { bi = 25; gT = Fb(x, y, z, 30, 2, 32, lw) > .1 ? CoralSand : Sand; gF = Sand; gFD = 3; }
            else if (tm < .3) { bi = 27; gT = Gravel; gF = Gravel; gFD = 3; }
            else { bi = 12; gT = Sand; gF = Sand; }
        }
        else if (tmp > .72)
        {
            if (wet < .1) { bi = 7; gT = SavannaGrass; }
            else if (Fb(x, y, z, 160 * bs, 2, 40, lw) > .15) { bi = 28; gT = JungleGrass; }
            else { bi = 6; gT = JungleGrass; }
            gF = Dirt;
        }
        else if (tm < .38)
        {
            if (wet > -.2)
            {
                if (tm > .2 && Fb(x, y, z, 200 * bs, 2, 41, lw) > .17) { bi = 19; gT = Fb(x, y, z, 16, 2, 33, lw) > .25 ? TaigaGrass : Podzol; }
                else { bi = 4; gT = TaigaGrass; }
            }
            else { bi = 1; gT = Grass; }
            gF = Dirt;
        }
        else
        {
            if (wet > .05)
            {
                double gs = Fb(x, y, z, 180 * bs, 2, 27, lw);
                if (gs > .2 && wet > .2) { bi = 14; gT = Fb(x, y, z, 18, 2, 32, lw) > .2 ? MossBlock : LushGrass; }
                else if (gs < -.24 && tm > .3 && tm < .85) { bi = 15; gT = Grass; }
                else { bi = Fb(x, y, z, 150 * bs, 2, 6, lw) > .12 ? 3 : 2; gT = Grass; }
            }
            else
            {
                bi = 1; gT = Grass;
                if (wet > -.25 && Fb(x, y, z, 150 * bs, 2, 39, lw) > .22) bi = 29;
            }
            gF = Dirt;
        }
        gH = H; gB = bi;
        if (lw <= 0) Lakes.Apply(x, y, z); // lakes in the full-detail terrain only (not the distant LOD)
    }

    public static string BiomeName(V3 p)
    {
        double r = p.Len; Terrain(p.X / r, p.Y / r, p.Z / r, 0); return BNM[gB];
    }
}
