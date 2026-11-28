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
    /// <summary>A number from the block's definition (properties.definition.key.name); read from the JSON once per block, key and name (machines
    /// ask for their power, light level, speed every step: walking the JSON each time was the biggest part of the machine step).</summary>
    public static double Key(BlockBehavior b, int id, string key, string name, double def)
    {
        var k = (id, key, name, def); if (keyCache.TryGetValue(k, out double v)) return v;
        v = DEFS[id] != null && DefinitionFile.Get(DEFS[id].Json, "properties.definition." + key) is { ValueKind: JsonValueKind.Object } j ? DefinitionFile.Num(j, name, def) : def;
        keyCache[k] = v; return v;
    }
    static readonly ConcurrentDictionary<(int, string, string, double), double> keyCache = new();
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
        if (Electric.Key(this, e.BlockId, "lamp", "grow", 0) > 0) { if (on) GrowLights.Lit[e.Pos.Key] = e.Pos; else GrowLights.Lit.TryRemove(e.Pos.Key, out _); } // grow lights
    }
    // off the grid (no network reached it this step): dark
    public override void OnTick(BlockEntity e, double dt) { if (!EnergyNet.NetOf.ContainsKey(e.Pos.Key) && e.Num("on") > 0) Light(e, false); }
    public override IEnumerable<ItemStack> Harvest(BlockPos p, int id, int state) { BlockLights.Remove(p); GrowLights.Lit.TryRemove(p.Key, out _); return base.Harvest(p, id, state); }
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
        bool canOut = r != null && Processing.Fits(e, output, r.Outs);
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
            Processing.Give(e, output, r.Output, r.Count);
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
/// Right click: FIXED (lying flat) / 1-AXIS: the panel turns about its north-south axis to follow the sun east to west, which gives much more
/// in the morning and evening (motor: 2 % of the panel's power) / 2-AXIS: it also tilts north-south, so it faces the sun straight on
/// (motors: 4 %). Turning is limited so the panel's corners stay above the ground (and by tilt_max).
/// Shade: every 2 s rays go from 3 points across the panel toward the sun, through blocks nearby (opaque blocks and leaves block it, glass and
/// water do not) and over the terrain further out (mountains, also in unloaded land, up to ~420 blocks); a shaded part still gets 12 % (sky
/// light).
/// </summary>
public sealed class SolarPanelBehavior : BlockBehavior, IEnergyMachine
{
    public const double TrackCost = .02, TurnRate = .4, Diffuse = .12, ShadeEvery = 2, Near = 48, Far = 420;
    static readonly string[] modeNames = { "FIXED (FLAT)", "1-AXIS TRACKING", "2-AXIS TRACKING" };
    static bool IsMain(BlockPos p, int id)
    {
        var d = DEFS[id]; var f = d?.State.Find("part"); return d == null || d.Parts.Length == 0 || f == null || f.Index((uint)p.State) == 0;
    }
    public override void OnPlace(BlockPos p, int id) { if (IsMain(p, id)) BlockStates.EntityOrNew(p, id); }
    public override bool Usable => true;
    public override bool OnUse(BlockPos p, int id, int state)
    {
        var m = Electric.MainPart(p); if (!BlockStates.entities.TryGetValue(m.Key, out var e)) return false;
        int m2 = ((int)e.Num("mode") + 1) % 3; e.SetNum("mode", m2); UI.Note("Solar panel: " + modeNames[m2], 1.5); return true;
    }
    double Rated(int id) => Electric.Key(this, id, "solar", "power", 3);
    double TiltMax(int id) => Electric.Key(this, id, "solar", "tilt_max", 60) * Math.PI / 180;
    /// <summary>The sun in the panel's block frame (x east, y up, z south) and its height above the horizon (sin).</summary>
    static (V3 sun, double sinElev) SunLocal(BlockPos p)
    {
        var (ax, ay, az) = p.Axes(); V3 s = Sky.SUN; return (new V3(M.Dot(s, ax), M.Dot(s, ay), M.Dot(s, az)), M.Dot(s, ay));
    }
    /// <summary>The panel's normal (block frame) at tilt a about the north-south axis (z) after tilt b about the east-west axis (x).</summary>
    public static V3 Normal(double a, double b) => new(-Math.Sin(a) * Math.Cos(b), Math.Cos(a) * Math.Cos(b), Math.Sin(b));
    /// <summary>Sunlight on the panel 0..1 at tilts a (about the north-south axis) and b (about the east-west axis), radians.</summary>
    public static double OnPanel(BlockPos p, double a, double b = 0)
    {
        var (s, el) = SunLocal(p); if (el <= 0) return 0;
        double fade = Math.Clamp(el / .12, 0, 1); fade = fade * fade * (3 - 2 * fade);
        return Math.Max(0, M.Dot(Normal(a, b), s)) * fade;
    }
    /// <summary>Do the panel's corners stay above the ground at these tilts? (lowest corner = pivot height - half width x (|sin a| + |sin b| cos a))</summary>
    static bool Fits(BlockEntity e, double a, double b)
    {
        var d = DEFS[e.BlockId]; V3 pv = d.MovingParts.Length > 0 ? d.MovingParts[0].Pivot : new V3(.5, .5, .5);
        return pv.Y - pv.X * (Math.Abs(Math.Sin(a)) + Math.Abs(Math.Sin(b)) * Math.Cos(a)) >= .04;
    }
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p)
    {
        int id = e.BlockId, mode = (int)e.Num("mode") % 3; bool track = mode > 0; var (s, el) = SunLocal(e.Pos);
        // tracking: turn toward the sun (or back to flat at night / when fixed); 2-axis also tilts north-south (angle2)
        double ta = 0, tb = 0;
        if (track && el > 0)
        {
            tb = mode == 2 ? Math.Clamp(Math.Asin(Math.Clamp(s.Z, -1, 1)), -TiltMax(id), TiltMax(id)) : 0;
            ta = Math.Clamp(Math.Atan2(-s.X, s.Y), -TiltMax(id), TiltMax(id));
            if (!Fits(e, ta, tb)) { double lo = 0, hi = 1; for (int i = 0; i < 12; i++) { double k = (lo + hi) / 2; if (Fits(e, ta * k, tb * k)) lo = k; else hi = k; } ta *= lo; tb *= lo; }
        }
        double a = e.Num("angle"), b = e.Num("angle2");
        a += Math.Clamp(ta - a, -TurnRate * dt, TurnRate * dt); b += Math.Clamp(tb - b, -TurnRate * dt, TurnRate * dt);
        e.SetNum("angle", a); e.SetNum("angle2", b); e.SetNum("speed", 0);
        double t = e.Num("shade_t") - dt; if (t <= 0) { t = ShadeEvery; e.SetNum("sunlit", el > 0 ? Sunlit(e) : 1); } e.SetNum("shade_t", t);
        double lit = e.Num("shade_t") > 0 && e.Numbers.ContainsKey("sunlit") ? e.Num("sunlit") : 1;
        double light = OnPanel(e.Pos, a, b) * (lit + (1 - lit) * Diffuse), pw = Rated(id) * light - (track && light > 0 ? TrackCost * mode * Rated(id) : 0);
        e.SetNum("light", light); p.Fixed = Math.Max(0, pw) * dt;
    }
    public void Settle(BlockEntity e, double dt, double made, double got) => WaterMachines.Show(e, "power", made / dt);
    /// <summary>Share of the panel (3 sample points across it) with a clear line to the sun.</summary>
    public static double Sunlit(BlockEntity e)
    {
        var d = DEFS[e.BlockId]; var p = e.Pos; if (p.Core) return 1;
        V3 pv = d.MovingParts.Length > 0 ? d.MovingParts[0].Pivot : new V3(.5, .5, .5); double half = Math.Max(.5, pv.X) * .7;
        Band B = bands[p.B]; V3 up = M.Nrm(p.Center); int clear = 0;
        foreach (double ox in new[] { -half, 0, half })
        {
            V3 o = Gp(B, p.F, p.Z + pv.Z, p.X + pv.X + ox, B.inner + p.Y + pv.Y) + up * .25;
            if (!Blocked(o, Sky.SUN)) clear++;
        }
        return clear / 3.0;
    }
    /// <summary>Does anything stand between a world point and the sun? Blocks where loaded (Near), the terrain surface elsewhere.</summary>
    public static bool Blocked(V3 o, V3 dir)
    {
        double maxR = R + 320;
        for (double t = .5; t < Far; t += t < Near ? .5 : 2)
        {
            V3 q = o + dir * t; double r = q.Len;
            if (r > maxR) return false; // above every mountain
            if (!BlockPos.TryAt(q.X, q.Y, q.Z, out var c)) return false;
            if (c.Core) return true;
            if (t < Near && c.Loaded) { int id = c.Id; if (id != 0 && (OPQ[id] != 0 || LEAFS[id])) return true; continue; }
            var col = ColsOf(bands[c.B], c.F, c.X >> 5, c.Z >> 5);
            if (bands[c.B].inner + c.Y < R + col.h[(c.X & 31) + 32 * (c.Z & 31)]) return true; // under the ground surface (a hill / mountain)
        }
        return false;
    }
    public override string Describe(BlockEntity e)
    {
        var (_, el) = SunLocal(e.Pos); double lit = e.Numbers.ContainsKey("sunlit") ? e.Num("sunlit") : 1;
        string shade = el <= 0 ? "" : lit <= 0 ? "  IN SHADE" : lit < 1 ? $"  PARTLY SHADED ({(1 - lit) * 100:0}%)" : "";
        return $"{modeNames[(int)e.Num("mode") % 3]}  SUN {Math.Asin(Math.Clamp(el, -1, 1)) * 180 / Math.PI:0} DEG UP{shade}  {e.Num("light") * 100:0}% ON THE PANEL  {e.Num("power"):0.0} / {Rated(e.BlockId):0} E/S";
    }
}
