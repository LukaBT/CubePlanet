using System.Numerics;
using static CubePlanet.Cube;
using static CubePlanet.Blocks;

namespace CubePlanet;

public class Band { public int b, fc, cols, rows, inner; public double d; }

public class Col
{
    public float[] h = new float[1024];
    public byte[] t = new byte[1024], f = new byte[1024], fd = new byte[1024], ice = new byte[1024], b = new byte[1024];
    public double hi = -1e9, lo = 1e9;
}

public class Mesh { public GAlloc a; public uint vb; public int n, nw; public bool i16; public V3 org; } // vb = vertex bytes (indices follow them in the same allocation)

public class Chunk
{
    public long key; public int band, face, cx, cy, cz;
    public byte[] d; public bool dirty = true, built, emp, solid, core, empty;
    public int gst; public bool gfail; // generation claim: 0 free, 1 claimed by a worker or the main thread; gfail = a worker threw, the main thread retries
    public Mesh mesh, wmesh;
    public double mx, my, mz, cr;
    public List<long> wait;
    public int[] cc;
    public long waitStamp = -1;
    public int mst;            // mesh claim: 0 idle, 1 a worker is meshing it
    public bool relight;       // a neighbour it was waiting for appeared: re-mesh in the background
    public int readyWait;      // frames spent waiting for neighbour data before meshing
    public bool mfail;         // background mesh threw: retry on the main thread
}

/// <summary>World state, terrain generation and block access. Mirrors the globals of the original page.</summary>
public static partial class W
{
    public const int CS = 32, CORE = 96, FC0 = 6, NT = 118;
    public const double PI = Math.PI;
    public static double R;
    public static int BC, OUTER, RD = 6, MAXL = 8, MINL = 2;
    public static List<Band> bands = new();
    public static Dictionary<long, Chunk> chunks = new();   // main thread (iteration)
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<long, Chunk> chunkMap = new(); // same content, for lookups from worker threads
    public static void AddChunk(Chunk ch) { chunks[ch.key] = ch; chunkMap[ch.key] = ch; }
    public static Dictionary<long, byte> edits = new();
    public static Dictionary<long, int> ecount = new();
    public static double HS = 1, HMAX = 100, MH = 70, OD = 24, DEP = 48, CAVB = 100;
    /// <summary>World-size scaling: biome regions (BSC), landforms such as mountains/rivers/ocean relief (LSC) and continent wavelength (LCN, blocks).</summary>
    public static double BSC = 1, LSC = 1, LCN = 900;
    public static int SEEDV = 1;
    public static int bandOf(double r) => 31 - BitOperations.LeadingZeroCount((uint)Math.Floor(r / CORE));

    // results of Wrap / P2b / Norm (globals as in the original)
    // [ThreadStatic]: chunk generation, meshing and the loader scan run on worker threads too
    [ThreadStatic] static long blkKey; [ThreadStatic] static Chunk blkCh;
    [ThreadStatic] public static int WF, WX, WZ;
    [ThreadStatic] public static int PF, PX, PY, PZ;
    [ThreadStatic] public static int NB, NF, NX, NY, NZ, NCV;
    public static void ResetBlkCache() { blkCh = null; blkKey = 0; }

    // ---------- geometry ----------
    public static V3 GpA(int f, double a, double b, double rad)
    {
        double ta = Math.Tan(a), tb = Math.Tan(b);
        V3 n = N[f], u = U[f], v = V[f];
        double x = n.X + u.X * ta + v.X * tb, y = n.Y + u.Y * ta + v.Y * tb, z = n.Z + u.Z * ta + v.Z * tb;
        double s = rad / Math.Sqrt(x * x + y * y + z * z);
        return new V3(x * s, y * s, z * s);
    }
    public static V3 Gp(Band B, int f, double ui, double vi, double rad) => GpA(f, -PI / 4 + ui * B.d, -PI / 4 + vi * B.d, rad);

