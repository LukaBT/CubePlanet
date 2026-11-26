using System.Collections.Concurrent;
using System.Text.Json;

namespace CubePlanet;

/// <summary>Items for machines: what a block drops, removing a block without the player's harvest, putting items into / taking them out of
/// block inventories.</summary>
public static class ItemsUtil
{
    /// <summary>What breaking this block gives: its behaviour's harvest, else its definition's "drops", else the item that places it.</summary>
    public static List<ItemStack> Drops(BlockPos p)
    {
        int id = p.Id, st = p.State; var res = new List<ItemStack>(); if (id <= 0) return res;
        var h = BEH[id]?.Harvest(p, id, st); if (h != null) { res.AddRange(h.Where(x => x != null && !x.IsEmpty)); return res; }
        if (DEFS[id] != null && DefinitionFile.Get(DEFS[id].Json, "properties.definition.drops") is { ValueKind: JsonValueKind.Array } ds)
        {
            foreach (var d in ds.EnumerateArray()) { string n = DefinitionFile.Str(d, "id_name"); if (GameAssets.ItemReg.Has(n)) res.Add(new ItemStack(GameAssets.ItemReg.Id(n), (int)DefinitionFile.Num(d, "count", 1))); }
            return res;
        }
        var it = GameAssets.ItemReg.All.FirstOrDefault(i => i != null && i.PlacesBlockId == id); if (it != null) res.Add(new ItemStack(it.Id, 1));
        return res;
    }
    /// <summary>Removes a block (and its multiblock parts) like breaking it, without giving its harvest to the player.</summary>
    public static void Remove(BlockPos p)
    {
        int id = p.Id; if (id == 0) return; int state = p.State; var parts = new List<(BlockPos pos, int part)>();
        BlockActions.Parts(p, id, state, parts); BEH[id]?.OnBreak(p, id, state);
        foreach (var (q, _) in parts) if (q.Id == id) { BlockStates.entities.Remove(q.Key); q.Set(0); }
        foreach (var (q, _) in parts) BlockActions.NotifyNeighbors(q);
    }
    public static int Hardness(int id) => (int)Math.Round((DEFS[id]?.Hardness ?? 1) * 2) / 2;
    /// <summary>Puts as much of a stack as fits into an inventory (same item stacks first, then empty slots); returns how many went in.</summary>
    public static int Insert(ItemStack[] inv, ItemStack s)
    {
        if (inv == null || s == null || s.IsEmpty) return 0; int left = s.Count, max = s.MaxStack;
        for (int i = 0; i < inv.Length && left > 0; i++)
            if (inv[i] is { IsEmpty: false } t && t.Item == s.Item && t.State == s.State && t.Count < max) { int m = Math.Min(left, max - t.Count); t.Count += m; left -= m; }
        for (int i = 0; i < inv.Length && left > 0; i++)
            if (inv[i] == null || inv[i].IsEmpty) { int m = Math.Min(left, max); inv[i] = new ItemStack(s.Item, m, s.State); left -= m; }
        return s.Count - left;
    }
    /// <summary>The inventory a machine / chest takes this item into (furnace / mill input if it can work it, fuel if it burns, chest slots).</summary>
    public static ItemStack[] InputFor(BlockEntity e, ItemStack s)
    {
        if (e.Inv("input") is { } inp)
        {
            bool works = GameAssets.RecipeReg.SmeltingFor(s.Item) != null || ProcessorBehavior.RecipesOf(e.BlockId).Any(r => r.Input == s.Item);
            if (works) return inp;
        }
        if (e.Inv("fuel") is { } fu && s.Def?.FuelTime > 0) return fu;
        return e.Inv("slots");
    }
    /// <summary>The inventory items are taken out of (a machine's output, a chest's slots).</summary>
    public static ItemStack[] OutputOf(BlockEntity e) => e.Inv("output") ?? e.Inv("slots");
    public static bool Accepts(BlockEntity e, ItemStack s) { var inv = InputFor(e, s); if (inv == null) return false; var probe = new ItemStack(s.Item, 1, s.State); var copy = inv.Select(x => x == null ? null : new ItemStack(x.Item, x.Count, x.State)).ToArray(); return Insert(copy, probe) == 1; }
    public static readonly (int, int, int)[] N6 = { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };
}

