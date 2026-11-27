namespace CubePlanet;

/// <summary>Ore veins placed into freshly generated chunk data.</summary>
public static class Ores
{
    // ore veins: (ore in stone, ore in deepslate, min depth, max depth below the local surface, veins per 32^3 chunk, vein size, mountains only)
    internal static readonly (int id, int deep, int dpMin, int dpMax, double tries, int size, bool mtn)[] ORES =
    {
        (CoalOre, DeepslateCoalOre, 4, 200, 3.4, 9, false),
        (CopperOre, DeepslateCopperOre, 6, 120, 2.2, 8, false),
        (IronOre, DeepslateIronOre, 8, 240, 2.8, 6, false),
        (GoldOre, DeepslateGoldOre, 30, 280, 1.1, 5, false),
        (RedstoneOre, DeepslateRedstoneOre, 55, 500, 1.3, 5, false),
        (LapisOre, DeepslateLapisOre, 22, 320, 1.0, 5, false),
        (DiamondOre, DeepslateDiamondOre, 70, 700, 0.9, 4, false),
        (EmeraldOre, DeepslateEmeraldOre, 3, 70, 1.6, 2, true),     // mountains only
    };
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)] internal static void ApplyOres(Chunk ch, ushort[] d, Col c, double r0, int f)
    {
        uint st = unchecked((uint)(HH(ch.band * 6 + f, ch.cx, ch.cy * 3 + 1, ch.cz * 5 + 2) * 4294967296.0)) | 1u;
        double Rn() { st ^= st << 13; st ^= st >> 17; st ^= st << 5; return st / 4294967296.0; }
        foreach (var o in ORES)
        {
            int n = (int)o.tries; if (Rn() < o.tries - n) n++;
            for (int t = 0; t < n; t++)
            {
                int x = (int)(Rn() * CS), y = (int)(Rn() * CS), z = (int)(Rn() * CS), i = x + CS * z; double dp = R + c.h[i] - (r0 + y + .5);
                if (dp < o.dpMin || dp > o.dpMax) continue;
                if (o.mtn && c.b[i] != 11 && c.b[i] != 13 && c.b[i] != 20) continue; // biomes: mountains, snowy peaks, glacier peaks
                int cx = x, cy = y, cz = z;
                for (int k = 0; k < o.size; k++)
                {
                    int j = cx + CS * (cz + CS * cy), b = d[j];
                    if (b == Stone) d[j] = (ushort)o.id; else if (b == Deepslate) d[j] = (ushort)o.deep;
                    cx += (int)(Rn() * 3) - 1; cy += (int)(Rn() * 3) - 1; cz += (int)(Rn() * 3) - 1;
                    if (cx < 0 || cx >= CS || cy < 0 || cy >= CS || cz < 0 || cz >= CS) break;
                }
            }
        }
    }
    // ---- oil reservoirs: flattened lenses of oil in stone, 35..90 blocks below the local surface under a cap of oil shale. One chance per
    // 96 x 96 block cell of the band's grid (30 %), so they line up across chunks. Plain natural oil: it only moves once something reaches it
    // (a pump's pipe, a tunnel), like any liquid.
    const int OilGrid = 96;
    internal static bool OilSite(int band, int f, int gx, int gz, out double cx, out double cz, out double cd, out double rh, out double rv)
    {
        double h = Features.HH(band * 6 + f + 7777, gx, 911, gz); cx = cz = cd = rh = rv = 0;
        if (h > .3) return false;
        cx = (gx + .15 + .7 * Features.HH(band * 6 + f, gx, 912, gz)) * OilGrid; cz = (gz + .15 + .7 * Features.HH(band * 6 + f, gx, 913, gz)) * OilGrid;
        cd = 35 + 55 * Features.HH(band * 6 + f, gx, 914, gz); rh = 5 + 6 * Features.HH(band * 6 + f, gx, 915, gz); rv = 2.5 + 2 * Features.HH(band * 6 + f, gx, 916, gz);
        return true;
    }
    /// <summary>Oil and its oil-shale cap where a reservoir lies in this chunk (only replaces stone, deepslate and ores). True if any was placed.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)] internal static bool ApplyOil(Chunk ch, ushort[] d, Col c, double r0, int f)
    {
        int x0 = ch.cx * CS, z0 = ch.cz * CS; bool any = false;
        for (int gz = (int)Math.Floor((z0 - 12.0) / OilGrid); gz <= (int)Math.Floor((z0 + CS + 12.0) / OilGrid); gz++)
            for (int gx = (int)Math.Floor((x0 - 12.0) / OilGrid); gx <= (int)Math.Floor((x0 + CS + 12.0) / OilGrid); gx++)
            {
                if (!OilSite(ch.band, f, gx, gz, out double cx, out double cz, out double cd, out double rh, out double rv)) continue;
                if (cx + rh * 1.4 < x0 || cx - rh * 1.4 > x0 + CS || cz + rh * 1.4 < z0 || cz - rh * 1.4 > z0 + CS) continue;
                for (int z = 0; z < CS; z++)
                    for (int x = 0; x < CS; x++)
                    {
                        double ux = (x0 + x + .5 - cx) / rh, uz = (z0 + z + .5 - cz) / rh, hq = ux * ux + uz * uz; if (hq > 1.9) continue;
                        int i = x + CS * z; double surf = R + c.h[i];
                        for (int y = 0; y < CS; y++)
                        {
                            double dp = surf - (r0 + y + .5), uy = (dp - cd) / rv, q = hq + uy * uy; if (q > 1.9) continue;
                            int j = x + CS * (z + CS * y), b = d[j];
                            if (b != Stone && b != Deepslate && !IsOre(b)) continue;
                            if (q < 1) { d[j] = (ushort)Oil; any = true; }
                            else if (uy < 0) { d[j] = (ushort)OilShale; any = true; } // the cap rock above the oil
                        }
                    }
            }
        return any;
    }
    static bool IsOre(int b) { foreach (var o in ORES) if (b == o.id || b == o.deep) return true; return false; }
}