    /// <summary>Wrap block coords across a cube-face seam. Result in WF/WX/WZ; false when outside (corner).</summary>
    public static bool Wrap(Band B, int f, int bx, int bz)
    {
        int n = B.cols; bool oU = bz < 0 || bz >= n, oV = bx < 0 || bx >= n;
        WF = f; WX = bx; WZ = bz;
        if (!oU && !oV) return true;
        if (oU && oV) return false;
        int s, m, al;
        if (oU) { s = bz >= n ? 0 : 1; m = bz >= n ? bz - n : -1 - bz; al = bx; }
        else { s = bx >= n ? 2 : 3; m = bx >= n ? bx - n : -1 - bx; al = bz; }
        if (m >= n) return false;
        Link l = LK[f * 4 + s];
        int ac = l.af ? n - 1 - m : m, ai = l.al ? n - 1 - al : al;
        WF = l.g;
        if (l.acU) { WZ = ac; WX = ai; } else { WX = ac; WZ = ai; }
        return true;
    }

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
    static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
    static double Grd(int h, double x, double y, double z)
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
    static int NOct(int oct, double L, double lw) => lw > 0 ? Math.Max(1, Math.Min(oct, (int)Math.Floor(Math.Log2(Math.Max(L / lw, 1))) + 1)) : oct;
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

