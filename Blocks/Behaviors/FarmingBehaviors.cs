using System.Text.Json;

namespace CubePlanet;

/// <summary>
/// Farming with real water (water goes soil -> plant -> air, nothing is created or lost):
/// <list type="bullet">
/// <item>Farmland ("farmland" behaviour, state "moisture" 0..8 = water units held in the soil): every <see cref="FarmlandMin"/> ..
/// <see cref="FarmlandMax"/> s, if not full, it takes 1 unit from the nearest water within 4 blocks sideways (its own row or one below),
/// so that water's level really drops. Without water and without a crop it dries out and turns back into dirt.</item>
/// <item>Crops ("crop" behaviour, state "age"): every <see cref="CropMin"/> .. <see cref="CropMax"/> s a growing crop drinks 1 unit from
/// the farmland under it and grows a stage; the water goes into the water cycle's reservoir (the plant gives it off). On dry farmland it
/// pauses, then wilts back a stage every <see cref="WiltAfter"/> dry checks; a seed (first stage) lies dormant 3 times as long before it
/// dries up into a dead bush. Ripe crops use no water.</item>
/// <item>Breaking a crop gives its harvest ("crop": {"seed", "harvest": [{"item", "min", "max"}]}) when ripe, else its seed back.
/// A hoe (item behaviour "hoe") tills dirt or grass into farmland.</item>
/// </list>
/// Farm blocks only tick in loaded chunks (like machines).
/// </summary>
public static class Farming
{
    public const double FarmlandMin = 8, FarmlandMax = 14, CropMin = 25, CropMax = 45; public const int WiltAfter = 3, DryToDirt = 6, Reach = 4;
    internal static readonly Random rng = new();
    /// <summary>How much faster than normal farm blocks tick (settings: Tick speed): soil soaking up water, crops growing and wilting.</summary>
    public static double Speed => Math.Clamp(Settings.SET.tick, .1, 100);
    internal static double Next(double a, double b) => a + rng.NextDouble() * (b - a);
    static int farmland = -2;
    public static int FarmlandId => farmland != -2 ? farmland : farmland = GameAssets.BlockReg.Has("farmland") ? GameAssets.BlockReg.Id("farmland") : -1;
    public static bool IsFarmland(int id) => id > 0 && BEH[id] is FarmlandBehavior;
    public static StateSchema.Field Moisture(int id) => DEFS[id].State.Find("moisture");
    public static int MoistureAt(BlockPos p) { var f = Moisture(p.Id); return f == null ? 0 : f.Value((uint)p.State); }
    public static void SetMoisture(BlockPos p, int m) { var f = Moisture(p.Id); if (f != null) BlockStates.Set(p, (int)f.With((uint)p.State, Math.Clamp(m, f.Min, f.Min + f.Count - 1) - f.Min), true); }

    /// <summary>Hoe: dirt or grass with air (or a plant) above becomes dry farmland.</summary>
    public static bool Till(BlockPos p)
    {
        int id = p.Id, fl = FarmlandId; if (fl < 0) return false;
        var d = DEFS[id];
        bool soil = id == Dirt || id == Grass || (d != null && d.Model == "cube" && (d.IdName.EndsWith("grass") || d.IdName == "podzol" || d.IdName == "moss_block"));
        if (!soil || !p.TryOffset(0, 1, 0, out var up)) return false;
        int u = up.Id; if (u != 0 && SPC[u] == 0) return false;
        if (u != 0) up.Set(0);
        p.Set(fl, 0); BEH[fl]?.OnPlace(p, fl); BlockActions.NotifyNeighbors(p);
        return true;
    }

    /// <summary>Nearest water cell within <see cref="Reach"/> blocks sideways, on the farmland's row or one below.</summary>
    public static WC WaterNear(BlockPos p)
    {
        for (int r = 1; r <= Reach; r++)
            for (int dy = 0; dy >= -1; dy--)
                for (int dz = -r; dz <= r; dz++) for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                        if (!p.TryOffset(dx, dy, dz, out var q) || q.Core || !q.Loaded) continue;
                        var c = Wcell(q.B, q.F, q.X, q.Y, q.Z); if (c.ok && c.id == Water && Wl(c) > 0) return c;
                    }
        return default;
    }
}

/// <summary>Farmland: holds water (state "moisture"), taken from water nearby. See <see cref="Farming"/>.</summary>
public sealed class FarmlandBehavior : BlockBehavior
{
    public override void OnPlace(BlockPos p, int id) { var e = BlockStates.EntityOrNew(p, id); if (e != null) e.SetNum("timer", Farming.Next(1, Farming.FarmlandMax)); }
    public override void OnTick(BlockEntity e, double dt)
    {
        var p = e.Pos; if (!p.Loaded) return;
        double t = e.Num("timer") - dt * Farming.Speed; if (t > 0) { e.SetNum("timer", t); return; }
        e.SetNum("timer", Farming.Next(Farming.FarmlandMin, Farming.FarmlandMax));
        if (!Farming.IsFarmland(p.Id)) return;
        int m = Farming.MoistureAt(p);
        if (m < 8)
        {
            var w = Farming.WaterNear(p);
            if (w.ok) { int took = TakeWater(w, 1); if (took > 0) Farming.SetMoisture(p, m += took); }
        }
        bool crop = p.TryOffset(0, 1, 0, out var up) && BEH[up.Id] is CropBehavior;
        if (m == 0)
        {
            double dry = e.Num("dry") + 1; e.SetNum("dry", dry);
            if (!crop && dry >= Farming.DryToDirt) { BlockStates.entities.Remove(p.Key); p.Set(Dirt); BlockActions.NotifyNeighbors(p); }
        }
        else e.SetNum("dry", 0);
    }
    public override string Describe(BlockEntity e) => $"MOISTURE {Farming.MoistureAt(e.Pos)} / 8";
}

