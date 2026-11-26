namespace CubePlanet;

/// <summary>The gases machines make and use (hydrogen, oxygen). Ids: 1 = hydrogen, 2 = oxygen.</summary>
public static class Gas
{
    public const int Hydrogen = 1, Oxygen = 2;
    public static readonly string[] Names = { "", "hydrogen", "oxygen" };
    /// <summary>kg per mol.</summary>
    public static readonly double[] MolarMass = { 0, .002016, .032 };
    public static string Name(int g) => g > 0 && g < Names.Length ? Names[g] : "nothing";
}

/// <summary>
/// What pipes carry. A single pipe network carries one liquid or gas at a time; a dual pipe has two channels, so two different liquids (or two
/// gases: hydrogen and oxygen) can share one line without mixing. The first thing to flow along a path claims its pipes (a dual pipe: one of its
/// two channels); a pipe that has carried nothing for <see cref="Idle"/> seconds is free again.
/// </summary>
public static class PipeContents
{
    public const double Idle = 60;
    // key -> what its channels carry (a: first channel, b: second, 0 = free; gases are stored as 10000 + gas id) and when it last carried
    static readonly Dictionary<long, (int a, int b, double t)> claims = new();
    public static bool IsDual(int id) => DEFS[id]?.HasTag("dual_pipe") ?? false;
    static (int a, int b, double t) Get(long k)
    {
        if (!claims.TryGetValue(k, out var c)) return default;
        if (Kinetics.Time - c.t > Idle) { claims.Remove(k); return default; }
        return c;
    }
    /// <summary>Can this pipe carry 'what' (a liquid block id, or 10000 + gas id) now?</summary>
    public static bool CanCarry(BlockPos p, int what)
    {
        var c = Get(p.Key); bool dual = IsDual(p.Id);
        if (c.a == what || c.b == what) return true;
        return dual ? c.a == 0 || c.b == 0 : c.a == 0;
    }
    public static void Claim(BlockPos p, int what)
    {
        var c = Get(p.Key); double now = Kinetics.Time;
        if (c.a == what) claims[p.Key] = (c.a, c.b, now);
        else if (c.b == what) claims[p.Key] = (c.a, c.b, now);
        else if (c.a == 0) claims[p.Key] = (what, c.b, now);
        else if (IsDual(p.Id) && c.b == 0) claims[p.Key] = (c.a, what, now);
    }
    public static string Describe(BlockPos p)
    {
        var c = Get(p.Key); string N(int w) => w == 0 ? "-" : w >= 10000 ? Gas.Name(w - 10000).ToUpperInvariant() : DEFS[w]?.Name.ToUpperInvariant() ?? "?";
        return IsDual(p.Id) ? $"DUAL PIPE  CHANNEL 1: {N(c.a)}  CHANNEL 2: {N(c.b)}" : $"CARRIES: {(c.a == 0 ? "NOTHING" : N(c.a))}";
    }
    public static void Reset() => claims.Clear();
}

