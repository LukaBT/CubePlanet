using System.Text.Json;

namespace CubePlanet;

/// <summary>
/// entity_definitions.txt: {"entities": [...]}. The format is reserved for later (player, dropped item, arrow, animals, monsters); the file is
/// loaded and checked to be valid JSON so content can be added without a code change to the loader.
/// </summary>
public sealed class EntityRegistry
{
    public readonly List<JsonElement> All;
    public EntityRegistry(List<JsonElement> defs) => All = defs;
}
