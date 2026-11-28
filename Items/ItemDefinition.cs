using System.Text.Json;
using static CubePlanet.DefinitionFile;

namespace CubePlanet;

/// <summary>
/// One item from item_definitions.txt. Item IDs are their own ID space (1..65535); the item texture ID equals the item ID.
/// properties.definition: stack_size [64], places_block {namespace, id_name} (block items), fuel_time (seconds of burning in a furnace),
/// behavior (bucket), capacity (bucket units), mass (kg of one item; items that place a block weigh what the block does, see Mass). properties.instance_state: packed fields (32 bits per stack, see StateSchema).
/// </summary>
public sealed class ItemDefinition
{
    public int Id; public string Namespace, IdName, Name;
    public string FullName => Namespace + ":" + IdName;
    public int StackSize = 64;
    public string PlacesBlock; // "namespace:id_name" or null
    public int PlacesBlockId;  // resolved by the registry (0 = none)
    public string Behavior = ""; public double FuelTime, Mass; public int Capacity;
    public string[] Tags = Array.Empty<string>();
    public StateSchema State = new();
    public JsonElement Json;
    /// <summary>
    /// How the inventory icon is made (properties.definition.icon): "model" = the placed block's 3D model, drawn live in the slot (no picture stored)
    /// (like Minecraft's block items); "sprite" = the block's texture drawn flat (plants, vines); "texture" = this item's own picture in the item atlas.
    /// Default: "model" for cube / boxes blocks, "sprite" for cross / attached / liquid blocks, "texture" for items that place no block.
    /// </summary>
    public string Icon = "";
    /// <summary>Block state values the icon shows (properties.definition.icon_state, e.g. fence arms); facing defaults to the front turned to the viewer.</summary>
    public JsonElement IconState;

    public static ItemDefinition Parse(JsonElement e, string file)
    {
        var it = new ItemDefinition { Json = e };
        it.Id = Get(e, "id") is { ValueKind: JsonValueKind.Number } id ? id.GetInt32() : throw new InvalidDataException($"{file}: an item has no numeric id");
        it.IdName = Str(e, "id_name"); it.Namespace = Str(e, "namespace", "base"); it.Name = Str(e, "name", it.IdName);
        if (it.IdName.Length == 0) throw new InvalidDataException($"{file}: item {it.Id} has no id_name");
        const string D = "properties.definition.";
        it.StackSize = (int)Num(e, D + "stack_size", 64);
        if (Get(e, D + "places_block") is { ValueKind: JsonValueKind.Object } pb) it.PlacesBlock = Str(pb, "namespace", "base") + ":" + Str(pb, "id_name");
        it.Icon = Str(e, D + "icon");
        if (it.Icon.Length > 0 && it.Icon != "model" && it.Icon != "sprite" && it.Icon != "texture") throw new InvalidDataException($"{file}: item {it.FullName}: unknown icon '{it.Icon}' (use model, sprite, texture)");
        it.IconState = Get(e, D + "icon_state") ?? default;
        it.Behavior = Str(e, D + "behavior"); it.FuelTime = Num(e, D + "fuel_time"); it.Capacity = (int)Num(e, D + "capacity"); it.Mass = Num(e, D + "mass");
        if (Get(e, "group_tags") is { ValueKind: JsonValueKind.Array } tags) it.Tags = tags.EnumerateArray().Select(t => t.GetString() ?? "").ToArray();
        it.State = StateSchema.Parse(Get(e, "properties.instance_state"), $"{file}: item {it.FullName}", 32);
        if (it.State.Entity.Count > 0) throw new InvalidDataException($"{file}: item {it.FullName}: item state can only hold packed fields (boolean, string with values, integer with min/max)");
        return it;
    }
    public bool HasTag(string t) => Array.IndexOf(Tags, t) >= 0;
}
