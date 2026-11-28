namespace CubePlanet;

/// <summary>
/// Furnace. Block entity fields: inventories "input", "fuel", "output" (1 slot each), numbers "progress" (seconds cooked), "burn" (seconds of
/// fuel left) and "burn_max"; optional packed boolean "lit" (state_overrides can show a lit front). Recipes: smelting entries of
/// recipe_definitions.txt; fuel: an item's fuel_time.
/// </summary>
public sealed class SmeltingBehavior : BlockBehavior
{
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);

    public override void OnTick(BlockEntity e, double dt)
    {
        var input = e.Inv("input"); var fuel = e.Inv("fuel"); var output = e.Inv("output");
        if (input == null || fuel == null || output == null) return;
        var r = input[0] is { IsEmpty: false } ? GameAssets.RecipeReg.SmeltingFor(input[0].Item) : null;
        bool canOut = r != null && Processing.Fits(e, output, r.Outs);
        double burn = e.Num("burn");
        if (burn <= 0 && canOut && fuel[0] is { IsEmpty: false } f && f.Def?.FuelTime > 0)
        {   // light the next piece of fuel
            burn = f.Def.FuelTime; e.SetNum("burn_max", burn);
            if (--f.Count <= 0) fuel[0] = null;
        }
        bool lit = burn > 0;
        if (lit) burn = Math.Max(0, burn - dt);
        e.SetNum("burn", burn);
        if (lit && canOut)
        {
            double prog = e.Num("progress") + dt;
            if (prog >= r.Time)
            {
                prog = 0;
                Processing.Give(e, output, r.Output, r.Count);
                if (--input[0].Count <= 0) input[0] = null;
            }
            e.SetNum("progress", prog);
            e.SetNum("cook_time", r.Time);
        }
        else e.SetNum("progress", Math.Max(0, e.Num("progress") - dt * 2)); // cools down without fuel or input
        var lf = DEFS[e.BlockId].State.Find("lit");
        if (lf != null && lf.Packed)
        {
            int st = e.Pos.State, want = (int)lf.With((uint)st, lit ? 1 : 0);
            if (want != st && e.Pos.Id == e.BlockId) BlockStates.Set(e.Pos, want);
        }
    }
}
