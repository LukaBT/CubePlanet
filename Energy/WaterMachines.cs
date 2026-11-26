namespace CubePlanet;

/// <summary>
/// Water paths of hydro machines. A machine's "back" is its intake, its "front" (the facing side) its outlet. Either side may be a water /
/// air cell or a pipe (blocks with group tag "liquid_pipe"): water then enters / leaves at the open ends of the connected pipes (a pipe with a
/// single connection opens on the opposite side): the intake takes from the end in water with the highest surface (a penstock reaching into
/// a lake), the outlet uses the highest open end (a pipe column lifting water up to a tank).
/// Energy: one unit of water (1/8 block) dropping one block = <see cref="K"/> E.
/// </summary>
/// <summary>Where a machine takes water from / puts it: a water (or air) cell, or a tank (through its valve).</summary>
public readonly struct WaterEnd
{
    public readonly WC Cell; public readonly Tank Tank;
    /// <summary>Friction of the pipe path to this end: the loss in J/kg is K x (units/s)^2 (0 without pipes).</summary>
    public readonly double K;
    public WaterEnd(WC c, double k = 0) { Cell = c; Tank = null; K = k; }
    public WaterEnd(Tank t, double k = 0) { Cell = default; Tank = t; K = k; }
    public bool Ok => Tank != null || Cell.ok;
    /// <summary>Water surface height as a source (block Y; a tank: its water surface).</summary>
    public double SourceSurface => Tank != null ? Tank.Y0 + Tank.Depth : Surface(Cell);
    /// <summary>Height water must reach to get in (a tank: its water surface; an air cell: its floor).</summary>
    public double OutletSurface => Tank != null ? Tank.Y0 + Tank.Depth : WaterMachines.OutletLevel(Cell);
    /// <summary>Pressure above the outside air at that surface, Pa (a sealed tank's air cushion; open water: 0).</summary>
    public double Gauge => Tank != null ? (Tank.Pressure - 1) * Tank.P0 : 0;
    public int Available => Tank != null ? (int)Math.Floor(Tank.Water + 1e-9) : WaterMachines.Column(Cell);
    public int Room => RoomFor(Liquid > 0 ? Liquid : Water);
    /// <summary>Units of this liquid that still fit (a tank holding another liquid, or a cell of another liquid: none).</summary>
    public int RoomFor(int liquid) => Tank != null ? (Tank.Accepts(liquid) ? Tank.Room() : 0) : WaterMachines.Room(Cell, liquid);
    /// <summary>The liquid there (a liquid cell's block, a tank's contents; 0 for air / an empty tank).</summary>
    public int Liquid => Tank != null ? Tank.Holds : (LIQ[Cell.id] ? Cell.id : 0);
    /// <summary>Density of that liquid, kg/m^3 (water if none).</summary>
    public double Rho => Liquid > 0 ? LiquidDensity[Liquid] : WaterMachines.Rho;
}

