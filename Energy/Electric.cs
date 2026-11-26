using System.Collections.Concurrent;
using System.Text.Json;

namespace CubePlanet;

/// <summary>
/// Block light: cells giving off light (lit lamps), read by the mesher (thread-safe) to light the blocks around them: level L at the lamp,
/// one less per block (Minecraft-like), separate from sky light so it shows at night. A change re-meshes the chunks within reach.
/// </summary>
public static class BlockLights
{
    public readonly record struct Light(int B, int F, int X, int Y, int Z, int Level);
    public static readonly ConcurrentDictionary<long, Light> All = new();
    public static void Set(BlockPos p, int level)
    {
        if (p.Core) return;
        var l = new Light(p.B, p.F, p.X, p.Y, p.Z, level);
        if (All.TryGetValue(p.Key, out var old) && old == l) return;
        All[p.Key] = l; Relight(p);
    }
    public static void Remove(BlockPos p) { if (All.TryRemove(p.Key, out _)) Relight(p); }
    /// <summary>Re-mesh (in the background) the chunks a light at p reaches.</summary>
    static void Relight(BlockPos p)
    {
        if (p.Core) return;
        for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                    if (chunks.TryGetValue(ck(p.B, p.F, (p.X >> 5) + dx, (p.Y >> 5) + dy, (p.Z >> 5) + dz), out var ch) && ch.d != null) { ch.relight = true; ch.ver++; }
    }
    public static void Reset() => All.Clear();
}

/// <summary>Shared bits of the electric machines.</summary>
public static class Electric
{
    /// <summary>Is it night at this block (the sun below ~3 degrees)?</summary>
    public static bool Night(BlockPos p) => M.Dot(Sky.SUN, M.Nrm(p.Center)) < .05;
    public static void SetBool(BlockEntity e, string field, bool on)
    {
        var f = DEFS[e.BlockId]?.State.Find(field); if (f == null || !f.Packed || !e.Pos.Loaded || e.Pos.Id != e.BlockId) return;
        int st = e.Pos.State, want = (int)f.With((uint)st, on ? 1 : 0);
        if (want != st) BlockStates.Set(e.Pos, want, true);
    }
    public static double Key(BlockBehavior b, int id, string key, string name, double def)
        => DEFS[id] != null && DefinitionFile.Get(DEFS[id].Json, "properties.definition." + key) is { ValueKind: JsonValueKind.Object } j ? DefinitionFile.Num(j, name, def) : def;
    /// <summary>The main part of a multiblock (its first part), where its block entity lives.</summary>
    public static BlockPos MainPart(BlockPos p)
    {
        int id = p.Id; var d = DEFS[id]; if (d == null || d.Parts.Length == 0) return p;
        var parts = new List<(BlockPos pos, int part)>();
        return BlockActions.Parts(p, id, p.State, parts) ? parts.FirstOrDefault(x => x.part == 0).pos : p;
    }
}

/// <summary>
/// Electric lamp: lights up when powered (0.5 E/s; key "lamp": {"level", "power"}), giving block light around it. Right click: AUTO (lit at
/// night only, like a street light) / ON / OFF. Without power (no network, or not enough energy) it goes dark.
/// </summary>
public sealed class LampBehavior : BlockBehavior, IEnergyMachine
{
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public override bool Usable => true;
    static readonly string[] modes = { "AUTO (NIGHT ONLY)", "ON", "OFF" };
    public override bool OnUse(BlockPos p, int id, int state)
    {
        var e = BlockStates.EntityOrNew(p, id); if (e == null) return false;
        int m = ((int)e.Num("mode") + 1) % 3; e.SetNum("mode", m); UI.Note("Lamp: " + modes[m], 1.5); return true;
    }
    double Power(int id) => Electric.Key(this, id, "lamp", "power", .5);
    int Level(int id) => (int)Electric.Key(this, id, "lamp", "level", 9);
    bool Wants(BlockEntity e) { int m = (int)e.Num("mode"); return m == 1 || (m == 0 && Electric.Night(e.Pos)); }
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p) { if (Wants(e)) p.Demand = Power(e.BlockId) * dt; }
    public void Settle(BlockEntity e, double dt, double made, double got) => Light(e, Wants(e) && got >= Power(e.BlockId) * dt * .95);
    void Light(BlockEntity e, bool on)
    {
        e.SetNum("on", on ? 1 : 0); Electric.SetBool(e, "lit", on);
        if (on) BlockLights.Set(e.Pos, Level(e.BlockId)); else BlockLights.Remove(e.Pos);
    }
    // off the grid (no network reached it this step): dark
    public override void OnTick(BlockEntity e, double dt) { if (!EnergyNet.NetOf.ContainsKey(e.Pos.Key) && e.Num("on") > 0) Light(e, false); }
    public override IEnumerable<ItemStack> Harvest(BlockPos p, int id, int state) { BlockLights.Remove(p); return base.Harvest(p, id, state); }
    public override string Describe(BlockEntity e) => $"{modes[(int)e.Num("mode") % 3]}  {(e.Num("on") > 0 ? "LIT" : "DARK")}  {Power(e.BlockId):0.0} E/S WHEN LIT";
}