/// <summary>
/// Automatic miner: digs out the area under it layer by layer (key "miner": {"radius": 4 -> 9 x 9, "depth", "power": E/s}) and puts what
/// the blocks drop into a chest touching it. Each block costs 2 E + 6 E x its hardness (dirt little, stone and ores more). Leaves machines,
/// water and air alone; stops when the chest is full or it reaches its depth. Right click: ON / OFF.
/// </summary>
public sealed class MinerBehavior : BlockBehavior, IEnergyMachine
{
    public override void OnPlace(BlockPos p, int id) { var e = BlockStates.EntityOrNew(p, id); e?.SetNum("cy", -1); }
    public override bool Usable => true;
    public override bool OnUse(BlockPos p, int id, int state)
    {
        var e = BlockStates.EntityOrNew(p, id); if (e == null) return false;
        e.SetNum("off", e.Num("off") > 0 ? 0 : 1); UI.Note(e.Num("off") > 0 ? "Miner OFF" : "Miner ON", 1.5); return true;
    }
    int Radius(int id) => (int)Electric.Key(this, id, "miner", "radius", 4);
    int Depth(int id) => (int)Electric.Key(this, id, "miner", "depth", 48);
    double Power(int id) => Electric.Key(this, id, "miner", "power", 20);
    public static double Cost(int id) => 2 + 6 * (DEFS[id]?.Hardness ?? 1);
    static BlockEntity Chest(BlockEntity e)
    {
        foreach (var (dx, dy, dz) in ItemsUtil.N6) if (e.Pos.TryOffset(dx, dy, dz, out var n) && BlockStates.entities.TryGetValue(n.Key, out var c) && c.Inv("slots") != null) return c;
        return null;
    }
    /// <summary>The next block to dig (scanning the layer, then the next layer down), or null when done.</summary>
    BlockPos? Next(BlockEntity e)
    {
        int r = Radius(e.BlockId), depth = Depth(e.BlockId); if (!e.Pos.Loaded) return null;
        for (int guard = 0; guard < 4000; guard++)
        {
            int cy = (int)e.Num("cy"), cx = (int)e.Num("cx"), cz = (int)e.Num("cz");
            if (-cy > depth) return null;
            if (e.Pos.TryOffset(cx, cy, cz, out var c) && c.Loaded)
            {
                int id = c.Id;
                if (id != 0 && !LIQ[id] && !BlockStates.entities.ContainsKey(c.Key) && (DEFS[id]?.Hardness ?? 1) >= 0 && SPC[id] == 0) return c;
                if (id != 0 && !LIQ[id] && SPC[id] != 0) return c; // plants: dig them too
            }
            cx++; if (cx > r) { cx = -r; cz++; if (cz > r) { cz = -r; cy--; } }
            e.SetNum("cx", cx); e.SetNum("cz", cz); e.SetNum("cy", cy);
        }
        return null;
    }
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p)
    {
        if (e.Num("off") > 0 || !e.Pos.Loaded) return;
        if (e.Num("cx") == 0 && e.Num("cz") == 0 && e.Num("init") == 0) { e.SetNum("cx", -Radius(e.BlockId)); e.SetNum("cz", -Radius(e.BlockId)); e.SetNum("cy", -1); e.SetNum("init", 1); }
        var c = Chest(e); if (c == null) { e.SetNum("status", 1); return; }
        var nx = Next(e); if (nx == null) { e.SetNum("status", 2); return; }
        e.SetNum("status", 0); p.Demand = Power(e.BlockId) * dt;
    }
    public void Settle(BlockEntity e, double dt, double made, double got)
    {
        WaterMachines.Show(e, "power", got / dt); if (got <= 0) return;
        double work = e.Num("work") + got; var chest = Chest(e); if (chest == null) return;
        for (int i = 0; i < 16; i++)
        {
            var nx = Next(e); if (nx == null) break; var c = nx.Value; double cost = Cost(c.Id); if (work < cost) break;
            var drops = ItemsUtil.Drops(c); var inv = chest.Inv("slots");
            var copy = inv.Select(x => x == null ? null : new ItemStack(x.Item, x.Count, x.State)).ToArray();
            if (drops.Any(d => ItemsUtil.Insert(copy, d) < d.Count)) { e.SetNum("status", 3); break; } // the chest is full
            foreach (var d in drops) ItemsUtil.Insert(inv, d);
            ItemsUtil.Remove(c); work -= cost; e.SetNum("mined", e.Num("mined") + 1);
        }
        e.SetNum("work", Math.Min(work, 200));
    }
    public override string Describe(BlockEntity e)
    {
        string st = e.Num("off") > 0 ? "OFF" : (int)e.Num("status") switch { 1 => "NO CHEST: PUT ONE NEXT TO IT", 2 => "DONE", 3 => "CHEST FULL", _ => "DIGGING" };
        int r = Radius(e.BlockId);
        return $"{st}  {2 * r + 1}X{2 * r + 1}, LAYER {-(int)e.Num("cy")} OF {Depth(e.BlockId)}  {e.Num("mined"):0} BLOCKS  {e.Num("power"):0.0} E/S";
    }
}

/// <summary>
/// Hydrogen: the electrolyzer splits water with electricity (real numbers: 141.8 MJ per kg of hydrogen at 100 %, 70 % efficient, 9 kg of
/// water per kg; 2 H2O -> 2 H2 + O2, so half as many mol of oxygen), sending the hydrogen and the oxygen through separate gas pipes into gas
/// tanks (1 m^3 each, up to 30 bar). The fuel cell turns hydrogen back into electricity (120 MJ/kg, 55 %; its oxygen comes from the air) and
/// the water back into the water cycle. Round trip ~33 %, but it keeps for ever.
/// 1 E = 12,262 J (125 kg x 9.81 m/s2 x 10 m: one unit of water falling 10 blocks on the surface).
/// </summary>
public static class Hydrogen
{
    public const double Hhv = 141.8e6, Lhv = 120e6, MolarMass = .002016, MaxBar = GasTankBehavior.MaxBar;
    public static double MaxMol => GasTankBehavior.MaxMol;
    public static double Bar(BlockEntity tank) => GasTankBehavior.Bar(tank);
}

