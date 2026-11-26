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
/// "recipes": [{"input": item, "output": item, "count", "work": E of rotation per item}]}. Takes rotation on any side but the top (a shaft
/// from a turbine, a motor or a gearbox); inventories "input", "output"; numbers "progress" (E done), "cook_time" (E needed).
/// Slower when the line gives it less than it wants.
/// </summary>
public sealed class ProcessorBehavior : BlockBehavior, IKinetic, IRotationLoad
{
    public sealed class Recipe { public int Input, Output, Count; public double Work; }
    sealed class Spec { public double Power = 15; public List<Recipe> Recipes = new(); }
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
                if (DefinitionFile.Get(j, "recipes") is { ValueKind: JsonValueKind.Array } rs)
                    foreach (var r in rs.EnumerateArray())
                    {
                        string i = DefinitionFile.Str(r, "input"), o = DefinitionFile.Str(r, "output");
                        if (GameAssets.ItemReg.Has(i) && GameAssets.ItemReg.Has(o))
                            s.Recipes.Add(new Recipe { Input = GameAssets.ItemReg.Id(i), Output = GameAssets.ItemReg.Id(o), Count = (int)DefinitionFile.Num(r, "count", 1), Work = DefinitionFile.Num(r, "work", 100) });
                    }
            }
            return specs[id] = s;
        }
    }
    public static IReadOnlyList<Recipe> RecipesOf(int id) => SpecOf(id).Recipes;
    static readonly (int, int, int)[] ports = { (1, 0, 0), (-1, 0, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public (int x, int y, int z)[] Ports(BlockEntity e) => ports;
    Recipe Job(BlockEntity e)
    {
        var input = e.Inv("input"); var output = e.Inv("output"); if (input == null || output == null || input[0] is not { IsEmpty: false } st) return null;
        var r = SpecOf(e.BlockId).Recipes.FirstOrDefault(x => x.Input == st.Item); if (r == null) return null;
        bool room = output[0] == null || output[0].IsEmpty || (output[0].Item == r.Output && output[0].State == 0 && output[0].Count + r.Count <= output[0].MaxStack);
        return room ? r : null;
    }
    public double Wants(BlockEntity e) => Job(e) != null ? SpecOf(e.BlockId).Power : 0;
    public void Work(BlockEntity e, double power, double dt)
    {
        e.SetNum("input_power", power); var r = Job(e); if (r == null) { e.SetNum("progress", 0); return; }
        e.SetNum("cook_time", r.Work);
        double prog = e.Num("progress") + power * dt;
        if (prog >= r.Work)
        {
            prog = 0; var input = e.Inv("input"); var output = e.Inv("output");
            if (output[0] == null || output[0].IsEmpty) output[0] = new ItemStack(r.Output, r.Count); else output[0].Count += r.Count;
            if (--input[0].Count <= 0) input[0] = null;
        }
        e.SetNum("progress", prog);
    }
    public override string Describe(BlockEntity e)
    {
        var r = Job(e); if (r == null) return "IDLE (NOTHING IT CAN PROCESS, OR OUTPUT FULL)";
        if (e.Num("turbines") == 0) return "NOT DRIVEN: NEEDS A SHAFT FROM A TURBINE, MOTOR OR GEARBOX (ANY SIDE BUT THE TOP)";
        return $"WORKING {e.Num("progress"):0} / {r.Work:0} E  ROTATION IN {e.Num("input_power"):0.0} / {SpecOf(e.BlockId).Power:0} E/S  {Kinetics.Rpm(e):0} RPM";
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
            double units = Math.Min(max, power * dt / cost + e.Num("carry")); int n = (int)units; e.SetNum("carry", Math.Min(1, units - n));
            moved = WaterMachines.Move(src, dst, n, WaterMachines.Dir(e));
        }
        WaterMachines.Show(e, "flow", moved / dt); WaterMachines.Show(e, "power", power);
    }
    public override string Describe(BlockEntity e) => e.Num("off") > 0 ? "OFF" : e.Num("turbines") == 0 ? "NOT DRIVEN: NEEDS A SHAFT ON A SIDE ACROSS ITS AXIS"
        : $"LIFT {e.Num("lift"):0.0} OF {e.Num("max_lift"):0}  FLOW {e.Num("flow"):0.0} U/S  ROTATION IN {e.Num("power"):0.0} E/S  {Kinetics.Rpm(e):0} RPM" + PumpBehavior.Why(e);
}
