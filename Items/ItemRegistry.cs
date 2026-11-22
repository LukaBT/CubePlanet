namespace CubePlanet;

/// <summary>Every item from item_definitions.txt, by ID and by "namespace:id_name" (unknown names throw).</summary>
public sealed class ItemRegistry
{
    readonly Dictionary<int, ItemDefinition> byId = new();
    readonly Dictionary<string, ItemDefinition> byName = new();
    readonly Dictionary<int, int> byBlock = new();
    public readonly List<ItemDefinition> All = new();

    public ItemRegistry(IEnumerable<ItemDefinition> defs, BlockRegistry blocks, string file)
    {
        All.AddRange(defs.OrderBy(d => d.Id));
        foreach (var d in All)
        {
            if (d.Id < 1 || d.Id > 65535) throw new InvalidDataException($"{file}: item '{d.FullName}' has id {d.Id}, ids must be 1..65535");
            if (!byId.TryAdd(d.Id, d)) throw new InvalidDataException($"{file}: item id {d.Id} is used twice");
            if (!byName.TryAdd(d.FullName, d)) throw new InvalidDataException($"{file}: item name '{d.FullName}' is used twice");
            if (d.PlacesBlock != null)
            {
                if (!blocks.Has(d.PlacesBlock)) throw new InvalidDataException($"{file}: item '{d.FullName}' places unknown block '{d.PlacesBlock}'");
                d.PlacesBlockId = blocks.Id(d.PlacesBlock); byBlock.TryAdd(d.PlacesBlockId, d.Id);
            }
        }
    }

    public ItemDefinition Get(int id) => byId.TryGetValue(id, out var d) ? d : null;
    public bool Has(string name) => byName.ContainsKey(name.Contains(':') ? name : "base:" + name);
    public int Id(string name) => byName.TryGetValue(name.Contains(':') ? name : "base:" + name, out var d) ? d.Id : throw new KeyNotFoundException($"unknown item '{name}' (not in {AssetPaths.ItemDefinitions})");
    /// <summary>The item that places a block (0 if none), e.g. for picking a block with the middle mouse button.</summary>
    public int ItemForBlock(int blockId) => byBlock.TryGetValue(blockId, out int i) ? i : 0;
}
