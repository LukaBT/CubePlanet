namespace CubePlanet;

/// <summary>
/// Rivers and waterfalls, applied to every terrain column by <see cref="TerrainGen.Terrain"/> (not to the low-detail LOD terrain).
/// <list type="bullet">
/// <item>Channels follow the zero line of a large, domain-warped noise (a river every few hundred blocks), in about 70 % of the land,
/// 2 .. 8 blocks wide (wider in lowlands), none on high mountains or in the sea.</item>
/// <item>The water surface S comes from the smoothed terrain (the same terrain without its detail octaves) 4 blocks lower, rounded down
/// to steps of <see cref="Step"/> blocks: constant along a stretch, so river water is level and at rest when generated; where it drops a step
/// the upper stretch ends in a vertical water face: a waterfall. Near the coast S reaches sea level and the river joins the sea.</item>
/// <item>The column is carved: a U-shaped gravel bed in the channel, a valley sloping up on both banks, and a levee up to S + 1 where the
/// ground would be lower than the river (so it never spills sideways). Caves stay away from rivers (<see cref="Strata.Cavable"/>).</item>
/// <item>Results per column (thread-static, like the rest of the terrain): <see cref="gRS"/> river surface (NaN = no river water),
/// <see cref="gRN"/> surface of a river within the bank zone (no caves above it), <see cref="gRD"/> downstream direction (world) and
/// <see cref="gRW"/> flow strength 0..7 for drawing.</item>
/// </list>
/// The water simulation keeps river water at its natural level when it is disturbed (WaterSim: topped up from / drained into the water
/// cycle's reservoir), so a river keeps flowing and can feed turbines, while its total water stays accounted for.
/// </summary>
public static class Rivers
{
    public const double Step = 3, Spacing = 320, Bank = 7;
    [ThreadStatic] public static double gRS, gRN; [ThreadStatic] public static V3 gRD; [ThreadStatic] public static int gRW;
    [ThreadStatic] static bool busy;

    static double N(double x, double y, double z, int k, double f) { var o = OFF[k]; return Noise3(x * f + o[0], y * f + o[1], z * f + o[2]); }
    // the channel noise (warped so rivers meander); its zero line is the river's centre
    static double Chan(double x, double y, double z)
    {
        double wx = N(x, y, z, 44, 1 / 260.0) * 70, wy = N(x, y, z, 45, 1 / 260.0) * 70, wz = N(x, y, z, 46, 1 / 260.0) * 70;
        return N(x + wx, y + wy, z + wz, 43, 1 / Spacing);
    }
    /// <summary>Smoothed terrain height (no detail octaves) at a point on the sphere of radius R.</summary>
    static double Smooth(double x, double y, double z)
    {
        double sh = gH, ss = gS; int st = gT, sf = gF, sfd = gFD, si = gI, sb = gB;
        busy = true; try { double r = Math.Sqrt(x * x + y * y + z * z); Terrain(x / r, y / r, z / r, 48); return gH; }
        finally { busy = false; gH = sh; gS = ss; gT = st; gF = sf; gFD = sfd; gI = si; gB = sb; }
    }

    /// <summary>Carves the column at (x, y, z) (a point on the sphere of radius R) whose terrain results are in the g* slots.</summary>
    public static void Apply(double x, double y, double z)
    {
        gRS = gRN = double.NaN; gRW = 0;
        if (busy || gH < -1 || gH > 70 * HS) return;
        double reg = N(x, y, z, 47, 1 / 1500.0), regF = M.Sm(-.4, -.15, reg);
        if (regF <= 0) return;
        double n = Chan(x, y, z);
        double r = Math.Sqrt(x * x + y * y + z * z); V3 up = new(x / r, y / r, z / r);
        double gx = Chan(x + 1, y, z) - n, gy = Chan(x, y + 1, z) - n, gz = Chan(x, y, z + 1) - n, gu = gx * up.X + gy * up.Y + gz * up.Z;
        V3 g = new(gx - up.X * gu, gy - up.Y * gu, gz - up.Z * gu); double gl = g.Len; if (gl < 1e-9) return;
        double d = Math.Abs(n) / gl; // blocks to the river's centre line
        if (d > 4 * regF + Bank) return;
        // level and width come from the nearest point of the centre line, so they are the same across the river (a waterfall is a straight
        // line across it, and the banks next to a stretch get that stretch's level)
        V3 gn = g * (1 / gl), t = M.Nrm(M.Cross(up, gn)), P = new(x, y, z), pc = P - gn * (n / gl); pc = pc * (r / pc.Len);
        double hl = Smooth(pc.X, pc.Y, pc.Z), S = Level(hl);
        double w = (1 + 3 * M.Sm(45 * HS, 5, hl)) * regF; // half width
        if (w < .6 || d > w + Bank) return;
        if (S > gH + 6) return; // the smoothed ground is far above this spot (a deep local hole): no river here
        gRN = S;
        if (d <= w)
        {
            double depth = 1 + 2 * (1 - (d / w) * (d / w)) * Math.Min(1, w / 2); // U-shaped bed, 1 .. 3 deep
            gH = S - depth; gRS = S; gT = gF = Gravel; gFD = 2; gI = 0;
            // downstream: along the channel, towards the lower smoothed ground
            double a = Smooth(pc.X + t.X * 10, pc.Y + t.Y * 10, pc.Z + t.Z * 10), b = Smooth(pc.X - t.X * 10, pc.Y - t.Y * 10, pc.Z - t.Z * 10);
            gRD = a <= b ? t : t * -1; gRW = Math.Clamp((int)Math.Round(2 + w * .8 + Math.Min(2, Math.Abs(a - b) * .3)), 1, 7);
        }
        else
        {
            // valley sides: from S + 1 at the water's edge blending into the untouched ground at the bank zone's edge; a levee up to S + 1
            // where the ground is lower than the river
            // right next to the water, a bank also holds the higher stretch of a waterfall 2 blocks up- or downstream
            double lev = S;
            if (d - w < 2.5) { lev = Math.Max(lev, Level(Smooth(pc.X + t.X * 2, pc.Y + t.Y * 2, pc.Z + t.Z * 2))); lev = Math.Max(lev, Level(Smooth(pc.X - t.X * 2, pc.Y - t.Y * 2, pc.Z - t.Z * 2))); }
            double k = (d - w) / Bank;
            gH = gH > S + 1 ? S + 1 + (gH - S - 1) * k * k : S + 1;
            gH = Math.Max(gH, lev + 1);
        }
    }
    /// <summary>River surface for a smoothed ground height: 4 blocks lower, in steps of <see cref="Step"/>, never below sea level.</summary>
    static double Level(double hl) => Math.Max(0, Step * Math.Floor((hl - 4) / Step));
}
