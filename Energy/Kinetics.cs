using System.Text.Json;

namespace CubePlanet;

/// <summary>A block in a rotation network: the sides (offsets) where shafts connect to it.</summary>
public interface IKinetic { (int x, int y, int z)[] Ports(BlockEntity e); }

/// <summary>
/// Rotation networks: wind turbines (sources), shafts (they carry rotation along their axis) and generators (they turn it into energy),
/// joined where a block's port faces another block's port. Every 0.1 s each network adds up its sources' power, shares it between its
/// generators (each takes up to <see cref="GeneratorBehavior.MaxIn"/>), and turns: free it spins fastest, load slows it down. All parts of a
/// network share one angle (block entity numbers "angle", "speed"), which <see cref="MovingParts"/> draws. The speed has a sign: turning
/// around the line's axis pointing toward +x / +y / +z (<see cref="MovingParts.WorldAxis"/>) is positive, the other way negative.
/// <para>Wind: one wind for the whole world (speed in m/s and the compass direction it blows toward, in every block grid's own north / east),
/// changing slowly with game time (<see cref="Clock"/>, saved with the world).</para>
/// </summary>
public static class Kinetics
{
    static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    static double lastTick;
    public static double Time => clock.Elapsed.TotalSeconds;
    /// <summary>Seconds since the last network update (the renderer turns parts on by speed x this).</summary>
    public static double SinceTick => Math.Clamp(Time - lastTick, 0, .25);
    public static IKinetic Of(BlockEntity e) => e == null ? null : BEH[e.BlockId] as IKinetic;
    /// <summary>Game time used by the wind (seconds of ticks; saved in world.json).</summary>
    public static double Clock;
    /// <summary>Tests / demos: a fixed wind instead of the changing one (speed m/s, heading radians).</summary>
    public static double? FixedSpeed, FixedHeading;
    /// <summary>Wind speed 0 .. 16 m/s (about 8 on average), gusting.</summary>
    public static double WindSpeed => FixedSpeed ?? Math.Clamp(8 + 3.5 * Math.Sin(Clock / 310) + 2 * Math.Sin(Clock / 47 + 1) + Math.Sin(Clock / 7.3 + 2), 0, 16);
    /// <summary>Where the wind blows toward: angle in the block grid's x (east) / z (south) plane, turning slowly (a full turn in ~2.3 h).</summary>
    public static double WindHeading => FixedHeading ?? Clock / 1300 + .5 * Math.Sin(Clock / 170);
    public static (double x, double z) WindDir => (Math.Cos(WindHeading), Math.Sin(WindHeading));
    /// <summary>Heading (radians, for <see cref="FixedHeading"/>) of a wind coming from a compass side (north = -z).</summary>
    public static double HeadingFrom(string side) => side switch { "north" => Math.PI / 2, "south" => -Math.PI / 2, "east" => Math.PI, _ => 0 };
    /// <summary>Bearing (degrees, 0 = north, 90 = east) the wind comes from.</summary>
    public static double FromBearing { get { var (x, z) = WindDir; return (Math.Atan2(-x, z) * 180 / Math.PI + 360) % 360; } }
    /// <summary>Set the wind (settings panel, saved with the world): speed m/s, the bearing it comes from in degrees. Natural() lets it change by itself again.</summary>
    public static void SetWind(double speed, double fromDeg) { double b = fromDeg * Math.PI / 180; FixedSpeed = Math.Max(0, speed); FixedHeading = Math.Atan2(Math.Cos(b), -Math.Sin(b)); }
    public static void Natural() { FixedSpeed = null; FixedHeading = null; }
    public static bool IsNatural => FixedSpeed == null;
    /// <summary>Compass name of where the wind comes from (N, NE, ... in the block grid: north = -z, east = +x).</summary>
    public static string WindFrom
    {
        get
        {
            return new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" }[(int)Math.Round(FromBearing / 45) % 8];
        }
    }
    public static (int, int, int) Dir(BlockEntity e) => WaterMachines.Dir(e);
    public static double Rpm(BlockEntity e) => Math.Abs(e.Num("speed")) * 60 / (2 * Math.PI);

