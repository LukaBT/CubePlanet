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
                if (id != 0 && id != Water && !BlockStates.entities.ContainsKey(c.Key) && (DEFS[id]?.Hardness ?? 1) >= 0 && SPC[id] == 0) return c;
                if (id != 0 && id != Water && SPC[id] != 0) return c; // plants: dig them too
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
/// water per kg), storing it in hydrogen tanks (1 m^3 each, up to 30 bar: ideal gas, pV = nRT at 20 C, ~2.5 kg = ~1230 mol). The fuel cell
/// turns it back into electricity (120 MJ/kg, 55 %) and the water back into the water cycle. Round trip ~33 %, but it keeps for ever.
/// 1 E = 12,262 J (125 kg x 9.81 m/s2 x 10 m: one unit of water falling 10 blocks on the surface).
/// </summary>
public static class Hydrogen
{
    public const double Hhv = 141.8e6, Lhv = 120e6, MolarMass = .002016, R = 8.314, T = 293, Volume = 1, MaxBar = 30;
    public static double MaxMol => MaxBar * 1e5 * Volume / (R * T);
    public static double Bar(BlockEntity tank) => tank.Num("mol") * R * T / Volume / 1e5;
    public static BlockEntity TankNear(BlockEntity e, bool wantGas)
    {
        BlockEntity best = null;
        foreach (var (dx, dy, dz) in ItemsUtil.N6)
            if (e.Pos.TryOffset(dx, dy, dz, out var n) && BlockStates.entities.TryGetValue(n.Key, out var t) && BEH[t.BlockId] is HydrogenTankBehavior)
                if (best == null || (wantGas ? t.Num("mol") > best.Num("mol") : t.Num("mol") < best.Num("mol"))) best = t;
        return best;
    }
}

public sealed class HydrogenTankBehavior : BlockBehavior
{
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public override string Describe(BlockEntity e) => $"HYDROGEN {e.Num("mol") * Hydrogen.MolarMass:0.000} KG ({e.Num("mol"):0} MOL)  {Hydrogen.Bar(e):0.0} / {Hydrogen.MaxBar:0} BAR  FUEL CELL ENERGY ~{e.Num("mol") * Hydrogen.MolarMass * Hydrogen.Lhv * FuelCellBehavior.Efficiency / Tank.JoulesPerE:0} E";
}

/// <summary>Electrolyzer: water from its back (a cell, pipe or tank) -> hydrogen into a hydrogen tank touching it, up to 100 E/s.</summary>
public sealed class ElectrolyzerBehavior : BlockBehavior, IEnergyMachine
{
    public const double Power = 100, Efficiency = .7;
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p)
    {
        var t = Hydrogen.TankNear(e, false); if (t == null || t.Num("mol") >= Hydrogen.MaxMol - 1e-6) { e.SetNum("status", t == null ? 1 : 2); return; }
        if (e.Num("water") < 1 && e.Pos.Loaded)
        {   // take a unit of water (125 kg) into its tank
            var src = WaterMachines.IntakeEnd(e);
            if (src.Ok && src.Available >= 1) { int got = src.Tank != null ? src.Tank.Take(1) : TakeWater(src.Cell, 1); e.SetNum("water", e.Num("water") + got); }
        }
        if (e.Num("water") <= 0) { e.SetNum("status", 3); return; }
        e.SetNum("status", 0); p.Demand = Power * dt;
    }
    public void Settle(BlockEntity e, double dt, double made, double got)
    {
        WaterMachines.Show(e, "power", got / dt); if (got <= 0) return;
        var t = Hydrogen.TankNear(e, false); if (t == null) return;
        double kg = got * Tank.JoulesPerE * Efficiency / Hydrogen.Hhv, mol = Math.Min(kg / Hydrogen.MolarMass, Hydrogen.MaxMol - t.Num("mol"));
        t.SetNum("mol", t.Num("mol") + mol); e.SetNum("water", Math.Max(0, e.Num("water") - mol * Hydrogen.MolarMass * 9 / 125)); // 9 kg of water per kg (the oxygen goes into the air)
        e.SetNum("made_kg", e.Num("made_kg") + mol * Hydrogen.MolarMass);
    }
    public override string Describe(BlockEntity e) => (int)e.Num("status") switch
    {
        1 => "NO HYDROGEN TANK NEXT TO IT", 2 => "HYDROGEN TANK FULL", 3 => "NO WATER (A WATER CELL, PIPE OR TANK AT ITS BACK)",
        _ => $"MAKING {e.Num("power") * Tank.JoulesPerE * Efficiency / Hydrogen.Hhv * 3600:0.00} KG HYDROGEN AN HOUR  {e.Num("power"):0.0} E/S  WATER {e.Num("water") * 125:0.0} KG"
    };
}

