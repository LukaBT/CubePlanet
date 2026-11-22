namespace CubePlanet;

/// <summary>Ore veins placed into freshly generated chunk data.</summary>
public static class Ores
{
    // ore veins: (block id, min depth, max depth below the local surface, veins per 32^3 chunk, vein size, mountains only). Deepslate variant = id + 9.
    internal static readonly (int id, int dpMin, int dpMax, double tries, int size, bool mtn)[] ORES =
    {
        (58, 4, 200, 3.4, 9, false),   // coal
        (64, 6, 120, 2.2, 8, false),   // copper
        (59, 8, 240, 2.8, 6, false),   // iron
        (60, 30, 280, 1.1, 5, false),  // gold
        (62, 55, 500, 1.3, 5, false),  // redstone
        (63, 22, 320, 1.0, 5, false),  // lapis
        (61, 70, 700, 0.9, 4, false),  // diamond
        (65, 3, 70, 1.6, 2, true),     // emerald (mountains only)
    };
    internal static void ApplyOres(Chunk ch, byte[] d, Col c, double r0, int f)
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
                if (o.mtn && c.b[i] != 11 && c.b[i] != 13 && c.b[i] != 20) continue;
                int cx = x, cy = y, cz = z;
                for (int k = 0; k < o.size; k++)
                {
                    int j = cx + CS * (cz + CS * cy), b = d[j];
                    if (b == 2) d[j] = (byte)o.id; else if (b == 66) d[j] = (byte)(o.id + 9);
                    cx += (int)(Rn() * 3) - 1; cy += (int)(Rn() * 3) - 1; cz += (int)(Rn() * 3) - 1;
                    if (cx < 0 || cx >= CS || cy < 0 || cy >= CS || cz < 0 || cz >= CS) break;
                }
            }
        }
    }
}
