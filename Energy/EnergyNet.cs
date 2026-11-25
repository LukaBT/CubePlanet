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
/// "cable": {"capacity": E/s}); a machine makes / takes only what actually gets through, the rest is not made (fixed production) or not
/// delivered. Only loaded chunks take part (a cable into an unloaded chunk ends there).
/// </summary>
public static class EnergyNet
{
    public sealed class Net { public readonly List<BlockEntity> Machines = new(); public double Supply, Demand, Stored, Capacity; }
    /// <summary>The networks of the last step (for the HUD).</summary>
    public static readonly Dictionary<long, Net> NetOf = new();
    public static int LastNetworks;
    /// <summary>Energy through each cable block in the last step (E/s) and its capacity (for the HUD).</summary>
    public static readonly Dictionary<long, (double flow, double cap)> CableLoad = new();
    static readonly Dictionary<int, double> capOf = new();
    /// <summary>A cable block's capacity in E/s (definition key "cable": {"capacity"}; 64 if missing).</summary>
    public static double Capacity(int id)
    {
        lock (capOf)
        {
            if (capOf.TryGetValue(id, out var c)) return c;
            c = DEFS[id] != null && DefinitionFile.Get(DEFS[id].Json, "properties.definition.cable") is { ValueKind: System.Text.Json.JsonValueKind.Object } j ? DefinitionFile.Num(j, "capacity", 64) : 64;
            return capOf[id] = c;
        }
    }
    public static string DescribeCable(BlockPos p)
    {
        double cap = Capacity(p.Id);
        if (!CableLoad.TryGetValue(p.Key, out var l)) return $"CARRIES UP TO {cap:0} E/S  (NOTHING FLOWING)";
        return $"{l.flow:0.0} / {cap:0} E/S" + (l.flow >= cap * .995 ? "  AT ITS LIMIT (USE A BETTER CABLE OR A SECOND LINE)" : "");
    }

    static readonly int[][] N6 = { new[] { 1, 0, 0 }, new[] { -1, 0, 0 }, new[] { 0, 1, 0 }, new[] { 0, -1, 0 }, new[] { 0, 0, 1 }, new[] { 0, 0, -1 } };
    public static bool IsCable(int id) => DEFS[id]?.HasTag("energy_cable") ?? false;
    public static IEnergyMachine MachineOf(BlockEntity e) => BEH[e.BlockId] as IEnergyMachine;

    public static void Tick(double dt)
    {
        NetOf.Clear(); CableLoad.Clear(); LastNetworks = 0;
        var done = new HashSet<long>(); var q = new Queue<BlockPos>();
        foreach (var e in BlockStates.entities.Values.ToArray())
        {
            if (MachineOf(e) == null || NetOf.ContainsKey(e.Pos.Key) || !e.Pos.Loaded) continue;
            // the network this machine belongs to: flood through cables and machines, recording the blocks and the joins between them
            var net = new Net(); var g = new CableFlow.Graph(); var node = new List<int>();
            done.Clear(); q.Clear(); q.Enqueue(e.Pos); g.Add(e.Pos.Key, CableFlow.Inf);
            while (q.Count > 0)
            {
                var p = q.Dequeue(); done.Add(p.Key); int pi = g.Index[p.Key];
                if (BlockStates.entities.TryGetValue(p.Key, out var me) && MachineOf(me) != null) { net.Machines.Add(me); node.Add(pi); NetOf[p.Key] = net; }
                foreach (var o in N6)
                {
                    if (!p.TryOffset(o[0], o[1], o[2], out var n) || done.Contains(n.Key) || !n.Loaded) continue;
                    if (!g.Index.TryGetValue(n.Key, out int ni))
                    {
                        int id = n.Id; bool cable = IsCable(id);
                        if (!cable && !(BlockStates.entities.TryGetValue(n.Key, out var ne) && MachineOf(ne) != null)) continue;
                        if (g.Keys.Count >= 4096) continue; // a network ends after 4096 blocks
                        ni = g.Add(n.Key, cable ? Capacity(id) * dt : CableFlow.Inf); q.Enqueue(n);
                    }
                    g.Edges.Add((pi, ni));
                }
            }
            LastNetworks++;
            Balance(net, dt, g, node.ToArray());
        }
    }

    static void Balance(Net net, double dt, CableFlow.Graph g, int[] node)
    {
        int n = net.Machines.Count; var plans = new EnergyPlan[n];
        double fix = 0, dis = 0, dem = 0, flex = 0, sin = 0, sout = 0, dst = 0;
        for (int i = 0; i < n; i++)
        {
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
        // what actually gets through the cables (and is used: surplus nobody takes is not made)
        var (mo, go, th) = CableFlow.Route(g, node, made, got);
        for (int v = 0; v < g.Keys.Count; v++) if (g.Cap[v] < CableFlow.Inf) CableLoad[g.Keys[v]] = (th[v] / dt, g.Cap[v] / dt);
        for (int i = 0; i < n; i++)
        {
            MachineOf(net.Machines[i]).Settle(net.Machines[i], dt, mo[i], go[i]);
            if (net.Machines[i].Numbers.TryGetValue("energy", out double st) && BEH[net.Machines[i].BlockId] is BatteryBehavior) { net.Stored += st; net.Capacity += BatteryBehavior.Capacity; }
        }
    }
}
