namespace CubePlanet;

/// <summary>
/// Multimeter (item behaviour "multimeter"): held while looking at a block of an energy network (cable, machine, transformer, pylon ...),
/// the HUD shows the whole network: what it makes, could make, uses, wants, loses and stores, and what limits it, in plain words. On a
/// rotation part (shaft, turbine, gearbox ...) it shows its line.
/// </summary>
public static class Multimeter
{
    public static bool Held => PlayerInventory.Held is { IsEmpty: false } h && h.Def?.Behavior == "multimeter";
    public static string Report(BlockPos p)
    {
        var main = Electric.MainPart(p);
        if (!EnergyNet.NodeNet.TryGetValue(p.Key, out var n) && !EnergyNet.NodeNet.TryGetValue(main.Key, out n))
        {
            if (BlockStates.entities.TryGetValue(main.Key, out var ke) && Kinetics.Of(ke) != null)
                return $"\nMULTIMETER (ROTATION LINE): SOURCES {ke.Num("line"):0.0} E/S, LOADS WANT {ke.Num("line_want"):0.0}, TAKE {ke.Num("line_load"):0.0} E/S, {Kinetics.Rpm(ke):0} RPM"
                     + (ke.Num("line") > ke.Num("line_load") + .5 ? $"\n  {ke.Num("line") - ke.Num("line_load"):0.0} E/S OF ROTATION UNUSED: ADD GENERATORS OR MACHINES ON THE LINE" : "")
                     + (ke.Num("line_want") > ke.Num("line") + .5 ? "\n  THE MACHINES WANT MORE THAN THE LINE MAKES: THEY RUN SLOWER" : "");
            return "\nMULTIMETER: NOT PART OF AN ENERGY NETWORK";
        }
        var s = new System.Text.StringBuilder();
        s.Append($"\nMULTIMETER: {n.Machines.Count} MACHINES, {n.Cables} CABLE BLOCKS" + (n.Wires > 0 ? $", {n.Wires} WIRES" : "") + (n.Transformers > 0 ? $", {n.Transformers} TRANSFORMERS ({n.Levels} VOLTAGE LEVELS)" : ""));
        s.Append($"\n  MAKING {n.Made:0.0} E/S (GENERATORS COULD MAKE {n.Available:0.0}{(n.FromStore > 0 ? $", BATTERIES GIVE UP TO {n.FromStore:0.0}" : "")})   USED {n.Got:0.0} E/S (WANTED {n.Wanted:0.0})   LOSSES {n.Loss:0.00} E/S");
        if (n.Capacity > 0) s.Append($"\n  STORED {n.Stored:0} / {n.Capacity:0} E ({n.Stored / n.Capacity * 100:0}%)");
        var lim = new List<string>();
        if (n.DemandMet < .995) lim.Add($"MACHINES GET ONLY {n.DemandMet * 100:0}% OF WHAT THEY NEED: MORE PRODUCTION OR STORAGE");
        if (n.CablesAtLimit > 0) lim.Add($"{n.CablesAtLimit} CABLE BLOCKS AT THEIR LIMIT: {string.Join(", ", n.LimitCables.Take(2))} - A BETTER CABLE, A SECOND LINE OR A HIGHER VOLTAGE");
        if (n.CablesOver > 0) lim.Add($"{n.CablesOver} CABLE BLOCKS ON A VOLTAGE ABOVE THEIR RATING CARRY NOTHING");
        if (n.WiresAtLimit > 0) lim.Add($"{n.WiresAtLimit} WIRES AT THEIR LIMIT: A HIGHER VOLTAGE OR A SECOND LINE");
        if (n.TransformersAtLimit > 0) lim.Add($"{n.TransformersAtLimit} TRANSFORMERS AT THEIR RATING: PUT ANOTHER BESIDE IT");
        if (n.TransformersFighting > 0) lim.Add($"{n.TransformersFighting} TRANSFORMERS PASS NOTHING: A SIDE TOUCHES ANOTHER VOLTAGE");
        if (n.Generators30 > 0) lim.Add($"{n.Generators30} GENERATORS AT THEIR 30 E/S MAX, {n.LineMore:0.0} E/S OF ROTATION LEFT: ADD GENERATORS ON THE SHAFT");
        if (n.BatteriesAtRate > 0) lim.Add($"{n.BatteriesAtRate} BATTERIES CHARGE AT THEIR 50 E/S MAX: MORE BATTERIES WOULD TAKE MORE");
        if (n.BatteriesFull > 0 && n.Unused > 0) lim.Add("BATTERIES FULL");
        if (n.BatteriesEmptying > 0) lim.Add("BATTERIES RUNNING EMPTY");
        if (n.Unused > .05) lim.Add($"{n.Unused:0.0} E/S OF PRODUCTION UNUSED: NOTHING TAKES IT");
        if (n.WrongV > 0) lim.Add($"{n.WrongV} MACHINES ON THE WRONG VOLTAGE DO NOTHING");
        if (n.Loss > n.Made * .1 && n.Made > 0) lim.Add($"LOSSES ARE {n.Loss / n.Made * 100:0}% OF WHAT IT MAKES: BETTER CABLES OR A HIGHER VOLTAGE");
        if (lim.Count == 0) s.Append("\n  NOTHING LIMITING IT");
        foreach (var l in lim) s.Append("\n  " + l);
        return s.ToString();
    }
}