/// <summary>
/// Electrolyzer: water from a liquid pipe or tank valve at its back (not loose water) -> hydrogen out of its front, oxygen out of its sides,
/// each into the gas tanks on the gas pipes there (or a tank touching that side), up to 100 E/s. Without an oxygen line the oxygen goes into
/// the air; without room for the hydrogen it stops.
/// </summary>
public sealed class ElectrolyzerBehavior : BlockBehavior, IEnergyMachine
{
    public const double Power = 100, Efficiency = .7;
    /// <summary>Water from the pipe / tank valve at its back (loose water in the world does not count).</summary>
    public static WaterEnd WaterIn(BlockEntity e)
    {
        var (dx, dy, dz) = WaterMachines.Dir(e);
        if (!e.Pos.TryOffset(-dx, -dy, -dz, out var b) || !b.Loaded || LIQ[b.Id] || !WaterMachines.IsPipe(b.Id)) return default;
        var src = WaterMachines.IntakeEnd(e); return src.Ok && src.Liquid == Water ? src : default;
    }
    static BlockPos? Front(BlockEntity e) { var (dx, dy, dz) = WaterMachines.Dir(e); return e.Pos.TryOffset(dx, dy, dz, out var f) ? f : null; }
    static IEnumerable<BlockPos> OxygenSides(BlockEntity e) { var d = WaterMachines.Dir(e); return GasNet.Sides(e, d, (-d.x, -d.y, -d.z)); }
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p)
    {
        var f = Front(e); if (f == null) return;
        var reach = GasNet.Reach(f.Value, e.Pos, Gas.Hydrogen);
        double room = reach.Where(r => GasTankBehavior.Accepts(r.tank, Gas.Hydrogen)).Sum(r => GasTankBehavior.MaxMol - r.tank.Num("mol"));
        if (room <= 1e-6) { e.SetNum("status", reach.Count == 0 ? 1 : 2); return; }
        if (e.Num("water") < 1 && e.Pos.Loaded)
        {   // take a unit of water (125 kg) into its tank
            var src = WaterIn(e);
            if (src.Ok && src.Available >= 1) { int got = src.Tank != null ? src.Tank.Take(1) : TakeWater(src.Cell, 1); e.SetNum("water", e.Num("water") + got); }
        }
        if (e.Num("water") <= 0) { e.SetNum("status", 3); return; }
        e.SetNum("status", 0); p.Demand = Power * dt;
    }
    public void Settle(BlockEntity e, double dt, double made, double got)
    {
        WaterMachines.Show(e, "power", got / dt); if (got <= 0) return;
        var f = Front(e); if (f == null) return;
        double mol = got * Tank.JoulesPerE * Efficiency / Hydrogen.Hhv / Hydrogen.MolarMass;
        mol = GasNet.Push(f.Value, e.Pos, Gas.Hydrogen, mol);
        e.SetNum("water", Math.Max(0, e.Num("water") - mol * Hydrogen.MolarMass * 9 / 125)); // 9 kg of water per kg of hydrogen
        e.SetNum("made_kg", e.Num("made_kg") + mol * Hydrogen.MolarMass);
        double o2 = mol / 2, o0 = o2; foreach (var s in OxygenSides(e)) { if (o2 <= 1e-12) break; o2 -= GasNet.Push(s, e.Pos, Gas.Oxygen, o2); }
        e.SetNum("o2_kg", e.Num("o2_kg") + (o0 - o2) * Gas.MolarMass[Gas.Oxygen]); e.SetNum("o2_vent", o2 > 1e-12 ? 1 : 0);
    }
    public override string Describe(BlockEntity e) => (int)e.Num("status") switch
    {
        1 => "NO HYDROGEN TANK: A GAS TANK (OR GAS PIPES TO ONE) AT ITS FRONT", 2 => "HYDROGEN TANKS FULL", 3 => "NO WATER: A LIQUID PIPE OR TANK VALVE WITH WATER AT ITS BACK",
        _ => $"MAKING {e.Num("power") * Tank.JoulesPerE * Efficiency / Hydrogen.Hhv * 3600:0.00} KG HYDROGEN AN HOUR  {e.Num("power"):0.0} E/S  WATER {e.Num("water") * 125:0.0} KG  OXYGEN {(e.Num("o2_vent") > 0 ? "INTO THE AIR (NO OXYGEN LINE AT ITS SIDES)" : $"{e.Num("o2_kg"):0.00} KG STORED")}"
    };
}

/// <summary>Fuel cell: hydrogen from gas tanks touching it or on gas pipes at any side -> electricity on demand (up to 40 E/s, 55 %); its
/// oxygen comes from the air, the water it makes goes back into the water cycle.</summary>
public sealed class FuelCellBehavior : BlockBehavior, IEnergyMachine
{
    public const double Power = 40, Efficiency = .55;
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    static double EPerMol => Hydrogen.MolarMass * Hydrogen.Lhv * Efficiency / Tank.JoulesPerE;
    static double Stored(BlockEntity e)
    {
        var seen = new HashSet<long>(); double s = 0;
        foreach (var side in GasNet.Sides(e)) foreach (var (t, _) in GasNet.Reach(side, e.Pos, Gas.Hydrogen)) if ((int)t.Num("gas") == Gas.Hydrogen && seen.Add(t.Pos.Key)) s += t.Num("mol");
        return s;
    }
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p)
    {
        double mol = Stored(e); e.SetNum("h2", mol); if (mol <= 1e-9) return;
        p.Dispatch = Math.Min(Power * dt, mol * EPerMol);
    }
    public void Settle(BlockEntity e, double dt, double made, double got)
    {
        WaterMachines.Show(e, "power", made / dt); if (made <= 0) return;
        double want = made / EPerMol, took = 0;
        foreach (var side in GasNet.Sides(e)) { if (want - took <= 1e-12) break; took += GasNet.Pull(side, e.Pos, Gas.Hydrogen, want - took); }
        WaterSim.Reservoir += took * Hydrogen.MolarMass * 9 / 125; // the water it makes goes into the air (the water cycle)
    }
    public override string Describe(BlockEntity e) => e.Num("h2") <= 1e-9 ? "NO HYDROGEN: A GAS TANK WITH HYDROGEN TOUCHING IT OR ON ITS GAS PIPES" :
        $"{e.Num("power"):0.0} / {Power:0} E/S ON DEMAND  HYDROGEN {e.Num("h2") * Hydrogen.MolarMass:0.000} KG ({EPerMol:0.0} E PER MOL)";
}