    // ---------- terrain ----------
    // results: height above sea level, top block, filler block, filler thickness, frozen-water flag, biome
    // per-thread result slots (Terrain() / BaseBlk() are called from the chunk worker threads as well)
    [ThreadStatic] public static double gH, gS;
    [ThreadStatic] public static int gT, gF, gFD, gI, gB;
    static readonly int[] BANDS = { 13, 14, 13, 13, 15, 13, 16, 13, 17, 13, 14, 13, 18, 13, 15, 16, 13, 14, 17, 13, 13, 14, 13, 16 };
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
            if (H < -.1) { gT = 7; gF = 7; gFD = 3; } else { gT = 1; gF = 3; }
        }
        else if (H < 0)
        {
            double dep = -H;
            if (l >= 0)
            {   // river / lake / pond
                bi = 0;
                if (H > -6) { gT = 7; gF = 7; gFD = 2; } else if (H > -15) { gT = 10; gF = 7; gFD = 2; } else { gT = 3; gF = 3; gFD = 2; }
            }
            else
            {
                double ck = Fb(x, y, z, 24, 2, 32, lw);   // clay patches
                if (tm < .1)
                {
                    bi = 23;
                    if (icb > .1) { gT = 79; gF = 79; gFD = 26; }
                    else if (dep < 7) { gT = 10; gF = 10; gFD = 2; } else { gT = 3; gF = 3; gFD = 2; }
                }
                else if (vol > .08) { bi = 0; gT = 83; gF = 83; gFD = 4; }
                else if (tm >= .56)
                {   // warm sea: pale sand, lagoons and coral reefs
                    bool reef = dep > 2.2 && dep < 20 && Fb(x, y, z, 70 * sl, 2, 42, lw) > .08;
                    bi = reef ? 21 : 31;
                    if (dep < 12) { gT = ck > .5 ? 81 : 82; gF = 7; gFD = 2; }
                    else if (dep < 20) { gT = 7; gF = 7; gFD = 2; }
                    else { gT = 10; gF = 3; gFD = 2; }
                }
                else if (dep > 17) { bi = 24; gT = ck > .38 ? 81 : (dep > 30 ? 2 : 10); gF = 3; gFD = 2; }
                else if (tm < .3) { bi = 22; gT = dep < 8 ? 10 : (ck > .42 ? 81 : 3); gF = dep < 8 ? 10 : 3; gFD = 2; }
                else
                {
                    bi = 0;
                    if (dep < 6) { gT = 7; gF = 7; gFD = 2; } else if (dep < 15) { gT = 10; gF = 7; gFD = 2; } else { gT = 3; gF = 3; gFD = 2; }
                    if (ck > .5 && dep > 3) gT = 81;
                }
            }
        }
        else if (icb > .1)
        {   // iceberg above the water
            bi = 23; double sn = Fb(x, y, z, 9, 2, 33, lw);
            gT = H > 6 ? (sn > .15 ? 80 : 79) : (sn > .2 ? 79 : 8); gF = 79; gFD = 26;
        }
        else if (vol > .14)
        {
            bi = 30;
            if (crat > .45 && H > 10) { gT = 75; gF = 83; gFD = 8; }
            else if (crat > .08) { gT = 86; gF = 83; gFD = 8; }
            else if (vol > .45 || H > 24) { gT = 83; gF = 83; gFD = 8; }
            else { gT = Fb(x, y, z, 7, 2, 33, lw) > .1 ? 83 : 84; gF = 84; gFD = 4; }
        }
        else if (isl > .1)
        {
            if (H <= 2.4) { bi = 25; gT = Fb(x, y, z, 30, 2, 32, lw) > .1 ? 82 : 7; gF = 7; gFD = 3; }
            else { bi = 26; gT = 19; gF = 3; }
        }
        else if (H > snowL || (tm < .06 && mtn > .3 && H > 12))
        {
            if (tm < .12) { bi = 20; double gl = Fb(x, y, z, 38, 2, 33, lw); gT = gl > .30 ? 80 : gl > .02 ? 79 : 8; gF = 79; gFD = 9; }
            else { bi = 13; gT = 8; gF = 2; gFD = 2; }
        }
        else if (mtn > .5 && H > 10 * HS) { bi = 11; gT = rock > .35 ? 10 : 2; gF = 2; gFD = 2; }
        else if (tm < .18)
        {
            if (wet > -.15 && tm > -.04) { bi = 18; gT = 8; gF = 3; }
            else { bi = 5; gT = (tm < -.1 && Fb(x, y, z, 50, 2, 33, lw) > .28) ? 79 : 8; gF = 3; }
        }
        else if (cm > .45) { bi = 17; gT = (cliff > .99 || H < 4.3) ? 12 : BANDS[(((int)Math.Floor(H) % 24) + 24) % 24]; gF = 250; gFD = 64; }
        else if (ds > .45)
        {
            if (bd > .5) { bi = 9; gT = H < 4 ? 12 : BANDS[(((int)Math.Floor(H) % 24) + 24) % 24]; gF = 250; gFD = 64; }
            else { bi = 8; gT = 7; gF = 11; gFD = 5; }
        }
        else if (sw) { bi = 10; gT = 22; gF = 3; }
        else if (H <= (tmp > .62 ? 3.2 : 2.6))
        {
            if (tmp > .62) { bi = 25; gT = Fb(x, y, z, 30, 2, 32, lw) > .1 ? 82 : 7; gF = 7; gFD = 3; }
            else if (tm < .3) { bi = 27; gT = 10; gF = 10; gFD = 3; }
            else { bi = 12; gT = 7; gF = 7; }
        }
        else if (tmp > .72)
        {
            if (wet < .1) { bi = 7; gT = 20; }
            else if (Fb(x, y, z, 160 * bs, 2, 40, lw) > .15) { bi = 28; gT = 19; }
            else { bi = 6; gT = 19; }
            gF = 3;
        }
        else if (tm < .38)
        {
            if (wet > -.2)
            {
                if (tm > .2 && Fb(x, y, z, 200 * bs, 2, 41, lw) > .17) { bi = 19; gT = Fb(x, y, z, 16, 2, 33, lw) > .25 ? 21 : 78; }
                else { bi = 4; gT = 21; }
            }
            else { bi = 1; gT = 1; }
            gF = 3;
        }
        else
        {
            if (wet > .05)
            {
                double gs = Fb(x, y, z, 180 * bs, 2, 27, lw);
                if (gs > .2 && wet > .2) { bi = 14; gT = Fb(x, y, z, 18, 2, 32, lw) > .2 ? 77 : 76; }
                else if (gs < -.24 && tm > .3 && tm < .85) { bi = 15; gT = 1; }
                else { bi = Fb(x, y, z, 150 * bs, 2, 6, lw) > .12 ? 3 : 2; gT = 1; }
            }
            else
            {
                bi = 1; gT = 1;
                if (wet > -.25 && Fb(x, y, z, 150 * bs, 2, 39, lw) > .22) bi = 29;
            }
            gF = 3;
        }
        gH = H; gB = bi;
    }

    /// <summary>Cave density (>0 = air): cheese caverns, spaghetti tunnels, a wide tunnel network in some regions, and ravines.</summary>
    static double CaveS(double x, double y, double z, double rel)
    {
        var o = OFF[14]; var p = OFF[15]; var q = OFF[16]; var u = OFF[17]; var w = OFF[18]; var mk = OFF[25];
        // cheese caves: big irregular caverns, rare close to the surface
        double c = Noise3(x * .016 + p[2], y * .024 + o[1], z * .016 + p[0]) + .55 * Noise3(x * .045 + o[2], y * .06 + p[1], z * .045 + o[0]);
        double cheese = (c - .58) / .3 - .45 * M.Sm(-30, -5, rel);
        // spaghetti caves: where two noise zero-surfaces cross; thickness varies along the network
        double th = .075 + .06 * (Noise3(x * .008 + q[0], y * .008 + q[1], z * .008 + q[2]) * .5 + .5);
        double a = Noise3(x * .034 + o[0], y * .034 + o[1], z * .034 + o[2]), b = Noise3(x * .034 + p[0], y * .034 + p[1], z * .034 + p[2]);
        double spag = 1 - Math.Max(Math.Abs(a), Math.Abs(b)) / th;
        // wide tunnel network, only in some regions
        double reg = Noise3(x * .006 + mk[0], y * .006 + mk[1], z * .006 + mk[2]);
        double a2 = Noise3(x * .021 + u[0], y * .021 + u[1], z * .021 + u[2]), b2 = Noise3(x * .021 + w[0], y * .021 + w[1], z * .021 + w[2]);
        double wide = (1 - Math.Max(Math.Abs(a2), Math.Abs(b2)) / .11) - M.Sm(.05, -.35, reg) * 1.2;
        // ravines: long vertical slots, narrowing towards the bottom, open to the sky in lowlands, only in some regions
        double rr = Math.Sqrt(x * x + y * y + z * z), sc = rr > 1 ? R / rr : 1;
        double rn = Noise3(x * sc * .011 + q[2], y * sc * .011 + q[0], z * sc * .011 + q[1]);
        double rm = Noise3(x * sc * .004 + mk[2], y * sc * .004 + mk[0], z * sc * .004 + mk[1]);
        double rav = 1 - Math.Abs(rn) / .055 - (1 - M.Sm(-52, -22, rel)) - M.Sm(10, 24, rel) * 1.5 - M.Sm(.25, -.05, rm) * 1.5;
        double tp = Math.Max(0, Math.Min(1.3, (-rel - (CAVB - 30)) / 30 * 1.3));
        return Math.Max(Math.Max(Math.Max(spag, cheese), wide), rav) - tp;
    }

    // ---------- column / cave caches ----------
    // caches are per thread (no locking); WGEN is bumped when a new world is created so every thread drops its old caches
    public static volatile int WGEN;
    [ThreadStatic] static int tlGen;
    [ThreadStatic] static Dictionary<long, double> tlNode;
    [ThreadStatic] static Dictionary<long, Feat> tlPlc, tlStc;
    static void TlSync() { if (tlNode == null || tlGen != WGEN) { tlNode = new(); tlPlc = new(); tlStc = new(); tlGen = WGEN; } }
    // the column cache is shared by all threads (the mesher on the main thread needs the same columns the workers just computed)
    static readonly System.Collections.Concurrent.ConcurrentDictionary<long, Col> colCache = new();
    static Dictionary<long, double> nodeCache { get { TlSync(); return tlNode; } }
    static Dictionary<long, Feat> PLC { get { TlSync(); return tlPlc; } }
    static Dictionary<long, Feat> STC { get { TlSync(); return tlStc; } }
    public static readonly object EditLock = new(); // guards edits / ecount between the main thread (writer) and the chunk workers (readers)

    public static Col ColsOf(Band B, int f, int cx, int cz)
    {
        long key = (((long)(B.b * 6 + f) * 1048576) + cx) * 1048576 + cz;
        if (colCache.TryGetValue(key, out var c)) return c;
        if (colCache.Count > 6000) colCache.Clear();
        c = new Col();
        for (int z = 0; z < CS; z++)
            for (int x = 0; x < CS; x++)
            {
                V3 t = GpA(f, -PI / 4 + (cz * CS + z + .5) * B.d, -PI / 4 + (cx * CS + x + .5) * B.d, 1);
                Terrain(t.X, t.Y, t.Z, 0);
                int i = x + CS * z;
                c.h[i] = (float)gH; c.t[i] = (byte)gT; c.f[i] = (byte)gF; c.fd[i] = (byte)gFD; c.ice[i] = (byte)gI; c.b[i] = (byte)gB;
                if (gH > c.hi) c.hi = gH; if (gH < c.lo) c.lo = gH;
            }
        colCache[key] = c; return c;
    }
    public static Col ColsPeek(Band B, int f, int cx, int cz) =>
        colCache.TryGetValue((((long)(B.b * 6 + f) * 1048576) + cx) * 1048576 + cz, out var c) ? c : null;

    // cave density is sampled on a 4-block lattice and trilinearly interpolated (>0 = air)
    public static double NodeV(Band B, int f, int gx, int gy, int gz)
    {
        long key = Mix(B.b * 6 + f, gx, gy, gz);
        if (nodeCache.TryGetValue(key, out double v)) return v;
        if (nodeCache.Count > 250000) nodeCache.Clear();
        V3 t = GpA(f, -PI / 4 + (gz * 4 + .5) * B.d, -PI / 4 + (gx * 4 + .5) * B.d, B.inner + gy * 4 + .5);
        v = CaveS(t.X, t.Y, t.Z, t.Len - R);
        nodeCache[key] = v; return v;
    }
    public static double Tri(double a, double b, double c, double d, double e, double f, double g, double h, double x, double y, double z) =>
        ((a * (1 - x) + b * x) * (1 - y) + (c * (1 - x) + d * x) * y) * (1 - z) + ((e * (1 - x) + f * x) * (1 - y) + (g * (1 - x) + h * x) * y) * z;
    public static double CaveAt(Band B, int f, int bx, int by, int bz)
    {
        int gx = bx >> 2, gy = by >> 2, gz = bz >> 2;
        return Tri(NodeV(B, f, gx, gy, gz), NodeV(B, f, gx + 1, gy, gz), NodeV(B, f, gx, gy + 1, gz), NodeV(B, f, gx + 1, gy + 1, gz),
            NodeV(B, f, gx, gy, gz + 1), NodeV(B, f, gx + 1, gy, gz + 1), NodeV(B, f, gx, gy + 1, gz + 1), NodeV(B, f, gx + 1, gy + 1, gz + 1),
            (bx & 3) / 4.0, (by & 3) / 4.0, (bz & 3) / 4.0);
    }

    // block ids: 0 air, 1 grass, 2 stone, 3 dirt, 6 ice, 7 sand, 8 snow, 9 water, 10 gravel, 11 sandstone
    /// <summary>Stone that turns into deepslate with depth (dithered between 56 and 72 blocks below the surface).</summary>
    public static int StoneAt(double s, int i, double r, int salt)
    {
        if (s < 56) return 2; if (s >= 72) return 66;
        int h = unchecked(salt * 374761393 ^ i * 668265263 ^ (int)Math.Floor(r) * 2147483647);
        h = unchecked((h ^ (int)((uint)h >> 13)) * 1274126177);
        return (uint)(h ^ (int)((uint)h >> 16)) / 4294967296.0 < (s - 56) / 16 ? 66 : 2;
    }
    public static int BaseBlk(Col c, int i, double r, int salt = 0)
    {
        double s = R + c.h[i] - r; gS = 0;
        if (s <= 0) { double rel = r - R; return rel < 0 ? (c.ice[i] != 0 && rel > -1 ? 6 : 9) : 0; }
        gS = s;
        if (s <= 1) return c.t[i];
        if (s <= 1 + c.fd[i]) { int q = c.f[i]; return q == 250 ? BANDS[(((int)Math.Floor(r - R) % 24) + 24) % 24] : q; }
        return StoneAt(s, i, r, salt);
    }
    public static bool Cavable(Col c, int i, double r) => gS > 0 && r - R > -CAVB && gS >= (c.h[i] < 3 ? 3 : 1.2);
    public static int Gen(Band B, int f, int bx, int by, int bz)
    {
        Col c = ColsOf(B, f, bx >> 5, bz >> 5); int i = (bx & 31) + 32 * (bz & 31); double r = B.inner + by + .5;
        int id = BaseBlk(c, i, r, (bx >> 5) * 7919 + (bz >> 5) * 104729);
        return id != 0 && Cavable(c, i, r) && CaveAt(B, f, bx, by, bz) > 0 ? 0 : id;
    }

    // ---------- position <-> block ----------
    /// <summary>Position to band index (or -1 for core/outside); result block coords in PF/PX/PY/PZ.</summary>
    public static int P2b(double x, double y, double z)
    {
        double r = Math.Sqrt(x * x + y * y + z * z);
        if (r < CORE || r >= OUTER) return -1;
        int bi = Math.Min(bandOf(r), BC - 1); Band B = bands[bi]; int f = FaceOf(x, y, z);
        V3 n = N[f], u = U[f], v = V[f];
        double pn = x * n.X + y * n.Y + z * n.Z;
        double aU = Math.Atan2(x * u.X + y * u.Y + z * u.Z, pn), aV = Math.Atan2(x * v.X + y * v.Y + z * v.Z, pn);
        PZ = (int)M.Clamp(Math.Floor((aU + PI / 4) / B.d), 0, B.cols - 1);
        PX = (int)M.Clamp(Math.Floor((aV + PI / 4) / B.d), 0, B.cols - 1);
        PY = (int)M.Clamp(Math.Floor(r - B.inner), 0, (double)B.rows * CS - 1);
        PF = f; return bi;
    }
    // chunk key: 7 bits band*6+face, 19 bits cx, 18 bits cy, 19 bits cz (enough for radius 10,000,000)
    public static long ck(int bi, int f, int cx, int cy, int cz) => ((((long)(bi * 6 + f) * 524288 + cx) * 262144 + cy) * 524288) + cz;
    // 64-bit mixing key for per-block tables (edits, water, cave nodes): block coordinates are too big to pack exactly
    public static long Mix(int bf, int x, int y, int z)
    {
        unchecked
        {
            ulong h = (ulong)(bf + 1) * 0x9E3779B97F4A7C15UL;
            h = (h ^ (ulong)(uint)x) * 0xC2B2AE3D27D4EB4FUL; h ^= h >> 29;
            h = (h ^ (ulong)(uint)z) * 0x165667B19E3779F9UL; h ^= h >> 32;
            h = (h ^ (ulong)(uint)y) * 0xD6E8FEB86659FD93UL; h ^= h >> 31;
            return (long)h;
        }
    }
    public static long ek() => Mix(NB * 6 + NF, NX, NY, NZ);
    public static long ekOf(int b, int f, int x, int y, int z) => Mix(b * 6 + f, x, y, z);

    /// <summary>Normalise block coords: wrap across face seams, move into the proper band when above/below. Result in NB..NZ.</summary>
    public static bool Norm(int bi, int f, int bx, int by, int bz)
    {
        NCV = 0; Band B = bands[bi];
        if (!Wrap(B, f, bx, bz)) return false;
        f = WF; bx = WX; bz = WZ;
        if (by < 0 || by >= B.rows)
        {
            V3 t = Gp(B, f, bz + .5, bx + .5, B.inner + by + .5);
            bi = P2b(t.X, t.Y, t.Z);
            if (bi < 0) { NCV = t.Len2 < CORE * CORE ? CoreAt(t.X, t.Y, t.Z) : 0; return false; }
            f = PF; bx = PX; by = PY; bz = PZ;
        }
        NB = bi; NF = f; NX = bx; NY = by; NZ = bz; return true;
    }
    public static int Blk(int bi, int f, int bx, int by, int bz)
    {
        if (!Norm(bi, f, bx, by, bz)) return NCV;
        long k = ck(NB, NF, NX >> 5, NY >> 5, NZ >> 5); Chunk ch;
        if (k == blkKey && blkCh != null) ch = blkCh; // one-entry cache: consecutive lookups almost always hit the same chunk
        else { chunkMap.TryGetValue(k, out ch); if (ch != null) { blkKey = k; blkCh = ch; } }
        if (ch != null && ch.d != null) return ch.d[(NX & 31) + 32 * ((NZ & 31) + 32 * (NY & 31))];
        byte e; bool he; lock (EditLock) he = edits.TryGetValue(ek(), out e);
        if (he) return e;
        return Gen(bands[NB], NF, NX, NY, NZ);
    }

    // ---------- Cartesian core: inside radius CORE the world is a plain grid of unit cubes (32^3 chunks), no polar bands ----------
    // a cell is stone when its centre is within CORE+.9 (so every point with r<CORE lies in a solid cell); player edits live in coreEd
    static readonly double CR2 = (CORE + .9) * (CORE + .9);
    public static readonly Dictionary<int, byte> coreEd = new();
    public static int cEk(int i, int j, int k) => ((i + 128) * 256 + (j + 128)) * 256 + (k + 128);
    public static long cKey(int a, int b, int c) => -(1 + (((a + 4) * 8 + (b + 4)) * 8 + (c + 4)));
    public static int CoreCell(int i, int j, int k)
    {
        if (i < -128 || i > 127 || j < -128 || j > 127 || k < -128 || k > 127) return 0;
        if (coreEd.TryGetValue(cEk(i, j, k), out byte e)) return e;
        double x = i + .5, y = j + .5, z = k + .5;
        return x * x + y * y + z * z < CR2 ? 66 : 0;
    }
    public static int CoreAt(double x, double y, double z) => CoreCell((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z));
    static readonly int[][] C7 = { new[] { 0, 0, 0 }, new[] { 1, 0, 0 }, new[] { -1, 0, 0 }, new[] { 0, 1, 0 }, new[] { 0, -1, 0 }, new[] { 0, 0, 1 }, new[] { 0, 0, -1 } };
    public static void CoreSet(double x, double y, double z, int id)
    {
        int i = (int)Math.Floor(x), j = (int)Math.Floor(y), k = (int)Math.Floor(z);
        if (CoreCell(i, j, k) == id) return;
        coreEd[cEk(i, j, k)] = (byte)id;
        foreach (var o in C7)
        {
            int a = i + o[0], b = j + o[1], c = k + o[2];
            if (chunks.TryGetValue(cKey(a >> 5, b >> 5, c >> 5), out var ch))
            {
                ch.dirty = true;
                if (ch.d != null && o[0] == 0 && o[1] == 0 && o[2] == 0) { ch.d[(i & 31) + 32 * ((k & 31) + 32 * (j & 31))] = (byte)id; ch.empty = false; }
            }
        }
    }
    public static int GetAt(double x, double y, double z)
    {
        int bi = P2b(x, y, z);
        return bi < 0 ? (x * x + y * y + z * z < CORE * CORE ? CoreAt(x, y, z) : 0) : Blk(bi, PF, PX, PY, PZ);
    }
    public static void SetAt(double x, double y, double z, int id)
    {
        if (x * x + y * y + z * z < CORE * CORE) { CoreSet(x, y, z, id); return; }
        int bi = P2b(x, y, z); if (bi < 0) return;
        SetCell(bi, PF, PX, PY, PZ, id);
    }
    public static void SetCell(int bi, int f, int bx, int by, int bz, int id, int lvl = 0)
    {
        if (!Norm(bi, f, bx, by, bz)) return;
        bi = NB; f = NF; bx = NX; by = NY; bz = NZ;
        long kk = ek();
        long k = ck(bi, f, bx >> 5, by >> 5, bz >> 5);
        lock (EditLock) { edits[kk] = (byte)id; ecount[k] = (ecount.TryGetValue(k, out int ec) ? ec : 0) + 1; }
        if (!chunks.ContainsKey(k)) lastScan = new V3(1e9, 0, 0); // chunk was skipped as deep solid: make the loader create it now
        if (chunks.TryGetValue(k, out var ch)) { Ensure(ch); ch.solid = false; ch.d[(bx & 31) + 32 * ((bz & 31) + 32 * (by & 31))] = (byte)id; ch.dirty = true; }
        for (int d = 0; d < 6; d++)
            if (Norm(bi, f, bx + DX[d] * 8, by + DY[d] * 8, bz + DZ[d] * 8))
            {
                long k2 = ck(NB, NF, NX >> 5, NY >> 5, NZ >> 5);
                if (!ecount.ContainsKey(k2)) { lock (EditLock) ecount[k2] = 0; if (!chunks.ContainsKey(k2)) lastScan = new V3(1e9, 0, 0); }
                if (chunks.TryGetValue(k2, out var nc)) nc.dirty = true;
            }
        lock (WL) { if (id == 9 && lvl > 0) WL[kk] = lvl; else if (WL.Count > 0) WL.Remove(kk); }
        Wake(bi, f, bx, by, bz);
    }
    public static string BiomeName(V3 p)
    {
        double r = p.Len; Terrain(p.X / r, p.Y / r, p.Z / r, 0); return BNM[gB];
    }
}
