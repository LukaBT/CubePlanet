using System.Text.Json.Nodes;

namespace CubePlanet;

/// <summary>
/// A sealed liquid tank (multiblock): a cuboid room up to 12 x 12 x 12 whose six faces are tank blocks (group tag "liquid_tank_wall": tank walls,
/// tank windows, tank valves). It holds water and an air cushion; 1 block = 1 m^3 = 8 units, 1 unit = 125 l. Real physics:
/// <list type="bullet">
/// <item>The air follows Boyle's law (isothermal): p x V_air = Air (m^3 of air at 1 bar). Pumping water into a sealed tank squeezes the
/// air and raises the pressure; a compressor adds air.</item>
/// <item>Water itself barely compresses (bulk modulus 2.2 GPa: 0.45 % at 100 bar), so pressure stores energy, not water: every bar
/// above the air outside is 10.2 m of extra head for a turbine (and costs a pump the same to push against).</item>
/// <item>A safety valve lets air out above the tank's rating (20 bar). VENTED (right click on a valve): open to the air, always 1 bar,
/// fills to the brim.</item>
/// <item>Breaking a face or putting a block inside bursts it: the water spills into the room.</item>
/// </list>
/// The water is not world water while it is in the tank; the renderer draws its level behind tank windows.
/// </summary>
public sealed class Tank
{
    public const double Rating = 20, BulkModulus = 2.2e9, P0 = 1e5, Unit = .125, HeadPerBar = P0 / (1000 * 9.81), JoulesPerE = 125 * 9.81 / WaterMachines.K;
    public int B, F, X0, Y0, Z0, X1, Y1, Z1;
    /// <summary>Units of liquid in it (1 unit = 125 l) and which liquid (one at a time; 0 = water for old saves / tests).</summary>
    public double Water, Air; public bool Vent = true; public int Liquid;
    /// <summary>The liquid it holds (0 when empty).</summary>
    public int Holds => Water >= 1e-9 ? (Liquid > 0 ? Liquid : CubePlanet.Blocks.Water) : 0;
    public double Rho => Holds > 0 ? LiquidDensity[Holds] : 1000;
    /// <summary>Can this liquid go in (empty, or the same liquid)?</summary>
    public bool Accepts(int liquid) => Holds == 0 || Holds == liquid;
    public int W => X1 - X0 + 1; public int H => Y1 - Y0 + 1; public int D => Z1 - Z0 + 1;
    public double Volume => W * H * D; public double Foot => W * D;
    public long Key => new BlockPos(false, B, F, X0, Y0, Z0).Key;
    public BlockPos Anchor => new(false, B, F, X0, Y0, Z0);
    /// <summary>Water volume in m^3 at pressure p (it shrinks a little under pressure).</summary>
    double WaterVol(double p) => Water * Unit / (1 + (p - 1) * P0 / BulkModulus);
    /// <summary>Pressure in bar (absolute).</summary>
    public double Pressure
    {
        get
        {
            if (Vent) return 1;
            if (Air <= 1e-9) { double over = Water * Unit / Volume - 1; return over > 0 ? 1 + over * BulkModulus / P0 : 1; }
            // p x V_air(p) = Air, where V_air = Volume - water volume (which shrinks a little as p rises): the left side only grows with p,
            // so bisect (on a log scale). A plain fixed-point iteration swings between huge and tiny values when the tank is nearly full.
            double lo = 1e-6, hi = 1e7;
            for (int i = 0; i < 100; i++)
            {
                double mid = Math.Sqrt(lo * hi);
                if (mid * Math.Max(0, Volume - WaterVol(mid)) < Air) lo = mid; else hi = mid;
            }
            return Math.Sqrt(lo * hi);
        }
    }
    /// <summary>The water surface's height in the tank (blocks above its floor).</summary>
    public double Depth => Math.Min(H, WaterVol(Pressure) / Foot);
    /// <summary>Gravity where the tank is (m/s^2).</summary>
    public double G => WaterMachines.G(Anchor);
    /// <summary>The pressure as height of water here (blocks): (p - 1 bar) / (rho g); 10.2 blocks per bar on the surface, more in low gravity.</summary>
    public double PressureHead => (Pressure - 1) * P0 / (Rho * Math.Max(G, 1e-6));
    /// <summary>The level a turbine / pump effectively sees (block Y): the water surface plus the pressure's head.</summary>
    public double Level => Y0 + Depth + PressureHead;
    /// <summary>Units of water that still fit before the pressure reaches the rating.</summary>
    public int Room()
    {
        double vmax = Vent ? Volume : (Volume - Air / Rating) * (1 + (Rating - 1) * P0 / BulkModulus);
        return Math.Max(0, (int)Math.Floor(vmax / Unit - Water + 1e-9));
    }
    public int Take(int n) { int t = Math.Max(0, Math.Min(n, (int)Math.Floor(Water + 1e-9))); Water -= t; if (Water < 1e-9) Liquid = 0; Settle(); return t; }
    public int Put(int n, int liquid = 0)
    {
        if (liquid <= 0) liquid = CubePlanet.Blocks.Water; if (!Accepts(liquid)) return 0;
        int t = Math.Max(0, Math.Min(n, Room())); if (t > 0) Liquid = liquid; Water += t; Settle(); return t;
    }
    /// <summary>A vented tank breathes (1 bar); a sealed one vents air above its rating (safety valve).</summary>
    public void Settle()
    {
        if (Vent) { Air = Math.Max(0, Volume - Water * Unit); return; }
        double p = Pressure; if (p > Rating && Air > 0) Air = Rating * Math.Max(0, Volume - WaterVol(Rating));
    }

