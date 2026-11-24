using System.Collections.Concurrent;

namespace CubePlanet;

/// <summary>
/// How fast water moves through each moving water cell, measured from the simulation's moves: units per second along the block grid
/// (x, z) and downwards (fall), smoothed over about <see cref="Tau"/> seconds. Still water has no record (nothing stored for oceans / pools).
/// Turbines read it, the mesher draws it (direction + strength in the water vertices, decoded in terrain.vert), and the HUD shows it.
/// Each record keeps a quantized snapshot (8 directions, strength 0..7, fall 0..7); when the snapshot changes the chunk is re-meshed in the
/// background, so a steady stream costs no meshing at all.
/// </summary>
public static class WaterFlow
{
    public const double Tau = 1.5;
    public sealed class Rec
    {
        public double fx, fz, fall, t; // smoothed units/s at time t
        public int b, f, x, y, z;
        public volatile int code;     // snapshot for the mesher: bits 0..2 direction (grid angle / 45 degrees), 3..5 strength, 6..8 fall strength
    }
    // read by the mesher on worker threads, written on the main thread
    public static readonly ConcurrentDictionary<long, Rec> Cells = new();
    public static double Now; // simulation clock (seconds)

    /// <summary>units of water moved from cell 'from' by (dx, dy, dz): counted for both cells.</summary>
    public static void Move(in WC from, int dx, int dy, int dz, int units)
    {
        if (units <= 0 || !from.ok) return;
        Add(from.k, from.b, from.F, from.X, from.Y, from.Z, dx, dy, dz, units);
        if (Norm(from.b, from.F, from.X + dx, from.Y + dy, from.Z + dz)) Add(ek(), NB, NF, NX, NY, NZ, dx, dy, dz, units);
    }
    /// <summary>Flow through one cell only (machine outlets / intakes).</summary>
    public static void AddCell(long k, int b, int f, int x, int y, int z, int dx, int dy, int dz, double units) => Add(k, b, f, x, y, z, dx, dy, dz, units);

    static void Add(long k, int b, int f, int x, int y, int z, int dx, int dy, int dz, double units)
    {
        var r = Cells.GetOrAdd(k, _ => new Rec { b = b, f = f, x = x, y = y, z = z, t = Now });
        Decay(r);
        double a = units / Tau; r.fx += dx * a; r.fz += dz * a; if (dy < 0) r.fall += a;
    }
    static void Decay(Rec r)
    {
        if (Now <= r.t) return;
        double k = Math.Exp(-(Now - r.t) / Tau); r.fx *= k; r.fz *= k; r.fall *= k; r.t = Now;
    }

    /// <summary>Flow through a cell now: (x, z, fall) in units per second.</summary>
    public static (double x, double z, double fall) At(long k)
    {
        if (!Cells.TryGetValue(k, out var r)) return (0, 0, 0);
        double d = Now > r.t ? Math.Exp(-(Now - r.t) / Tau) : 1; return (r.fx * d, r.fz * d, r.fall * d);
    }

    /// <summary>Strength 0..7 from units per second (log scale: 0.5 u/s = 1 ... 40+ u/s = 7).</summary>
    public static int Strength(double ups) => ups < .35 ? 0 : Math.Clamp((int)Math.Round(Math.Log2(1 + ups) * 1.35), 1, 7);
    public static int Code(double fx, double fz, double fall)
    {
        double m = Math.Sqrt(fx * fx + fz * fz); int s = Strength(m), fs = Strength(fall);
        int dir = s == 0 ? 0 : ((int)Math.Round(Math.Atan2(fz, fx) / (Math.PI / 4)) + 8) & 7;
        return dir | s << 3 | fs << 6;
    }

    static double sweepT;
    /// <summary>Advances the clock; twice a second refreshes the snapshots, re-meshes chunks whose drawn flow changed, forgets stopped cells.</summary>
    public static void Tick(double dt)
    {
        Now += dt; sweepT += dt; if (sweepT < .5) return; sweepT = 0;
        foreach (var (k, r) in Cells)
        {
            Decay(r);
            int c = Code(r.fx, r.fz, r.fall);
            if (c != r.code)
            {
                r.code = c;
                if (chunks.TryGetValue(ck(r.b, r.f, r.x >> 5, r.y >> 5, r.z >> 5), out var ch)) { ch.ver++; ch.relight = true; }
            }
            if (c == 0 && Math.Abs(r.fx) + Math.Abs(r.fz) + r.fall < .05) Cells.TryRemove(k, out _);
        }
    }
    /// <summary>Drawn flow of a cell (the snapshot code, 0 = still).</summary>
    public static int CodeAt(long k) => Cells.TryGetValue(k, out var r) ? r.code : 0;
    public static void Clear() { Cells.Clear(); }
}
