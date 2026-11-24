namespace CubePlanet;

/// <summary>A block in a rotation network: the sides (offsets) where shafts connect to it.</summary>
public interface IKinetic { (int x, int y, int z)[] Ports(BlockEntity e); }

/// <summary>
/// Rotation networks: wind turbines (sources), shafts (they carry rotation along their axis) and generators (they turn it into energy),
/// joined where a block's port faces another block's port. Every 0.1 s each network adds up its sources' power, shares it between its
/// generators (each takes up to <see cref="GeneratorBehavior.MaxIn"/>), and turns: free it spins fastest, load slows it down. All parts of a
/// network share one angle (block entity numbers "angle", "speed"), which <see cref="MovingParts"/> draws.
/// </summary>
public static class Kinetics
{
    static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    static double lastTick;
    public static double Time => clock.Elapsed.TotalSeconds;
    /// <summary>Seconds since the last network update (the renderer turns parts on by speed x this).</summary>
    public static double SinceTick => Math.Clamp(Time - lastTick, 0, .25);
    public static IKinetic Of(BlockEntity e) => e == null ? null : BEH[e.BlockId] as IKinetic;
    /// <summary>Wind strength 0.2 .. 1.2, gusting slowly over time.</summary>
    public static double Wind(double t) => Math.Clamp(.7 + .3 * Math.Sin(t / 40) + .15 * Math.Sin(t / 7.3 + 1), .2, 1.2);
    /// <summary>Game time used by the wind (seconds of ticks).</summary>
    public static double Clock;
    public static (int, int, int) Dir(BlockEntity e) => WaterMachines.Dir(e);

    public static void Tick(double dt)
    {
        lastTick = Time; Clock += dt;
        var done = new HashSet<long>(); var q = new Queue<BlockEntity>(); var net = new List<BlockEntity>();
        foreach (var e in BlockStates.entities.Values.ToArray())
        {
            if (Of(e) == null || done.Contains(e.Pos.Key) || !e.Pos.Loaded) continue;
            net.Clear(); q.Clear(); q.Enqueue(e); done.Add(e.Pos.Key);
            while (q.Count > 0 && net.Count < 2048)
            {
                var a = q.Dequeue(); net.Add(a);
                foreach (var (dx, dy, dz) in Of(a).Ports(a))
                {
                    if (!a.Pos.TryOffset(dx, dy, dz, out var np) || done.Contains(np.Key) || !np.Loaded) continue;
                    if (!BlockStates.entities.TryGetValue(np.Key, out var b) || Of(b) == null) continue;
                    if (!Of(b).Ports(b).Contains((-dx, -dy, -dz))) continue; // its port must face back
                    done.Add(np.Key); q.Enqueue(b);
                }
            }
            Run(net, dt);
        }
    }

    static void Run(List<BlockEntity> net, double dt)
    {
        double src = 0, free = 0; int turbines = 0; var gens = new List<BlockEntity>();
        foreach (var e in net)
            if (BEH[e.BlockId] is WindTurbineBehavior wt) { double p = wt.Power(e); src += p; turbines++; if (p > 0) free += 2.5 * Wind(Clock); }
            else if (BEH[e.BlockId] is GeneratorBehavior) gens.Add(e);
        double used = 0;
        foreach (var g in gens) { double inp = Math.Min(GeneratorBehavior.MaxIn, src / gens.Count); g.SetNum("input", inp); used += inp; }
        double w0 = turbines > 0 ? free / turbines : 0, load = src > 0 ? used / src : 0;
        // spin up / run down smoothly; free it turns fastest, load slows it; every part continues from the network's first part's angle
        double speed = net[0].Num("speed"), target = w0 * (1 - .55 * load);
        speed += (target - speed) * Math.Min(1, dt * 1.5); if (Math.Abs(speed) < 1e-3) speed = 0;
        double angle = (net[0].Num("angle") + speed * dt) % (2 * Math.PI);
        foreach (var e in net) { e.SetNum("angle", angle); e.SetNum("speed", speed); }
    }
}