    /// <summary>Usable energy (E) of the compressed air: the work it does expanding to 1 bar, minus pushing the outside air away
    /// (isothermal): p0 (n ln p - (n - V_air)), n = Air (m^3 at 1 bar), V_air = n / p.</summary>
    public double AirEnergy { get { double p = Pressure; return p > 1 ? P0 * (Air * Math.Log(p) - (Air - Air / p)) / JoulesPerE : 0; } }
    public bool Contains(BlockPos p) => !p.Core && p.B == B && p.F == F && p.X >= X0 - 1 && p.X <= X1 + 1 && p.Y >= Y0 - 1 && p.Y <= Y1 + 1 && p.Z >= Z0 - 1 && p.Z <= Z1 + 1;
    public string Describe() =>
        $"TANK {W}X{H}X{D} ({Volume * 8:0} UNITS)  {(Holds > 0 ? DEFS[Holds].Name.ToUpperInvariant() : "EMPTY")} {Water:0} ({Water * Unit / Volume * 100:0}%)  {(Vent ? "VENTED" : "SEALED")} {Pressure:0.00} BAR" +
        (Vent ? "" : $"  = {PressureHead:0} BLOCKS OF HEAD HERE  AIR STORES {AirEnergy:0} E") + $"  LEVEL Y {Level:0.0}";
}

public static class Tanks
{
    public const int MaxSide = 12;
    public static readonly Dictionary<long, Tank> All = new();
    public static bool IsWall(int id) => DEFS[id]?.HasTag("liquid_tank_wall") ?? false;
    public static bool IsValve(int id) => BEH[id] is TankValveBehavior;
    /// <summary>The tank whose shell holds this block (a valve), if any.</summary>
    public static Tank Of(BlockPos p) { foreach (var t in All.Values) if (t.Contains(p)) return t; return null; }

