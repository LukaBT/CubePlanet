using System.Text.Json;
using static CubePlanet.DefinitionFile;

namespace CubePlanet;

/// <summary>
/// recipe_definitions.txt: {"recipes": [{"type": "smelting", "id_name": "...", "input": {namespace, id_name}, "output": {namespace, id_name, count},
/// "time": seconds}]}; count may be a fraction (0.6 glass from a sand block: the furnace keeps the fraction). Only smelting so far.
/// </summary>
public sealed class RecipeRegistry
{
    public sealed class Smelting { public string IdName; public int Input, Output; public double Count, Time; public (int, double)[] Outs => new[] { (Output, Count) }; }
    public readonly List<Smelting> SmeltingRecipes = new();
    readonly Dictionary<int, Smelting> byInput = new();

    public RecipeRegistry(IEnumerable<JsonElement> defs, ItemRegistry items, string file)
    {
        foreach (var e in defs)
        {
            string type = Str(e, "type"), name = Str(e, "id_name", "?");
            if (type != "smelting") throw new InvalidDataException($"{file}: recipe '{name}' has unknown type '{type}' (smelting)");
            int Item(string key)
            {
                var o = Get(e, key) ?? throw new InvalidDataException($"{file}: recipe '{name}' has no {key}");
                string n = Str(o, "namespace", "base") + ":" + Str(o, "id_name");
                return items.Has(n) ? items.Id(n) : throw new InvalidDataException($"{file}: recipe '{name}': unknown item '{n}'");
            }
            var r = new Smelting { IdName = name, Input = Item("input"), Output = Item("output"), Count = Num(e, "output.count", 1), Time = Num(e, "time", 10) };
            if (!byInput.TryAdd(r.Input, r)) throw new InvalidDataException($"{file}: two smelting recipes use the same input ({name})");
            SmeltingRecipes.Add(r);
        }
    }
    public Smelting SmeltingFor(int item) => byInput.TryGetValue(item, out var r) ? r : null;
}
