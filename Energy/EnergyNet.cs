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
/// Only loaded chunks take part (a cable into an unloaded chunk ends there).
/// </summary>
public static class EnergyNet
{
    public sealed class Net { public readonly List<BlockEntity> Machines = new(); public double Supply, Demand, Stored, Capacity; }
    /// <summary>The networks of the last step (for the HUD).</summary>
    public static readonly Dictionary<long, Net> NetOf = new();
    public static int LastNetworks;

    static readonly int[][] N6 = { new[] { 1, 0, 0 }, new[] { -1, 0, 0 }, new[] { 0, 1, 0 }, new[] { 0, -1, 0 }, new[] { 0, 0, 1 }, new[] { 0, 0, -1 } };
    public static bool IsCable(int id) => DEFS[id]?.HasTag("energy_cable") ?? false;
    public static IEnergyMachine MachineOf(BlockEntity e) => BEH[e.BlockId] as IEnergyMachine;

    public static void Tick(double dt)
    {
        NetOf.Clear(); LastNetworks = 0;
        var seen = new HashSet<long>(); var q = new Queue<BlockPos>();
        foreach (var e in BlockStates.entities.Values.ToArray())
        {
            if (MachineOf(e) == null || NetOf.ContainsKey(e.Pos.Key) || !e.Pos.Loaded) continue;
            // the network this machine belongs to: flood through cables and machines
            var net = new Net(); seen.Clear(); q.Clear(); q.Enqueue(e.Pos); seen.Add(e.Pos.Key);
            while (q.Count > 0 && seen.Count < 4096)
            {
                var p = q.Dequeue();
                if (BlockStates.entities.TryGetValue(p.Key, out var me) && MachineOf(me) != null) { net.Machines.Add(me); NetOf[p.Key] = net; }
                foreach (var o in N6)
                {
                    if (!p.TryOffset(o[0], o[1], o[2], out var n) || seen.Contains(n.Key) || !n.Loaded) continue;
                    int id = n.Id;
                    bool node = IsCable(id) || (BlockStates.entities.TryGetValue(n.Key, out var ne) && MachineOf(ne) != null);
                    if (!node) continue;
                    seen.Add(n.Key); q.Enqueue(n);
                }
            }
            LastNetworks++;
            Balance(net, dt);
        }
    }

    static void Balance(Net net, double dt)
    {
        int n = net.Machines.Count; var plans = new EnergyPlan[n];
        double fix = 0, dis = 0, dem = 0, flex = 0, sin = 0, sout = 0;
        for (int i = 0; i < n; i++)
        {
            MachineOf(net.Machines[i]).Plan(net.Machines[i], dt, ref plans[i]);
            fix += plans[i].Fixed; dis += plans[i].Dispatch; dem += plans[i].Demand; flex += plans[i].Flexible; sin += plans[i].StoreIn; sout += plans[i].StoreOut;
        }
        // shares (0..1) of each kind that is actually used this step
        double fDis = 0, fDem = 1, fFlex = 0, fIn = 0, fOut = 0;
        if (fix >= dem)
        {
            double surplus = fix - dem;
            double toStore = Math.Min(surplus, sin); fIn = sin > 0 ? toStore / sin : 0; surplus -= toStore;
            double toFlex = Math.Min(surplus, flex); fFlex = flex > 0 ? toFlex / flex : 0;
        }
        else
        {
            double deficit = dem - fix;
            double d1 = Math.Min(deficit, dis); fDis = dis > 0 ? d1 / dis : 0; deficit -= d1;
            double d2 = Math.Min(deficit, sout); fOut = sout > 0 ? d2 / sout : 0; deficit -= d2;
            fDem = dem > 0 ? (dem - deficit) / dem : 1;
        }
        net.Supply = (fix + dis * fDis + sout * fOut) / dt; net.Demand = dem / dt;
        for (int i = 0; i < n; i++)
        {
            var p = plans[i];
            double made = p.Fixed + p.Dispatch * fDis + p.StoreOut * fOut, got = p.Demand * fDem + p.Flexible * fFlex + p.StoreIn * fIn;
            MachineOf(net.Machines[i]).Settle(net.Machines[i], dt, made, got);
            if (net.Machines[i].Numbers.TryGetValue("energy", out double st) && BEH[net.Machines[i].BlockId] is BatteryBehavior) { net.Stored += st; net.Capacity += BatteryBehavior.Capacity; }
        }
    }
}