/// <summary>
/// Electric tools: items with a "charge" state field (E); the charger (a block with one slot) fills them from the grid at up to 40 E/s,
/// 95 % efficient. Drill: digs 3 x 3 across the face you look at (2 E + 6 E x hardness a block). Chainsaw: fells the whole tree you hit
/// (logs and leaves touching, 2 E a block).
/// </summary>
public static class Tools
{
    public static StateSchema.Field ChargeField(ItemStack s) => s?.Def?.State.Find("charge");
    public static int Charge(ItemStack s) { var f = ChargeField(s); return f == null ? 0 : f.Value(s.State); }
    public static int MaxCharge(ItemStack s) { var f = ChargeField(s); return f == null ? 0 : f.Min + f.Count - 1; }
    public static void SetCharge(ItemStack s, int c) { var f = ChargeField(s); if (f != null) s.State = f.With(s.State, Math.Clamp(c, f.Min, f.Min + f.Count - 1) - f.Min); }
    static void Give(BlockPos p) { foreach (var d in ItemsUtil.Drops(p)) PlayerInventory.Add(d); ItemsUtil.Remove(p); }
    static bool Diggable(BlockPos c) { if (!c.Loaded) return false; int id = c.Id; return id != 0 && !LIQ[id] && !BlockStates.entities.ContainsKey(c.Key) && (DEFS[id]?.Hardness ?? 1) >= 0; }
    /// <summary>Drill: 3 x 3 across the axis you look along, centred on the hit block. Returns blocks dug.</summary>
    public static int Drill(ItemStack tool, BlockPos hit, V3 look)
    {
        var (ax, ay, az) = hit.Axes(); double x = Math.Abs(M.Dot(look, ax)), y = Math.Abs(M.Dot(look, ay)), z = Math.Abs(M.Dot(look, az));
        (int, int, int) u, v; if (x >= y && x >= z) { u = (0, 1, 0); v = (0, 0, 1); } else if (y >= z) { u = (1, 0, 0); v = (0, 0, 1); } else { u = (1, 0, 0); v = (0, 1, 0); }
        int dug = 0, ch = Charge(tool);
        for (int a = -1; a <= 1; a++) for (int b = -1; b <= 1; b++)
            {
                if (!hit.TryOffset(u.Item1 * a + v.Item1 * b, u.Item2 * a + v.Item2 * b, u.Item3 * a + v.Item3 * b, out var c) || !Diggable(c)) continue;
                int cost = (int)Math.Ceiling(MinerBehavior.Cost(c.Id)); if (ch < cost) { UI.Note("Drill: out of charge (charge it in a charger)", 2); goto done; }
                ch -= cost; Give(c); dug++;
            }
        done: SetCharge(tool, ch); return dug;
    }
    /// <summary>Chainsaw: the logs and leaves joined to the hit log (up to 400 blocks).</summary>
    public static int Chainsaw(ItemStack tool, BlockPos hit)
    {
        if (!LOGID[hit.Id]) { UI.Note("Chainsaw: hit a log", 1.5); return 0; }
        int ch = Charge(tool), cut = 0; var seen = new HashSet<long> { hit.Key }; var q = new Queue<BlockPos>(); q.Enqueue(hit); var list = new List<BlockPos>();
        while (q.Count > 0 && list.Count < 400)
        {
            var p = q.Dequeue(); list.Add(p);
            for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                    {
                        if (!p.TryOffset(dx, dy, dz, out var n) || !n.Loaded || seen.Contains(n.Key)) continue; int id = n.Id;
                        if (LOGID[id] || LEAFS[id]) { seen.Add(n.Key); q.Enqueue(n); }
                    }
        }
        foreach (var p in list.OrderBy(p => LEAFS[p.Id] ? 1 : 0)) { if (ch < 2) { UI.Note("Chainsaw: out of charge", 2); break; } ch -= 2; Give(p); cut++; }
        SetCharge(tool, ch); return cut;
    }
    public static string Hud(ItemStack held) => held?.Def?.Behavior is "drill" or "chainsaw" ? $"\n{held.Def.Name.ToUpperInvariant()}: {Charge(held)} / {MaxCharge(held)} E" : "";
}

/// <summary>Charger: fills the electric tool in its slot from the grid (40 E/s, 95 %).</summary>
public sealed class ChargerBehavior : BlockBehavior, IEnergyMachine
{
    public const double Rate = 40, Efficiency = .95;
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p)
    {
        var s = e.Inv("slot")?[0]; if (s == null || s.IsEmpty || Tools.ChargeField(s) == null) { e.SetNum("charge", 0); e.SetNum("charge_max", 1); return; }
        e.SetNum("charge", Tools.Charge(s)); e.SetNum("charge_max", Tools.MaxCharge(s));
        int room = Tools.MaxCharge(s) - Tools.Charge(s); if (room <= 0) return;
        p.Demand = Math.Min(Rate * dt, room / Efficiency);
    }
    public void Settle(BlockEntity e, double dt, double made, double got)
    {
        WaterMachines.Show(e, "power", got / dt); var s = e.Inv("slot")?[0]; if (s == null || got <= 0) return;
        double acc = e.Num("part") + got * Efficiency; int add = (int)acc; e.SetNum("part", acc - add); Tools.SetCharge(s, Tools.Charge(s) + add);
    }
    public override string Describe(BlockEntity e) => e.Num("charge_max") > 1 ? $"CHARGING {e.Num("charge"):0} / {e.Num("charge_max"):0} E  {e.Num("power"):0.0} E/S" : "PUT AN ELECTRIC TOOL IN IT";
}

/// <summary>Grow lights (lamps with "lamp": {"grow": true}) that are lit make crops within 4 blocks grow twice as fast.</summary>
public static class GrowLights
{
    public static readonly ConcurrentDictionary<long, BlockPos> Lit = new();
    public const int Reach = 4;
    public static double Boost(BlockPos p)
    {
        if (Lit.IsEmpty || p.Core) return 1;
        foreach (var l in Lit.Values) if (l.B == p.B && l.F == p.F && Math.Abs(l.X - p.X) <= Reach && Math.Abs(l.Y - p.Y) <= Reach && Math.Abs(l.Z - p.Z) <= Reach) return 2;
        return 1;
    }
}

