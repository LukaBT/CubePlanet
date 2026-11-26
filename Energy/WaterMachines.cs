namespace CubePlanet;

/// <summary>
/// Water paths of hydro machines. A machine's "back" is its intake, its "front" (the facing side) its outlet. Either side may be a water /
/// air cell or a pipe (blocks with group tag "water_pipe"): water then enters / leaves at the open ends of the connected pipes (a pipe with a
/// single connection opens on the opposite side): the intake takes from the end in water with the highest surface (a penstock reaching into
/// a lake), the outlet uses the highest open end (a pipe column lifting water up to a tank).
/// Energy: one unit of water (1/8 block) dropping one block = <see cref="K"/> E.
/// </summary>
/// <summary>Where a machine takes water from / puts it: a water (or air) cell, or a tank (through its valve).</summary>
public readonly struct WaterEnd
{
    public readonly WC Cell; public readonly Tank Tank;
    public WaterEnd(WC c) { Cell = c; Tank = null; }
    public WaterEnd(Tank t) { Cell = default; Tank = t; }
    public bool Ok => Tank != null || Cell.ok;
    /// <summary>Surface height as a source (a tank: its water surface + its pressure's head).</summary>
    public double SourceLevel => Tank?.Level ?? Surface(Cell);
    /// <summary>Height water must reach to get in (a tank: its level; an air cell: its floor).</summary>
    public double OutletLevel => Tank?.Level ?? WaterMachines.OutletLevel(Cell);
    public int Available => Tank != null ? (int)Math.Floor(Tank.Water + 1e-9) : WaterMachines.Column(Cell);
    public int Room => Tank?.Room() ?? WaterMachines.Room(Cell);
}

public static class WaterMachines
{
    public const double K = .1;
    public static bool IsPipe(int id) => DEFS[id]?.HasTag("water_pipe") ?? false;

    public static (int x, int y, int z) Dir(BlockEntity e)
    {
        var d = DEFS[e.BlockId]; var f = d?.State.Find("facing");
        string s = f == null ? "north" : f.Values[f.Index((uint)e.Pos.State)];
        return s switch { "east" => (1, 0, 0), "south" => (0, 0, 1), "west" => (-1, 0, 0), "up" => (0, 1, 0), "down" => (0, -1, 0), _ => (0, 0, -1) };
    }
    static WC Cell(BlockPos p) => p.Core ? default : Wcell(p.B, p.F, p.X, p.Y, p.Z);

    /// <summary>The water cell this machine draws from (behind it, or at the end of its pipes); not ok if none.</summary>
    public static WC Intake(BlockEntity e)
    {
        var (dx, dy, dz) = Dir(e);
        if (!e.Pos.TryOffset(-dx, -dy, -dz, out var b) || !b.Loaded) return default;
        int id = b.Id;
        if (id == Water) return Cell(b);
        if (!IsPipe(id)) return default;
        WC best = default; double bestH = double.NegativeInfinity;
        foreach (var (pipe, n) in PipeEnds(b, e.Pos))
        {
            var c = Cell(n); if (!c.ok || c.id != Water) continue;
            double h = Surface(c); if (h > bestH) { bestH = h; best = c; }
        }
        return best;
    }
    /// <summary>The cell water leaves into (in front, or the highest open cell at the end of its pipes); not ok if none.</summary>
    public static WC Outlet(BlockEntity e)
    {
        var (dx, dy, dz) = Dir(e);
        if (!e.Pos.TryOffset(dx, dy, dz, out var f) || !f.Loaded) return default;
        int id = f.Id;
        if (id == Water || Wfree(id)) return Cell(f);
        if (!IsPipe(id)) return default;
        WC best = default; double bestH = double.NegativeInfinity;
        foreach (var (pipe, n) in PipeEnds(f, e.Pos))
        {
            var c = Cell(n); if (!c.ok || (c.id != Water && !Wfree(c.id))) continue;
            if (n.Y > bestH) { bestH = n.Y; best = c; }
        }
        return best;
    }

