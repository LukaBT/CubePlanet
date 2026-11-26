namespace CubePlanet;

/// <summary>What a machine offers / wants in one energy step (amounts in E for this step, not per second).</summary>
public struct EnergyPlan
{
    /// <summary>Made no matter what (a turbine set to "on"); surplus charges batteries, then runs pumps, else it is wasted.</summary>
    public double Fixed;
    /// <summary>Can be made on demand (a turbine set to "auto"): used only when the fixed production does not cover the demand.</summary>
    public double Dispatch;
    /// <summary>Wanted (an oxygen generator, a pump set to "on"); shared out proportionally when there is not enough.</summary>
    public double Demand;
    /// <summary>Takes surplus only (a pump set to "auto": pumped storage charges from spare energy).</summary>
    public double Flexible;
    /// <summary>Can be made on demand and also to fill batteries (a coal generator: it burns only when the energy is used or stored).</summary>
    public double DispatchStore;
    /// <summary>Storage (batteries): how much it can take in / give out this step.</summary>
    public double StoreIn, StoreOut;
}

/// <summary>A block whose behaviour takes part in energy networks.</summary>
public interface IEnergyMachine
{
    void Plan(BlockEntity e, double dt, ref EnergyPlan p);
    /// <summary>made = energy it produced (fixed + dispatched + discharged), got = energy it received (demand + flexible + charged).</summary>
    void Settle(BlockEntity e, double dt, double made, double got);
}

/// <summary>
/// Energy networks: machines (block entities whose behaviour is an <see cref="IEnergyMachine"/>) joined by cable blocks (group tag
/// "energy_cable") or touching each other. Every 0.1 s each network is balanced:
/// <list type="number">
/// <item>fixed production (turbines "on") against demand (machines, pumps "on");</item>
/// <item>surplus: batteries charge, then flexible consumers (pumps "auto": pumped storage) take the rest, anything left is wasted;</item>
/// <item>deficit: dispatchable production (turbines "auto": a dam opens its turbine) covers it, then batteries; if still short, every consumer
/// gets the same share of what it asked for.</item>
/// </list>
/// Then the energy is routed through the network (<see cref="CableFlow"/>): each cable block carries at most its capacity (definition key
/// "cable": {"capacity": E/s at LV, "loss"}) times the voltage level of its part of the grid (<see cref="Voltage"/>); transformers join
/// levels, wires (<see cref="Wires"/>) join pylons. A machine makes / takes only what actually gets through, the rest is not made (fixed
/// production) or not delivered; line and transformer losses come off what the consumers get. Unloaded land takes part too: its grid
/// blocks come from <see cref="GridCache"/>.
/// </summary>
public static class EnergyNet
{
    /// <summary>A network's last step, for the HUD and the multimeter (E/s unless noted).</summary>
    public sealed class Net
    {
        public readonly List<BlockEntity> Machines = new(); public double Supply, Demand, Stored, Capacity, Loss;
        public double Available, FromStore, Made, Wanted, Got, Unused, DemandMet = 1; public int Cables, Wires, Transformers, Levels;
        public int CablesAtLimit, CablesOver, TransformersAtLimit, TransformersFighting, WiresAtLimit, BatteriesAtRate, BatteriesFull, BatteriesEmptying, Generators30, WrongV;
        public readonly SortedSet<string> LimitCables = new(); public double LineMore;
    }
    /// <summary>The networks of the last step (for the HUD): by machine.</summary>
    public static readonly Dictionary<long, Net> NetOf = new();
    /// <summary>... and by every block in them (cables, transformers, anchors too): the multimeter.</summary>
    public static readonly Dictionary<long, Net> NodeNet = new();
    public static int LastNetworks;
    /// <summary>Energy through each cable block in the last step: flow and capacity (E/s), its voltage level, its loss and cooling (E/s), and
    /// whether the level is above its rating (then it carries nothing), for the HUD.</summary>
    public readonly record struct Load(double Flow, double Cap, double Level, double Loss, double Upkeep = 0, bool Over = false);
    public static readonly Dictionary<long, Load> CableLoad = new();
    /// <summary>Machines on a part of the grid above LV (they do nothing), with that level (HUD).</summary>
    public static readonly Dictionary<long, double> WrongVoltage = new();
    /// <summary>A cable: capacity in E/s at LV, loss at full current per block, the highest voltage its insulation takes, cooling E/s per block.</summary>
    public readonly record struct CableInfo(double Cap, double Loss, double MaxV, double Upkeep);
    static readonly Dictionary<int, CableInfo> cableOf = new();
    /// <summary>Definition key "cable": {"capacity", "loss", "max_voltage": "LV" / "MV" / "HV", "upkeep"}.</summary>
    public static CableInfo CableSpec(int id)
    {
        lock (cableOf)
        {
            if (cableOf.TryGetValue(id, out var c)) return c;
            c = DEFS[id] != null && DefinitionFile.Get(DEFS[id].Json, "properties.definition.cable") is { ValueKind: System.Text.Json.JsonValueKind.Object } j
                ? new CableInfo(DefinitionFile.Num(j, "capacity", 64), DefinitionFile.Num(j, "loss", .003), Voltage.Of(DefinitionFile.Str(j, "max_voltage", "LV")), DefinitionFile.Num(j, "upkeep", 0))
                : new CableInfo(64, .003, 1, 0);
            return cableOf[id] = c;
        }
    }
    public static double Capacity(int id) => CableSpec(id).Cap;
    public static string DescribeCable(BlockPos p)
    {
        var c = CableSpec(p.Id); string rated = $"RATED UP TO {Voltage.Name(c.MaxV)}" + (c.Upkeep > 0 ? $", COOLING {c.Upkeep:0.00} E/S" : "");
        if (!CableLoad.TryGetValue(p.Key, out var l))
            return $"CARRIES UP TO {c.Cap:0} E/S AT LV" + (c.MaxV >= 8 ? $", {c.Cap * 8:0} AT MV" : "") + (c.MaxV >= 64 ? $", {c.Cap * 64:0} AT HV" : "") + $"  {rated}  NOTHING FLOWING";
        if (l.Over) return $"{Voltage.Name(l.Level)} LINE BUT THIS CABLE IS {rated}: ITS INSULATION FAILS, IT CARRIES NOTHING";
        return $"{Voltage.Name(l.Level)}  {l.Flow:0.0} / {l.Cap:0} E/S  LOSS {l.Loss + l.Upkeep:0.00} E/S" + (l.Upkeep > 0 ? " (COOLING)" : "")
             + (l.Flow >= l.Cap * .995 ? "  AT ITS LIMIT (A BETTER CABLE, A SECOND LINE OR A HIGHER VOLTAGE)" : "");
    }