public static class WaterMachines
{
    public const double K = .1;
    // ---- hydraulics with real numbers: water 1000 kg/m^3, 1 unit = 125 kg, gravity g = 9.81 m/s^2 x the planet's gravity factor where the
    // machine is (full at the surface, less inside the planet and out in space); 1 E = 12,262 J (one unit falling 10 blocks on the surface; one block gives K = 0.1 E)
    public const double Rho = 1000, G0 = 9.81, UnitKg = 125, Patm = 101325, Pvap = 2340, Npshr = 25000;
    public static double G(BlockPos p) => G0 * GravK(p.Center.Len);
    /// <summary>Energy per kg water gains going from src to dst (J/kg): g x the height difference + the pressure difference / density.</summary>
    public static double SpecificEnergy(WaterEnd src, WaterEnd dst, double g) => g * (dst.OutletSurface - src.SourceSurface) + (dst.Gauge - src.Gauge) / src.Rho;
    /// <summary>E per unit (1/8 m^3) of a liquid of density rho given J/kg: oil (850 kg/m^3) gives less than water for the same drop.</summary>
    public static double EPerUnit(double jPerKg, double rho = Rho) => jPerKg * rho * UnitM3 / Tank.JoulesPerE;
    /// <summary>The highest a pump can pull water up to itself (blocks): the outside air (plus a source's pressure) pushes water up its
    /// intake, less the water's vapour pressure (it would boil) and a margin against cavitation, over rho x g.</summary>
    public static double MaxSuction(double g, double sourceGauge = 0, double rho = Rho) => (Patm + sourceGauge - Pvap - Npshr) / (rho * Math.Max(g, 1e-6));
    public static bool IsPipe(int id) => DEFS[id]?.HasTag("liquid_pipe") ?? false;

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
        foreach (var (pipe, n, pk) in PipeEnds(b, e.Pos))
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
        foreach (var (pipe, n, pk) in PipeEnds(f, e.Pos))
        {
            var c = Cell(n); if (!c.ok || (c.id != Water && !Wfree(c.id))) continue;
            if (n.Y > bestH) { bestH = n.Y; best = c; }
        }
        return best;
    }

    static readonly int[][] N6 = { new[] { 0, 1, 0 }, new[] { 1, 0, 0 }, new[] { -1, 0, 0 }, new[] { 0, 0, 1 }, new[] { 0, 0, -1 }, new[] { 0, -1, 0 } };
    /// <summary>Open ends of the pipes connected to 'start' (not passing through the machine): a pipe joined to exactly one pipe or pipe
    /// machine opens into the cell on the opposite side.</summary>
    // ---- pipe friction (Darcy-Weisbach): a pipe block (1 m) of bore D loses f / D x v^2 / 2 J/kg, v = flow / area; an open end (entry or
    // exit) loses 0.5 x v^2 / 2. With flow in units/s (1 unit = 0.125 m^3) that is K x Q^2, K summed along the path.
    public const double Friction = .015, EndLoss = .5, UnitM3 = .125;
    static readonly Dictionary<int, double> bore = new();
    /// <summary>A pipe's inner diameter in m (definition key "pipe": {"diameter"}; 0.35 if missing).</summary>
    public static double Bore(int id)
    {
        lock (bore)
        {
            if (bore.TryGetValue(id, out var d)) return d;
            d = DEFS[id] != null && DefinitionFile.Get(DEFS[id].Json, "properties.definition.pipe") is { ValueKind: System.Text.Json.JsonValueKind.Object } j ? DefinitionFile.Num(j, "diameter", .35) : .35;
            return bore[id] = d;
        }
    }
    static double VelFactor(double d) { double a = Math.PI * d * d / 4, u = UnitM3 / a; return u * u / 2; } // (v per unit/s)^2 / 2
    public static double PipeK(int id) => Friction / Bore(id) * VelFactor(Bore(id));
    public static double MouthK(int id) => EndLoss * VelFactor(Bore(id));
    /// <summary>Open ends of the pipes connected to 'start' (not passing through the machine): a pipe joined to exactly one pipe or pipe
    /// machine opens into the cell on the opposite side; a pipe into a tank valve ends at the tank. k = the friction of the path there.</summary>
    /// With a liquid given, only pipes that can carry it (<see cref="PipeContents"/>: a single pipe holds one liquid, a dual pipe two).
    static IEnumerable<(BlockPos pipe, BlockPos next, double k)> PipeEnds(BlockPos start, BlockPos machine, int liquid = 0)
    {
        if (liquid > 0 && !BlockStates.entities.ContainsKey(start.Key) && !PipeContents.CanCarry(start, liquid)) yield break;
        var seen = new HashSet<long> { start.Key, machine.Key }; var q = new Queue<BlockPos>(); q.Enqueue(start);
        var pathK = new Dictionary<long, double> { [start.Key] = PipeK(start.Id) };
        while (q.Count > 0 && seen.Count < 1024)
        {
            var p = q.Dequeue(); int links = 0, lx = 0, ly = 0, lz = 0; double kp = pathK[p.Key], km = MouthK(p.Id);
            foreach (var o in N6)
            {
                if (!p.TryOffset(o[0], o[1], o[2], out var n) || !n.Loaded || !IsPipe(n.Id)) continue; // pipes and pipe machines (tag liquid_pipe)
                if (Tanks.IsValve(n.Id) && n.Key != machine.Key) { yield return (p, n, kp + km); continue; } // a pipe into a tank valve: the tank
                links++; lx = o[0]; ly = o[1]; lz = o[2];
                if (!seen.Contains(n.Key) && !BlockStates.entities.ContainsKey(n.Key) && (liquid <= 0 || PipeContents.CanCarry(n, liquid))) { seen.Add(n.Key); pathK[n.Key] = kp + PipeK(n.Id); parentOf[n.Key] = p; q.Enqueue(n); }
            }
            if (links == 1 && p.TryOffset(-lx, -ly, -lz, out var open) && open.Loaded && !IsPipe(open.Id)) yield return (p, open, kp + km);
        }
    }
    // the pipe each searched pipe was reached from (the last search), to claim the path to the chosen end
    static readonly Dictionary<long, BlockPos> parentOf = new();
    static void ClaimPath(BlockPos pipe, BlockPos start, int liquid)
    {
        if (liquid <= 0) return; var p = pipe;
        for (int i = 0; i < 1100; i++)
        {
            if (!BlockStates.entities.ContainsKey(p.Key)) PipeContents.Claim(p, liquid);
            if (p.Key == start.Key || !parentOf.TryGetValue(p.Key, out var up)) break; p = up;
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
        if (LIQ[id]) return new WaterEnd(Cell(b));
        if (!IsPipe(id)) return default;
        WaterEnd best = default; double bestH = double.NegativeInfinity; BlockPos bestPipe = default;
        for (int pass = 0; pass < 2; pass++)
        {   // first any liquid (which is best?), then only the ends that liquid can reach through pipes that may carry it
            int only = pass == 0 ? 0 : best.Liquid; if (pass == 1) { if (only <= 0) return default; best = default; bestH = double.NegativeInfinity; parentOf.Clear(); }
            foreach (var (pipe, n, pk) in PipeEnds(b, e.Pos, only))
            {
                WaterEnd c = ValveAt(n, out var t) ? (t.Water >= 1 ? new WaterEnd(t, pk) : default) : new WaterEnd(Cell(n), pk);
                if (!c.Ok || (c.Tank == null && !LIQ[c.Cell.id]) || (only > 0 && c.Liquid != only)) continue;
                double h = c.SourceSurface + c.Gauge / (c.Rho * Math.Max(G(e.Pos), 1e-6)); if (h > bestH) { bestH = h; best = c; bestPipe = pipe; }
            }
        }
        if (best.Ok) ClaimPath(bestPipe, b, best.Liquid);
        return best;
    }
    /// <summary>Like Outlet, but a tank in front (its valve) or at the end of its pipes counts too (the highest open end wins). Only ends that
    /// can take this liquid (air, the same liquid, a tank that is empty or holds it).</summary>
    public static WaterEnd OutletEnd(BlockEntity e, int liquid = 0)
    {
        if (liquid <= 0) liquid = Water;
        var (dx, dy, dz) = Dir(e);
        if (!e.Pos.TryOffset(dx, dy, dz, out var f) || !f.Loaded) return default;
        if (ValveAt(f, out var tf)) return tf.Accepts(liquid) ? new WaterEnd(tf) : default;
        int id = f.Id;
        if (id == liquid || Wfree(id)) return new WaterEnd(Cell(f));
        if (!IsPipe(id)) return default;
        WaterEnd best = default; double bestH = double.NegativeInfinity; BlockPos bestPipe = default; parentOf.Clear();
        foreach (var (pipe, n, pk) in PipeEnds(f, e.Pos, liquid))
        {
            WaterEnd c = ValveAt(n, out var t) ? (t.Accepts(liquid) ? new WaterEnd(t, pk) : default) : new WaterEnd(Cell(n), pk);
            if (!c.Ok || (c.Tank == null && c.Cell.id != liquid && !Wfree(c.Cell.id))) continue;
            if (n.Y > bestH) { bestH = n.Y; best = c; bestPipe = pipe; }
        }
        if (best.Ok) ClaimPath(bestPipe, f, liquid);
        return best;
    }
    /// <summary>Move between water ends (cells or tanks); returns the units that arrived (the rest goes back).</summary>
    public static int Move(WaterEnd from, WaterEnd to, int n, (int x, int y, int z) dir)
    {
        if (n <= 0 || !from.Ok || !to.Ok) return 0;
        if (from.Tank == null && to.Tank == null) return Move(from.Cell, to.Cell, n, dir);
        int liq = from.Liquid; if (liq <= 0) return 0;
        int took = from.Tank != null ? from.Tank.Take(n) : TakeWater(from.Cell, n); if (took == 0) return 0;
        int put;
        if (to.Tank != null) put = to.Tank.Put(took, liq);
        else { var t2 = Wcell(to.Cell.b, to.Cell.F, to.Cell.X, to.Cell.Y, to.Cell.Z); put = StackAdd(t2, took, liq); if (put < took) put += SpreadAdd(t2, took - put); if (put > 0) WaterFlow.AddCell(to.Cell.k, to.Cell.b, to.Cell.F, to.Cell.X, to.Cell.Y, to.Cell.Z, dir.x, dir.y, dir.z, put); }
        if (put < took) { if (from.Tank != null) from.Tank.Put(took - put, liq); else AddLiquid(Wcell(from.Cell.b, from.Cell.F, from.Cell.X, from.Cell.Y, from.Cell.Z), took - put, liq); }
        if (put > 0 && from.Tank == null) WaterFlow.AddCell(from.Cell.k, from.Cell.b, from.Cell.F, from.Cell.X, from.Cell.Y, from.Cell.Z, dir.x, dir.y, dir.z, put);
        return put;
    }
    /// <summary>Moves up to n units from 'from' to 'to'; returns the units that arrived (the rest goes back).</summary>
    public static int Move(WC from, WC to, int n, (int x, int y, int z) dir)
    {
        if (n <= 0 || !from.ok || !to.ok || !LIQ[from.id]) return 0;
        int liq = from.id; if (to.id != liq && !Wfree(to.id)) return 0;
        int took = TakeWater(from, n); if (took == 0) return 0;
        var t2 = Wcell(to.b, to.F, to.X, to.Y, to.Z); // re-read: taking may have changed it if both are the same column
        int put = StackAdd(t2, took, liq);
        if (put < took) put += SpreadAdd(t2, took - put);
        if (put < took) AddLiquid(Wcell(from.b, from.F, from.X, from.Y, from.Z), took - put, liq);
        if (put > 0)
        {
            WaterFlow.AddCell(from.k, from.b, from.F, from.X, from.Y, from.Z, dir.x, dir.y, dir.z, put);
            WaterFlow.AddCell(to.k, to.b, to.F, to.X, to.Y, to.Z, dir.x, dir.y, dir.z, put);
        }
        return put;
    }
    /// <summary>Units a cell can still take: its own room plus up to 7 cells above (water stacks up over an outlet, then spreads out;
    /// the simulation does not level a 1-unit difference, so counting only the outlet cell could stall a pump with its tank half empty).</summary>
    public static int Room(WC c, int liquid = 0)
    {
        if (liquid <= 0) liquid = LIQ[c.id] ? c.id : Water;
        int n = 0; var c0 = c;
        for (int i = 0; i < 8 && c.ok && (c.id == liquid || Wfree(c.id)) && (i == 0 || Contained(c, liquid)); i++) { n += 8 - Wl(c); c = Wcell(c.b, c.F, c.X, c.Y + 1, c.Z); }
        if (c0.ok && LIQ[c0.id] && c0.id != liquid) return 0;
        foreach (var s in SurfaceAround(c0)) n += 8 - Wl(s);
        return n;
    }
    /// <summary>Water put into this cell stays in its basin: each side is solid, water, or air resting on water (not open air beyond the
    /// basin's walls), so a pump stops when its tank is full instead of spilling over the walls.</summary>
    static bool Contained(WC c, int liquid)
    {
        foreach (var n in WaterSim.N4)
        {
            var s = Wcell(c.b, c.F, c.X + n[0], c.Y, c.Z + n[1]); if (!s.ok) return false;
            if (s.id == liquid || !Wfree(s.id)) continue;
            var below = Wcell(s.b, s.F, s.X, s.Y - 1, s.Z); if (!below.ok || below.id != liquid) return false;
        }
        return true;
    }
    /// <summary>Like AddWater (fill the cell, then the cells above), but only while the water stays contained (see <see cref="Contained"/>).</summary>
    static int StackAdd(WC c, int units, int liquid)
    {
        int placed = 0;
        for (int i = 0; i < 8 && units > 0 && c.ok && (c.id == liquid || Wfree(c.id)) && (i == 0 || Contained(c, liquid)); i++)
        {
            int l = c.id == liquid ? Wl(c) : 0, t = Math.Min(8, l + units);
            if (t > l) { Wput(c, t, liquid); units -= t - l; placed += t - l; }
            c = Wcell(c.b, c.F, c.X, c.Y + 1, c.Z);
        }
        return placed;
    }
    /// <summary>The not-full water cells of the surface row of the water body around an outlet (breadth-first along that row, up to 64
    /// cells): an outlet whose own column is full pushes water into them, lowest first, like water pressed into a tank spreading under
    /// pressure (the simulation alone does not level 1-unit differences).</summary>
    static List<WC> SurfaceAround(WC c)
    {
        var res = new List<WC>(); if (!c.ok || !LIQ[c.id]) return res; int L = c.id;
        var top = c; for (int i = 0; i < 64; i++) { var u = Wcell(top.b, top.F, top.X, top.Y + 1, top.Z); if (!u.ok || u.id != L) break; top = u; }
        var seen = new HashSet<long> { top.k }; var q = new Queue<WC>(); q.Enqueue(top);
        while (q.Count > 0 && seen.Count < 64)
        {
            var s = q.Dequeue(); if (Wl(s) < 8) res.Add(s);
            foreach (var n in WaterSim.N4)
            {
                var nb = Wcell(s.b, s.F, s.X + n[0], s.Y, s.Z + n[1]);
                if (nb.ok && nb.id == L && seen.Add(nb.k)) q.Enqueue(nb);
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
            Wput(s, l + m, s.id); units -= m; put += m;
        }
        return put;
    }
    /// <summary>Units in the water column above (and including) c.</summary>
    public static int Column(WC c)
    {
        int n = 0, L = c.id; if (!c.ok || !LIQ[L]) return 0;
        for (int i = 0; i < 64 && c.ok && c.id == L; i++) { n += Wl(c); c = Wcell(c.b, c.F, c.X, c.Y + 1, c.Z); }
        return n;
    }
    /// <summary>Height of an outlet's water surface (its floor when it is air).</summary>
    public static double OutletLevel(WC c) => LIQ[c.id] ? Surface(c) : c.Y;

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
        var src = WaterMachines.IntakeEnd(e); var dst = src.Ok ? WaterMachines.OutletEnd(e, src.Liquid) : default;
        if (!src.Ok || !dst.Ok) { e.SetNum("head", 0); return; }
        // energy per kg the water gives falling from src to dst: gravity here x the drop + the pressure it leaves behind
        double g = WaterMachines.G(e.Pos), spec = -WaterMachines.SpecificEnergy(src, dst, g);
        e.SetNum("head", spec / Math.Max(g, 1e-6)); e.SetNum("spec", spec); e.SetNum("g", g);
        if (spec < g * .5 || spec < .5) return; // less than half a block's worth
        // pipe friction takes K x Q^2 of it: the turbine runs at the flow giving the most power (friction = a third of the head), at most 16/s
        double k = src.K + dst.K, q = k > 0 ? Math.Min(MaxFlow, Math.Sqrt(spec / (3 * k))) : MaxFlow, net = spec - k * q * q;
        e.SetNum("loss", spec > 0 ? k * q * q / spec : 0);
        double units = Math.Min(q * dt, Math.Min(src.Available, dst.RoomFor(src.Liquid)));
        double perUnit = WaterMachines.EPerUnit(net, src.Rho) * Efficiency, energy = Math.Max(0, units) * perUnit; e.SetNum("per_unit", perUnit);
        if (mode == 1) p.Fixed = energy; else p.Dispatch = energy;
        pending[e.Pos.Key] = (src, dst, perUnit);
    }
    public void Settle(BlockEntity e, double dt, double made, double got)
    {
        int moved = 0;
        if (made > 0 && pending.TryGetValue(e.Pos.Key, out var w))
        {
            // a running total of the water paid for (energy made / energy per unit); whole units move, the fraction waits for the next step
            double acc = e.Num("carry") + made / w.head; int n = (int)acc;
            moved = WaterMachines.Move(w.src, w.dst, n, WaterMachines.Dir(e)); e.SetNum("carry", Math.Clamp(acc - moved, 0, 2));
        }
        WaterMachines.Show(e, "flow", moved / dt); WaterMachines.Show(e, "power", made / dt);
    }
    public override string Describe(BlockEntity e) => $"{WaterMachines.ModeName(e).ToUpperInvariant()}  HEAD {e.Num("head"):0.0} ({e.Num("spec"):0} J/KG, G {e.Num("g"):0.00}, PIPES TAKE {e.Num("loss") * 100:0}%)  FLOW {e.Num("flow"):0.0} U/S  POWER {e.Num("power"):0.0} E/S";
}