    static readonly int[][] N6 = { new[] { 0, 1, 0 }, new[] { 1, 0, 0 }, new[] { -1, 0, 0 }, new[] { 0, 0, 1 }, new[] { 0, 0, -1 }, new[] { 0, -1, 0 } };
    /// <summary>Open ends of the pipes connected to 'start' (not passing through the machine): a pipe joined to exactly one pipe or pipe
    /// machine opens into the cell on the opposite side.</summary>
    static IEnumerable<(BlockPos pipe, BlockPos next)> PipeEnds(BlockPos start, BlockPos machine)
    {
        var seen = new HashSet<long> { start.Key, machine.Key }; var q = new Queue<BlockPos>(); q.Enqueue(start);
        while (q.Count > 0 && seen.Count < 1024)
        {
            var p = q.Dequeue(); int links = 0, lx = 0, ly = 0, lz = 0;
            foreach (var o in N6)
            {
                if (!p.TryOffset(o[0], o[1], o[2], out var n) || !n.Loaded || !IsPipe(n.Id)) continue; // pipes and pipe machines (tag water_pipe)
                if (Tanks.IsValve(n.Id) && n.Key != machine.Key) { yield return (p, n); continue; } // a pipe into a tank valve: the tank
                links++; lx = o[0]; ly = o[1]; lz = o[2];
                if (!seen.Contains(n.Key) && !BlockStates.entities.ContainsKey(n.Key)) { seen.Add(n.Key); q.Enqueue(n); }
            }
            if (links == 1 && p.TryOffset(-lx, -ly, -lz, out var open) && open.Loaded && !IsPipe(open.Id)) yield return (p, open);
        }
    }

