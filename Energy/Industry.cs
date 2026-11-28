using System.Text.Json;

namespace CubePlanet;

/// <summary>
/// A machine recipe: items in (each with a count), items out (counts may be fractions: 0.6 planks a log, 0.13 copper concentrate from
/// crushed ore; the machine keeps the fraction and hands out a whole item when it adds up), optional gas in / out through gas pipes (mol),
/// optional water in (units of 125 kg, from a liquid pipe at its back), and the work: seconds at full power (electric machines) or E of
/// rotation (millstone, sawmill). JSON: {"name", "in": [["iron_ore", 1]], "out": [["crushed_iron_ore", 16], ["tailings", 0.3]],
/// "gas_in": ["oxygen", 1100], "gas_out": ["hydrogen", 400], "water": 1, "time": 20}; the old form {"input", "output", "count", "work"} too.
/// </summary>
public sealed class ProcRecipe
{
    public string Name = "";
    public (int item, int count)[] In = Array.Empty<(int, int)>();
    public (int item, double count)[] Out = Array.Empty<(int, double)>();
    public int GasIn, GasOut, Water; public double GasInMol, GasOutMol, Time = 10;
    /// <summary>The first input (machines that take one kind of item).</summary>
    public int Input => In.Length > 0 ? In[0].item : 0;
    public bool Uses(int item) => In.Any(i => i.item == item);
    /// <summary>kg going in: items, gas and water (the self-test checks that <see cref="MassOut"/> is never more).</summary>
    public double MassIn => In.Sum(i => Mass.OfItem(i.item) * i.count) + (GasIn > 0 ? GasInMol * Gas.MolarMass[GasIn] : 0) + Water * WaterMachines.UnitKg;
    public double MassOut => Out.Sum(o => Mass.OfItem(o.item) * o.count) + (GasOut > 0 ? GasOutMol * Gas.MolarMass[GasOut] : 0);
    public override string ToString() => Name.Length > 0 ? Name : string.Join(" + ", In.Select(i => $"{i.count} {GameAssets.ItemReg.Get(i.item)?.IdName}"));
}