/// <summary>
/// Sprinkler: takes water only from a water pipe at its back (the pipe's far end in water or at a tank valve; the water really leaves).
/// The water needs pressure to spray: the source's surface (plus a tank's air pressure) must stand above the nozzle, so feed it from a
/// higher basin or a pressurised tank (a pump can fill those). Every 4 s it looks at the farmland within a radius of 4 blocks (its own row and
/// two below): when any of it needs water (moisture 6 of 8 or less) it sprays, filling the dry farmland back to 8, 1 unit of water per
/// moisture level (at most 24 a spray), 0.3 E a unit for the valve. Wet farmland: no spray, no water used.
/// </summary>
public sealed class SprinklerBehavior : BlockBehavior, IEnergyMachine
{
    public const double EPerUnit = .3, Every = 4, RadiusSq = 4.5 * 4.5, SprayShow = 2.5; public const int Reach = 4, MaxPerCycle = 24, NeedsAt = 6;
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    IEnumerable<BlockPos> Thirsty(BlockEntity e)
    {
        for (int dy = -2; dy <= 0; dy++) for (int dz = -Reach; dz <= Reach; dz++) for (int dx = -Reach; dx <= Reach; dx++)
                    if (dx * dx + dz * dz <= RadiusSq && e.Pos.TryOffset(dx, dy, dz, out var c) && c.Loaded && Farming.IsFarmland(c.Id) && Farming.MoistureAt(c) < 8) yield return c;
    }
    /// <summary>A pipe proper (water pipe, large pipe, pipe valve): not a tank valve, pump or machine that also joins pipes.</summary>
    public static bool IsPlainPipe(int id) => id > 0 && DEFS[id]?.IdName is "liquid_pipe" or "large_liquid_pipe" or "liquid_pipe_valve" or "dual_liquid_pipe";
    /// <summary>Water from the pipe at its back, with enough pressure to reach the nozzle (status 1: no pipe, 2: no water on it, 3: too little pressure).</summary>
    static WaterEnd Source(BlockEntity e, out int status)
    {
        var (dx, dy, dz) = WaterMachines.Dir(e); status = 1;
        if (!e.Pos.TryOffset(-dx, -dy, -dz, out var b) || !b.Loaded || !IsPlainPipe(b.Id)) return default;
        var src = WaterMachines.IntakeEnd(e); status = 2; if (!src.Ok || src.Available < 1 || src.Liquid != Water) return default;
        double g = Math.Max(WaterMachines.G(e.Pos), 1e-6), head = src.SourceSurface + src.Gauge / (WaterMachines.Rho * g);
        status = 3; if (head < e.Pos.Y + 1) return default;
        status = 0; return src;
    }
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p)
    {
        e.SetNum("spray", Math.Max(0, e.Num("spray") - dt)); if (!e.Pos.Loaded) return;
        if (e.Num("job") > 0)
        {   // a spray still going: the rest of its water as the valve gets energy
            Source(e, out int st); e.SetNum("status", st); if (st != 0) { e.SetNum("job", 0); return; }
            p.Demand = e.Num("job") * EPerUnit; return;
        }
        double t = e.Num("timer") - dt; if (t > 0) { e.SetNum("timer", t); return; } e.SetNum("timer", Every);
        var src = Source(e, out int status); e.SetNum("status", status); if (status != 0) return;
        bool needs = false; int need = 0;
        foreach (var c in Thirsty(e)) { int m = Farming.MoistureAt(c); need += 8 - m; needs |= m <= NeedsAt; }
        if (!needs) return; // the soil is still wet enough
        need = Math.Min(Math.Min(need, MaxPerCycle), src.Available); if (need <= 0) return;
        e.SetNum("job", need); p.Demand = need * EPerUnit;
    }
    public void Settle(BlockEntity e, double dt, double made, double got)
    {
        int n = (int)Math.Min(e.Num("job"), Math.Floor(got / EPerUnit + 1e-9)); if (n <= 0) return;
        var src = Source(e, out int status); if (status != 0) return;
        int took = src.Tank != null ? src.Tank.Take(n) : TakeWater(src.Cell, n), used = took;
        foreach (var c in Thirsty(e).OrderBy(c => Farming.MoistureAt(c)).ToList())
        {
            if (took <= 0) break; int m = Farming.MoistureAt(c), add = Math.Min(8 - m, took); Farming.SetMoisture(c, m + add); took -= add;
        }
        if (took > 0) WaterSim.Reservoir += took; // (never happens: it only takes what the soil needs)
        e.SetNum("job", Math.Max(0, e.Num("job") - n)); e.SetNum("watered", e.Num("watered") + used); e.SetNum("spray", SprayShow);
    }
    public override string Describe(BlockEntity e) => (int)e.Num("status") switch
    {
        1 => "NO PIPE: CONNECT A WATER PIPE TO ITS BACK", 2 => "NO WATER ON ITS PIPE",
        3 => "NOT ENOUGH PRESSURE: THE WATER SOURCE MUST BE HIGHER THAN THE NOZZLE (OR A PRESSURISED TANK)",
        _ => $"{(e.Num("spray") > 0 ? "SPRAYING" : "WAITING FOR DRY SOIL")}  WATERED {e.Num("watered"):0} UNITS SO FAR  (RADIUS {Reach}, SPRAYS AT MOISTURE {NeedsAt} OR LESS)"
    };
}

/// <summary>
/// Elevator, built like a real traction lift:
/// <list type="bullet">
/// <item><b>Guide Rails</b>: a straight vertical column of rail blocks along one side of the shaft;</item>
/// <item><b>Elevator Motor</b> on top of the rail column (the hoist: a cable drum; it takes energy from the grid);</item>
/// <item><b>Elevator Car</b> 1x1 / 3x3 / 5x5: placed against the rail column (anywhere along it), it hangs on the rails from that side and is
/// carried by a cable from the motor; a counterweight runs down the rails as the car goes up.</item>
/// </list>
/// The car's floor is solid and carries you. Floors: the bottom of the rails, the top (2 blocks under the motor) and every <b>Elevator Stop</b>
/// placed beside the rail column one block above a landing's floor (where a call button goes). On the car: jump = next floor up, sneak =
/// next floor down; right click a stop to call the car there, the motor to send it down. The car stops if something is in the shaft.
/// Energy is real: the counterweight balances the car, so the motor lifts your 80 kg (x gravity x height / 80 %; going down gives half
/// back) plus friction. It keeps up to 50 E from the grid (5 E/s); without energy the car waits. Breaking the motor gives the car back.
/// </summary>
public sealed class ElevatorBehavior : BlockBehavior, IEnergyMachine
{
    public const double Buffer = 50, ChargeRate = 5;
    public override void OnPlace(BlockPos p, int id) => Elevator.Add(BlockStates.EntityOrNew(p, id));
    public override void OnBreak(BlockPos p, int id, int state) => Elevator.Cars.TryRemove(p.Key, out _);
    public override IEnumerable<ItemStack> Harvest(BlockPos p, int id, int state)
    {
        var res = new List<ItemStack>(); var it = GameAssets.ItemReg.All.FirstOrDefault(i => i != null && i.PlacesBlockId == id); if (it != null) res.Add(new ItemStack(it.Id, 1));
        if (BlockStates.entities.TryGetValue(p.Key, out var e) && e.Num("car") > 0) { var car = Elevator.CarItem((int)e.Num("car")); if (car != null) res.Add(new ItemStack(car.Id, 1)); }
        return res;
    }
    public override bool Usable => true;
    public override bool OnUse(BlockPos p, int id, int state)
    {
        var e = BlockStates.EntityOrNew(p, id); if (e == null) return false; Elevator.Add(e);
        if (e.Num("car") <= 0) { UI.Note(Elevator.Rails(e) < 2 ? "Elevator Motor: build a column of Guide Rails under it" : "Elevator Motor: place an Elevator Car against its rails", 2.5); return true; }
        Elevator.Call(e, Elevator.Floors(e)[0]); return true;
    }
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p) { Elevator.Add(e); p.StoreIn = Math.Max(0, Math.Min(ChargeRate * dt, Buffer - e.Num("energy"))); }
    public void Settle(BlockEntity e, double dt, double made, double got) => e.SetNum("energy", Math.Min(Buffer, e.Num("energy") + got));
    public override string Describe(BlockEntity e)
    {
        int rails = Elevator.Rails(e);
        if (rails < 2) return "NO GUIDE RAILS: BUILD A COLUMN OF GUIDE RAILS STRAIGHT DOWN FROM THE MOTOR";
        if (e.Num("car") <= 0) return $"{rails} RAILS, NO CAR: PLACE AN ELEVATOR CAR AGAINST THE RAILS  {e.Num("energy"):0} / {Buffer:0} E";
        int s = (int)e.Num("car"); var fl = Elevator.Floors(e); int bottom = fl[0];
        string moving = Math.Abs(e.Num("target") - e.Num("fy")) > 1e-6 ? $"GOING TO {e.Num("target") - bottom:0}" : "STOPPED";
        return $"CAR {s}X{s} AT {e.Num("fy") - bottom:0.0} ({moving})  FLOORS {string.Join(" ", fl.Select(f => f - bottom))}  {e.Num("energy"):0} / {Buffer:0} E" + (e.Num("blocked") > 0 ? "  SHAFT BLOCKED" : "") + (e.Num("stalled") > 0 ? "  NO ENERGY" : "");
    }
}

