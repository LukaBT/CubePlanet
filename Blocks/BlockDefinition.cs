using System.Text.Json;
using static CubePlanet.DefinitionFile;

namespace CubePlanet;

/// <summary>
/// One block from block_definitions.txt. Editor keys: type, namespace, id_name, id, name, stateless, group_tags, textures, properties.definition,
/// properties.instance_state. Game keys read from properties.definition (all optional, defaults in brackets):
///   model [cube]          cube | cross (crossed sprites) | liquid | attached (see attach) | boxes (see boxes)
///   boxes                 boxes model: [{"x","y","z","w","h","d", "textures": {...optional, same slots as the block}}] in block units (0..1)
///   geometry.collision    physical hitbox, list of boxes (missing: full block for cubes, nothing for other models; [] = walk through)
///   geometry.selection    what the cursor targets / breaks (missing: full block, nothing for liquids; [] = untargetable)
///   rotation [none]       none | horizontal | all (boxes and face textures turn with the block's facing; facing state arrives in phase 4)
///   attach                vine (thin planes on supporting sides) | pod (small box hanging on a log)
///   random_offset [true]  cross: jitter the sprite position per block
///   waterlogged [false]   sits inside water (underwater plants: water faces next to it are hidden, breaking it leaves water)
///   lod_color [r,g,b]     colour of this block as a surface block on distant LOD terrain
///   render.layer [opaque] opaque (hides neighbour faces) | cutout | translucent | liquid
///   render.connect_group  faces between two blocks of the same group are hidden (leaves, glass, ice)
///   render.sway [none]    none | leaves | plant | underwater (vertex animation in the shader)
///   render.emissive [0]   > 0: self-lit and glowing (lava, magma, sea lanterns)
///   render.casts_shadow [true]
/// textures: "all", or "top" / "bottom" / "side", or per face "north" (-z) / "south" (+z) / "east" (+x) / "west" (-x) / "top" / "bottom";
/// a face takes its own slot, then "side" (horizontal faces), then "all".
/// group_tags used by the game: "log" (trees / cocoa attach to it), "leaves".
/// </summary>
public sealed class BlockDefinition
{
    public int Id; public string Namespace, IdName, Name;
    public string FullName => Namespace + ":" + IdName;
    public string[] Tags = Array.Empty<string>();
    public readonly Dictionary<string, int> Textures = new(); // flattened slot path ("all", "lower.closed") -> block texture ID
    public string Model = "cube", Attach = "", RenderLayer = "opaque", ConnectGroup = "", Sway = "none", Rotation = "none";
    /// <summary>Texture of each face, in the mesher's face order: +x (east), -x (west), +y (top), -y (bottom), +z (south), -z (north).</summary>
    public int[] Faces = new int[6];
    public sealed class ModelBox { public BlockBox Box; public int[] Faces; }
    public ModelBox[] Boxes = Array.Empty<ModelBox>();
    /// <summary>null = full block.</summary>
    public BlockBox[] Collision, Selection;
    public static readonly string[] FaceNames = { "east", "west", "top", "bottom", "south", "north" };
    public bool RandomOffset = true, Waterlogged, CastsShadow = true;
    public double Emissive, Hardness = 1;
    public int[] LodColor = { 128, 128, 128 };
    public JsonElement Json; // the full definition, for keys later phases read

    public bool HasTag(string t) => Array.IndexOf(Tags, t) >= 0;
    /// <summary>Texture of a slot, falling back to "all" / "default" / the first slot.</summary>
    public int Texture(string slot = "all") => Texture(Textures, slot);
    static int Texture(Dictionary<string, int> tex, string slot)
    {
        if (tex.TryGetValue(slot, out int t) || tex.TryGetValue("all", out t) || tex.TryGetValue("default", out t)) return t;
        foreach (var v in tex.Values) return v;
        return 0;
    }
    /// <summary>Face textures from a slot map: the face's own slot, then side (horizontal faces), then all.</summary>
    static int[] FacesOf(Dictionary<string, int> tex)
    {
        var f = new int[6];
        for (int i = 0; i < 6; i++)
            f[i] = tex.TryGetValue(FaceNames[i], out int t) ? t : (i != 2 && i != 3 && tex.TryGetValue("side", out t)) ? t : Texture(tex, "all");
        return f;
    }