/// <summary>Fuel cell: hydrogen from a tank touching it -> electricity on demand (up to 40 E/s, 55 %), the water back into the water cycle.</summary>
public sealed class FuelCellBehavior : BlockBehavior, IEnergyMachine
{
    public const double Power = 40, Efficiency = .55;
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    static double EPerMol => Hydrogen.MolarMass * Hydrogen.Lhv * Efficiency / Tank.JoulesPerE;
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p)
    {
        var t = Hydrogen.TankNear(e, true); if (t == null || t.Num("mol") <= 1e-9) return;
        p.Dispatch = Math.Min(Power * dt, t.Num("mol") * EPerMol);
    }
    public void Settle(BlockEntity e, double dt, double made, double got)
    {
        WaterMachines.Show(e, "power", made / dt); if (made <= 0) return;
        var t = Hydrogen.TankNear(e, true); if (t == null) return;
        double mol = Math.Min(t.Num("mol"), made / EPerMol); t.SetNum("mol", t.Num("mol") - mol);
        WaterSim.Reservoir += mol * Hydrogen.MolarMass * 9 / 125; // the water it makes goes into the air (the water cycle)
    }
    public override string Describe(BlockEntity e) => $"{e.Num("power"):0.0} / {Power:0} E/S ON DEMAND  (HYDROGEN: {EPerMol:0.0} E PER MOL)";
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
    static bool Diggable(BlockPos c) { if (!c.Loaded) return false; int id = c.Id; return id != 0 && id != Water && !BlockStates.entities.ContainsKey(c.Key) && (DEFS[id]?.Hardness ?? 1) >= 0; }
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
/// Sprinkler: every 4 s waters the farmland within 4 blocks (its own row and one below), 1 unit of water per moisture level, taken from
/// its back (a water cell, a pipe or a tank: the water really leaves). Uses 0.3 E per unit (pressure for the spray).
/// </summary>
public sealed class SprinklerBehavior : BlockBehavior, IEnergyMachine
{
    public const double EPerUnit = .3, Every = 4; public const int Reach = 4, MaxPerCycle = 24;
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    IEnumerable<BlockPos> Thirsty(BlockEntity e)
    {
        for (int dy = -2; dy <= 0; dy++) for (int dz = -Reach; dz <= Reach; dz++) for (int dx = -Reach; dx <= Reach; dx++)
                    if (e.Pos.TryOffset(dx, dy, dz, out var c) && c.Loaded && Farming.IsFarmland(c.Id) && Farming.MoistureAt(c) < 8) yield return c;
    }
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p)
    {
        e.SetNum("job", 0); if (!e.Pos.Loaded) return;
        double t = e.Num("timer") - dt; if (t > 0) { e.SetNum("timer", t); return; } e.SetNum("timer", Every);
        int need = 0; foreach (var c in Thirsty(e)) need += 8 - Farming.MoistureAt(c); need = Math.Min(need, MaxPerCycle);
        var src = WaterMachines.IntakeEnd(e); if (!src.Ok) { e.SetNum("status", 1); return; }
        need = Math.Min(need, src.Available); if (need <= 0) { e.SetNum("status", 0); return; }
        e.SetNum("job", need); e.SetNum("status", 0); p.Demand = need * EPerUnit;
    }
    public void Settle(BlockEntity e, double dt, double made, double got)
    {
        int n = (int)Math.Min(e.Num("job"), Math.Floor(got / EPerUnit + 1e-9)); if (n <= 0) return;
        var src = WaterMachines.IntakeEnd(e); if (!src.Ok) return;
        int took = src.Tank != null ? src.Tank.Take(n) : TakeWater(src.Cell, n);
        foreach (var c in Thirsty(e).OrderBy(c => Farming.MoistureAt(c)).ToList())
        {
            if (took <= 0) break; int m = Farming.MoistureAt(c), add = Math.Min(8 - m, took); Farming.SetMoisture(c, m + add); took -= add;
        }
        if (took > 0) WaterSim.Reservoir += took; // (never happens: it only takes what the soil needs)
        e.SetNum("watered", e.Num("watered") + n);
    }
    public override string Describe(BlockEntity e) => e.Num("status") > 0 ? "NO WATER: A WATER CELL, PIPE OR TANK BELOW IT" : $"WATERED {e.Num("watered"):0} UNITS SO FAR  (REACH {Reach} BLOCKS)";
}