/// <summary>Elevator Car (1x1 / 3x3 / 5x5: key "elevator_car": {"size"}): placed against a guide rail column with a motor on top, it becomes that
/// motor's car (the block itself goes; the car is drawn and moved by the motor).</summary>
public sealed class ElevatorCarBehavior : BlockBehavior
{
    public static int Size(int id) => Math.Clamp((int)Electric.Key(BEH[id], id, "elevator_car", "size", 1), 1, 9) | 1;
    public override bool CanPlaceAt(BlockPos at, int id, out string why)
    {
        why = "An Elevator Car goes against a column of Guide Rails with an Elevator Motor on top";
        if (!Elevator.RailBeside(at, out var rail, out _)) return false;
        var m = Elevator.MotorOver(rail); if (m == null) return false;
        if (m.Num("car") > 0) { why = "This elevator already has a car (break the motor to take it back)"; return false; }
        return true;
    }
    public override void OnPlace(BlockPos p, int id)
    {
        if (!Elevator.RailBeside(p, out var rail, out var dir) || Elevator.MotorOver(rail) is not { } m) return;
        int s = Size(id); m.SetNum("car", s); m.SetNum("cdx", dir.x); m.SetNum("cdz", dir.z);
        double fy = Math.Clamp(p.Y - m.Pos.Y, Elevator.Bottom(m), -2); m.SetNum("fy", fy); m.SetNum("target", fy);
        Elevator.Add(m); p.Set(0); // the car now hangs on the rails (drawn and moved by the motor)
        UI.Note($"Elevator Car {s}x{s} hung on the rails", 1.5);
    }
}

/// <summary>Elevator Stop: beside the guide rail column, one block above a landing's floor (a call button) it adds a floor there; right click calls
/// the car to it.</summary>
public sealed class ElevatorStopBehavior : BlockBehavior
{
    public override bool Usable => true;
    public override bool OnUse(BlockPos p, int id, int state)
    {
        var e = Elevator.ForStop(p);
        if (e == null) { UI.Note("Elevator Stop: put it beside an elevator's guide rails, one block above the landing's floor", 2.5); return true; }
        if (e.Num("car") <= 0) { UI.Note("This elevator has no car yet", 1.5); return true; }
        Elevator.Call(e, p.Y - 1 - e.Pos.Y); return true;
    }
}