/// <summary>
/// Electric furnace: smelts like a furnace (same recipes) without fuel, twice as fast, using 12 E/s while it works (key "electric_furnace":
/// {"power", "speed"}). Slower when the network cannot give it all. Inventories "input", "output"; numbers "progress", "cook_time".
/// </summary>
public sealed class ElectricFurnaceBehavior : BlockBehavior, IEnergyMachine
{
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    double Power(int id) => Electric.Key(this, id, "electric_furnace", "power", 12);
    double Speed(int id) => Electric.Key(this, id, "electric_furnace", "speed", 2);
    static RecipeRegistry.Smelting Job(BlockEntity e)
    {
        var input = e.Inv("input"); var output = e.Inv("output"); if (input == null || output == null) return null;
        var r = input[0] is { IsEmpty: false } ? GameAssets.RecipeReg.SmeltingFor(input[0].Item) : null;
        bool canOut = r != null && (output[0] == null || output[0].IsEmpty || (output[0].Item == r.Output && output[0].State == 0 && output[0].Count + r.Count <= output[0].MaxStack));
        return canOut ? r : null;
    }
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p) { if (Job(e) != null) p.Demand = Power(e.BlockId) * dt; }
    public void Settle(BlockEntity e, double dt, double made, double got)
    {
        var r = Job(e); double share = got / Math.Max(1e-9, Power(e.BlockId) * dt);
        WaterMachines.Show(e, "power", got / dt);
        if (r == null || got <= 0) { Electric.SetBool(e, "lit", false); e.SetNum("progress", Math.Max(0, e.Num("progress") - dt * 2)); return; }
        Electric.SetBool(e, "lit", true); e.SetNum("cook_time", r.Time);
        double prog = e.Num("progress") + dt * Speed(e.BlockId) * Math.Min(1, share);
        if (prog >= r.Time)
        {
            prog = 0; var input = e.Inv("input"); var output = e.Inv("output");
            if (output[0] == null || output[0].IsEmpty) output[0] = new ItemStack(r.Output, r.Count); else output[0].Count += r.Count;
            if (--input[0].Count <= 0) input[0] = null;
        }
        e.SetNum("progress", prog);
    }
    public override string Describe(BlockEntity e) => Job(e) == null ? "IDLE (NOTHING TO SMELT OR OUTPUT FULL)" : $"SMELTING {e.Num("progress"):0.0} / {e.Num("cook_time"):0.0} S  {e.Num("power"):0.0} E/S";
}

/// <summary>
/// Electric motor: turns energy into rotation at its front (a shaft line, like a turbine's back): up to 30 E/s in, 90 % out as rotation
/// power for generators or other rotation machines on the line. Right click: 100 % / 50 % / 25 % / OFF.
/// </summary>
public sealed class MotorBehavior : BlockBehavior, IEnergyMachine, IKinetic
{
    public const double MaxPower = 30, Efficiency = .9;
    static readonly double[] throttle = { 1, .5, .25, 0 };
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public override bool Usable => true;
    public override bool OnUse(BlockPos p, int id, int state)
    {
        var e = BlockStates.EntityOrNew(p, id); if (e == null) return false;
        int m = ((int)e.Num("mode") + 1) % 4; e.SetNum("mode", m); UI.Note(m == 3 ? "Motor: OFF" : $"Motor: {throttle[m] * 100:0}%", 1.5); return true;
    }
    public (int x, int y, int z)[] Ports(BlockEntity e) { var (x, y, z) = Kinetics.Dir(e); return new[] { (x, y, z) }; }
    /// <summary>Which way it turns its line around the world axis (clockwise seen from the front).</summary>
    public static double Spin(BlockEntity e) { var (x, y, z) = Kinetics.Dir(e); return -(x + y + z); }
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p) => p.Demand = MaxPower * throttle[(int)e.Num("mode") % 4] * dt;
    public void Settle(BlockEntity e, double dt, double made, double got) { e.SetNum("drive", got / dt * Efficiency); WaterMachines.Show(e, "power", got / dt); }
    public override string Describe(BlockEntity e)
    {
        int m = (int)e.Num("mode") % 4;
        return m == 3 ? "OFF" : $"{throttle[m] * 100:0}%  IN {e.Num("power"):0.0} E/S  ROTATION OUT {e.Num("drive"):0.0} E/S  {Kinetics.Rpm(e):0} RPM";
    }
}