    static readonly (int x, int y, int z)[] N6 = { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };
    public static bool IsCable(int id) => DEFS[id]?.HasTag("energy_cable") ?? false;
    public static IEnergyMachine MachineOf(BlockEntity e) => BEH[e.BlockId] as IEnergyMachine;
    static bool IsMachine(BlockPos p) => BlockStates.entities.TryGetValue(p.Key, out var e) && MachineOf(e) != null;
    static bool IsTransformer(int id) => BEH[id] is TransformerBehavior;
    /// <summary>A transformer's back (low side) and front (high side) neighbours.</summary>
    static (BlockPos back, BlockPos front, bool ok) Sides(BlockPos t)
    {
        var e = BlockStates.entities.TryGetValue(t.Key, out var te) ? te : null;
        var (fx, fy, fz) = e != null ? WaterMachines.Dir(e) : (0, 0, -1);
        bool a = t.TryOffset(-fx, -fy, -fz, out var back), b = t.TryOffset(fx, fy, fz, out var front);
        return (back, front, a && b);
    }

    enum Kind : byte { Machine, Cable, Transformer, Anchor, Wire }
    /// <summary>One network being built: the flow graph plus what each node is.</summary>
    sealed class Build
    {
        public readonly CableFlow.Graph G = new();
        public readonly List<Kind> K = new(); public readonly List<double> CapLv = new(), LossF = new(), Len = new();
        public readonly List<BlockPos> Pos = new(); public readonly List<Wires.Link> Wire = new(); public readonly List<int> Id = new();
        public readonly Dictionary<int, (double maxV, double upkeep)> Rating = new(); // cables / wires: highest voltage, cooling per block
        public int Add(long key, Kind k, BlockPos p, int id, double cap = 0, double loss = 0, double len = 1, Wires.Link w = null)
        {
            K.Add(k); Id.Add(id); CapLv.Add(cap); LossF.Add(loss); Len.Add(len); Pos.Add(p); Wire.Add(w); return G.Add(key, CableFlow.Inf);
        }
    }