/// <summary>
/// Elevator: elevator blocks in a column, one per floor. Standing on one, jump to ride up to the next elevator above (within 64 blocks,
/// with room to stand on it), sneak to ride down. Energy is real: 80 kg x gravity here x the height / 80 % motor; riding down gives half
/// of it back (regenerative braking). Each elevator keeps up to 50 E from the grid (charging at 5 E/s); the ride uses the start's.
/// </summary>
public sealed class ElevatorBehavior : BlockBehavior, IEnergyMachine
{
    public const double Buffer = 50, ChargeRate = 5;
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p) => p.StoreIn = Math.Max(0, Math.Min(ChargeRate * dt, Buffer - e.Num("energy")));
    public void Settle(BlockEntity e, double dt, double made, double got) => e.SetNum("energy", Math.Min(Buffer, e.Num("energy") + got));
    public override string Describe(BlockEntity e) => $"ELEVATOR  {e.Num("energy"):0} / {Buffer:0} E  (JUMP: UP, SNEAK: DOWN)";
}

public static class Elevator
{
    public const double PlayerKg = 80, MotorEff = .8, Regen = .5, Speed = 4, Reach = 64;
    static bool riding; static V3 target; static bool lastJump, lastSneak;
    public static bool Riding => riding;
    static bool IsElevator(BlockPos p) => p.Loaded && BEH[p.Id] is ElevatorBehavior;
    /// <summary>The next elevator above (dir 1) or below (dir -1) in the column, with two free cells on top of it.</summary>
    public static BlockPos? Next(BlockPos from, int dir)
    {
        for (int k = 1; k <= Reach; k++)
        {
            if (!from.TryOffset(0, dir * k, 0, out var c) || !c.Loaded) return null;
            if (!IsElevator(c)) continue;
            if (c.TryOffset(0, 1, 0, out var a1) && c.TryOffset(0, 2, 0, out var a2) && (a1.Id == 0 || SPC[a1.Id] != 0) && (a2.Id == 0 || SPC[a2.Id] != 0)) return c;
        }
        return null;
    }
    /// <summary>Energy (E) to lift the player h blocks at gravity g (negative h: what riding down gives back).</summary>
    public static double Cost(double h, double g) => h >= 0 ? PlayerKg * g * h / MotorEff / Tank.JoulesPerE : PlayerKg * g * h * Regen / Tank.JoulesPerE;
    /// <summary>Called every frame before the player's physics; true while riding (the physics are skipped).</summary>
    public static bool Ride(double dt, bool jump, bool sneak)
    {
        bool jumpEdge = jump && !lastJump, sneakEdge = sneak && !lastSneak; lastJump = jump; lastSneak = sneak;
        var P = PlayerPhysics.P;
        if (riding)
        {
            V3 d = target - P.p; double len = d.Len, step = Speed * dt;
            if (len <= step) { P.p = target; riding = false; P.vu = 0; P.vh = new V3(); return false; }
            P.p = P.p + d * (step / len); P.vu = 0; P.vh = new V3(); return true;
        }
        if (!P.on || P.fly || !(jumpEdge || sneakEdge)) return false;
        V3 up = M.Nrm(P.p); if (!BlockPos.TryAt(P.p.X - up.X * .2, P.p.Y - up.Y * .2, P.p.Z - up.Z * .2, out var under) || !IsElevator(under)) return false;
        int dir = jumpEdge ? 1 : -1; var next = Next(under, dir); if (next == null) { UI.Note(dir > 0 ? "No elevator above" : "No elevator below", 1.5); return false; }
        var src = BlockStates.entities.TryGetValue(under.Key, out var se) ? se : null;
        double h = next.Value.Y - under.Y, g = WaterMachines.G(under), cost = Cost(h, g);
        if (src == null || src.Num("energy") < cost) { UI.Note($"Elevator: needs {cost:0.0} E, has {src?.Num("energy") ?? 0:0.0} (connect it to the grid)", 2); return false; }
        src.SetNum("energy", Math.Min(ElevatorBehavior.Buffer, src.Num("energy") - cost));
        V3 c = next.Value.Center; target = c + M.Nrm(c) * .5; riding = true; return true;
    }
}