/// <summary>
/// Solar panel (key "solar": {"power": E/s in full sun straight on, "pivot_y", "tilt_max": degrees}); sizes are multiblocks (2 x 2, 3 x 3)
/// whose first part holds the block entity; every part joins cables (group tag energy_cable), so panels side by side form one array.
/// Power = power x sunlight on the panel: the share of the sun's light falling straight on it (cos of the angle) x a fade near the horizon.
/// Right click: FIXED (lying flat) / TRACKING: the panel turns about its north-south axis to face the sun (up to tilt_max each way), which
/// gives much more in the morning and evening; the tracking motor uses 2 % of the panel's power.
/// </summary>
public sealed class SolarPanelBehavior : BlockBehavior, IEnergyMachine
{
    public const double TrackCost = .02, TurnRate = .4;
    static bool IsMain(BlockPos p, int id)
    {
        var d = DEFS[id]; var f = d?.State.Find("part"); return d == null || d.Parts.Length == 0 || f == null || f.Index((uint)p.State) == 0;
    }
    public override void OnPlace(BlockPos p, int id) { if (IsMain(p, id)) BlockStates.EntityOrNew(p, id); }
    public override bool Usable => true;
    public override bool OnUse(BlockPos p, int id, int state)
    {
        var m = Electric.MainPart(p); if (!BlockStates.entities.TryGetValue(m.Key, out var e)) return false;
        e.SetNum("mode", e.Num("mode") > 0 ? 0 : 1); UI.Note(e.Num("mode") > 0 ? "Solar panel: TRACKING THE SUN" : "Solar panel: FIXED (FLAT)", 1.5); return true;
    }
    double Rated(int id) => Electric.Key(this, id, "solar", "power", 3);
    double TiltMax(int id) => Electric.Key(this, id, "solar", "tilt_max", 60) * Math.PI / 180;
    /// <summary>The sun in the panel's block frame (x east, y up, z south) and its height above the horizon (sin).</summary>
    static (V3 sun, double sinElev) SunLocal(BlockPos p)
    {
        var (ax, ay, az) = p.Axes(); V3 s = Sky.SUN; return (new V3(M.Dot(s, ax), M.Dot(s, ay), M.Dot(s, az)), M.Dot(s, ay));
    }
    /// <summary>Sunlight on the panel 0..1 at tilt angle a (radians, about the north-south axis).</summary>
    public static double OnPanel(BlockPos p, double a)
    {
        var (s, el) = SunLocal(p); if (el <= 0) return 0;
        double fade = Math.Clamp(el / .12, 0, 1); fade = fade * fade * (3 - 2 * fade);
        return Math.Max(0, -Math.Sin(a) * s.X + Math.Cos(a) * s.Y) * fade;
    }
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p)
    {
        int id = e.BlockId; bool track = e.Num("mode") > 0; var (s, el) = SunLocal(e.Pos);
        // tracking: turn toward the sun (or back to flat at night / when fixed)
        double target = track && el > 0 ? Math.Clamp(Math.Atan2(-s.X, s.Y), -TiltMax(id), TiltMax(id)) : 0, a = e.Num("angle");
        a += Math.Clamp(target - a, -TurnRate * dt, TurnRate * dt); e.SetNum("angle", a); e.SetNum("speed", 0);
        double light = OnPanel(e.Pos, a), pw = Rated(id) * light - (track && light > 0 ? TrackCost * Rated(id) : 0);
        e.SetNum("light", light); p.Fixed = Math.Max(0, pw) * dt;
    }
    public void Settle(BlockEntity e, double dt, double made, double got) => WaterMachines.Show(e, "power", made / dt);
    public override string Describe(BlockEntity e)
    {
        var (_, el) = SunLocal(e.Pos);
        return $"{(e.Num("mode") > 0 ? "TRACKING" : "FIXED (FLAT)")}  SUN {Math.Asin(Math.Clamp(el, -1, 1)) * 180 / Math.PI:0} DEG UP  {e.Num("light") * 100:0}% ON THE PANEL  {e.Num("power"):0.0} / {Rated(e.BlockId):0} E/S";
    }
}
