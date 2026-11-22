using System.Text.Json;
using static CubePlanet.DefinitionFile;

namespace CubePlanet;

/// <summary>
/// sound_definitions.txt: {"sounds": [{"id": 1, "id_name": "stone_break", "file": "sounds/stone_break.ogg"}]}. Blocks name their sounds in
/// properties.definition.sounds {break, place, step, use}. IDs and references are checked at startup; nothing plays yet (no audio backend).
/// </summary>
public sealed class SoundRegistry
{
    public sealed class Sound { public int Id; public string IdName, File; }
    public readonly List<Sound> All = new();
    readonly Dictionary<string, Sound> byName = new();

    public SoundRegistry(IEnumerable<JsonElement> defs, string file)
    {
        foreach (var e in defs)
        {
            var s = new Sound { Id = (int)Num(e, "id"), IdName = Str(e, "id_name"), File = Str(e, "file") };
            if (s.IdName.Length == 0) throw new InvalidDataException($"{file}: a sound has no id_name");
            if (!byName.TryAdd(s.IdName, s)) throw new InvalidDataException($"{file}: sound '{s.IdName}' is defined twice");
            All.Add(s);
        }
    }
    public bool Has(string name) => byName.ContainsKey(name);
    /// <summary>Would play a sound; there is no audio yet.</summary>
    public void Play(string name) { }
}