/// <summary>Reading recipes and running them on a machine's "input" / "output" inventories.</summary>
public static class Processing
{
    static int ItemId(string n, string owner) => GameAssets.ItemReg.Has(n) ? GameAssets.ItemReg.Id(n) : throw new InvalidDataException($"{owner}: unknown item '{n}'");
    static int GasId(string n, string owner) { int g = Array.IndexOf(Gas.Names, n); return g > 0 ? g : throw new InvalidDataException($"{owner}: unknown gas '{n}' ({string.Join(", ", Gas.Names.Skip(1))})"); }
    public static List<ProcRecipe> Parse(JsonElement? arr, string owner, double defaultTime)
    {
        var res = new List<ProcRecipe>(); if (arr is not { ValueKind: JsonValueKind.Array } a) return res;
        foreach (var r in a.EnumerateArray())
        {
            var p = new ProcRecipe { Name = DefinitionFile.Str(r, "name") };
            if (DefinitionFile.Get(r, "in") is { ValueKind: JsonValueKind.Array } ins)
            {
                p.In = ins.EnumerateArray().Select(x => (ItemId(x[0].GetString(), owner), x.GetArrayLength() > 1 ? x[1].GetInt32() : 1)).ToArray();
                p.Out = DefinitionFile.Get(r, "out") is { ValueKind: JsonValueKind.Array } outs ? outs.EnumerateArray().Select(x => (ItemId(x[0].GetString(), owner), x.GetArrayLength() > 1 ? x[1].GetDouble() : 1)).ToArray() : p.Out;
                p.Time = DefinitionFile.Num(r, "time", defaultTime);
            }
            else
            {   // old form: one item in, one kind out
                p.In = new[] { (ItemId(DefinitionFile.Str(r, "input"), owner), 1) };
                p.Out = new[] { (ItemId(DefinitionFile.Str(r, "output"), owner), DefinitionFile.Num(r, "count", 1)) };
                p.Time = DefinitionFile.Num(r, "work", defaultTime);
            }
            if (DefinitionFile.Get(r, "gas_in") is { ValueKind: JsonValueKind.Array } gi) { p.GasIn = GasId(gi[0].GetString(), owner); p.GasInMol = gi[1].GetDouble(); }
            if (DefinitionFile.Get(r, "gas_out") is { ValueKind: JsonValueKind.Array } go) { p.GasOut = GasId(go[0].GetString(), owner); p.GasOutMol = go[1].GetDouble(); }
            p.Water = (int)DefinitionFile.Num(r, "water");
            if (p.In.Length == 0 || p.Out.Length == 0) throw new InvalidDataException($"{owner}: a recipe needs something in and something out");
            res.Add(p);
        }
        return res;
    }
    static int Count(ItemStack[] inv, int item) => inv == null ? 0 : inv.Where(s => s != null && !s.IsEmpty && s.Item == item && s.State == 0).Sum(s => s.Count);
    /// <summary>The input inventory holds everything the recipe takes.</summary>
    public static bool Has(ItemStack[] inv, ProcRecipe r) => r.In.All(i => Count(inv, i.item) >= i.count);
    static string CarryKey(int item) => "carry:" + (GameAssets.ItemReg.Get(item)?.IdName ?? item.ToString());
    /// <summary>Whole items the next batch hands out (the kept fraction + this batch's count).</summary>
    static int Whole(BlockEntity e, int item, double count) => (int)Math.Floor(e.Num(CarryKey(item)) + count + 1e-9);
    /// <summary>Everything the recipe makes fits into the output inventory.</summary>
    public static bool Fits(BlockEntity e, ItemStack[] output, ProcRecipe r) => Fits(e, output, r.Out);
    public static bool Fits(BlockEntity e, ItemStack[] output, IEnumerable<(int item, double count)> outs)
    {
        if (output == null) return false;
        var copy = output.Select(x => x?.Clone()).ToArray();
        foreach (var (item, count) in outs) { int n = Whole(e, item, count); if (n > 0 && ItemsUtil.Insert(copy, new ItemStack(item, n)) < n) return false; }
        return true;
    }
    public static void Consume(ItemStack[] inv, ProcRecipe r)
    {
        foreach (var (item, count) in r.In)
        {
            int left = count;
            for (int i = 0; i < inv.Length && left > 0; i++)
                if (inv[i] is { IsEmpty: false } s && s.Item == item && s.State == 0) { int m = Math.Min(left, s.Count); s.Count -= m; left -= m; if (s.Count <= 0) inv[i] = null; }
        }
    }
    /// <summary>Hands out 'count' of an item (a fraction is kept for the next time).</summary>
    public static void Give(BlockEntity e, ItemStack[] output, int item, double count)
    {
        string k = CarryKey(item); double c = e.Num(k) + count; int n = (int)Math.Floor(c + 1e-9);
        if (n > 0) ItemsUtil.Insert(output, new ItemStack(item, n));
        e.SetNum(k, Math.Max(0, c - n));
    }
    public static void GiveAll(BlockEntity e, ItemStack[] output, ProcRecipe r) { foreach (var (item, count) in r.Out) Give(e, output, item, count); }
    /// <summary>The first recipe whose inputs are all there and whose outputs fit.</summary>
    public static ProcRecipe Job(BlockEntity e, IReadOnlyList<ProcRecipe> recipes)
    {
        var input = e.Inv("input"); var output = e.Inv("output"); if (input == null || output == null) return null;
        foreach (var r in recipes) if (Has(input, r) && Fits(e, output, r)) return r;
        return null;
    }
    /// <summary>mol of a gas in the tanks reachable through the gas pipes on any side (each tank once).</summary>
    public static double GasStored(BlockEntity e, int gas)
    {
        var seen = new HashSet<long>(); double s = 0;
        foreach (var side in GasNet.Sides(e)) foreach (var (t, _) in GasNet.Reach(side, e.Pos, gas)) if ((int)t.Num("gas") == gas && seen.Add(t.Pos.Key)) s += t.Num("mol");
        return s;
    }
    public static double GasPull(BlockEntity e, int gas, double mol) { double got = 0; foreach (var side in GasNet.Sides(e)) { if (mol - got <= 1e-9) break; got += GasNet.Pull(side, e.Pos, gas, mol - got); } return got; }
    /// <summary>Pushes gas into the tanks on any side; returns what did not fit (it goes into the air).</summary>
    public static double GasPush(BlockEntity e, int gas, double mol) { foreach (var side in GasNet.Sides(e)) { if (mol <= 1e-9) break; mol -= GasNet.Push(side, e.Pos, gas, mol); } return Math.Max(0, mol); }
}