/// <summary>A crop (state "age"): grows on farmland, one stage per unit of water. See <see cref="Farming"/>.</summary>
public sealed class CropBehavior : BlockBehavior
{
    sealed class Def { public string Seed = ""; public List<(string item, int min, int max)> Harvest = new(); }
    static readonly Dictionary<int, Def> defs = new();
    static Def DefOf(int id)
    {
        lock (defs)
        {
            if (defs.TryGetValue(id, out var d)) return d;
            d = new Def(); var j = DEFS[id].Json;
            if (DefinitionFile.Get(j, "properties.definition.crop") is { ValueKind: JsonValueKind.Object } c)
            {
                d.Seed = DefinitionFile.Str(c, "seed");
                if (DefinitionFile.Get(c, "harvest") is { ValueKind: JsonValueKind.Array } h)
                    foreach (var x in h.EnumerateArray()) d.Harvest.Add((DefinitionFile.Str(x, "item"), (int)DefinitionFile.Num(x, "min", 1), (int)DefinitionFile.Num(x, "max", 1)));
            }
            return defs[id] = d;
        }
    }
    static StateSchema.Field Age(int id) => DEFS[id].State.Find("age");
    public static int AgeOf(int id, int state) { var f = Age(id); return f == null ? 0 : f.Value((uint)state); }
    public static int MaxAge(int id) { var f = Age(id); return f == null ? 0 : f.Min + f.Count - 1; }

    public override bool CanPlaceAt(BlockPos at, int id, out string why)
    {
        why = $"{DEFS[id].Name} needs farmland (till dirt with a hoe)";
        return at.TryOffset(0, -1, 0, out var below) && Farming.IsFarmland(below.Id);
    }
    public override void OnPlace(BlockPos p, int id) { var e = BlockStates.EntityOrNew(p, id); if (e != null) e.SetNum("timer", Farming.Next(Farming.CropMin, Farming.CropMax)); }
    public override void OnNeighborChanged(BlockPos p, int id, BlockPos from)
    {
        if (p.TryOffset(0, -1, 0, out var below) && !Farming.IsFarmland(below.Id) && below.Loaded) BlockActions.Break(p); // its farmland is gone
    }
    public override void OnTick(BlockEntity e, double dt)
    {
        var p = e.Pos; if (!p.Loaded) return;
        double t = e.Num("timer") - dt * Farming.Speed * GrowLights.Boost(p); if (t > 0) { e.SetNum("timer", t); return; } // a lit grow light nearby: twice as fast
        e.SetNum("timer", Farming.Next(Farming.CropMin, Farming.CropMax));
        int id = p.Id; if (BEH[id] is not CropBehavior) return;
        int age = AgeOf(id, p.State), max = MaxAge(id); if (age >= max) return; // ripe: no more water
        if (!p.TryOffset(0, -1, 0, out var soil) || !Farming.IsFarmland(soil.Id)) return;
        int m = Farming.MoistureAt(soil);
        if (m >= 1)
        {   // drink 1 unit, grow a stage; the water leaves through the plant into the water cycle
            Farming.SetMoisture(soil, m - 1); WaterSim.Reservoir += 1;
            BlockStates.Set(p, (int)Age(id).With((uint)p.State, age + 1 - Age(id).Min), true); e.SetNum("dry", 0);
            return;
        }
        double dry = e.Num("dry") + 1; e.SetNum("dry", dry);
        if (dry < (age == 0 ? Farming.WiltAfter * 3 : Farming.WiltAfter)) return; // a seed lies dormant much longer before it dries up
        e.SetNum("dry", 0);
        if (age > 0) BlockStates.Set(p, (int)Age(id).With((uint)p.State, age - 1 - Age(id).Min), true); // wilts back a stage
        else { BlockStates.entities.Remove(p.Key); p.Set(DeadBush); BlockActions.NotifyNeighbors(p); } // dried up
    }
    public override IEnumerable<ItemStack> Harvest(BlockPos p, int id, int state)
    {
        var d = DefOf(id); var res = new List<ItemStack>();
        if (AgeOf(id, state) >= MaxAge(id))
            foreach (var (item, min, max) in d.Harvest) { int n = Farming.rng.Next(min, max + 1); if (n > 0 && GameAssets.ItemReg.Has(item)) res.Add(new ItemStack(GameAssets.ItemReg.Id(item), n)); }
        else if (d.Seed.Length > 0 && GameAssets.ItemReg.Has(d.Seed)) res.Add(new ItemStack(GameAssets.ItemReg.Id(d.Seed), 1));
        return res;
    }
    public override string Describe(BlockEntity e)
    {
        int id = e.Pos.Id, age = AgeOf(id, e.Pos.State), max = MaxAge(id);
        return age >= max ? "RIPE" : $"GROWING {age} / {max}" + (e.Num("dry") > 0 ? "  DRY" : "");
    }
}