public static class Elevator
{
    public const double PlayerKg = 80, MotorEff = .8, Regen = .5, Speed = 3, Thick = .125, FrictionPerBlock = .01; public const int MaxRails = 96;
    public sealed class Car { public BlockEntity E; public V3 At; }
    /// <summary>Every elevator motor (filled when placed and by the energy tick, so loaded worlds find theirs).</summary>
    public static readonly ConcurrentDictionary<long, Car> Cars = new();
    public static void Add(BlockEntity e)
    {
        if (e == null || e.Pos.Core || BEH[e.BlockId] is not ElevatorBehavior) return;
        if (!Cars.TryGetValue(e.Pos.Key, out var l) || l.E != e) Cars[e.Pos.Key] = new Car { E = e, At = e.Pos.Center };
    }
    static bool Alive(BlockEntity e) => BlockStates.entities.TryGetValue(e.Pos.Key, out var x) && x == e && BEH[e.BlockId] is ElevatorBehavior;
    public static bool IsRail(int id) => id > 0 && DEFS[id]?.IdName == "guide_rail";
    public static ItemDefinition CarItem(int size)
    {
        foreach (var b in GameAssets.BlockReg.All) if (b != null && BEH[b.Id] is ElevatorCarBehavior && ElevatorCarBehavior.Size(b.Id) == size) return GameAssets.ItemReg.All.FirstOrDefault(i => i != null && i.PlacesBlockId == b.Id);
        return null;
    }
    /// <summary>Guide rails in the column straight down from the motor.</summary>
    public static int Rails(BlockEntity m) { int n = 0; while (n < MaxRails && m.Pos.TryOffset(0, -(n + 1), 0, out var c) && c.Loaded && IsRail(c.Id)) n++; return n; }
    /// <summary>Lowest car floor (relative to the motor's Y): the bottom of the rails.</summary>
    public static int Bottom(BlockEntity m) => -Rails(m);
    static readonly (int x, int y, int z)[] Sides = { (1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1) };
    /// <summary>A guide rail beside this cell; dir points from the rail to the cell.</summary>
    public static bool RailBeside(BlockPos at, out BlockPos rail, out (int x, int y, int z) dir)
    {
        foreach (var d in Sides) if (at.TryOffset(-d.x, 0, -d.z, out rail) && rail.Loaded && IsRail(rail.Id)) { dir = d; return true; }
        rail = default; dir = default; return false;
    }
    /// <summary>The motor at the top of the rail column this rail belongs to.</summary>
    public static BlockEntity MotorOver(BlockPos rail)
    {
        for (int k = 1; k <= MaxRails + 1; k++)
        {
            if (!rail.TryOffset(0, k, 0, out var c) || !c.Loaded) return null;
            if (IsRail(c.Id)) continue;
            return BEH[c.Id] is ElevatorBehavior ? BlockStates.EntityOrNew(c, c.Id) : null;
        }
        return null;
    }
    static bool Free(BlockPos c) { if (!c.Loaded) return false; int id = c.Id; return id == 0 || LIQ[id] || SPC[id] != 0; }
    /// <summary>The car's cells at floor height fy (relative to the motor): N deep away from the rails, N wide.</summary>
    static IEnumerable<(int dx, int dz)> Footprint(BlockEntity m)
    {
        int n = (int)m.Num("car"), fx = (int)m.Num("cdx"), fz = (int)m.Num("cdz"), lx = fz, lz = -fx;
        for (int i = 1; i <= n; i++) for (int j = -n / 2; j <= n / 2; j++) yield return (fx * i + lx * j, fz * i + lz * j);
    }
    /// <summary>Is the shaft clear for the car (2 blocks over its floor) at every whole floor height between a and b?</summary>
    static bool ShaftClear(BlockEntity m, double a, double b, out int at)
    {
        int lo = (int)Math.Floor(Math.Min(a, b)), hi = (int)Math.Ceiling(Math.Max(a, b)); at = 0;
        for (int y = lo; y <= hi + 1; y++)
            foreach (var (dx, dz) in Footprint(m))
                if (!m.Pos.TryOffset(dx, y, dz, out var c) || !Free(c)) { at = y; return false; }
        return true;
    }
    /// <summary>Car floor heights (relative to the motor) it stops at: the bottom, the top (2 under the motor) and each stop beside the rails.</summary>
    public static List<int> Floors(BlockEntity m)
    {
        int bottom = Bottom(m); var fl = new SortedSet<int> { bottom, Math.Max(bottom, -2) };
        for (int k = 1; k <= -bottom; k++)
            foreach (var d in Sides)
                if (m.Pos.TryOffset(d.x, -k, d.z, out var c) && c.Loaded && BEH[c.Id] is ElevatorStopBehavior) { int f = -k - 1; if (f >= bottom && f <= -2) fl.Add(f); }
        return fl.ToList();
    }
    /// <summary>The elevator whose rails this stop stands beside.</summary>
    public static BlockEntity ForStop(BlockPos s)
    {
        foreach (var d in Sides) if (s.TryOffset(d.x, 0, d.z, out var r) && r.Loaded && IsRail(r.Id)) return MotorOver(r);
        return null;
    }
    /// <summary>Sends the car to floor height f (relative to the motor), if the shaft is clear.</summary>
    public static void Call(BlockEntity m, int f)
    {
        f = Math.Clamp(f, Bottom(m), -2);
        if (!ShaftClear(m, m.Num("fy"), f, out int at)) { m.SetNum("blocked", 1); UI.Note($"Elevator: something is in the shaft ({at - Bottom(m)} up)", 2); return; }
        m.SetNum("blocked", 0); m.SetNum("target", f);
        UI.Note(Math.Abs(f - m.Num("fy")) < 1e-6 ? "Elevator: the car is here" : "Elevator: car coming", 1.5);
    }
    /// <summary>A world point against a car: inside its floor area (widened by pad) and its height above the floor.</summary>
    static bool Over(Car l, V3 pt, double pad, out double above)
    {
        above = 0; var m = l.E.Pos; int n = (int)l.E.Num("car"); if (n <= 0) return false;
        int bi = P2b(pt.X, pt.Y, pt.Z); if (bi != m.B || PF != m.F) return false;
        double x = PX + PFX - (m.X + .5), z = PZ + PFZ - (m.Z + .5); int fx = (int)l.E.Num("cdx"), fz = (int)l.E.Num("cdz");
        double u = x * fx + z * fz, v = x * fz - z * fx;
        if (u < .5 - pad || u > .5 + n + pad || Math.Abs(v) > n / 2.0 + pad) return false;
        above = PY + PFY - m.Y - l.E.Num("fy"); return true;
    }
    /// <summary>Collision: is this world point inside a car's floor? (Called from the player's hitbox test.)</summary>
    public static bool SolidAt(double x, double y, double z)
    {
        V3 pt = new(x, y, z);
        foreach (var l in Cars.Values)
        {
            if (l.E.Num("car") <= 0 || (pt - l.At).Len > MaxRails + 10) continue;
            if (Over(l, pt, 0, out double above) && above < 0 && above >= -Thick) return true;
        }
        return false;
    }
    /// <summary>Energy (E) to lift the player h blocks at gravity g (negative h: what going down gives back).</summary>
    public static double Cost(double h, double g) => h >= 0 ? PlayerKg * g * h / MotorEff / Tank.JoulesPerE : PlayerKg * g * h * Regen / Tank.JoulesPerE;
    static bool lastJump, lastSneak;
    /// <summary>Moves the cars (and the player standing in one) and reads jump / sneak in a car. Called every frame before the player's
    /// physics; true when the jump was used to ride (the player does not jump).</summary>
    public static bool Update(double dt, bool jump, bool sneak)
    {
        bool jumpEdge = jump && !lastJump, sneakEdge = sneak && !lastSneak; lastJump = jump; lastSneak = sneak;
        if (Cars.IsEmpty) return false;
        var P = PlayerPhysics.P; Car on = null; bool eat = false;
        foreach (var (key, l) in Cars)
        {
            var e = l.E; if (!Alive(e)) { Cars.TryRemove(key, out _); continue; }
            if (!e.Pos.Loaded || e.Num("car") <= 0) continue;
            double h = e.Num("fy"), target = e.Num("target"), above = 0;
            bool rider = P.on && !P.fly && !P.core && Over(l, P.p, PlayerPhysics.HW, out above) && above > -.2 && above < .25;
            if (rider) on = l;
            if (Math.Abs(target - h) < 1e-9) continue;
            double dh = Math.Clamp(target - h, -Speed * dt, Speed * dt); V3 up = M.Nrm(P.p);
            if (rider)
            {
                if (dh > 0 && Collision.Pcol(P.p + up * (dh + Math.Max(0, -above) + .01), PlayerPhysics.HW)) { e.SetNum("target", h); UI.Note("Elevator: no room above you", 1.5); continue; }
            }
            else if (dh < 0 && P.on && Over(l, P.p, PlayerPhysics.HW, out double ab) && ab < -Thick && ab + 1.8 > dh - Thick) { e.SetNum("target", h); UI.Note("Elevator: stopped (you are under the car)", 1.5); continue; }
            int n = (int)e.Num("car");
            double g = WaterMachines.G(e.Pos), cost = FrictionPerBlock * n * n * Math.Abs(dh) + (rider ? Cost(dh, g) : 0);
            if (cost > e.Num("energy")) { e.SetNum("stalled", 1); continue; } // waits for energy from the grid
            e.SetNum("stalled", 0); e.SetNum("energy", Math.Min(ElevatorBehavior.Buffer, e.Num("energy") - cost));
            e.SetNum("fy", Math.Abs(target - h - dh) < 1e-9 ? target : h + dh);
            if (rider) { P.p = P.p + up * (dh + Math.Max(0, -above)); if (above < .05) { P.vu = 0; P.gnd = true; } eat |= jump; }
        }
        if (on != null && (jumpEdge || sneakEdge) && Math.Abs(on.E.Num("target") - on.E.Num("fy")) < 1e-9)
        {
            var e = on.E; double h = e.Num("fy"); var fl = Floors(e);
            int? next = jumpEdge ? fl.Where(f => f > h + .01).Cast<int?>().FirstOrDefault() : fl.Where(f => f < h - .01).Cast<int?>().LastOrDefault();
            if (next == null) UI.Note(jumpEdge ? "Elevator: top floor" : "Elevator: bottom floor", 1.2);
            else { Call(e, next.Value); eat |= jumpEdge && e.Num("blocked") == 0; }
        }
        return eat;
    }
}