    public static BlockDefinition Parse(JsonElement e, string file)
    {
        var b = new BlockDefinition { Json = e };
        b.Id = Get(e, "id") is { ValueKind: JsonValueKind.Number } id ? id.GetInt32() : throw new InvalidDataException($"{file}: a block has no numeric id");
        b.IdName = Str(e, "id_name"); b.Namespace = Str(e, "namespace", "base"); b.Name = Str(e, "name", b.IdName);
        if (b.IdName.Length == 0) throw new InvalidDataException($"{file}: block {b.Id} has no id_name");
        if (Get(e, "group_tags") is { ValueKind: JsonValueKind.Array } tags) b.Tags = tags.EnumerateArray().Select(t => t.GetString() ?? "").ToArray();
        if (Get(e, "textures") is { ValueKind: JsonValueKind.Object } tex) Flatten(b.Textures, tex, "");
        b.Faces = FacesOf(b.Textures);
        const string D = "properties.definition.";
        b.Model = Str(e, D + "model", "cube"); b.Attach = Str(e, D + "attach"); b.Rotation = Str(e, D + "rotation", "none");
        if (Get(e, D + "boxes") is { ValueKind: JsonValueKind.Array } boxes)
            b.Boxes = boxes.EnumerateArray().Select(be =>
            {
                var mb = new ModelBox { Box = BlockBox.Parse(be), Faces = b.Faces };
                if (Get(be, "textures") is { ValueKind: JsonValueKind.Object } bt) { var m = new Dictionary<string, int>(b.Textures); Flatten(m, bt, ""); mb.Faces = FacesOf(m); }
                return mb;
            }).ToArray();
        b.Collision = BlockBox.ParseList(Get(e, D + "geometry.collision")) ?? (b.Model == "cube" ? null : Array.Empty<BlockBox>());
        b.Selection = BlockBox.ParseList(Get(e, D + "geometry.selection")) ?? (b.Model == "liquid" ? Array.Empty<BlockBox>() : null);
        if (b.Collision != null && b.Collision.Length == 1 && b.Collision[0].Equals(BlockBox.Full)) b.Collision = null;
        if (b.Selection != null && b.Selection.Length == 1 && b.Selection[0].Equals(BlockBox.Full)) b.Selection = null;
        b.RandomOffset = Bool(e, D + "random_offset", true); b.Waterlogged = Bool(e, D + "waterlogged");
        b.RenderLayer = Str(e, D + "render.layer", b.Model == "cube" ? "opaque" : b.Model == "liquid" ? "liquid" : "cutout");
        b.ConnectGroup = Str(e, D + "render.connect_group"); b.Sway = Str(e, D + "render.sway", "none");
        b.Emissive = Num(e, D + "render.emissive"); b.CastsShadow = Bool(e, D + "render.casts_shadow", true); b.Hardness = Num(e, D + "hardness", 1);
        if (Get(e, D + "lod_color") is { ValueKind: JsonValueKind.Array } lc && lc.GetArrayLength() >= 3) b.LodColor = lc.EnumerateArray().Take(3).Select(v => v.GetInt32()).ToArray();
        string[] models = { "cube", "cross", "liquid", "attached", "boxes" }, layers = { "opaque", "cutout", "translucent", "liquid" }, sways = { "none", "leaves", "plant", "underwater" };
        if (Array.IndexOf(models, b.Model) < 0) throw new InvalidDataException($"{file}: block {b.FullName}: unknown model '{b.Model}' (use {string.Join(", ", models)})");
        if (Array.IndexOf(layers, b.RenderLayer) < 0) throw new InvalidDataException($"{file}: block {b.FullName}: unknown render.layer '{b.RenderLayer}' (use {string.Join(", ", layers)})");
        if (Array.IndexOf(sways, b.Sway) < 0) throw new InvalidDataException($"{file}: block {b.FullName}: unknown render.sway '{b.Sway}' (use {string.Join(", ", sways)})");
        if (b.Model == "attached" && b.Attach != "vine" && b.Attach != "pod") throw new InvalidDataException($"{file}: block {b.FullName}: model 'attached' needs attach = vine or pod");
        if (b.Model == "boxes" && b.Boxes.Length == 0) throw new InvalidDataException($"{file}: block {b.FullName}: model 'boxes' needs a non-empty boxes list");
        if (b.Rotation != "none" && b.Rotation != "horizontal" && b.Rotation != "all") throw new InvalidDataException($"{file}: block {b.FullName}: unknown rotation '{b.Rotation}' (use none, horizontal, all)");
        return b;
    }

    static void Flatten(Dictionary<string, int> into, JsonElement o, string prefix)
    {
        foreach (var p in o.EnumerateObject())
        {
            string k = prefix.Length == 0 ? p.Name : prefix + "." + p.Name;
            if (p.Value.ValueKind == JsonValueKind.Number) into[k] = p.Value.GetInt32();
            else if (p.Value.ValueKind == JsonValueKind.Object) Flatten(into, p.Value, k);
        }
    }
}