/// <summary>
/// Item transport: item pipes and conveyor belts (group tag "item_pipe") join an extractor's front to inventories (chests, machines) along
/// them. The extractor takes items from the inventory at its back (a machine's output or a chest) and sends them to the nearest inventory
/// on its pipes that takes them (a furnace / mill input if it can work them, fuel slots for fuel, chests), 4 items a second, 0.2 E each.
/// </summary>
public sealed class ExtractorBehavior : BlockBehavior, IEnergyMachine
{
    public const double PerItem = .2, ItemsPerSecond = 4;
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public static bool IsItemPipe(int id) => DEFS[id]?.HasTag("item_pipe") ?? false;
    BlockEntity Source(BlockEntity e) { var (dx, dy, dz) = WaterMachines.Dir(e); return e.Pos.TryOffset(-dx, -dy, -dz, out var b) && BlockStates.entities.TryGetValue(b.Key, out var s) && ItemsUtil.OutputOf(s) != null ? s : null; }
    /// <summary>Inventories along the pipes from the front, nearest first.</summary>
    List<BlockEntity> Targets(BlockEntity e, BlockEntity src)
    {
        var res = new List<BlockEntity>(); var (dx, dy, dz) = WaterMachines.Dir(e);
        if (!e.Pos.TryOffset(dx, dy, dz, out var f) || !f.Loaded) return res;
        if (BlockStates.entities.TryGetValue(f.Key, out var direct) && direct != src && direct.Inventories.Count > 0) { res.Add(direct); return res; }
        if (!IsItemPipe(f.Id)) return res;
        var seen = new HashSet<long> { f.Key, e.Pos.Key }; var q = new Queue<BlockPos>(); q.Enqueue(f); var added = new HashSet<long>();
        while (q.Count > 0 && seen.Count < 2048)
        {
            var p = q.Dequeue();
            foreach (var (ox, oy, oz) in ItemsUtil.N6)
            {
                if (!p.TryOffset(ox, oy, oz, out var n) || !n.Loaded || seen.Contains(n.Key)) continue;
                if (IsItemPipe(n.Id)) { seen.Add(n.Key); q.Enqueue(n); continue; }
                if (BlockStates.entities.TryGetValue(n.Key, out var t) && t != src && t.Inventories.Count > 0 && added.Add(n.Key)) res.Add(t);
            }
        }
        return res;
    }
    public void Plan(BlockEntity e, double dt, ref EnergyPlan p)
    {
        var src = Source(e); var outInv = src == null ? null : ItemsUtil.OutputOf(src);
        if (outInv == null || outInv.All(s => s == null || s.IsEmpty)) { e.SetNum("status", src == null ? 1 : 2); return; }
        e.SetNum("status", 0); p.Demand = PerItem * ItemsPerSecond * dt;
    }
    public void Settle(BlockEntity e, double dt, double made, double got)
    {
        if (got <= 0) return; double acc = e.Num("acc") + got / PerItem; int n = (int)acc; e.SetNum("acc", Math.Min(acc - n, 1));
        var src = Source(e); if (src == null) return; var outInv = ItemsUtil.OutputOf(src); var targets = Targets(e, src);
        for (int k = 0; k < n; k++)
        {
            int si = Array.FindIndex(outInv, s => s != null && !s.IsEmpty); if (si < 0) break; var s = outInv[si];
            var t = targets.FirstOrDefault(x => ItemsUtil.Accepts(x, s)); if (t == null) { e.SetNum("status", 3); break; }
            ItemsUtil.Insert(ItemsUtil.InputFor(t, s), new ItemStack(s.Item, 1, s.State)); if (--s.Count <= 0) outInv[si] = null;
            e.SetNum("moved", e.Num("moved") + 1);
        }
    }
    public override string Describe(BlockEntity e) => (int)e.Num("status") switch
    {
        1 => "NOTHING TO TAKE FROM: ITS BACK MUST TOUCH A CHEST OR A MACHINE", 2 => "SOURCE EMPTY", 3 => "NO INVENTORY ON ITS PIPES TAKES IT",
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