    public static void Tick(double dt)
    {
        NetOf.Clear(); NodeNet.Clear(); CableLoad.Clear(); WrongVoltage.Clear(); LastNetworks = 0;
        Wires.Clean();
        // wires by anchor (both ends)
        var wiresAt = new Dictionary<long, List<Wires.Link>>();
        foreach (var l in Wires.Links) { foreach (var p in new[] { l.A, l.B }) { if (!wiresAt.TryGetValue(p.Key, out var ls)) wiresAt[p.Key] = ls = new(); ls.Add(l); } l.Flow = l.LossNow = 0; }
        var q = new Queue<int>();
        foreach (var e in BlockStates.entities.Values.ToArray())
        {
            if (MachineOf(e) == null || NetOf.ContainsKey(e.Pos.Key)) continue; // machines in unloaded land take part too (GridCache)
            // the network this machine belongs to: flood through cables, machines, transformers (front / back only), anchors and their wires
            var net = new Net(); var b = new Build(); var node = new List<int>(); var done = new HashSet<int>();
            q.Clear(); q.Enqueue(b.Add(e.Pos.Key, Kind.Machine, e.Pos, e.BlockId));
            while (q.Count > 0)
            {
                int pi = q.Dequeue(); if (!done.Add(pi)) continue;
                var p = b.Pos[pi]; var kind = b.K[pi];
                if (kind == Kind.Wire)
                {
                    var l = b.Wire[pi];
                    foreach (var end in new[] { l.A, l.B })
                    {
                        if (!Wires.IsAnchor(end)) continue;
                        if (!b.G.Index.TryGetValue(end.Key, out int ei)) { if (b.G.Keys.Count >= 4096) continue; var (eid, est) = GridCache.At(end); if (end.Loaded) GridCache.Note(end, eid, est); ei = b.Add(end.Key, KindOf(end, eid), end, eid); q.Enqueue(ei); MaybeCable(b, ei); }
                        b.G.Edges.Add((pi, ei));
                    }
                    continue;
                }
                if (kind == Kind.Machine && BlockStates.entities.TryGetValue(p.Key, out var me) && MachineOf(me) != null) { net.Machines.Add(me); node.Add(pi); NetOf[p.Key] = net; }
                IEnumerable<BlockPos> Around()
                {
                    if (kind == Kind.Transformer) { var (bk, fr, ok) = Sides(p); if (ok) { yield return bk; yield return fr; } yield break; }
                    foreach (var (x, y, z) in N6) if (p.TryOffset(x, y, z, out var n)) yield return n;
                }
                foreach (var n in Around())
                {
                    if (!b.G.Index.TryGetValue(n.Key, out int ni))
                    {
                        // the live block where loaded (and remembered for when it unloads), the remembered block elsewhere
                        bool live = n.Loaded; var (id, st) = GridCache.At(n);
                        if (live) { if (GridCache.IsGridId(id)) GridCache.Note(n, id, st); else { GridCache.Forget(n); continue; } }
                        Kind nk;
                        if (IsTransformer(id)) { var (bk, fr, ok) = Sides(n); if (!ok || (bk.Key != p.Key && fr.Key != p.Key)) continue; nk = Kind.Transformer; }
                        else if (IsMachine(n)) nk = Kind.Machine; // before cables: a solar panel's main part is both
                        else if (IsCable(id)) nk = Kind.Cable;
                        else if (Wires.IsAnchor(id, st, out _)) nk = Kind.Anchor;
                        else continue;
                        if (b.G.Keys.Count >= 4096) continue; // a network ends after 4096 blocks
                        ni = b.Add(n.Key, nk, n, id); MaybeCable(b, ni); q.Enqueue(ni);
                    }
                    else if (b.K[ni] == Kind.Transformer && kind != Kind.Transformer) { var (bk, fr, ok) = Sides(b.Pos[ni]); if (!ok || (bk.Key != p.Key && fr.Key != p.Key)) continue; }
                    if (!done.Contains(ni)) b.G.Edges.Add((pi, ni));
                }
                // wires from this block (an anchor; cables and machines can be anchors too: wire connectors)
                if (wiresAt.TryGetValue(p.Key, out var wl) && Wires.IsAnchor(p))
                    foreach (var l in wl)
                    {
                        long wk = 0x7E00_0000_0000_0000L + Wires.Links.IndexOf(l);
                        if (!b.G.Index.TryGetValue(wk, out int wi))
                        {
                            if (b.G.Keys.Count >= 4096) continue;
                            wi = b.Add(wk, Kind.Wire, p, 0, Wires.WireSpec.Capacity, Wires.WireSpec.Loss, Math.Max(1, l.Length), l); q.Enqueue(wi); b.Rating[wi] = (Wires.WireSpec.MaxV, 0);
                        }
                        if (!done.Contains(wi)) b.G.Edges.Add((pi, wi));
                    }
            }
            LastNetworks++;
            foreach (var k in b.G.Keys) if (k < 0x7E00_0000_0000_0000L) NodeNet[k] = net;
            Balance(net, dt, b, node.ToArray());
        }
    }
    static Kind KindOf(BlockPos p, int id) => IsMachine(p) ? Kind.Machine : IsCable(id) ? Kind.Cable : Kind.Anchor;
    static void MaybeCable(Build b, int i)
    {
        if (b.K[i] == Kind.Cable) { var c = CableSpec(b.Id[i]); b.CapLv[i] = c.Cap; b.LossF[i] = c.Loss; b.Rating[i] = (c.MaxV, c.Upkeep); }
        else if (b.K[i] == Kind.Transformer) b.CapLv[i] = TransformerBehavior.SpecOf(b.Id[i]).Rating;
    }

