using System.Text.Json;

namespace CubePlanet;

/// <summary>
/// Reads an editor definition file (block / item / ui definitions). Accepts {"blocks":[...]}, a bare list or a single definition object,
/// and tolerates // and /* */ comments and trailing commas, like the editor does.
/// </summary>
public static class DefinitionFile
{
    static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static List<JsonElement> Load(string path, string listKey)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Asset file missing: " + path);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), Options);
            var root = doc.RootElement; var list = new List<JsonElement>();
            if (root.ValueKind == JsonValueKind.Array) foreach (var e in root.EnumerateArray()) list.Add(e.Clone());
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(listKey, out var arr)) foreach (var e in arr.EnumerateArray()) list.Add(e.Clone());
            else if (root.ValueKind == JsonValueKind.Object) list.Add(root.Clone());
            return list;
        }
        catch (JsonException e) { throw new InvalidDataException($"{Path.GetFileName(path)}: invalid JSON (line {e.LineNumber + 1}): {e.Message}", e); }
    }

    // small helpers for optional keys
    public static JsonElement? Get(JsonElement e, string dottedPath)
    {
        foreach (var k in dottedPath.Split('.'))
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(k, out var c)) return null;
            e = c;
        }
        return e;
    }
    public static string Str(JsonElement e, string path, string def = "") => Get(e, path) is { ValueKind: JsonValueKind.String } v ? v.GetString() : def;
    public static double Num(JsonElement e, string path, double def = 0) => Get(e, path) is { ValueKind: JsonValueKind.Number } v ? v.GetDouble() : def;
    public static bool Bool(JsonElement e, string path, bool def = false) => Get(e, path) is { } v && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : def;
}