    static bool ValveAt(BlockPos p, out Tank t) { t = null; if (!p.Loaded || !Tanks.IsValve(p.Id)) return false; t = Tanks.Of(p); return t != null; }
    /// <summary>Like Intake, but a tank behind the machine (its valve) or at the end of its pipes counts too: the highest source wins.</summary>
    public static WaterEnd IntakeEnd(BlockEntity e)
    {
        var (dx, dy, dz) = Dir(e);
        if (!e.Pos.TryOffset(-dx, -dy, -dz, out var b) || !b.Loaded) return default;
        if (ValveAt(b, out var tb)) return new WaterEnd(tb);
        int id = b.Id;
        if (id == Water) return new WaterEnd(Cell(b));
        if (!IsPipe(id)) return default;
        WaterEnd best = default; double bestH = double.NegativeInfinity;
        foreach (var (pipe, n) in PipeEnds(b, e.Pos))
        {
            WaterEnd c = ValveAt(n, out var t) ? (t.Water >= 1 ? new WaterEnd(t) : default) : new WaterEnd(Cell(n));
            if (!c.Ok || (c.Tank == null && c.Cell.id != Water)) continue;
            double h = c.SourceLevel; if (h > bestH) { bestH = h; best = c; }
        }
        return best;
    }
    /// <summary>Like Outlet, but a tank in front (its valve) or at the end of its pipes counts too (the highest open end wins).</summary>
    public static WaterEnd OutletEnd(BlockEntity e)
    {
        var (dx, dy, dz) = Dir(e);
        if (!e.Pos.TryOffset(dx, dy, dz, out var f) || !f.Loaded) return default;
        if (ValveAt(f, out var tf)) return new WaterEnd(tf);
        int id = f.Id;
        if (id == Water || Wfree(id)) return new WaterEnd(Cell(f));
        if (!IsPipe(id)) return default;
        WaterEnd best = default; double bestH = double.NegativeInfinity;
        foreach (var (pipe, n) in PipeEnds(f, e.Pos))
        {
            WaterEnd c = ValveAt(n, out var t) ? new WaterEnd(t) : new WaterEnd(Cell(n));
            if (!c.Ok || (c.Tank == null && c.Cell.id != Water && !Wfree(c.Cell.id))) continue;
            if (n.Y > bestH) { bestH = n.Y; best = c; }
        }
        return best;
    }
    /// <summary>Move between water ends (cells or tanks); returns the units that arrived (the rest goes back).</summary>
    public static int Move(WaterEnd from, WaterEnd to, int n, (int x, int y, int z) dir)
    {
        if (n <= 0 || !from.Ok || !to.Ok) return 0;
        if (from.Tank == null && to.Tank == null) return Move(from.Cell, to.Cell, n, dir);
        int took = from.Tank != null ? from.Tank.Take(n) : TakeWater(from.Cell, n); if (took == 0) return 0;
        int put;
        if (to.Tank != null) put = to.Tank.Put(took);
        else { var t2 = Wcell(to.Cell.b, to.Cell.F, to.Cell.X, to.Cell.Y, to.Cell.Z); put = StackAdd(t2, took); if (put < took) put += SpreadAdd(t2, took - put); if (put > 0) WaterFlow.AddCell(to.Cell.k, to.Cell.b, to.Cell.F, to.Cell.X, to.Cell.Y, to.Cell.Z, dir.x, dir.y, dir.z, put); }
        if (put < took) { if (from.Tank != null) from.Tank.Water += took - put; else AddWater(Wcell(from.Cell.b, from.Cell.F, from.Cell.X, from.Cell.Y, from.Cell.Z), took - put); }
        if (put > 0 && from.Tank == null) WaterFlow.AddCell(from.Cell.k, from.Cell.b, from.Cell.F, from.Cell.X, from.Cell.Y, from.Cell.Z, dir.x, dir.y, dir.z, put);
        return put;
    }
    /// <summary>Moves up to n units from 'from' to 'to'; returns the units that arrived (the rest goes back).</summary>
    public static int Move(WC from, WC to, int n, (int x, int y, int z) dir)
    {
        if (n <= 0 || !from.ok || !to.ok) return 0;
        int took = TakeWater(from, n); if (took == 0) return 0;
        var t2 = Wcell(to.b, to.F, to.X, to.Y, to.Z); // re-read: taking may have changed it if both are the same column
        int put = StackAdd(t2, took);
        if (put < took) put += SpreadAdd(t2, took - put);
        if (put < took) AddWater(Wcell(from.b, from.F, from.X, from.Y, from.Z), took - put);
        if (put > 0)
        {
            WaterFlow.AddCell(from.k, from.b, from.F, from.X, from.Y, from.Z, dir.x, dir.y, dir.z, put);
            WaterFlow.AddCell(to.k, to.b, to.F, to.X, to.Y, to.Z, dir.x, dir.y, dir.z, put);
        }
        return put;
    }
    /// <summary>Units a cell can still take: its own room plus up to 7 cells above (water stacks up over an outlet, then spreads out;
    /// the simulation does not level a 1-unit difference, so counting only the outlet cell could stall a pump with its tank half empty).</summary>
    public static int Room(WC c)
    {
        int n = 0; var c0 = c;
        for (int i = 0; i < 8 && c.ok && (c.id == Water || Wfree(c.id)) && (i == 0 || Contained(c)); i++) { n += 8 - Wl(c); c = Wcell(c.b, c.F, c.X, c.Y + 1, c.Z); }
        foreach (var s in SurfaceAround(c0)) n += 8 - Wl(s);
        return n;
    }
    /// <summary>Water put into this cell stays in its basin: each side is solid, water, or air resting on water (not open air beyond the
    /// basin's walls), so a pump stops when its tank is full instead of spilling over the walls.</summary>
    static bool Contained(WC c)
    {
        foreach (var n in WaterSim.N4)
        {
            var s = Wcell(c.b, c.F, c.X + n[0], c.Y, c.Z + n[1]); if (!s.ok) return false;
            if (s.id == Water || !Wfree(s.id)) continue;
            var below = Wcell(s.b, s.F, s.X, s.Y - 1, s.Z); if (!below.ok || below.id != Water) return false;
        }
        return true;
    }
    /// <summary>Like AddWater (fill the cell, then the cells above), but only while the water stays contained (see <see cref="Contained"/>).</summary>
    static int StackAdd(WC c, int units)
    {
        int placed = 0;
        for (int i = 0; i < 8 && units > 0 && c.ok && (c.id == Water || Wfree(c.id)) && (i == 0 || Contained(c)); i++)
        {
            int l = Wl(c), t = Math.Min(8, l + units);
            if (t > l) { Wput(c, t); units -= t - l; placed += t - l; }
            c = Wcell(c.b, c.F, c.X, c.Y + 1, c.Z);
        }
        return placed;
    }
    /// <summary>The not-full water cells of the surface row of the water body around an outlet (breadth-first along that row, up to 64
    /// cells): an outlet whose own column is full pushes water into them, lowest first, like water pressed into a tank spreading under
    /// pressure (the simulation alone does not level 1-unit differences).</summary>
    static List<WC> SurfaceAround(WC c)
    {
        var res = new List<WC>(); if (!c.ok || c.id != Water) return res;
        var top = c; for (int i = 0; i < 64; i++) { var u = Wcell(top.b, top.F, top.X, top.Y + 1, top.Z); if (!u.ok || u.id != Water) break; top = u; }
        var seen = new HashSet<long> { top.k }; var q = new Queue<WC>(); q.Enqueue(top);
        while (q.Count > 0 && seen.Count < 64)
        {
            var s = q.Dequeue(); if (Wl(s) < 8) res.Add(s);
            foreach (var n in WaterSim.N4)
            {
                var nb = Wcell(s.b, s.F, s.X + n[0], s.Y, s.Z + n[1]);
                if (nb.ok && nb.id == Water && seen.Add(nb.k)) q.Enqueue(nb);
            }
        }
        res.Sort((a, b) => Wl(a).CompareTo(Wl(b))); return res;
    }
    static int SpreadAdd(WC c, int units)
    {
        int put = 0;
        foreach (var s in SurfaceAround(c))
        {
            if (units <= 0) break;
            int l = Wl(s), m = Math.Min(units, 8 - l); if (m <= 0) continue;
            Wput(s, l + m); units -= m; put += m;
        }
        return put;
    }
    /// <summary>Units in the water column above (and including) c.</summary>
    public static int Column(WC c)
    {
        int n = 0;
        for (int i = 0; i < 64 && c.ok && c.id == Water; i++) { n += Wl(c); c = Wcell(c.b, c.F, c.X, c.Y + 1, c.Z); }
        return n;
    }
    /// <summary>Height of an outlet's water surface (its floor when it is air).</summary>
    public static double OutletLevel(WC c) => c.id == Water ? Surface(c) : c.Y;