/// <summary>
/// Water pump: lifts water from its intake (back) to its outlet (front, or the top of a pipe column) using energy = units x lift x K / 0.75
/// (+ a little to move water at all). At most 16 units a second. Modes: auto = only with spare energy (pumped storage), on = always, off.
/// A pump and a turbine on the same reservoirs give back about 0.8 x 0.75 = 60 % of the energy: storage, never free energy.
/// </summary>
public sealed class PumpBehavior : BlockBehavior, IEnergyMachine
{
    public const double MaxFlow = 16, Efficiency = .75, BaseCost = .02;
    /// <summary>A pump's design (definition key "pump"): centrifugal (an impeller: it gives water up to a fixed energy per kg, so its flow
    /// falls along a pump curve to zero at its maximum head; best efficiency at ~60 % of it) or piston (positive displacement: a steady flow
    /// up to its maximum, then it stops). max_head is in blocks at surface gravity (in lower gravity the same pump lifts higher).</summary>
    public sealed class Spec { public bool Piston; public double MaxHead = 60, Flow = 16, Efficiency = .75; public double MaxSpecific => MaxHead * WaterMachines.G0; }
    static readonly Dictionary<int, Spec> specs = new();
    public static Spec SpecOf(int id)
    {
        lock (specs)
        {
            if (specs.TryGetValue(id, out var s)) return s;
            s = new Spec();
            if (DEFS[id] != null && DefinitionFile.Get(DEFS[id].Json, "properties.definition.pump") is { ValueKind: System.Text.Json.JsonValueKind.Object } j)
            { s.Piston = DefinitionFile.Str(j, "type", "centrifugal") == "piston"; s.MaxHead = DefinitionFile.Num(j, "max_head", 60); s.Flow = DefinitionFile.Num(j, "flow", 16); s.Efficiency = DefinitionFile.Num(j, "efficiency", .75); }
            return specs[id] = s;
        }
    }
    /// <summary>Flow (0..1 of the design flow) and efficiency at a needed energy per kg.</summary>
    public static (double flow, double eff) Curve(Spec s, double spec)
    {
        double x = Math.Max(0, spec) / s.MaxSpecific; if (x >= 1) return (0, s.Efficiency);
        if (s.Piston) return (1, s.Efficiency);
        double q = Math.Sqrt(1 - x), bep = (x - .6) / .65; // flow falls with head; efficiency peaks at 60 % of the maximum head
        return (q, s.Efficiency * Math.Clamp(1 - bep * bep, .3, 1));
    }
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public override bool Usable => true;
    public override bool OnUse(BlockPos p, int id, int state) => WaterMachines.CycleMode(BlockStates.EntityOrNew(p, id));