/// <summary>
/// Extractor: takes items out of the inventory at its back (a machine's output or a chest) and puts them onto the conveyor belt at its front
/// (at the belt's start, when there is room), or straight into a chest / machine in front. 4 items a second, 0.2 E each.
/// </summary>
public sealed class ExtractorBehavior : BlockBehavior, IEnergyMachine
{
    public const double PerItem = .2, ItemsPerSecond = 4;
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    static BlockEntity Source(BlockEntity e) { var (dx, dy, dz) = WaterMachines.Dir(e); return e.Pos.TryOffset(-dx, -dy, -dz, out var b) && BlockStates.entities.TryGetValue(b.Key, out var s) && !Conveyor.IsBelt(s.BlockId) && ItemsUtil.OutputOf(s) != null ? s : null; }
    static BlockEntity Front(BlockEntity e) { var (dx, dy, dz) = WaterMachines.Dir(e); return e.Pos.TryOffset(dx, dy, dz, out var f) && f.Loaded && BlockStates.entities.TryGetValue(f.Key, out var t) ? t : null; }
    static bool Give(BlockEntity front, ItemStack s, bool really)
    {
        if (front == null) return false;
        if (Conveyor.IsBelt(front.BlockId)) return Conveyor.CanTake(front) && (!really || Conveyor.Put(front, s, 0));
        if (!ItemsUtil.Accepts(front, s)) return false;
        return !really || ItemsUtil.Insert(ItemsUtil.InputFor(front, s), new ItemStack(s.Item, 1, s.State)) == 1;
    }
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p)
    {
        var src = Source(e); var outInv = src == null ? null : ItemsUtil.OutputOf(src);
        if (outInv == null || outInv.All(s => s == null || s.IsEmpty)) { e.SetNum("status", src == null ? 1 : 2); return; }
        var s0 = outInv.First(s => s != null && !s.IsEmpty);
        if (!Give(Front(e), s0, false)) { e.SetNum("status", Front(e) == null ? 4 : 3); return; }
        e.SetNum("status", 0); p.Demand = PerItem * ItemsPerSecond * dt;
    }
    public void Settle(BlockEntity e, double dt, double made, double got)
    {
        if (got <= 0) return; double acc = e.Num("acc") + got / PerItem; int n = (int)acc; e.SetNum("acc", Math.Min(acc - n, 1));
        var src = Source(e); if (src == null) return; var outInv = ItemsUtil.OutputOf(src); var front = Front(e);
        for (int k = 0; k < n; k++)
        {
            int si = Array.FindIndex(outInv, s => s != null && !s.IsEmpty); if (si < 0) break; var s = outInv[si];
            if (!Give(front, s, true)) break;
            if (--s.Count <= 0) outInv[si] = null;
            e.SetNum("moved", e.Num("moved") + 1);
        }
    }
    public override string Describe(BlockEntity e) => (int)e.Num("status") switch
    {
        1 => "NOTHING TO TAKE FROM: ITS BACK MUST TOUCH A CHEST OR A MACHINE", 2 => "SOURCE EMPTY", 3 => "THE BELT / INVENTORY IN FRONT IS FULL",
        4 => "NOTHING IN FRONT: PUT A CONVEYOR BELT OR A CHEST THERE",
        _ => $"MOVED {e.Num("moved"):0} ITEMS  (4 A SECOND, 0.2 E EACH)"
    };
}

/// <summary>Grid status lamp: shows its network's stored energy: green above half, yellow above 10 %, red below (or no storage); glows.</summary>
public sealed class StatusLampBehavior : BlockBehavior, IEnergyMachine
{
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p) => p.Demand = .1 * dt;
    public void Settle(BlockEntity e, double dt, double made, double got)
    {
        int level = 0;
        if (got > 0 && EnergyNet.NetOf.TryGetValue(e.Pos.Key, out var n) && n.Capacity > 0) { double f = n.Stored / n.Capacity; level = f > .5 ? 2 : f > .1 ? 1 : 0; }
        e.SetNum("level", level); e.SetNum("on", got > 0 ? 1 : 0);
        var fl = DEFS[e.BlockId]?.State.Find("level");
        if (fl != null && fl.Packed && e.Pos.Loaded && e.Pos.Id == e.BlockId) { int st = e.Pos.State, want = (int)fl.With((uint)st, level); if (want != st) BlockStates.Set(e.Pos, want, true); }
        if (got > 0) BlockLights.Set(e.Pos, 5); else BlockLights.Remove(e.Pos);
    }
    public override string Describe(BlockEntity e) => EnergyNet.NetOf.TryGetValue(e.Pos.Key, out var n) && n.Capacity > 0 ? $"GRID STORAGE {n.Stored / n.Capacity * 100:0}% ({(int)e.Num("level") switch { 2 => "GREEN", 1 => "YELLOW", _ => "RED" }})" : "NO STORAGE ON THIS GRID (RED)";
}