    public static string ModeName(BlockEntity e) => (int)e.Num("mode") switch { 1 => "on", 2 => "off", _ => "auto" };
    public static bool CycleMode(BlockEntity e) { e.SetNum("mode", ((int)e.Num("mode") + 1) % 3); UI.Note($"{DEFS[e.BlockId].Name}: {ModeName(e)}", 1.5); return true; }
    /// <summary>Smoothed readings for the HUD (per second).</summary>
    public static void Show(BlockEntity e, string key, double perSecond) => e.SetNum(key, e.Num(key) + (perSecond - e.Num(key)) * .3);
}

/// <summary>
/// Water turbine: water flows from its intake (back) to its outlet (front) and makes energy = units x head x K x 0.8 (head = height of the
/// intake water surface over the outlet's). At most 16 units (2 blocks) a second. Modes (right click): auto = runs only when its network needs
/// energy (a dam keeps its water until then: that is the storage), on = always runs, off.
/// </summary>
public sealed class TurbineBehavior : BlockBehavior, IEnergyMachine
{
    public const double MaxFlow = 16, Efficiency = .8;
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public override bool Usable => true;
    public override bool OnUse(BlockPos p, int id, int state) => WaterMachines.CycleMode(BlockStates.EntityOrNew(p, id));

    // what this step can do (set in Plan, used in Settle)
    static readonly Dictionary<long, (WaterEnd src, WaterEnd dst, double head)> pending = new();
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p)
    {
        pending.Remove(e.Pos.Key);
        if (!e.Pos.Loaded) return; // water only moves in loaded land
        int mode = (int)e.Num("mode"); if (mode == 2) { e.SetNum("head", 0); return; }
        var src = WaterMachines.IntakeEnd(e); var dst = WaterMachines.OutletEnd(e);
        if (!src.Ok || !dst.Ok) { e.SetNum("head", 0); return; }
        double head = src.SourceLevel - dst.OutletLevel; e.SetNum("head", head); // a pressurized tank adds 10.2 blocks per bar
        if (head < .5) return;
        double units = Math.Min(MaxFlow * dt + e.Num("carry"), Math.Min(src.Available, dst.Room));
        double energy = Math.Max(0, units) * head * WaterMachines.K * Efficiency;
        if (mode == 1) p.Fixed = energy; else p.Dispatch = energy;
        pending[e.Pos.Key] = (src, dst, head);
    }
    public void Settle(BlockEntity e, double dt, double made, double got)
    {
        int moved = 0;
        if (made > 0 && pending.TryGetValue(e.Pos.Key, out var w))
        {
            double units = made / (w.head * WaterMachines.K * Efficiency) + e.Num("carry");
            int n = (int)units; e.SetNum("carry", Math.Min(1, units - n));
            moved = WaterMachines.Move(w.src, w.dst, n, WaterMachines.Dir(e));
        }
        WaterMachines.Show(e, "flow", moved / dt); WaterMachines.Show(e, "power", made / dt);
    }
    public override string Describe(BlockEntity e) => $"{WaterMachines.ModeName(e).ToUpperInvariant()}  HEAD {e.Num("head"):0.0}  FLOW {e.Num("flow"):0.0} U/S  POWER {e.Num("power"):0.0} E/S";
}

