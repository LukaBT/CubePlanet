using System.Text.Json;
using static CubePlanet.DefinitionFile;

namespace CubePlanet;

/// <summary>UI definitions from ui_definitions.txt (window + widgets). Loaded and checked now; the UI system that draws them comes in phase 4.</summary>
public sealed class UiRegistry
{
    public sealed class UiDefinition { public int Id; public string Namespace, IdName; public JsonElement Json; public string FullName => Namespace + ":" + IdName; }
    public readonly List<UiDefinition> All = new();
    readonly Dictionary<string, UiDefinition> byName = new();

    public UiRegistry(IEnumerable<JsonElement> defs, string file)
    {
        foreach (var e in defs)
        {
            var u = new UiDefinition { Json = e, Id = (int)Num(e, "id"), IdName = Str(e, "id_name"), Namespace = Str(e, "namespace", "base") };
            if (u.IdName.Length == 0) throw new InvalidDataException($"{file}: a UI has no id_name");
            if (!byName.TryAdd(u.FullName, u)) throw new InvalidDataException($"{file}: UI '{u.FullName}' is defined twice");
            All.Add(u);
        }
    }
    public UiDefinition Get(string name) => byName.TryGetValue(name.Contains(':') ? name : "base:" + name, out var u) ? u : throw new KeyNotFoundException($"unknown UI '{name}'");
}
