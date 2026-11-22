using System.Text.Json;
using static CubePlanet.DefinitionFile;

namespace CubePlanet;

/// <summary>
/// One block from block_definitions.txt. Editor keys: type, namespace, id_name, id, name, stateless, group_tags, textures, properties.definition,
/// properties.instance_state. Game keys read from properties.definition (all optional, defaults in brackets):
///   model [cube]          cube | cross (crossed sprites) | liquid | attached (see attach)
///   attach                vine (thin planes on supporting sides) | pod (small box hanging on a log)
///   random_offset [true]  cross: jitter the sprite position per block
///   waterlogged [false]   sits inside water (underwater plants: water faces next to it are hidden, breaking it leaves water)
///   lod_color [r,g,b]     colour of this block as a surface block on distant LOD terrain
///   render.layer [opaque] opaque (hides neighbour faces) | cutout | translucent | liquid
///   render.connect_group  faces between two blocks of the same group are hidden (leaves, glass, ice)
///   render.sway [none]    none | leaves | plant | underwater (vertex animation in the shader)
///   render.emissive [0]   > 0: self-lit and glowing (lava, magma, sea lanterns)
///   render.casts_shadow [true]
/// group_tags used by the game: "log" (trees / cocoa attach to it), "leaves".
/// </summary>
public sealed class BlockDefinition
{
    public int Id; public string Namespace, IdName, Name;
    public string FullName => Namespace + ":" + IdName;
    public string[] Tags = Array.Empty<string>();
    public readonly Dictionary<string, int> Textures = new(); // flattened slot path ("all", "lower.closed") -> block texture ID
    public string Model = "cube", Attach = "", RenderLayer = "opaque", ConnectGroup = "", Sway = "none";
    public bool RandomOffset = true, Waterlogged, CastsShadow = true;
    public double Emissive, Hardness = 1;
    public int[] LodColor = { 128, 128, 128 };
    public JsonElement Json; // the full definition, for keys later phases read

    public bool HasTag(string t) => Array.IndexOf(Tags, t) >= 0;
    /// <summary>Texture of a slot, falling back to "all" / "default" / the first slot.</summary>
    public int Texture(string slot = "all")
    {
        if (Textures.TryGetValue(slot, out int t) || Textures.TryGetValue("all", out t) || Textures.TryGetValue("default", out t)) return t;
        foreach (var v in Textures.Values) return v;
        return 0;
    }

    public static BlockDefinition Parse(JsonElement e, string file)
    {
        var b = new BlockDefinition { Json = e };
        b.Id = Get(e, "id") is { ValueKind: JsonValueKind.Number } id ? id.GetInt32() : throw new InvalidDataException($"{file}: a block has no numeric id");
        b.IdName = Str(e, "id_name"); b.Namespace = Str(e, "namespace", "base"); b.Name = Str(e, "name", b.IdName);
        if (b.IdName.Length == 0) throw new InvalidDataException($"{file}: block {b.Id} has no id_name");
        if (Get(e, "group_tags") is { ValueKind: JsonValueKind.Array } tags) b.Tags = tags.EnumerateArray().Select(t => t.GetString() ?? "").ToArray();
        if (Get(e, "textures") is { ValueKind: JsonValueKind.Object } tex) Flatten(b, tex, "");
        const string D = "properties.definition.";
        b.Model = Str(e, D + "model", "cube"); b.Attach = Str(e, D + "attach");
        b.RandomOffset = Bool(e, D + "random_offset", true); b.Waterlogged = Bool(e, D + "waterlogged");
        b.RenderLayer = Str(e, D + "render.layer", b.Model == "cube" ? "opaque" : b.Model == "liquid" ? "liquid" : "cutout");
        b.ConnectGroup = Str(e, D + "render.connect_group"); b.Sway = Str(e, D + "render.sway", "none");
        b.Emissive = Num(e, D + "render.emissive"); b.CastsShadow = Bool(e, D + "render.casts_shadow", true); b.Hardness = Num(e, D + "hardness", 1);
        if (Get(e, D + "lod_color") is { ValueKind: JsonValueKind.Array } lc && lc.GetArrayLength() >= 3) b.LodColor = lc.EnumerateArray().Take(3).Select(v => v.GetInt32()).ToArray();
        string[] models = { "cube", "cross", "liquid", "attached" }, layers = { "opaque", "cutout", "translucent", "liquid" }, sways = { "none", "leaves", "plant", "underwater" };
        if (Array.IndexOf(models, b.Model) < 0) throw new InvalidDataException($"{file}: block {b.FullName}: unknown model '{b.Model}' (use {string.Join(", ", models)})");
        if (Array.IndexOf(layers, b.RenderLayer) < 0) throw new InvalidDataException($"{file}: block {b.FullName}: unknown render.layer '{b.RenderLayer}' (use {string.Join(", ", layers)})");
        if (Array.IndexOf(sways, b.Sway) < 0) throw new InvalidDataException($"{file}: block {b.FullName}: unknown render.sway '{b.Sway}' (use {string.Join(", ", sways)})");
        if (b.Model == "attached" && b.Attach != "vine" && b.Attach != "pod") throw new InvalidDataException($"{file}: block {b.FullName}: model 'attached' needs attach = vine or pod");
        return b;
    }

    static void Flatten(BlockDefinition b, JsonElement o, string prefix)
    {
        foreach (var p in o.EnumerateObject())
        {
            string k = prefix.Length == 0 ? p.Name : prefix + "." + p.Name;
            if (p.Value.ValueKind == JsonValueKind.Number) b.Textures[k] = p.Value.GetInt32();
            else if (p.Value.ValueKind == JsonValueKind.Object) Flatten(b, p.Value, k);
        }
    }
}
