namespace CubePlanet;

/// <summary>
/// Every block from block_definitions.txt, by ID and by "namespace:id_name". IDs are 2 bytes (1..65535), 0 is air.
/// Lookups by name throw on an unknown name, so a typo in code or in a definition is found at startup.
/// </summary>
public sealed class BlockRegistry
{
    readonly BlockDefinition[] byId;
    readonly Dictionary<string, BlockDefinition> byName = new();
    public readonly List<BlockDefinition> All = new();
    /// <summary>Highest block ID + 1.</summary>
    public int Size => byId.Length;

    public BlockRegistry(IEnumerable<BlockDefinition> defs, string file)
    {
        All.AddRange(defs.OrderBy(d => d.Id));
        int max = 0;
        foreach (var d in All)
        {
            if (d.Id < 1 || d.Id > 65535) throw new InvalidDataException($"{file}: block '{d.FullName}' has id {d.Id}, ids must be 1..65535 (0 is air)");
            max = Math.Max(max, d.Id);
        }
        byId = new BlockDefinition[max + 1];
        foreach (var d in All)
        {
            if (byId[d.Id] != null) throw new InvalidDataException($"{file}: block id {d.Id} is used by '{byId[d.Id].FullName}' and '{d.FullName}'");
            if (!byName.TryAdd(d.FullName, d)) throw new InvalidDataException($"{file}: block name '{d.FullName}' is used twice");
            byId[d.Id] = d;
        }
    }

    public BlockDefinition Get(int id) => id > 0 && id < byId.Length ? byId[id] : null;
    public bool Has(string name) => byName.ContainsKey(Qualify(name));
    /// <summary>ID of "namespace:id_name" (a bare id_name means namespace "base"). Throws if unknown.</summary>
    public int Id(string name) => byName.TryGetValue(Qualify(name), out var d) ? d.Id : throw new KeyNotFoundException($"unknown block '{name}' (not in {AssetPaths.BlockDefinitions})");
    static string Qualify(string n) => n.Contains(':') ? n : "base:" + n;
}
