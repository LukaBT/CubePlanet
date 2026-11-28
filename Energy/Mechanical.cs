using System.Text.Json;

namespace CubePlanet;

/// <summary>A block that takes rotation power from its line (generators, millstones, sawmills, mechanical pumps).</summary>
public interface IRotationLoad
{
    /// <summary>Rotation power it would take now (E/s).</summary>
    double Wants(BlockEntity e);
    /// <summary>The power it gets this step (E/s; the line shares out what its sources make).</summary>
    void Work(BlockEntity e, double power, double dt);
}

/// <summary>Gearbox: joins shafts on all six sides (turns a line round a corner, splits it).</summary>
public sealed class GearboxBehavior : BlockBehavior, IKinetic
{
    static readonly (int, int, int)[] all = { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public (int x, int y, int z)[] Ports(BlockEntity e) => all;
    public override string Describe(BlockEntity e) => e.Num("turbines") > 0 ? $"{Kinetics.Rpm(e):0} RPM  LINE {e.Num("line"):0.0} E/S" : "NOT DRIVEN";
}

/// <summary>
/// A machine processing items with rotation power (millstone, sawmill): definition key "processing": {"power": E/s it takes while working,
/// "recipes": [<see cref="ProcRecipe"/>, its "time" = E of rotation per batch]}. Takes rotation on any side but the top (a shaft from a turbine,
/// a motor or a gearbox); inventories "input", "output"; numbers "progress" (E done), "cook_time" (E needed).
/// Slower when the line gives it less than it wants.
/// </summary>
public sealed class ProcessorBehavior : BlockBehavior, IKinetic, IRotationLoad
{
    sealed class Spec { public double Power = 15; public List<ProcRecipe> Recipes = new(); }
    static readonly Dictionary<int, Spec> specs = new();
    static Spec SpecOf(int id)
    {
        lock (specs)
        {
            if (specs.TryGetValue(id, out var s)) return s;
            s = new Spec();
            if (DEFS[id] != null && DefinitionFile.Get(DEFS[id].Json, "properties.definition.processing") is { ValueKind: JsonValueKind.Object } j)
            {
                s.Power = DefinitionFile.Num(j, "power", 15);
                s.Recipes = Processing.Parse(DefinitionFile.Get(j, "recipes"), $"block {DEFS[id].FullName}", 100);
            }
            return specs[id] = s;
        }
    }
    public static IReadOnlyList<ProcRecipe> RecipesOf(int id) => BEH[id] is ProcessorBehavior ? SpecOf(id).Recipes : Array.Empty<ProcRecipe>();
    static readonly (int, int, int)[] ports = { (1, 0, 0), (-1, 0, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public (int x, int y, int z)[] Ports(BlockEntity e) => ports;
    ProcRecipe Job(BlockEntity e) => Processing.Job(e, SpecOf(e.BlockId).Recipes);
    public double Wants(BlockEntity e) => Job(e) != null ? SpecOf(e.BlockId).Power : 0;
    public void Work(BlockEntity e, double power, double dt)
    {
        e.SetNum("input_power", power); var r = Job(e); if (r == null) { e.SetNum("progress", 0); return; }
        e.SetNum("cook_time", r.Time);
        double prog = e.Num("progress") + power * dt;
        if (prog >= r.Time) { prog = 0; Processing.Consume(e.Inv("input"), r); Processing.GiveAll(e, e.Inv("output"), r); }
        e.SetNum("progress", prog);
    }
    public override string Describe(BlockEntity e)
    {
        var r = Job(e); if (r == null) return "IDLE (NOTHING IT CAN PROCESS, OR OUTPUT FULL)";
        if (e.Num("turbines") == 0) return "NOT DRIVEN: NEEDS A SHAFT FROM A TURBINE, MOTOR OR GEARBOX (ANY SIDE BUT THE TOP)";
        return $"WORKING {e.Num("progress"):0} / {r.Time:0} E  ROTATION IN {e.Num("input_power"):0.0} / {SpecOf(e.BlockId).Power:0} E/S  {Kinetics.Rpm(e):0} RPM";
    }
}

/// <summary>
/// Mechanical pump: lifts water from its back to its front like the electric pump (same cost per unit and lift, at most 16 units/s), driven
/// by rotation from a shaft on any side across its axis (a wind pump). Right click: ON / OFF.
/// </summary>
public sealed class MechanicalPumpBehavior : BlockBehavior, IKinetic, IRotationLoad
{
    public const double MaxIn = 30;
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public override bool Usable => true;
    public override bool OnUse(BlockPos p, int id, int state)
    {
        var e = BlockStates.EntityOrNew(p, id); if (e == null) return false;
        e.SetNum("off", e.Num("off") > 0 ? 0 : 1); UI.Note(e.Num("off") > 0 ? "Mechanical pump: OFF" : "Mechanical pump: ON", 1.5); return true;
    }
    public (int x, int y, int z)[] Ports(BlockEntity e)
    {
        var (fx, fy, fz) = Kinetics.Dir(e); var l = new List<(int, int, int)>();
        foreach (var d in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
            if (d != (fx, fy, fz) && d != (-fx, -fy, -fz)) l.Add(d);
        return l.ToArray();
    }
    public double Wants(BlockEntity e)
    {
        if (e.Num("off") > 0 || !PumpBehavior.Job(e, .1, out WaterEnd _, out WaterEnd _, out double cost, out double units)) return 0;
        return Math.Min(MaxIn, units * cost / .1); // (the pump curve and suction limit are in PumpBehavior.Job)
    }
    public void Work(BlockEntity e, double power, double dt)
    {
        int moved = 0;
        if (power > 0 && e.Num("off") == 0 && PumpBehavior.Job(e, dt, out var src, out var dst, out double cost, out double max))
        {
            double acc = e.Num("carry") + power * dt / cost; int n = (int)Math.Min(acc, max + 1); // a running total of the water paid for
            moved = WaterMachines.Move(src, dst, n, WaterMachines.Dir(e)); e.SetNum("carry", Math.Clamp(acc - moved, 0, 2));
        }
        WaterMachines.Show(e, "flow", moved / dt); WaterMachines.Show(e, "power", power);
    }
    public override string Describe(BlockEntity e) => e.Num("off") > 0 ? "OFF" : e.Num("turbines") == 0 ? "NOT DRIVEN: NEEDS A SHAFT ON A SIDE ACROSS ITS AXIS"
        : $"LIFT {e.Num("lift"):0.0} OF {e.Num("max_lift"):0}  FLOW {e.Num("flow"):0.0} U/S  ROTATION IN {e.Num("power"):0.0} E/S  {Kinetics.Rpm(e):0} RPM" + PumpBehavior.Why(e);
}

/// <summary>
/// Pipe valve: lets water flow from its back to its front through pipes on its own, driven by gravity and pressure (a pressurized tank
/// pushing water up a pipe, a siphon from a high lake to a low one), as fast as pipe friction allows: the energy per kg available
/// (g x the height difference + the pressure difference / 1000) all goes into friction, Q = sqrt(available / K). One way (a check valve:
/// nothing flows backwards). Right click: OPEN / CLOSED.
/// </summary>
public sealed class PipeValveBehavior : BlockBehavior
{
    public const double ValveLoss = .3, MaxFlow = 64;
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public override bool Usable => true;
    public override bool OnUse(BlockPos p, int id, int state)
    {
        var e = BlockStates.EntityOrNew(p, id); if (e == null) return false;
        e.SetNum("closed", e.Num("closed") > 0 ? 0 : 1); UI.Note(e.Num("closed") > 0 ? "Valve CLOSED" : "Valve OPEN", 1.5); return true;
    }
    public override void OnTick(BlockEntity e, double dt)
    {
        int moved = 0; e.SetNum("q", 0);
        if (e.Num("closed") == 0 && e.Pos.Loaded)
        {
            var src = WaterMachines.IntakeEnd(e); var dst = src.Ok ? WaterMachines.OutletEnd(e, src.Liquid) : default;
            if (src.Ok && dst.Ok)
            {
                double g = WaterMachines.G(e.Pos), avail = -WaterMachines.SpecificEnergy(src, dst, g);
                double k = src.K + dst.K + ValveLoss * WaterMachines.MouthK(GameAssets.BlockReg.Id("liquid_pipe")) / WaterMachines.EndLoss + 2 * WaterMachines.MouthK(GameAssets.BlockReg.Id("liquid_pipe"));
                double q = avail > 0 ? Math.Min(MaxFlow, Math.Sqrt(avail / k)) : 0; e.SetNum("q", q); e.SetNum("drive", avail / Math.Max(g, 1e-6));
                double units = Math.Min(q * dt + e.Num("carry"), Math.Min(src.Available, dst.RoomFor(src.Liquid))); int n = (int)units; e.SetNum("carry", Math.Clamp(units - n, 0, 1));
                if (n > 0) moved = WaterMachines.Move(src, dst, n, WaterMachines.Dir(e));
            }
        }
        WaterMachines.Show(e, "flow", moved / dt);
    }
    public override string Describe(BlockEntity e) => e.Num("closed") > 0 ? "CLOSED" :
        $"OPEN  FLOW {e.Num("flow"):0.0} U/S  DRIVING HEAD {e.Num("drive"):0.0} BLOCKS" + (e.Num("drive") <= 0 ? "  (THE FRONT IS HIGHER OR UNDER MORE PRESSURE: NOTHING FLOWS)" : "");
}