/// <summary>
/// An electric processing machine (crusher, ore concentrator, coke oven, blast furnace, oxygen furnace, copper smelter, electrolytic refinery,
/// rolling mills): definition key "industry": {"power": E/s at full speed (0: it heats itself, like a coke oven burning its own gas),
/// "recipes": [<see cref="ProcRecipe"/>]}. Inventories "input" and "output" (several slots); the first recipe whose inputs are all in the input
/// slots and whose outputs fit runs; less power = slower. Gas comes from / goes to gas tanks through gas pipes on any side (gas it makes with
/// nowhere to go is let into the air); water comes from a liquid pipe or tank valve at its back. A batch waits at the end until its gas and
/// water are there.
/// </summary>
public sealed class IndustryBehavior : BlockBehavior, IEnergyMachine
{
    sealed class Spec { public double Power; public List<ProcRecipe> Recipes = new(); }
    static readonly Dictionary<int, Spec> specs = new();
    static Spec SpecOf(int id)
    {
        lock (specs)
        {
            if (specs.TryGetValue(id, out var s)) return s;
            s = new Spec();
            if (DEFS[id] != null && DefinitionFile.Get(DEFS[id].Json, "properties.definition.industry") is { ValueKind: JsonValueKind.Object } j)
            {
                s.Power = DefinitionFile.Num(j, "power", 20); s.Recipes = Processing.Parse(DefinitionFile.Get(j, "recipes"), $"block {DEFS[id].FullName}", 10);
            }
            return specs[id] = s;
        }
    }
    public static IReadOnlyList<ProcRecipe> RecipesOf(int id) => BEH[id] is IndustryBehavior ? SpecOf(id).Recipes : Array.Empty<ProcRecipe>();
    public static double PowerOf(int id) => SpecOf(id).Power;
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    // status: 0 working, 1 idle, 2 waiting for gas, 3 waiting for water, 4 no power
    bool TryFinish(BlockEntity e, ProcRecipe r)
    {
        WaterEnd src = default;
        if (r.GasIn > 0 && Processing.GasStored(e, r.GasIn) < r.GasInMol - 1e-6) { e.SetNum("status", 2); return false; }
        if (r.Water > 0) { src = ElectrolyzerBehavior.WaterIn(e); if (!src.Ok || src.Available < r.Water) { e.SetNum("status", 3); return false; } }
        if (r.GasIn > 0) Processing.GasPull(e, r.GasIn, r.GasInMol);
        if (r.Water > 0) { if (src.Tank != null) src.Tank.Take(r.Water); else TakeWater(src.Cell, r.Water); }
        Processing.Consume(e.Inv("input"), r); Processing.GiveAll(e, e.Inv("output"), r);
        if (r.GasOut > 0) { double vent = Processing.GasPush(e, r.GasOut, r.GasOutMol); e.SetNum("vented", vent > 1e-9 ? 1 : 0); e.SetNum("gas_out_kg", e.Num("gas_out_kg") + (r.GasOutMol - vent) * Gas.MolarMass[r.GasOut]); }
        e.SetNum("batches", e.Num("batches") + 1); e.SetNum("status", 0);
        return true;
    }
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p)
    {
        var s = SpecOf(e.BlockId); var r = Processing.Job(e, s.Recipes);
        if (r == null) { e.SetNum("status", 1); e.SetNum("progress", 0); e.SetNum("job", -1); Electric.SetBool(e, "lit", false); return; }
        int ji = s.Recipes.IndexOf(r); if (ji != (int)e.Num("job")) { e.SetNum("job", ji); e.SetNum("progress", 0); }
        e.SetNum("cook_time", r.Time);
        if (e.Num("progress") >= r.Time && TryFinish(e, r)) { e.SetNum("progress", 0); r = Processing.Job(e, s.Recipes); if (r == null) return; }
        if (e.Num("progress") >= r.Time) return; // waiting for gas / water
        if (s.Power > 0) p.Demand = s.Power * dt;
    }
    public void Settle(BlockEntity e, double dt, double made, double got)
    {
        var s = SpecOf(e.BlockId); WaterMachines.Show(e, "power", got / dt);
        int ji = (int)e.Num("job"); if (ji < 0 || ji >= s.Recipes.Count) return; var r = s.Recipes[ji];
        if (e.Num("progress") >= r.Time) return;
        double share = s.Power > 0 ? Math.Min(1, got / (s.Power * dt)) : 1;
        if (share <= 0) { e.SetNum("status", 4); return; }
        e.SetNum("status", 0);
        double prog = Math.Min(r.Time, e.Num("progress") + dt * share); e.SetNum("progress", prog);
        if (prog >= r.Time && TryFinish(e, r)) e.SetNum("progress", 0);
        Electric.SetBool(e, "lit", true);
    }
    public override string Describe(BlockEntity e)
    {
        var s = SpecOf(e.BlockId); int ji = (int)e.Num("job"); var r = ji >= 0 && ji < s.Recipes.Count ? s.Recipes[ji] : null;
        string what = r == null ? "" : $"{r}  ";
        return (int)e.Num("status") switch
        {
            1 => "IDLE: NOTHING IT CAN WORK IN THE INPUT SLOTS, OR THE OUTPUT IS FULL",
            2 => $"{what}WAITING FOR {r?.GasInMol:0} MOL OF {Gas.Name(r?.GasIn ?? 0).ToUpperInvariant()} (GAS TANKS ON GAS PIPES AT ANY SIDE; {Processing.GasStored(e, r?.GasIn ?? 0):0} THERE)",
            3 => $"{what}WAITING FOR {r?.Water} UNITS OF WATER (A LIQUID PIPE OR TANK VALVE AT ITS BACK)",
            4 => $"{what}NO POWER: NEEDS {s.Power:0} E/S",
            _ => $"{what}{e.Num("progress"):0.0} / {e.Num("cook_time"):0} S  {(s.Power > 0 ? $"{e.Num("power"):0.0} / {s.Power:0} E/S" : "SELF-HEATED")}" +
                 (r?.GasOut > 0 ? (e.Num("vented") > 0 ? $"  {Gas.Name(r.GasOut).ToUpperInvariant()} INTO THE AIR (NO TANK ON ITS GAS PIPES)" : $"  {e.Num("gas_out_kg"):0.0} KG {Gas.Name(r.GasOut).ToUpperInvariant()} STORED") : "")
        };
    }
}