/// <summary>
/// Gas pipes (group tag "gas_pipe", no block entity) join machines to gas tanks. Machines push what they make into the tanks reachable through
/// the pipes (or a tank touching them) and pull what they use out of them. A tank holds one gas (optionally set by right click).
/// </summary>
public static class GasNet
{
    public static bool IsPipe(int id) => id > 0 && (DEFS[id]?.HasTag("gas_pipe") ?? false) && (BEH[id] == null || BEH[id] is ConnectingBehavior);
    static readonly (int x, int y, int z)[] N6 = { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };
    /// <summary>Gas tanks reachable from 'start' (the cell next to a machine: a tank, or a pipe) for this gas, each with the pipes on its path.</summary>
    public static List<(BlockEntity tank, List<BlockPos> path)> Reach(BlockPos start, BlockPos machine, int gas)
    {
        var res = new List<(BlockEntity, List<BlockPos>)>();
        if (!start.Loaded) return res;
        if (BlockStates.entities.TryGetValue(start.Key, out var t0) && BEH[t0.BlockId] is GasTankBehavior) { res.Add((t0, new List<BlockPos>())); return res; }
        if (!IsPipe(start.Id) || !PipeContents.CanCarry(start, 10000 + gas)) return res;
        var parent = new Dictionary<long, BlockPos?> { [start.Key] = null }; var q = new Queue<BlockPos>(); q.Enqueue(start); var seenT = new HashSet<long>();
        while (q.Count > 0 && parent.Count < 2048)
        {
            var p = q.Dequeue();
            foreach (var (dx, dy, dz) in N6)
            {
                if (!p.TryOffset(dx, dy, dz, out var n) || !n.Loaded || n.Key == machine.Key) continue;
                if (BlockStates.entities.TryGetValue(n.Key, out var t) && BEH[t.BlockId] is GasTankBehavior)
                {
                    if (!seenT.Add(n.Key)) continue;
                    var path = new List<BlockPos>(); BlockPos? c = p; while (c != null) { path.Add(c.Value); c = parent[c.Value.Key]; }
                    res.Add((t, path)); continue;
                }
                if (parent.ContainsKey(n.Key) || !IsPipe(n.Id) || !PipeContents.CanCarry(n, 10000 + gas)) continue;
                parent[n.Key] = p; q.Enqueue(n);
            }
        }
        return res;
    }
    /// <summary>How many mol of this gas the reachable tanks can still take.</summary>
    public static double Room(BlockPos start, BlockPos machine, int gas) => Reach(start, machine, gas).Where(r => GasTankBehavior.Accepts(r.tank, gas)).Sum(r => GasTankBehavior.MaxMol - r.tank.Num("mol"));
    /// <summary>How many mol of this gas the reachable tanks hold.</summary>
    public static double Stored(BlockPos start, BlockPos machine, int gas) => Reach(start, machine, gas).Where(r => (int)r.tank.Num("gas") == gas).Sum(r => r.tank.Num("mol"));
    /// <summary>Puts up to mol of a gas into the reachable tanks (emptiest first); returns what went in.</summary>
    public static double Push(BlockPos start, BlockPos machine, int gas, double mol)
    {
        double put = 0;
        foreach (var (t, path) in Reach(start, machine, gas).Where(r => GasTankBehavior.Accepts(r.tank, gas)).OrderBy(r => r.tank.Num("mol")))
        {
            if (mol - put <= 1e-12) break;
            double m = Math.Min(mol - put, GasTankBehavior.MaxMol - t.Num("mol")); if (m <= 0) continue;
            t.SetNum("gas", gas); t.SetNum("mol", t.Num("mol") + m); put += m;
            foreach (var p in path) PipeContents.Claim(p, 10000 + gas);
        }
        return put;
    }
    /// <summary>Takes up to mol of a gas out of the reachable tanks (fullest first); returns what came out.</summary>
    public static double Pull(BlockPos start, BlockPos machine, int gas, double mol)
    {
        double got = 0;
        foreach (var (t, path) in Reach(start, machine, gas).Where(r => (int)r.tank.Num("gas") == gas).OrderByDescending(r => r.tank.Num("mol")))
        {
            if (mol - got <= 1e-12) break;
            double m = Math.Min(mol - got, t.Num("mol")); if (m <= 0) continue;
            t.SetNum("mol", t.Num("mol") - m); if (t.Num("mol") <= 1e-9) { t.SetNum("mol", 0); if (t.Num("filter") == 0) t.SetNum("gas", 0); }
            got += m; foreach (var p in path) PipeContents.Claim(p, 10000 + gas);
        }
        return got;
    }
    /// <summary>Every side of a machine except the ones given (as offsets).</summary>
    public static IEnumerable<BlockPos> Sides(BlockEntity e, params (int x, int y, int z)[] except)
    {
        foreach (var d in N6) if (!except.Contains(d) && e.Pos.TryOffset(d.x, d.y, d.z, out var n)) yield return n;
    }
}

/// <summary>
/// Gas tank: 1 m^3, filled up to 30 bar (ideal gas at 20 C: pV = nRT, ~1230 mol), one gas at a time. Right click: which gas it takes (any /
/// hydrogen / oxygen), so a line with two tanks can keep them apart. Machines fill and empty it through gas pipes or by touching it.
/// </summary>
public sealed class GasTankBehavior : BlockBehavior
{
    public const double R = 8.314, T = 293, Volume = 1, MaxBar = 30;
    public static double MaxMol => MaxBar * 1e5 * Volume / (R * T);
    public static double Bar(BlockEntity t) => t.Num("mol") * R * T / Volume / 1e5;
    public static bool Accepts(BlockEntity t, int gas) { int g = (int)t.Num("gas"), f = (int)t.Num("filter"); return (f == 0 || f == gas) && (g == 0 || g == gas || t.Num("mol") <= 1e-9) && t.Num("mol") < MaxMol - 1e-6; }
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public override bool Usable => true;
    public override bool OnUse(BlockPos p, int id, int state)
    {
        var e = BlockStates.EntityOrNew(p, id); if (e == null) return false;
        int f = ((int)e.Num("filter") + 1) % Gas.Names.Length; e.SetNum("filter", f);
        UI.Note(f == 0 ? "Gas Tank: takes any gas" : $"Gas Tank: takes only {Gas.Name(f)}", 1.5); return true;
    }
    public override string Describe(BlockEntity e)
    {
        int g = (int)e.Num("gas"), f = (int)e.Num("filter"); double kg = e.Num("mol") * (g > 0 ? Gas.MolarMass[g] : 0);
        string what = e.Num("mol") <= 1e-9 ? "EMPTY" : $"{Gas.Name(g).ToUpperInvariant()} {kg:0.000} KG ({e.Num("mol"):0} MOL)";
        string extra = g == Gas.Hydrogen && e.Num("mol") > 0 ? $"  FUEL CELL ENERGY ~{e.Num("mol") * Gas.MolarMass[1] * Hydrogen.Lhv * FuelCellBehavior.Efficiency / Tank.JoulesPerE:0} E" : "";
        return $"{what}  {Bar(e):0.0} / {MaxBar:0} BAR  TAKES {(f == 0 ? "ANY GAS" : Gas.Name(f).ToUpperInvariant())}{extra}";
    }
}