    /// <summary>Voltage level of every node: parts of the grid between transformers get the level of the transformer sides touching them.
    /// Returns the level per node and whether each transformer works.</summary>
    static (double[] level, bool[] tOk) Levels(Build b)
    {
        int n = b.K.Count; var seg = new int[n]; for (int i = 0; i < n; i++) seg[i] = i;
        int Find(int x) { while (seg[x] != x) x = seg[x] = seg[seg[x]]; return x; }
        foreach (var (a, c) in b.G.Edges) if (b.K[a] != Kind.Transformer && b.K[c] != Kind.Transformer) seg[Find(a)] = Find(c);
        var lv = new Dictionary<int, double>(); var conflict = new HashSet<int>(); var tOk = new bool[n];
        var sides = new Dictionary<int, (int back, int front)>();
        for (int t = 0; t < n; t++)
        {
            if (b.K[t] != Kind.Transformer) continue;
            var (bk, fr, ok) = Sides(b.Pos[t]); int bi = -1, fi = -1;
            if (ok) { b.G.Index.TryGetValue(bk.Key, out bi); b.G.Index.TryGetValue(fr.Key, out fi); if (!b.G.Index.ContainsKey(bk.Key)) bi = -1; if (!b.G.Index.ContainsKey(fr.Key)) fi = -1; }
            sides[t] = (bi, fi); var s = TransformerBehavior.SpecOf(b.Id[t]);
            void Set(int node, double v) { if (node < 0 || b.K[node] == Kind.Transformer) return; int r = Find(node); if (lv.TryGetValue(r, out var o) && o != v) conflict.Add(r); lv[r] = Math.Max(o, v); }
            Set(bi, s.Low); Set(fi, s.High);
        }
        var level = new double[n];
        for (int i = 0; i < n; i++) level[i] = b.K[i] == Kind.Transformer ? 0 : lv.TryGetValue(Find(i), out var v) ? v : 1;
        foreach (var (t, (bi, fi)) in sides)
        {
            var s = TransformerBehavior.SpecOf(b.Id[t]);
            bool ok = (bi < 0 || (level[bi] == s.Low && !conflict.Contains(Find(bi)))) && (fi < 0 || (level[fi] == s.High && !conflict.Contains(Find(fi))));
            tOk[t] = ok; level[t] = s.High;
            if (BlockStates.entities.TryGetValue(b.Pos[t].Key, out var te)) te.SetNum("ok", ok ? 1 : -1);
        }
        return (level, tOk);
    }

