namespace CubePlanet;

/// <summary>
/// Cave water, like Minecraft's aquifers: the planet surface is split into regions (jittered 3D cells of <see cref="Cell"/> blocks, Voronoi
/// on the sphere, so cube-face seams and bands don't matter). Each region is dry or has ONE water level, so a flooded cave is level and
/// already at rest when generated: the water simulation has nothing to do until the player changes something. Where a cave crosses from
/// one region into another with a different level, a stone barrier (<see cref="Barrier"/> blocks around the border, up to the higher of the
/// two levels) keeps the higher water from standing next to air or lower water.
/// </summary>
public static class Aquifers
{
    const double Cell = 40, Barrier = 3, Dry = -1e9;

    /// <summary>Water level of a column (relative to sea level; very low = dry) and the height up to which cave air there stays stone (border barrier).</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)] public static (double level, double barrierTop) Column(int f, double a, double b)
    {
        V3 p = GpA(f, a, b, R);
        int gx = (int)Math.Floor(p.X / Cell), gy = (int)Math.Floor(p.Y / Cell), gz = (int)Math.Floor(p.Z / Cell);
        // nearest region centre
        Span<double> cx = stackalloc double[27], cy = stackalloc double[27], cz = stackalloc double[27], lv = stackalloc double[27], d2 = stackalloc double[27];
        int best = 0;
        for (int k = 0; k < 27; k++)
        {
            int x = gx + k % 3 - 1, y = gy + k / 3 % 3 - 1, z = gz + k / 9 - 1;
            ulong h = Hash(x, y, z);
            cx[k] = (x + U(h, 0)) * Cell; cy[k] = (y + U(h, 1)) * Cell; cz[k] = (z + U(h, 2)) * Cell;
            // 25 % of regions are dry; the others hold water at -54 .. -2 below sea level
            lv[k] = U(h, 3) < .25 ? Dry : -54 + 52 * U(h, 4);
            double dx = p.X - cx[k], dy = p.Y - cy[k], dz = p.Z - cz[k]; d2[k] = dx * dx + dy * dy + dz * dz;
            if (d2[k] < d2[best]) best = k;
        }
        double level = lv[best], top = Dry;
        for (int k = 0; k < 27; k++)
        {
            if (k == best || lv[k] == level) continue;
            // distance to the border (bisector plane) between this column's region and region k
            double ex = cx[k] - cx[best], ey = cy[k] - cy[best], ez = cz[k] - cz[best], len = Math.Sqrt(ex * ex + ey * ey + ez * ez);
            if ((d2[k] - d2[best]) / (2 * len) < Barrier) top = Math.Max(top, Math.Max(level, lv[k]));
        }
        return (level, top);
    }

    /// <summary>What a cave cell (density says air) becomes: lava at the bottom, stone in a barrier, water below the level, else air.</summary>
    public static int CaveFill(double rel, int stone, (double level, double barrierTop) aq) =>
        rel < -(CAVB - 20) ? Lava : rel < aq.barrierTop ? stone : rel < aq.level ? Water : 0;

    static ulong Hash(int x, int y, int z)
    {
        var o = OFF[26];
        ulong h = (ulong)(uint)x * 0x9E3779B97F4A7C15UL ^ (ulong)(uint)y * 0xC2B2AE3D27D4EB4FUL ^ (ulong)(uint)z * 0x165667B19E3779F9UL ^ (ulong)(o[0] * 1e6 + o[1] * 1e3 + o[2]);
        h ^= h >> 33; h *= 0xFF51AFD7ED558CCDUL; h ^= h >> 33; h *= 0xC4CEB9FE1A85EC53UL; h ^= h >> 33; return h;
    }
    // the i-th 0..1 value from a hash (12 bits each)
    static double U(ulong h, int i) => ((h >> (i * 12)) & 4095) / 4096.0;
}
