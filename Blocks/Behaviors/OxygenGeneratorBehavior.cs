namespace CubePlanet;

/// <summary>
/// Oxygen generator, the example for KSP / industrial-mod style machines: burns fuel into "energy", takes water from buckets put into its
/// "water_in" slot into a "water" tank, and turns energy + water into "oxygen"; every 100 oxygen fills one oxygen canister in "output".
/// Block entity fields: inventories "fuel", "water_in", "output"; numbers "energy" (0..100), "water" (0..32 units), "oxygen" (0..100).
/// </summary>
public sealed class OxygenGeneratorBehavior : BlockBehavior
{
    public const double MaxEnergy = 100, MaxWater = 32, MaxOxygen = 100;
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);

    public override void OnTick(BlockEntity e, double dt)
    {
        var fuel = e.Inv("fuel"); var win = e.Inv("water_in"); var output = e.Inv("output");
        if (fuel == null || win == null || output == null) return;
        double energy = e.Num("energy"), water = e.Num("water"), oxygen = e.Num("oxygen");
        // fuel -> energy (a coal = 40 s of furnace fuel = 100 energy)
        if (energy < MaxEnergy - 50 && fuel[0] is { IsEmpty: false } f && f.Def?.FuelTime > 0)
        {
            energy = Math.Min(MaxEnergy, energy + f.Def.FuelTime * 2.5);
            if (--f.Count <= 0) fuel[0] = null;
        }
        // buckets -> water tank (the bucket stays, emptied)
        if (win[0] is { IsEmpty: false } b && b.Def?.Behavior == "bucket")
        {
            var fill = b.Def.State.Find("fill");
            int units = fill?.Value(b.State) ?? 0;
            if (units > 0 && water + units <= MaxWater) { water += units; b.State = fill.With(b.State, 0); }
        }
        // energy + water -> oxygen
        if (energy > 0 && water > 0 && oxygen < MaxOxygen)
        {
            double k = Math.Min(dt, Math.Min(energy / 5, water / .5));
            oxygen = Math.Min(MaxOxygen, oxygen + 10 * k); energy -= 5 * k; water -= .5 * k;
        }
        if (oxygen >= MaxOxygen && GameAssets.ItemReg.Has("oxygen_canister"))
        {
            int can = GameAssets.ItemReg.Id("oxygen_canister");
            if (output[0] == null || output[0].IsEmpty) { output[0] = new ItemStack(can, 1); oxygen = 0; }
            else if (output[0].Item == can && output[0].Count < output[0].MaxStack) { output[0].Count++; oxygen = 0; }
        }
        e.SetNum("energy", energy); e.SetNum("water", water); e.SetNum("oxygen", oxygen);
    }
}