/// <summary>
/// Wind turbine: a hub whose blades (a moving part, 5 blocks across) turn in front of it; rotation comes out of its back. Power =
/// 10 E/s x wind (Kinetics.Wind, gusting) x height above sea level (x0.5 at sea level .. x2 at 60 blocks up). The 5 x 5 area of the
/// blades must be free (air or plants), else it stops (HUD: BLOCKED).
/// </summary>
public sealed class WindTurbineBehavior : BlockBehavior, IKinetic
{
    public const double BasePower = 10;
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public (int x, int y, int z)[] Ports(BlockEntity e) { var (x, y, z) = Kinetics.Dir(e); return new[] { (-x, -y, -z) }; }
    public bool Blocked(BlockEntity e)
    {
        var (fx, fy, fz) = Kinetics.Dir(e);
        // the blade disc: the plane in front of the hub, 2 cells each way across the facing
        (int, int, int) u = fy == 0 ? (0, 1, 0) : (1, 0, 0), v = fx != 0 ? (0, 0, 1) : fz != 0 ? (1, 0, 0) : (0, 0, 1);
        for (int a = -2; a <= 2; a++) for (int b = -2; b <= 2; b++)
            {
                int dx = fx + u.Item1 * a + v.Item1 * b, dy = fy + u.Item2 * a + v.Item2 * b, dz = fz + u.Item3 * a + v.Item3 * b;
                if (!e.Pos.TryOffset(dx, dy, dz, out var c) || !c.Loaded) continue;
                int id = c.Id; if (id != 0 && SPC[id] == 0) return true;
            }
        return false;
    }
    public double Power(BlockEntity e)
    {
        bool blocked = Blocked(e); e.SetNum("blocked", blocked ? 1 : 0);
        if (blocked || e.Pos.Core) { e.SetNum("power", 0); return 0; }
        double alt = bands[e.Pos.B].inner + e.Pos.Y + .5 - R, hf = Math.Clamp(.5 + alt / 40, .5, 2);
        double p = BasePower * Kinetics.Wind(Kinetics.Clock) * hf; e.SetNum("power", p); return p;
    }
    public override string Describe(BlockEntity e) => e.Num("blocked") > 0 ? "BLOCKED: THE BLADES NEED A FREE 5 X 5 AREA IN FRONT"
        : $"WIND {Kinetics.Wind(Kinetics.Clock) * 100:0}%  POWER {e.Num("power"):0.0} E/S  {e.Num("speed") * 60 / (2 * Math.PI):0} RPM";
}

/// <summary>Shaft: carries rotation along its axis (its facing, either end), drawn turning.</summary>
public sealed class ShaftBehavior : BlockBehavior, IKinetic
{
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public (int x, int y, int z)[] Ports(BlockEntity e) { var (x, y, z) = Kinetics.Dir(e); return new[] { (x, y, z), (-x, -y, -z) }; }
    public override string Describe(BlockEntity e) => $"{e.Num("speed") * 60 / (2 * Math.PI):0} RPM";
}

/// <summary>Generator: takes rotation in at its back (up to 30 E/s of power) and gives 90 % of it to its energy network.</summary>
public sealed class GeneratorBehavior : BlockBehavior, IKinetic, IEnergyMachine
{
    public const double MaxIn = 30, Efficiency = .9;
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public (int x, int y, int z)[] Ports(BlockEntity e) { var (x, y, z) = Kinetics.Dir(e); return new[] { (-x, -y, -z) }; }
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p) => p.Fixed = e.Num("input") * Efficiency * dt;
    public void Settle(BlockEntity e, double dt, double made, double got) => WaterMachines.Show(e, "power", made / dt);
    public override string Describe(BlockEntity e) => $"ROTATION IN {e.Num("input"):0.0} E/S  OUTPUT {e.Num("power"):0.0} E/S  {e.Num("speed") * 60 / (2 * Math.PI):0} RPM";
}

/// <summary>
/// Coal generator: burns fuel from its fuel slot (any item with a fuel_time) and makes 20 E/s while burning, but only when its network
/// needs energy or a battery has room (no fuel is wasted). One coal (40 s of furnace fuel) = 40 s at full output = 800 E.
/// </summary>
public sealed class CoalGeneratorBehavior : BlockBehavior, IEnergyMachine
{
    public const double Rate = 20;
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p)
    {
        bool fuel = e.Num("burn") > 0 || (e.Inv("fuel") is { } inv && inv[0] is { IsEmpty: false } f && f.Def?.FuelTime > 0);
        if (fuel) p.DispatchStore = Rate * dt;
    }
    public void Settle(BlockEntity e, double dt, double made, double got)
    {
        double burn = e.Num("burn") - made / Rate;
        if (burn <= 0 && made > 0 && e.Inv("fuel") is { } inv && inv[0] is { IsEmpty: false } f && f.Def?.FuelTime > 0)
        {   // light the next piece of fuel
            burn += f.Def.FuelTime; e.SetNum("burn_max", f.Def.FuelTime);
            if (--f.Count <= 0) inv[0] = null;
        }
        e.SetNum("burn", Math.Max(0, burn)); WaterMachines.Show(e, "power", made / dt);
        var lf = DEFS[e.BlockId].State.Find("lit"); // glowing front while it burns
        if (lf != null && lf.Packed)
        {
            int st = e.Pos.State, want = (int)lf.With((uint)st, made > 0 ? 1 : 0);
            if (want != st && e.Pos.Id == e.BlockId) BlockStates.Set(e.Pos, want, true);
        }
    }
    public override string Describe(BlockEntity e) => e.Num("burn") > 0 ? $"BURNING {e.Num("burn"):0} S LEFT  {e.Num("power"):0.0} E/S" : "NO FUEL BURNING";
}