/// <summary>
/// Air separation unit: cools air until it turns liquid and distils it (oxygen boils at -183 C, nitrogen at -196 C), the way nearly all
/// industrial oxygen is made. Real cost ~0.4 kWh per m^3 of oxygen (~32 kJ a mol, 2.6 E): with 100 E/s it makes ~38 mol (1.2 kg) of
/// oxygen a second, 25 times what an electrolyzer of the same power gives as its by-product. The oxygen goes into gas tanks on gas pipes at
/// any side; the nitrogen goes back into the air. Stops when the tanks are full.
/// </summary>
public sealed class AirSeparatorBehavior : BlockBehavior, IEnergyMachine
{
    public const double Power = 100, EPerMol = 32000 / Tank.JoulesPerE;
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    static double Room(BlockEntity e)
    {
        var seen = new HashSet<long>(); double s = 0;
        foreach (var side in GasNet.Sides(e)) foreach (var (t, _) in GasNet.Reach(side, e.Pos, Gas.Oxygen)) if (GasTankBehavior.Accepts(t, Gas.Oxygen) && seen.Add(t.Pos.Key)) s += GasTankBehavior.MaxMol - t.Num("mol");
        return s;
    }
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p)
    {
        double room = Room(e); e.SetNum("room", room); if (room <= 1e-6) return;
        p.Demand = Math.Min(Power * dt, room * EPerMol);
    }
    public void Settle(BlockEntity e, double dt, double made, double got)
    {
        WaterMachines.Show(e, "power", got / dt); if (got <= 0) return;
        double mol = got / EPerMol, left = Processing.GasPush(e, Gas.Oxygen, mol);
        e.SetNum("o2_kg", e.Num("o2_kg") + (mol - left) * Gas.MolarMass[Gas.Oxygen]);
    }
    public override string Describe(BlockEntity e) => e.Num("room") <= 1e-6 ? "NO ROOM: NEEDS A GAS TANK (OR GAS PIPES TO ONE) THAT TAKES OXYGEN" :
        $"MAKING {e.Num("power") / EPerMol:0.0} MOL ({e.Num("power") / EPerMol * Gas.MolarMass[Gas.Oxygen] * 3600:0} KG AN HOUR) OF OXYGEN  {e.Num("power"):0.0} / {Power:0} E/S  {e.Num("o2_kg"):0.0} KG MADE";
}
