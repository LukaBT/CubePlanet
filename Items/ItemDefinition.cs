using System.Text.Json;
using static CubePlanet.DefinitionFile;

namespace CubePlanet;

/// <summary>
/// One item from item_definitions.txt. Item IDs are their own ID space (1..65535); the item texture ID equals the item ID.
/// properties.definition: stack_size [64], places_block {namespace, id_name} (block items).
/// </summary>
public sealed class ItemDefinition
{
    public int Id; public string Namespace, IdName, Name;
    public string FullName => Namespace + ":" + IdName;
    public int StackSize = 64;
    public string PlacesBlock; // "namespace:id_name" or null
    public JsonElement Json;

    public static ItemDefinition Parse(JsonElement e, string file)
    {
        var it = new ItemDefinition { Json = e };
        it.Id = Get(e, "id") is { ValueKind: JsonValueKind.Number } id ? id.GetInt32() : throw new InvalidDataException($"{file}: an item has no numeric id");
        it.IdName = Str(e, "id_name"); it.Namespace = Str(e, "namespace", "base"); it.Name = Str(e, "name", it.IdName);
        if (it.IdName.Length == 0) throw new InvalidDataException($"{file}: item {it.Id} has no id_name");
        it.StackSize = (int)Num(e, "properties.definition.stack_size", 64);
        if (Get(e, "properties.definition.places_block") is { ValueKind: JsonValueKind.Object } pb)
            it.PlacesBlock = Str(pb, "namespace", "base") + ":" + Str(pb, "id_name");
        return it;
    }
}