    static readonly Dictionary<long, (WaterEnd src, WaterEnd dst, double cost)> pending = new();
    /// <summary>What a pump at e can do this step: water from src to dst at cost E per unit, at most units. Real limits: it can only pull
    /// water up to itself as far as the outside air pushes it (suction, ~7.5 blocks at the surface), and push it as far as its design allows
    /// (pump curve / maximum head); a pressurized tank counts as height (its pressure / (rho g)).</summary>
    public static bool Job(BlockEntity e, double dt, out WaterEnd src, out WaterEnd dst, out double cost, out double units)
    {
        src = dst = default; cost = units = 0; e.SetNum("why", 0);
        if (!e.Pos.Loaded) return false; // water only moves in loaded land
        src = WaterMachines.IntakeEnd(e); dst = src.Ok ? WaterMachines.OutletEnd(e, src.Liquid) : default;
        if (!src.Ok || !dst.Ok) return false;
        var s = SpecOf(e.BlockId); double g = WaterMachines.G(e.Pos);
        double spec = WaterMachines.SpecificEnergy(src, dst, g); e.SetNum("lift", spec / Math.Max(g, 1e-6)); e.SetNum("spec", spec); e.SetNum("g", g);
        // suction: the source surface may be at most MaxSuction below the pump
        double suction = e.Pos.Y + .5 - src.SourceSurface, maxS = WaterMachines.MaxSuction(g, src.Gauge, src.Rho); e.SetNum("max_suction", maxS);
        if (suction > maxS) { e.SetNum("why", 1); return false; }
        e.SetNum("max_lift", s.MaxSpecific / Math.Max(g, 1e-6));
        // pipe friction adds K x Q^2: find the flow where the pump's curve meets lift + friction
        double k = src.K + dst.K, q;
        if (s.Piston) { q = s.Flow; if (k > 0 && spec + k * q * q > s.MaxSpecific) q = Math.Sqrt(Math.Max(0, (s.MaxSpecific - spec) / k)); if (spec >= s.MaxSpecific) q = 0; }
        else { q = s.Flow * Curve(s, spec).flow; for (int i = 0; i < 12; i++) q = .5 * q + .5 * s.Flow * Curve(s, spec + k * q * q).flow; }
        double total = spec + k * q * q; var (_, eff) = Curve(s, total); e.SetNum("loss", total > 0 ? k * q * q / total : 0);
        if (q <= 1e-3) { e.SetNum("why", 2); return false; }
        cost = WaterMachines.EPerUnit(Math.Max(0, total), src.Rho) / eff + BaseCost;
        units = Math.Min(q * dt, Math.Min(src.Available, dst.RoomFor(src.Liquid)));
        return units > 0;
    }
    public static string Why(BlockEntity e) => (int)e.Num("why") switch
    {
        1 => $"  CAN'T PULL WATER UP MORE THAN {e.Num("max_suction"):0.0} BLOCKS HERE (THE AIR PUSHES IT UP; PUT THE PUMP LOWER)",
        2 => $"  TOO HIGH: THIS PUMP PUSHES AT MOST {e.Num("max_lift"):0} BLOCKS HERE (OR THE TANK'S PRESSURE IS TOO HIGH)",
        _ => ""
    };
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
            double acc = e.Num("carry") + got / w.cost; int n = (int)acc; // a running total of the water paid for
            moved = WaterMachines.Move(w.src, w.dst, n, WaterMachines.Dir(e)); e.SetNum("carry", Math.Clamp(acc - moved, 0, 2));
        }
        WaterMachines.Show(e, "flow", moved / dt); WaterMachines.Show(e, "power", got / dt);
    }
    public override string Describe(BlockEntity e) => $"{WaterMachines.ModeName(e).ToUpperInvariant()}  LIFT {e.Num("lift"):0.0} OF {e.Num("max_lift"):0} (G {e.Num("g"):0.00})  FLOW {e.Num("flow"):0.0} U/S  USES {e.Num("power"):0.0} E/S" + Why(e);
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
