using System.Numerics;

namespace CubePlanet;

/// <summary>World state shared by every system: planet radius and height scaling, bands, loaded chunks, player edits; world creation and spawn.</summary>
public static class World
{
    public const int CS = 32, CORE = 96, FC0 = 6;
    public const double PI = Math.PI;
    public static double R;
    public static int BC, OUTER, RD = 6, MAXL = 8, MINL = 2;
    public static List<Band> bands = new();
    public static Dictionary<long, Chunk> chunks = new();   // main thread (iteration)
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<long, Chunk> chunkMap = new(); // same content, for lookups from worker threads
    public static void AddChunk(Chunk ch) { chunks[ch.key] = ch; chunkMap[ch.key] = ch; }
    public static Dictionary<long, ushort> edits = new();
    public static Dictionary<long, int> ecount = new();
    public static double HS = 1, HMAX = 100, MH = 70, OD = 24, DEP = 48, CAVB = 100;
    /// <summary>World-size scaling: biome regions (BSC), landforms such as mountains/rivers/ocean relief (LSC) and continent wavelength (LCN, blocks).</summary>
    public static double BSC = 1, LSC = 1, LCN = 900;
    public static int SEEDV = 1;
    public static int bandOf(double r) => 31 - BitOperations.LeadingZeroCount((uint)Math.Floor(r / CORE));

    public static bool Started;

    // ---------- world creation ----------
    public static void InitWorld(double radius, int seed)
    {
        InitWorldData(radius, seed); MakeAtmosphereResources(); SetHour(9); C.d = R * 3;
        Started = true; P.ready = true; if (P.on) ToPlayer();
    }
    /// <summary>World parameters, bands and spawn point only (no GPU): shared by InitWorld and the headless world fingerprint.</summary>
    public static void InitWorldData(double radius, int seed)
    {
        R = Math.Round(radius); SeedNoise(seed); SEEDV = seed; ClearFeatCaches();
        double rr0 = R / 1200; BSC = Math.Max(1, Math.Sqrt(rr0)); LSC = Math.Max(1, Math.Pow(rr0, .3)); LCN = R <= 1000 ? R * .9 : 900 * Math.Pow(R / 1000, .85);
        HS = R <= 1200 ? Math.Max(.4, R / 1000) : Math.Min(2.5, 1.2 * Math.Pow(rr0, .2)); OD = 24 * Math.Max(1, HS); HMAX = Math.Ceiling(96 * HS + 12); MH = 70 * HS; HA = Math.Acos(R / (R + HMAX)) + .02; DEP = Math.Max(48, R - CORE);
        BC = bandOf(R + HMAX + 2) + 1; OUTER = CORE << BC; bands.Clear();
        for (int b = 0; b < BC; b++) { int fc = FC0 << b, cols = fc * CS; bands.Add(new Band { b = b, fc = fc, cols = cols, rows = (CORE / CS) * (1 << b) * CS, inner = CORE << b, d = PI / 2 / cols }); }
        lodDelta = bands[bandOf(R)].d; spd = Math.Max(1, R / 1200); Spawn();
    }
    internal static void Spawn()
    {
        int n0 = 900; double ga = PI * (3 - Math.Sqrt(5)); double[] best = null, any = null;
        for (int i = 0; i < n0; i++)
        {
            double y = 1 - 2 * (i + .5) / n0, rr = Math.Sqrt(1 - y * y), th = ga * i, x = Math.Cos(th) * rr, z = Math.Sin(th) * rr;
            Terrain(x, y, z, 200);
            if (gH > 3 && gH < 14 && gT == Grass && Math.Abs(y) < .6) { best = new[] { x, y, z, gH }; break; }
            if (any == null && gH > 2) any = new[] { x, y, z, gH };
        }
        var s = best ?? any ?? new[] { 0, 1, 0, 0.0 };
        C.lat = Math.Asin(s[1]); C.lon = Math.Atan2(s[0], s[2]); SetOrbit(false);
        double p = R + Math.Max(s[3], 0) + (P.on ? 4 : 30); C.x = s[0] * p; C.y = s[1] * p; C.z = s[2] * p;
    }
}
