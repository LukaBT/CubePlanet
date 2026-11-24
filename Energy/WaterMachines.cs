namespace CubePlanet;

/// <summary>
/// Water paths of hydro machines. A machine's "back" is its intake, its "front" (the facing side) its outlet. Either side may be a water /
/// air cell or a pipe (blocks with group tag "water_pipe"): water then enters / leaves at the open ends of the connected pipes (a pipe with a
/// single connection opens on the opposite side): the intake takes from the end in water with the highest surface (a penstock reaching into
/// a lake), the outlet uses the highest open end (a pipe column lifting water up to a tank).
/// Energy: one unit of water (1/8 block) dropping one block = <see cref="K"/> E.
/// </summary>
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
                links++; lx = o[0]; ly = o[1]; lz = o[2];
                if (!seen.Contains(n.Key) && !BlockStates.entities.ContainsKey(n.Key)) { seen.Add(n.Key); q.Enqueue(n); }
            }
            if (links == 1 && p.TryOffset(-lx, -ly, -lz, out var open) && open.Loaded && !IsPipe(open.Id)) yield return (p, open);
        }
    }

    /// <summary>Moves up to n units from 'from' to 'to'; returns the units that arrived (the rest goes back).</summary>
    public static int Move(WC from, WC to, int n, (int x, int y, int z) dir)
    {
        if (n <= 0 || !from.ok || !to.ok) return 0;
        int took = TakeWater(from, n); if (took == 0) return 0;
        var t2 = Wcell(to.b, to.F, to.X, to.Y, to.Z); // re-read: taking may have changed it if both are the same column
        int put = AddWater(t2, took);
        if (put < took) AddWater(Wcell(from.b, from.F, from.X, from.Y, from.Z), took - put);
        if (put > 0)
        {
            WaterFlow.AddCell(from.k, from.b, from.F, from.X, from.Y, from.Z, dir.x, dir.y, dir.z, put);
            WaterFlow.AddCell(to.k, to.b, to.F, to.X, to.Y, to.Z, dir.x, dir.y, dir.z, put);
        }
        return put;
    }
    /// <summary>Units a cell can still take (its own room plus the cell above).</summary>
    public static int Room(WC c)
    {
        if (!c.ok || (c.id != Water && !Wfree(c.id))) return 0;
        var u = Wcell(c.b, c.F, c.X, c.Y + 1, c.Z);
        return 8 - Wl(c) + (u.ok && (u.id == Water || Wfree(u.id)) ? 8 - Wl(u) : 0);
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
    static readonly Dictionary<long, (WC src, WC dst, double head)> pending = new();
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p)
    {
        pending.Remove(e.Pos.Key);
        int mode = (int)e.Num("mode"); if (mode == 2) { e.SetNum("head", 0); return; }
        var src = WaterMachines.Intake(e); var dst = WaterMachines.Outlet(e);
        if (!src.ok || !dst.ok) { e.SetNum("head", 0); return; }
        double head = Surface(src) - WaterMachines.OutletLevel(dst); e.SetNum("head", head);
        if (head < .5) return;
        double units = Math.Min(MaxFlow * dt + e.Num("carry"), Math.Min(WaterMachines.Column(src), WaterMachines.Room(dst)));
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

    static readonly Dictionary<long, (WC src, WC dst, double cost)> pending = new();
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p)
    {
        pending.Remove(e.Pos.Key);
        int mode = (int)e.Num("mode"); if (mode == 2) return;
        var src = WaterMachines.Intake(e); var dst = WaterMachines.Outlet(e);
        if (!src.ok || !dst.ok) return;
        double lift = Math.Max(0, WaterMachines.OutletLevel(dst) - Surface(src)); e.SetNum("lift", lift);
        double cost = (lift * WaterMachines.K + BaseCost) / Efficiency;
        double units = Math.Min(MaxFlow * dt + e.Num("carry"), Math.Min(WaterMachines.Column(src), WaterMachines.Room(dst)));
        if (units <= 0) return;
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