    static void Balance(Net net, double dt, Build b, int[] node)
    {
        var (level, tOk) = Levels(b);
        int n = net.Machines.Count; var plans = new EnergyPlan[n];
        double fix = 0, dis = 0, dem = 0, flex = 0, sin = 0, sout = 0, dst = 0;
        for (int i = 0; i < n; i++)
        {
            if (level[node[i]] != 1) { WrongVoltage[net.Machines[i].Pos.Key] = level[node[i]]; continue; } // machines run on LV only
            MachineOf(net.Machines[i]).Plan(net.Machines[i], dt, ref plans[i]);
            fix += plans[i].Fixed; dis += plans[i].Dispatch; dst += plans[i].DispatchStore; dem += plans[i].Demand; flex += plans[i].Flexible; sin += plans[i].StoreIn; sout += plans[i].StoreOut;
        }
        // shares (0..1) of each kind that is actually used this step
        double fDis = 0, fDem = 1, fFlex = 0, fIn = 0, fOut = 0, fDst = 0;
        if (fix >= dem)
        {
            double surplus = fix - dem;
            double toStore = Math.Min(surplus, sin); surplus -= toStore;
            double toFlex = Math.Min(surplus, flex); fFlex = flex > 0 ? toFlex / flex : 0;
            // batteries still have room: generators that may fill storage (coal) run for it
            double d0 = Math.Min(sin - toStore, dst); fDst = dst > 0 ? d0 / dst : 0; fIn = sin > 0 ? (toStore + d0) / sin : 0;
        }
        else
        {
            double deficit = dem - fix;
            double d1 = Math.Min(deficit, dis); fDis = dis > 0 ? d1 / dis : 0; deficit -= d1;
            double dd = Math.Min(deficit, dst); deficit -= dd;
            // coal left after covering the deficit fills the batteries
            double room = Math.Min(sin, dst - dd); fDst = dst > 0 ? (dd + room) / dst : 0; fIn = sin > 0 ? room / sin : 0;
            double d2 = Math.Min(deficit, sout); fOut = sout > 0 ? d2 / sout : 0; deficit -= d2;
            fDem = dem > 0 ? (dem - deficit) / dem : 1;
        }
        net.Supply = (fix + dis * fDis + dst * fDst + sout * fOut) / dt; net.Demand = dem / dt;
        var made = new double[n]; var got = new double[n];
        for (int i = 0; i < n; i++)
        {
            var p = plans[i];
            made[i] = p.Fixed + p.Dispatch * fDis + p.DispatchStore * fDst + p.StoreOut * fOut; got[i] = p.Demand * fDem + p.Flexible * fFlex + p.StoreIn * fIn;
        }
        // node capacities this step: cables and wires carry their current x the level (nothing above their voltage rating: the insulation
        // fails), transformers their rating (nothing if fighting)
        var G = b.G; var over = new bool[G.Keys.Count];
        for (int v = 0; v < G.Keys.Count; v++)
        {
            if (b.Rating.TryGetValue(v, out var r) && level[v] > r.maxV) over[v] = true;
            G.Cap[v] = b.K[v] switch
            {
                Kind.Cable or Kind.Wire => over[v] ? 0 : b.CapLv[v] * level[v] * dt,
                Kind.Transformer => tOk[v] ? b.CapLv[v] * dt : 0,
                _ => CableFlow.Inf
            };
        }
        bool powered = made.Sum() > 0; // cooling (superconductors) runs while the network has a source
        net.Available = (fix + dis + dst) / dt; net.FromStore = sout / dt; net.Wanted = (dem + flex + sin) / dt;
        var lvSeen = new HashSet<double>(); for (int v = 0; v < G.Keys.Count; v++) if (b.K[v] != Kind.Transformer) lvSeen.Add(level[v]); net.Levels = lvSeen.Count;
        // what actually gets through (and is used: surplus nobody takes is not made)
        var (mo, go, th) = CableFlow.Route(G, node, made, got);
        // losses: I^2 R in cables and wires (I = energy / level; a block at full current loses "loss" of what it carries), 2 % in transformers
        double loss = 0;
        for (int v = 0; v < G.Keys.Count; v++)
        {
            double flow = th[v] / dt, l = 0;
            if (b.K[v] is Kind.Cable or Kind.Wire)
            {
                if (b.K[v] == Kind.Cable) net.Cables++; else net.Wires++;
                if (over[v]) net.CablesOver++;
                else if (G.Cap[v] > 0 && th[v] >= G.Cap[v] * .995)
                {
                    if (b.K[v] == Kind.Cable) { net.CablesAtLimit++; net.LimitCables.Add($"{(DEFS[b.Id[v]]?.Name ?? "cable").ToUpperInvariant()} ({b.CapLv[v] * level[v]:0} E/S AT {Voltage.Name(level[v])})"); }
                    else net.WiresAtLimit++;
                }
                double cur = flow / level[v], cap = b.CapLv[v]; l = cap > 0 ? b.LossF[v] * cap * (cur / cap) * (cur / cap) * b.Len[v] : 0;
                double up = powered && b.Rating.TryGetValue(v, out var rr) ? rr.upkeep * b.Len[v] : 0;
                if (b.K[v] == Kind.Cable) CableLoad[G.Keys[v]] = new Load(flow, over[v] ? 0 : b.CapLv[v] * level[v], level[v], l, up, over[v]);
                else { var w = b.Wire[v]; w.Flow = flow; w.Cap = over[v] ? 0 : b.CapLv[v] * level[v]; w.Level = level[v]; w.LossNow = l; }
                l += up;
            }
            else if (b.K[v] == Kind.Transformer)
            {
                net.Transformers++; if (!tOk[v]) net.TransformersFighting++; else if (G.Cap[v] > 0 && th[v] >= G.Cap[v] * .995) net.TransformersAtLimit++;
                l = flow * TransformerBehavior.SpecOf(b.Id[v]).Loss;
                if (BlockStates.entities.TryGetValue(G.Keys[v], out var te)) te.SetNum("flow", flow);
            }
            loss += l * dt;
        }
        double gotSum = go.Sum(); net.Loss = loss / dt;
        if (loss > 0 && gotSum > 0) { double k = Math.Max(0, 1 - loss / gotSum); for (int i = 0; i < n; i++) go[i] *= k; }
        // the multimeter's findings: what is made / used, what is wasted, what limits it
        net.Made = mo.Sum() / dt; net.Got = go.Sum() / dt; net.Unused = Math.Max(0, fix - mo.Sum()) / dt; net.WrongV = 0;
        double demW = 0, demG = 0;
        for (int i = 0; i < n; i++)
        {
            var me = net.Machines[i]; var pl = plans[i];
            if (WrongVoltage.ContainsKey(me.Pos.Key)) net.WrongV++;
            if (pl.Demand > 0) { demW += pl.Demand; demG += Math.Min(go[i], pl.Demand); }
            if (BEH[me.BlockId] is BatteryBehavior)
            {
                if (pl.StoreIn >= BatteryBehavior.Rate * dt * .999 && go[i] >= pl.StoreIn * .99 && net.Unused > 0) net.BatteriesAtRate++;
                if (pl.StoreIn <= 1e-9 && me.Num("energy") >= BatteryBehavior.Capacity - 1e-6) net.BatteriesFull++;
                if (pl.StoreOut > 0 && mo[i] >= pl.StoreOut * .99 && pl.StoreOut < BatteryBehavior.Rate * dt * .999) net.BatteriesEmptying++;
            }
            if (BEH[me.BlockId] is GeneratorBehavior && me.Num("input") >= GeneratorBehavior.MaxIn - 1e-6 && me.Num("line") > me.Num("line_load") + .5) { net.Generators30++; net.LineMore = Math.Max(net.LineMore, me.Num("line") - me.Num("line_load")); }
        }
        net.DemandMet = demW > 0 ? demG / demW : 1;
        for (int i = 0; i < n; i++)
        {
            MachineOf(net.Machines[i]).Settle(net.Machines[i], dt, mo[i], go[i]);
            if (net.Machines[i].Numbers.TryGetValue("energy", out double st) && BEH[net.Machines[i].BlockId] is BatteryBehavior) { net.Stored += st; net.Capacity += BatteryBehavior.Capacity; }
        }
    }
}