/// <summary>
/// Water pump: lifts water from its intake (back) to its outlet (front, or the top of a pipe column) using energy = units x lift x K / 0.75
/// (+ a little to move water at all). At most 16 units a second. Modes: auto = only with spare energy (pumped storage), on = always, off.
/// A pump and a turbine on the same reservoirs give back about 0.8 x 0.75 = 60 % of the energy: storage, never free energy.
/// </summary>
public sealed class PumpBehavior : BlockBehavior, IEnergyMachine
{
    public const double MaxFlow = 16, Efficiency = .75, BaseCost = .02;
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public override bool Usable => true;
    public override bool OnUse(BlockPos p, int id, int state) => WaterMachines.CycleMode(BlockStates.EntityOrNew(p, id));

    static readonly Dictionary<long, (WaterEnd src, WaterEnd dst, double cost)> pending = new();
    /// <summary>What a pump at e can do this step: water from src to dst at cost E per unit, at most units (into a pressurized tank the
    /// lift includes the pressure's head: pushing against 10 bar is like lifting 102 blocks).</summary>
    public static bool Job(BlockEntity e, double dt, out WaterEnd src, out WaterEnd dst, out double cost, out double units)
    {
        src = dst = default; cost = units = 0;
        if (!e.Pos.Loaded) return false; // water only moves in loaded land
        src = WaterMachines.IntakeEnd(e); dst = WaterMachines.OutletEnd(e);
        if (!src.Ok || !dst.Ok) return false;
        double lift = Math.Max(0, dst.OutletLevel - src.SourceLevel); e.SetNum("lift", lift);
        cost = (lift * WaterMachines.K + BaseCost) / Efficiency;
        units = Math.Min(MaxFlow * dt + e.Num("carry"), Math.Min(src.Available, dst.Room));
        return units > 0;
    }
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p)
    {
        pending.Remove(e.Pos.Key);
        if (!e.Pos.Loaded) return; // water only moves in loaded land
        int mode = (int)e.Num("mode"); if (mode == 2) return;
        if (!Job(e, dt, out var src, out var dst, out double cost, out double units)) return;
        if (mode == 1) p.Demand = units * cost; else p.Flexible = units * cost;
        pending[e.Pos.Key] = (src, dst, cost);
    }
    public void Settle(BlockEntity e, double dt, double made, double got)
    {
        int moved = 0;
        if (got > 0 && pending.TryGetValue(e.Pos.Key, out var w))
        {
            double units = got / w.cost + e.Num("carry");
            int n = (int)units; e.SetNum("carry", Math.Min(1, units - n));
            moved = WaterMachines.Move(w.src, w.dst, n, WaterMachines.Dir(e));
        }
        WaterMachines.Show(e, "flow", moved / dt); WaterMachines.Show(e, "power", got / dt);
    }
    public override string Describe(BlockEntity e) => $"{WaterMachines.ModeName(e).ToUpperInvariant()}  LIFT {e.Num("lift"):0.0}  FLOW {e.Num("flow"):0.0} U/S  USES {e.Num("power"):0.0} E/S";
}