    /// <summary>Forms a tank behind a valve: the empty cuboid room on one of its sides, every face of it tank blocks.</summary>
    public static Tank Form(BlockPos valve)
    {
        if (valve.Core || !valve.Loaded) return null;
        var old = Of(valve); if (old != null) return old;
        foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
        {
            if (!valve.TryOffset(dx, dy, dz, out var q) || !q.Loaded || !Hollow(q)) continue;
            // flood the room (air / water), at most 12^3 cells
            var seen = new HashSet<long> { q.Key }; var list = new List<BlockPos> { q }; bool ok = true;
            for (int i = 0; i < list.Count && ok; i++)
                foreach (var (ex, ey, ez) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
                {
                    if (!list[i].TryOffset(ex, ey, ez, out var n) || !n.Loaded) { ok = false; break; }
                    if (!Hollow(n) || !seen.Add(n.Key)) continue;
                    if (n.B != q.B || n.F != q.F || list.Count >= MaxSide * MaxSide * MaxSide) { ok = false; break; }
                    list.Add(n);
                }
            if (!ok) continue;
            var t = new Tank { B = q.B, F = q.F, X0 = list.Min(c => c.X), X1 = list.Max(c => c.X), Y0 = list.Min(c => c.Y), Y1 = list.Max(c => c.Y), Z0 = list.Min(c => c.Z), Z1 = list.Max(c => c.Z) };
            if (list.Count != (int)t.Volume || t.W > MaxSide || t.H > MaxSide || t.D > MaxSide || !ShellOk(t)) continue;
            // liquid already in the room becomes the tank's (one liquid: the first found)
            foreach (var c in list) { var w = Wcell(c.B, c.F, c.X, c.Y, c.Z); if (w.ok && LIQ[w.id] && (t.Liquid == 0 || t.Liquid == w.id)) { t.Liquid = w.id; t.Water += Wl(w); Wput(w, 0); } }
            t.Settle(); All[t.Key] = t; return t;
        }
        return null;
    }
    static bool Hollow(BlockPos p) { int id = p.Id; return id == 0 || LIQ[id]; }
    /// <summary>Every face of the room is tank blocks (edges and corners do not matter).</summary>
    static bool ShellOk(Tank t)
    {
        for (int y = t.Y0; y <= t.Y1; y++) for (int z = t.Z0; z <= t.Z1; z++) { if (!Wall(t, t.X0 - 1, y, z) || !Wall(t, t.X1 + 1, y, z)) return false; }
        for (int y = t.Y0; y <= t.Y1; y++) for (int x = t.X0; x <= t.X1; x++) { if (!Wall(t, x, y, t.Z0 - 1) || !Wall(t, x, y, t.Z1 + 1)) return false; }
        for (int z = t.Z0; z <= t.Z1; z++) for (int x = t.X0; x <= t.X1; x++) { if (!Wall(t, x, t.Y0 - 1, z) || !Wall(t, x, t.Y1 + 1, z)) return false; }
        return true;
    }
    static bool Wall(Tank t, int x, int y, int z) => IsWall(new BlockPos(false, t.B, t.F, x, y, z).Id);
    static bool Intact(Tank t)
    {
        if (!ShellOk(t)) return false;
        for (int y = t.Y0; y <= t.Y1; y++) for (int z = t.Z0; z <= t.Z1; z++) for (int x = t.X0; x <= t.X1; x++) if (new BlockPos(false, t.B, t.F, x, y, z).Id != 0) return false;
        return true;
    }
    static double clock;
    /// <summary>Once a second: loaded tanks whose shell is broken (or with a block inside) burst and spill their water into the room.</summary>
    public static void Tick(double dt)
    {
        clock += dt; if (clock < 1) return; clock = 0;
        foreach (var t in All.Values.ToArray())
        {
            if (!t.Anchor.Loaded || !new BlockPos(false, t.B, t.F, t.X1, t.Y1, t.Z1).Loaded) continue;
            if (!Intact(t)) Burst(t);
        }
    }
    public static void Burst(Tank t)
    {
        All.Remove(t.Key); double left = t.Water; int liq = t.Holds > 0 ? t.Holds : Water;
        for (int y = t.Y0; y <= t.Y1 && left >= 1; y++) for (int z = t.Z0; z <= t.Z1 && left >= 1; z++) for (int x = t.X0; x <= t.X1 && left >= 1; x++)
                {
                    var c = Wcell(t.B, t.F, x, y, z); if (!c.ok || !(Wfree(c.id) || c.id == liq)) continue;
                    int put = (int)Math.Min(8 - (c.id == liq ? Wl(c) : 0), Math.Floor(left)); if (put <= 0) continue;
                    AddLiquid(c, put, liq); left -= put;
                }
        if (liq == Water) WaterSim.Reservoir += left; // (the last fractions)
    }
    public static JsonArray Save()
    {
        var r = new JsonArray();
        foreach (var t in All.Values) r.Add(new JsonObject { ["b"] = t.B, ["f"] = t.F, ["min"] = new JsonArray { t.X0, t.Y0, t.Z0 }, ["max"] = new JsonArray { t.X1, t.Y1, t.Z1 }, ["water"] = t.Water, ["air"] = t.Air, ["vent"] = t.Vent, ["liquid"] = t.Holds > 0 ? DEFS[t.Holds].FullName : "" });
        return r;
    }
    public static void Load(JsonNode n)
    {
        All.Clear(); if (n is not JsonArray a) return;
        foreach (var x in a)
        {
            var t = new Tank { B = (int)x["b"], F = (int)x["f"], X0 = (int)x["min"][0], Y0 = (int)x["min"][1], Z0 = (int)x["min"][2], X1 = (int)x["max"][0], Y1 = (int)x["max"][1], Z1 = (int)x["max"][2], Water = (double)x["water"], Air = (double)x["air"], Vent = (bool)x["vent"] };
            string ln = (string)x["liquid"] ?? ""; if (ln.Length > 0 && GameAssets.BlockReg.Has(ln)) t.Liquid = GameAssets.BlockReg.Id(ln);
            All[t.Key] = t;
        }
    }
    public static void Reset() { All.Clear(); PipeContents.Reset(); }
}

/// <summary>Tank valve: part of a tank's shell where pipes and machines connect; forms the tank (right after placing, then every second);
/// right click: VENTED / SEALED. The HUD shows the tank.</summary>
public sealed class TankValveBehavior : BlockBehavior
{
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public override bool Usable => true;
    public override bool OnUse(BlockPos p, int id, int state)
    {
        var t = Tanks.Of(p) ?? Tanks.Form(p); if (t == null) { UI.Note("No tank: the valve must be in a face of a closed cuboid room (up to 12x12x12) walled with tank blocks", 3); return true; }
        t.Vent = !t.Vent; t.Settle(); UI.Note(t.Vent ? "Tank VENTED (1 bar, fills to the brim)" : "Tank SEALED (the air cushion holds pressure)", 2); return true;
    }
    public override void OnTick(BlockEntity e, double dt)
    {
        double c = e.Num("form_t") - dt; if (c > 0) { e.SetNum("form_t", c); return; }
        e.SetNum("form_t", 1); if (e.Pos.Loaded && Tanks.Of(e.Pos) == null) Tanks.Form(e.Pos);
    }
    public override string Describe(BlockEntity e) => Tanks.Of(e.Pos)?.Describe() ?? "NO TANK: PUT ME IN A FACE OF A CLOSED CUBOID ROOM WALLED WITH TANK BLOCKS";
}

/// <summary>
/// Compressor: pumps air into the sealed tank of a valve it touches, up to a target pressure (right click: 2 / 5 / 10 / 20 bar / OFF),
/// 0.5 m^3 of free air a second at most, using energy for the isothermal compression work p0 V ln(p) (70 % efficient; up to ~20 E/s).
/// The air stores that energy: water leaving the tank carries 10.2 blocks of head per bar.
/// </summary>
public sealed class CompressorBehavior : BlockBehavior, IEnergyMachine
{
    public const double FreeAir = .5, Efficiency = .7;
    static readonly double[] targets = { 10, 20, 0, 2, 5 };
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public override bool Usable => true;
    public override bool OnUse(BlockPos p, int id, int state)
    {
        var e = BlockStates.EntityOrNew(p, id); if (e == null) return false;
        int m = ((int)e.Num("mode") + 1) % targets.Length; e.SetNum("mode", m); UI.Note(targets[m] == 0 ? "Compressor: OFF" : $"Compressor: up to {targets[m]:0} bar", 1.5); return true;
    }
    public static Tank TankOf(BlockEntity e)
    {
        foreach (var (dx, dy, dz) in new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) })
            if (e.Pos.TryOffset(dx, dy, dz, out var n) && Tanks.IsValve(GridCache.At(n).id)) { var t = Tanks.Of(n); if (t != null) return t; }
        return null;
    }
    static double JoulesPerM3(double p) => Tank.P0 * Math.Log(Math.Max(p, 1.02)); // compressing 1 m^3 of free air to p (isothermal)
    public void Plan(BlockEntity e, double dt, ref EnergyPlan pl)
    {
        var t = TankOf(e); double target = targets[(int)e.Num("mode") % targets.Length];
        if (t == null || t.Vent || target <= 0 || t.Pressure >= target) return;
        pl.Demand = FreeAir * JoulesPerM3(t.Pressure) / Tank.JoulesPerE / Efficiency * dt;
    }
    public void Settle(BlockEntity e, double dt, double made, double got)
    {
        var t = TankOf(e); WaterMachines.Show(e, "power", got / dt);
        if (t == null || got <= 0) return;
        t.Air += Math.Min(FreeAir * dt, got * Tank.JoulesPerE * Efficiency / JoulesPerM3(t.Pressure)); t.Settle();
    }
    public override string Describe(BlockEntity e)
    {
        var t = TankOf(e); double target = targets[(int)e.Num("mode") % targets.Length];
        if (t == null) return "NOT ON A TANK VALVE";
        return (target == 0 ? "OFF" : $"TARGET {target:0} BAR") + $"  TANK {t.Pressure:0.00} BAR{(t.Vent ? " (VENTED: SEAL IT TO BUILD PRESSURE)" : "")}  {e.Num("power"):0.0} E/S";
    }
}