    public static void Tick(double dt)
    {
        lastTick = Time; Clock += dt;
        var done = new HashSet<long>(); var q = new Queue<BlockEntity>(); var net = new List<BlockEntity>();
        foreach (var e in BlockStates.entities.Values.ToArray())
        {
            if (Of(e) == null || done.Contains(e.Pos.Key)) continue; // rotation parts are block entities: they run in unloaded land too
            net.Clear(); q.Clear(); q.Enqueue(e); done.Add(e.Pos.Key);
            while (q.Count > 0 && net.Count < 2048)
            {
                var a = q.Dequeue(); net.Add(a);
                foreach (var (dx, dy, dz) in Of(a).Ports(a))
                {
                    if (!a.Pos.TryOffset(dx, dy, dz, out var np) || done.Contains(np.Key)) continue;
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
        // turbines push the line one way or the other (wind from the front or from behind): signed sums, opposing pushes cancel
        double torque = 0, free = 0; int turbines = 0, turning = 0; var gens = new List<BlockEntity>();
        foreach (var e in net)
            if (BEH[e.BlockId] is WindTurbineBehavior wt)
            {
                double p = wt.Power(e), sg = WindTurbineBehavior.Spin(e); torque += sg * p; turbines++;
                double w = wt.FreeSpeed(e); if (w > 0) { free += sg * w; turning++; }
            }
            else if (BEH[e.BlockId] is MotorBehavior)
            {   // an electric motor drives the line with the energy it got last step
                double p = e.Num("drive"), sg = MotorBehavior.Spin(e); torque += sg * p; turbines++;
                if (p > 0) { free += sg * 2.5; turning++; }
            }
            else if (BEH[e.BlockId] is IRotationLoad) gens.Add(e);
        double src = Math.Abs(torque);
        // loads (generators, millstones, sawmills, pumps) get what they want, or a share of the line's power in proportion to it
        double want = 0; var wants = new double[gens.Count];
        for (int i = 0; i < gens.Count; i++) { wants[i] = Math.Max(0, ((IRotationLoad)BEH[gens[i].BlockId]).Wants(gens[i])); want += wants[i]; }
        double k = want > 0 ? Math.Min(1, src / want) : 0, used = 0;
        for (int i = 0; i < gens.Count; i++) { double inp = wants[i] * k; ((IRotationLoad)BEH[gens[i].BlockId]).Work(gens[i], inp, dt); used += inp; }
        double w0 = turning > 0 ? free / turning : 0, load = src > 0 ? used / src : 0;
        // spin up / run down smoothly; free it turns fastest, load slows it; every part continues from the network's first part's angle
        double speed = net[0].Num("speed"), target = w0 * (1 - .55 * load);
        speed += (target - speed) * Math.Min(1, dt * 1.5); if (Math.Abs(speed) < 1e-3) speed = 0;
        double angle = (net[0].Num("angle") + speed * dt) % (2 * Math.PI);
        foreach (var e in net) { e.SetNum("angle", angle); e.SetNum("speed", speed); e.SetNum("turbines", turbines); e.SetNum("line", src); e.SetNum("line_load", used); e.SetNum("line_want", want); }
    }
}

/// <summary>
/// Wind turbine: a hub whose blades (a moving part) turn in front of it; rotation comes out of its back only. Sizes come from the definition
/// key "wind_turbine": {"diameter": blade span in blocks (odd), "power": E/s at 10 m/s of wind straight into the blades}.
/// <list type="bullet">
/// <item>Only the part of the wind along the turbine's axis counts: v = wind speed x |cos(angle between the wind and the turbine's facing)|;
/// wind from the side gives nothing. Wind from the front turns it one way, wind from behind the other way (same power). Taller is windier:
/// x0.8 at sea level .. x1.26 at 60 blocks up.</item>
/// <item>Power = power x (v / 10)^3 (wind power grows with the cube of its speed), at most 1.5 x power; nothing below 2.5 m/s.</item>
/// <item>The diameter x diameter area of the blades (in front of the hub) must be free (air or plants), else it stops (HUD: BLOCKED).</item>
/// <item>Bigger blades turn slower (tip speed): free it turns at 0.8 x v / radius rad/s.</item>
/// </list>
/// </summary>
public sealed class WindTurbineBehavior : BlockBehavior, IKinetic
{
    public const double RefWind = 10, CutIn = 2.5, MaxFactor = 1.5;
    public sealed class Spec { public double Diameter = 5, Power = 10; public int Half => (int)Math.Round((Diameter - 1) / 2); }
    static readonly Dictionary<int, Spec> specs = new();
    public static Spec SpecOf(int id)
    {
        lock (specs)
        {
            if (specs.TryGetValue(id, out var s)) return s;
            s = new Spec();
            if (DEFS[id] != null && DefinitionFile.Get(DEFS[id].Json, "properties.definition.wind_turbine") is { ValueKind: JsonValueKind.Object } j)
            { s.Diameter = DefinitionFile.Num(j, "diameter", 5); s.Power = DefinitionFile.Num(j, "power", 10); }
            return specs[id] = s;
        }
    }
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public (int x, int y, int z)[] Ports(BlockEntity e) { var (x, y, z) = Kinetics.Dir(e); return new[] { (-x, -y, -z) }; }
    public bool Blocked(BlockEntity e)
    {
        var (fx, fy, fz) = Kinetics.Dir(e); int h = SpecOf(e.BlockId).Half;
        // the blade disc: the plane in front of the hub, half the diameter each way across the facing
        (int, int, int) u = fy == 0 ? (0, 1, 0) : (1, 0, 0), v = fx != 0 ? (0, 0, 1) : fz != 0 ? (1, 0, 0) : (0, 0, 1);
        for (int a = -h; a <= h; a++) for (int b = -h; b <= h; b++)
            {
                int dx = fx + u.Item1 * a + v.Item1 * b, dy = fy + u.Item2 * a + v.Item2 * b, dz = fz + u.Item3 * a + v.Item3 * b;
                if (!e.Pos.TryOffset(dx, dy, dz, out var c) || !c.Loaded) continue;
                int id = c.Id; if (id != 0 && SPC[id] == 0) return true;
            }
        return false;
    }
    /// <summary>Share of the wind along the turbine's axis: +1 straight into the blades (from the front), -1 straight from behind, 0 from the side.</summary>
    public static double Into(BlockEntity e)
    {
        var (fx, _, fz) = Kinetics.Dir(e); var (wx, wz) = Kinetics.WindDir;
        double c = -(fx * wx + fz * wz); return Math.Abs(c) < 1e-9 ? 0 : c; // the wind blows against the facing = into the front
    }
    /// <summary>Which way the wind turns this turbine around its line's world axis (<see cref="MovingParts.WorldAxis"/>): +1, -1, or 0.
    /// The spin follows the wind along the axis, so two turbines back to back on one line both push the same way.</summary>
    public static double Spin(BlockEntity e)
    {
        var (fx, fy, fz) = Kinetics.Dir(e); double into = Into(e);
        return Math.Sign(into) * -(fx + fy + fz); // from the front: turning about -facing (clockwise seen from the front)
    }
    public static double Shear(BlockEntity e) => Math.Clamp(.8 + (bands[e.Pos.B].inner + e.Pos.Y + .5 - R) / 130, .8, 1.26);
    public double Power(BlockEntity e)
    {
        var sp = SpecOf(e.BlockId);
        bool blocked = Blocked(e); e.SetNum("blocked", blocked ? 1 : 0);
        double v = e.Pos.Core ? 0 : Kinetics.WindSpeed * Shear(e) * Math.Abs(Into(e)); e.SetNum("wind", v);
        if (blocked || v < CutIn) { e.SetNum("power", 0); return 0; }
        double p = sp.Power * Math.Min(MaxFactor, Math.Pow(v / RefWind, 3)); e.SetNum("power", p); return p;
    }
    public double FreeSpeed(BlockEntity e) => e.Num("power") > 0 ? .8 * e.Num("wind") / (SpecOf(e.BlockId).Diameter / 2) : 0;
    public override string Describe(BlockEntity e)
    {
        var sp = SpecOf(e.BlockId); string wind = $"WIND {Kinetics.WindSpeed:0.0} M/S FROM {Kinetics.WindFrom}";
        if (e.Num("blocked") > 0) return $"BLOCKED: THE BLADES NEED A FREE {sp.Diameter:0} X {sp.Diameter:0} AREA IN FRONT";
        double into = Into(e);
        if (into == 0) return wind + ": FROM THE SIDE, NOT TURNING (TURN THE TURBINE TOWARD OR AWAY FROM IT)";
        return $"{wind}, {Math.Abs(into) * 100:0}% {(into > 0 ? "INTO THE FRONT" : "FROM BEHIND (TURNS THE OTHER WAY)")}  POWER {e.Num("power"):0.0} E/S  {Kinetics.Rpm(e):0} RPM";
    }
}

/// <summary>Shaft: carries rotation along its axis (its facing, either end), drawn turning.</summary>
public sealed class ShaftBehavior : BlockBehavior, IKinetic
{
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public (int x, int y, int z)[] Ports(BlockEntity e) { var (x, y, z) = Kinetics.Dir(e); return new[] { (x, y, z), (-x, -y, -z) }; }
    public override string Describe(BlockEntity e) => e.Num("turbines") > 0 ? $"{Kinetics.Rpm(e):0} RPM" : "NOT DRIVEN: NO TURBINE BACK ON THIS SHAFT LINE";
}

/// <summary>Generator: the shaft runs through it (either end joins a shaft or a turbine's back); takes up to 30 E/s of rotation power and
/// gives 90 % of it to its energy network. Several generators on one shaft line share the power.</summary>
public sealed class GeneratorBehavior : BlockBehavior, IKinetic, IEnergyMachine, IRotationLoad
{
    public const double MaxIn = 30, Efficiency = .9;
    public double Wants(BlockEntity e) => MaxIn;
    public void Work(BlockEntity e, double power, double dt) => e.SetNum("input", power);
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public (int x, int y, int z)[] Ports(BlockEntity e) { var (x, y, z) = Kinetics.Dir(e); return new[] { (x, y, z), (-x, -y, -z) }; }
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p) => p.Fixed = e.Num("input") * Efficiency * dt;
    public void Settle(BlockEntity e, double dt, double made, double got) => WaterMachines.Show(e, "power", made / dt);
    public override string Describe(BlockEntity e) => e.Num("turbines") == 0 ? "NOT DRIVEN: NEEDS A TURBINE BACK OR A SHAFT FROM ONE AT AN END"
        : $"ROTATION IN {e.Num("input"):0.0} E/S  OUTPUT {e.Num("power"):0.0} E/S  {Kinetics.Rpm(e):0} RPM";
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
        if (lf != null && lf.Packed && e.Pos.Loaded)
        {
            int st = e.Pos.State, want = (int)lf.With((uint)st, made > 0 ? 1 : 0);
            if (want != st && e.Pos.Id == e.BlockId) BlockStates.Set(e.Pos, want, true);
        }
    }
    public override string Describe(BlockEntity e) => e.Num("burn") > 0 ? $"BURNING {e.Num("burn"):0} S LEFT  {e.Num("power"):0.0} E/S" : "NO FUEL BURNING";
}