/// <summary>Battery: stores up to 2000 E, charges / discharges at up to 50 E/s, 95 % efficient each way.</summary>
public sealed class BatteryBehavior : BlockBehavior, IEnergyMachine
{
    public const double Capacity = 2000, Rate = 50, Efficiency = .95;
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p)
    {
        double s = e.Num("energy");
        p.StoreIn = Math.Max(0, Math.Min(Rate * dt, (Capacity - s) / Efficiency));
        p.StoreOut = Math.Max(0, Math.Min(Rate * dt, s * Efficiency));
    }
    public void Settle(BlockEntity e, double dt, double made, double got)
    {
        e.SetNum("energy", Math.Clamp(e.Num("energy") + got * Efficiency - made / Efficiency, 0, Capacity));
        WaterMachines.Show(e, "power", (got - made) / dt);
    }
    public override string Describe(BlockEntity e) => $"{e.Num("energy"):0} / {Capacity:0} E  {(e.Num("power") >= 0 ? "+" : "")}{e.Num("power"):0.0} E/S";
}

/// <summary>
/// Drain: takes water from the cell in front of it (up to 32 units a second) back into the water cycle's reservoir, the opposite of a
/// spring: where a turbine's water goes when there is no river or sea to take it.
/// </summary>
public sealed class DrainBehavior : BlockBehavior
{
    public const double Rate = 32;
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public override void OnTick(BlockEntity e, double dt)
    {
        var src = WaterMachines.Outlet(e); if (!src.ok || src.id != Water) { WaterMachines.Show(e, "flow", 0); return; }
        double want = Rate * dt + e.Num("carry"); int n = (int)want; e.SetNum("carry", Math.Min(1, want - n));
        int took = TakeWater(src, n); WaterSim.Reservoir += took;
        var (dx, dy, dz) = WaterMachines.Dir(e);
        WaterFlow.AddCell(src.k, src.b, src.F, src.X, src.Y, src.Z, -dx, -dy, -dz, took);
        WaterMachines.Show(e, "flow", took / dt);
    }
    public override string Describe(BlockEntity e) => $"DRAINS {e.Num("flow"):0.0} U/S";
}

/// <summary>
/// Spring: water comes out of its front at a set rate (right click: 1, 2, 4, 8, 16 units a second), taken from the water cycle's reservoir
/// (the sea takes it back when it gets there), so rivers and waterfalls keep flowing without creating water.
/// </summary>
public sealed class SpringBehavior : BlockBehavior
{
    static readonly double[] Rates = { 1, 2, 4, 8, 16 };
    public override void OnPlace(BlockPos p, int id) { var e = BlockStates.EntityOrNew(p, id); if (e.Num("rate") <= 0) e.SetNum("rate", 4); }
    public override bool Usable => true;
    public override bool OnUse(BlockPos p, int id, int state)
    {
        var e = BlockStates.EntityOrNew(p, id); int i = Array.IndexOf(Rates, e.Num("rate"));
        e.SetNum("rate", Rates[(i + 1) % Rates.Length]); UI.Note($"Spring: {e.Num("rate")} units/s", 1.5); return true;
    }
    public override void OnTick(BlockEntity e, double dt)
    {
        var dst = WaterMachines.Outlet(e); if (!dst.ok) return;
        double want = e.Num("rate") * dt + e.Num("carry"); int n = (int)Math.Min(want, WaterSim.Reservoir);
        e.SetNum("carry", Math.Min(1, want - n));
        if (n <= 0) return;
        int put = AddWater(dst, n); WaterSim.Reservoir -= put;
        WaterFlow.AddCell(dst.k, dst.b, dst.F, dst.X, dst.Y, dst.Z, WaterMachines.Dir(e).x, WaterMachines.Dir(e).y, WaterMachines.Dir(e).z, put);
    }
    public override string Describe(BlockEntity e) => $"{e.Num("rate"):0} U/S";
}
