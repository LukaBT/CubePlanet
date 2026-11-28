using System.Text.Json;
using static CubePlanet.DefinitionFile;

namespace CubePlanet;

/// <summary>
/// One block from block_definitions.txt. Editor keys: type, namespace, id_name, id, name, stateless, group_tags, textures, ui_id, properties.definition,
/// properties.instance_state (see <see cref="StateSchema"/>). Game keys read from properties.definition (all optional, defaults in brackets):
///   model [cube]          cube | cross (crossed sprites) | liquid | attached (see attach) | boxes (see boxes)
///   boxes                 boxes model: [{"x","y","z","w","h","d", "textures": {...optional, same slots as the block}}] in block units (0..1)
///   geometry.collision    physical hitbox, list of boxes (missing: full block for cubes, nothing for other models; [] = walk through)
///   geometry.selection    what the cursor targets / breaks (missing: full block, nothing for liquids; [] = untargetable)
///   geometry.state_overrides  [{"when": {state fields}, "boxes", "collision", "selection", "textures"}]: every matching entry replaces those keys
///                         (textures are merged into the block's slots), in list order
///   rotation [none]       none | horizontal | all: boxes, geometry and face textures turn with the "facing" state field (added automatically:
///                         north, east, south, west [, up, down]); "north" = as written in the definition
///   placement_facing [toward_player]  toward_player (the block's north side faces you, e.g. a furnace front) | away (stairs, doors, beds)
///   multiblock.parts      [{"offset": [x, y, z], "state": {fields identifying the part}}], offsets relative to the facing; placing checks that
///                         every part fits, breaking one part removes all
///   behavior              name of the C# behaviour (door, bed, container, smelting, oxygen_generator, stairs, connecting)
///   connects_to           connecting blocks (fences): group tags of the neighbours they join (full solid cubes always count)
///   attach                vine (thin planes on supporting sides) | pod (small box hanging on a log)
///   random_offset [true]  cross: jitter the sprite position per block
///   waterlogged [false]   sits inside water (underwater plants: water faces next to it are hidden, breaking it leaves water)
///   lod_color [r,g,b]     colour of this block as a surface block on distant LOD terrain
///   mass                  kg of the whole block (every block has one, see Mass: density x the volume it really fills)
///   render.layer [opaque] opaque (hides neighbour faces) | cutout | translucent | liquid
///   render.connect_group  faces between two blocks of the same group are hidden (leaves, glass, ice)
///   render.sway [none]    none | leaves | plant | underwater (vertex animation in the shader)
///   render.emissive [0]   > 0: self-lit and glowing (lava, magma, sea lanterns)
///   render.casts_shadow [true]
///   sounds                {"break", "place", "step", "use"}: sound id_names from sound_definitions.txt (nothing plays yet)
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
    public string Model = "cube", Attach = "", RenderLayer = "opaque", ConnectGroup = "", Sway = "none", Rotation = "none", Behavior = "", UiId = "";
    public bool FacingAway;
    public string[] ConnectsTo = Array.Empty<string>();
    /// <summary>Texture of each face of the default state, in the mesher's face order: +x (east), -x (west), +y (top), -y (bottom), +z (south), -z (north).</summary>
    public int[] Faces = new int[6];
    public sealed class ModelBox { public BlockBox Box; public Dictionary<string, int> Tex; public int[] Faces; }
    public ModelBox[] Boxes = Array.Empty<ModelBox>();
    /// <summary>null = full block.</summary>
    public BlockBox[] Collision, Selection;
    /// <summary>A state override: Boxes replace the model's boxes, AddBoxes are added to them (cable / pipe arms: one override per side).</summary>
    public sealed class Override { public JsonElement When; public ModelBox[] Boxes, AddBoxes; public BlockBox[] Collision, Selection; public bool HasCollision, HasSelection; public Dictionary<string, int> Textures; }
    public Override[] Overrides = Array.Empty<Override>();
    public sealed class Part { public int X, Y, Z; public JsonElement State; }
    /// <summary>A moving part (properties.definition.moving_parts): boxes (they may reach out of the block, e.g. blades) turning around Axis
    /// through Pivot (block-local) at the speed of the block's rotation network (block entity numbers "angle" / "speed") times Speed.</summary>
    /// <summary>Axis2 (optional, "axis2"): a second turn applied first, by the block entity number "angle2" (a sun tracker tilting both ways).</summary>
    public sealed class MovingPart { public ModelBox[] Boxes; public V3 Axis = new(0, 0, 1), Pivot = new(.5, .5, .5); public V3? Axis2; public double Speed = 1; }
    public MovingPart[] MovingParts = Array.Empty<MovingPart>();
    public Part[] Parts = Array.Empty<Part>();
    public StateSchema State = new();
    public Dictionary<string, string> Sounds = new();
    public static readonly string[] FaceNames = { "east", "west", "top", "bottom", "south", "north" };
    public bool RandomOffset = true, Waterlogged, CastsShadow = true;
    /// <summary>geometry.collision / geometry.selection = "boxes": the visible boxes of the current state (cables, pipes).</summary>
    public bool CollideBoxes, SelectBoxes;
    /// <summary>connect_solid (default true): "connecting" blocks also join full solid cubes (fences do, cables and pipes don't).</summary>
    public bool ConnectSolid = true;
    public double Emissive, Hardness = 1;
    /// <summary>Mass in kg of the whole block (a multiblock: all its parts), properties.definition.mass; see <see cref="Mass"/>.</summary>
    public double Mass;
    public int[] LodColor = { 128, 128, 128 };
    public JsonElement Json; // the full definition, for keys other systems read
    /// <summary>Do some states look or collide differently (rotation, overrides)? Then the game looks up a variant per state word.</summary>
    public bool HasVariants => Rotation != "none" || Overrides.Length > 0;

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
    public static int[] FacesOf(Dictionary<string, int> tex)
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
        string who = $"{file}: block {b.FullName}";
        if (Get(e, "group_tags") is { ValueKind: JsonValueKind.Array } tags) b.Tags = tags.EnumerateArray().Select(t => t.GetString() ?? "").ToArray();
        if (Get(e, "textures") is { ValueKind: JsonValueKind.Object } tex) Flatten(b.Textures, tex, "");
        b.Faces = FacesOf(b.Textures);
        if (Get(e, "ui_id") is { ValueKind: JsonValueKind.Object } ui) b.UiId = Str(ui, "namespace", "base") + ":" + Str(ui, "id_name");
        const string D = "properties.definition.";
        b.Model = Str(e, D + "model", "cube"); b.Attach = Str(e, D + "attach"); b.Rotation = Str(e, D + "rotation", "none");
        b.Behavior = Str(e, D + "behavior");
        if (Get(e, D + "connects_to") is { ValueKind: JsonValueKind.Array } ct) b.ConnectsTo = ct.EnumerateArray().Select(t => t.GetString() ?? "").ToArray(); b.FacingAway = Str(e, D + "placement_facing", "toward_player") == "away";
        if (Get(e, D + "boxes") is { ValueKind: JsonValueKind.Array } boxes) b.Boxes = ParseBoxes(boxes);
        foreach (var mb in b.Boxes) mb.Faces = FacesOf(mb.Tex == null ? b.Textures : Merge(b.Textures, mb.Tex));
        b.Collision = BlockBox.ParseList(Get(e, D + "geometry.collision")) ?? (b.Model == "cube" ? null : Array.Empty<BlockBox>());
        b.Selection = BlockBox.ParseList(Get(e, D + "geometry.selection")) ?? (b.Model == "liquid" ? Array.Empty<BlockBox>() : null);
        b.CollideBoxes = Str(e, D + "geometry.collision") == "boxes"; b.SelectBoxes = Str(e, D + "geometry.selection") == "boxes";
        if (b.CollideBoxes) b.Collision = b.Boxes.Select(m => m.Box).ToArray();
        if (b.SelectBoxes) b.Selection = b.Boxes.Select(m => m.Box).ToArray();
        b.ConnectSolid = Bool(e, D + "connect_solid", true);
        b.Collision = FullToNull(b.Collision); b.Selection = FullToNull(b.Selection);
        if (Get(e, D + "geometry.state_overrides") is { ValueKind: JsonValueKind.Array } ovs)
            b.Overrides = ovs.EnumerateArray().Select(o =>
            {
                var ov = new Override { When = Get(o, "when") ?? default };
                if (Get(o, "boxes") is { ValueKind: JsonValueKind.Array } ob) ov.Boxes = ParseBoxes(ob);
                if (Get(o, "add_boxes") is { ValueKind: JsonValueKind.Array } oa) ov.AddBoxes = ParseBoxes(oa);
                if (Get(o, "collision") is { ValueKind: JsonValueKind.Array } oc) { ov.Collision = FullToNull(BlockBox.ParseList(oc)); ov.HasCollision = true; }
                if (Get(o, "selection") is { ValueKind: JsonValueKind.Array } os) { ov.Selection = FullToNull(BlockBox.ParseList(os)); ov.HasSelection = true; }
                if (Get(o, "textures") is { ValueKind: JsonValueKind.Object } ot) { ov.Textures = new(); Flatten(ov.Textures, ot, ""); }
                return ov;
            }).ToArray();
        if (Get(e, D + "multiblock.parts") is { ValueKind: JsonValueKind.Array } parts)
            b.Parts = parts.EnumerateArray().Select(p =>
            {
                var off = Get(p, "offset") is { ValueKind: JsonValueKind.Array } oa ? oa.EnumerateArray().Select(v => v.GetInt32()).ToArray() : new int[3];
                if (off.Length == 2) off = new[] { off[0], off[1], 0 }; // the editor's 2D offsets
                return new Part { X = off[0], Y = off[1], Z = off.Length > 2 ? off[2] : 0, State = Get(p, "state") ?? default };
            }).ToArray();
        if (Get(e, D + "moving_parts") is { ValueKind: JsonValueKind.Array } mps)
            b.MovingParts = mps.EnumerateArray().Select(mp =>
            {
                var m = new MovingPart { Boxes = Get(mp, "boxes") is { ValueKind: JsonValueKind.Array } mb ? ParseBoxes(mb, true) : Array.Empty<ModelBox>(), Speed = Num(mp, "speed", 1) };
                m.Axis = Str(mp, "axis", "z") switch { "x" => new V3(1, 0, 0), "y" => new V3(0, 1, 0), _ => new V3(0, 0, 1) };
                if (Str(mp, "axis2", "") is { Length: > 0 } a2) m.Axis2 = a2 switch { "x" => new V3(1, 0, 0), "y" => new V3(0, 1, 0), _ => new V3(0, 0, 1) };
                if (Get(mp, "pivot") is { ValueKind: JsonValueKind.Array } pv && pv.GetArrayLength() == 3) { var a = pv.EnumerateArray().Select(v => v.GetDouble()).ToArray(); m.Pivot = new V3(a[0], a[1], a[2]); }
                foreach (var x in m.Boxes) x.Faces = FacesOf(x.Tex == null ? b.Textures : Merge(b.Textures, x.Tex));
                return m;
            }).ToArray();
        if (Get(e, D + "sounds") is { ValueKind: JsonValueKind.Object } snd) foreach (var p in snd.EnumerateObject()) b.Sounds[p.Name] = p.Value.GetString();
        b.RandomOffset = Bool(e, D + "random_offset", true); b.Waterlogged = Bool(e, D + "waterlogged");
        b.RenderLayer = Str(e, D + "render.layer", b.Model == "cube" ? "opaque" : b.Model == "liquid" ? "liquid" : "cutout");
        b.ConnectGroup = Str(e, D + "render.connect_group"); b.Sway = Str(e, D + "render.sway", "none");
        b.Emissive = Num(e, D + "render.emissive"); b.CastsShadow = Bool(e, D + "render.casts_shadow", true); b.Hardness = Num(e, D + "hardness", 1); b.Mass = Num(e, D + "mass");
        if (Get(e, D + "lod_color") is { ValueKind: JsonValueKind.Array } lc && lc.GetArrayLength() >= 3) b.LodColor = lc.EnumerateArray().Take(3).Select(v => v.GetInt32()).ToArray();
        string[] models = { "cube", "cross", "liquid", "attached", "boxes" }, layers = { "opaque", "cutout", "translucent", "liquid" }, sways = { "none", "leaves", "plant", "underwater" };
        if (Array.IndexOf(models, b.Model) < 0) throw new InvalidDataException($"{who}: unknown model '{b.Model}' (use {string.Join(", ", models)})");
        if (Array.IndexOf(layers, b.RenderLayer) < 0) throw new InvalidDataException($"{who}: unknown render.layer '{b.RenderLayer}' (use {string.Join(", ", layers)})");
        if (Array.IndexOf(sways, b.Sway) < 0) throw new InvalidDataException($"{who}: unknown render.sway '{b.Sway}' (use {string.Join(", ", sways)})");
        if (b.Model == "attached" && b.Attach != "vine" && b.Attach != "pod") throw new InvalidDataException($"{who}: model 'attached' needs attach = vine or pod");
        if (b.Model == "boxes" && b.Boxes.Length == 0 && b.MovingParts.Length == 0) throw new InvalidDataException($"{who}: model 'boxes' needs a non-empty boxes list (or moving_parts)");
        if (b.Rotation != "none" && b.Rotation != "horizontal" && b.Rotation != "all") throw new InvalidDataException($"{who}: unknown rotation '{b.Rotation}' (use none, horizontal, all)");

        // instance state (packed fields: 16 bits); rotating blocks get a "facing" field if they don't declare one
        b.State = StateSchema.Parse(Get(e, "properties.instance_state"), who, 16);
        if (b.Rotation != "none" && b.State.Find("facing") == null)
        {
            var vals = b.Rotation == "all" ? new[] { "north", "east", "south", "west", "up", "down" } : new[] { "north", "east", "south", "west" };
            var f = new StateSchema.Field { Name = "facing", Type = "string", Values = vals, Count = vals.Length, Packed = true, Bits = vals.Length > 4 ? 3 : 2, Shift = b.State.Bits };
            b.State.Packed.Add(f); b.State.Bits += f.Bits;
            if (b.State.Bits > 16) throw new InvalidDataException($"{who}: packed state fields need {b.State.Bits} bits with facing, at most 16 fit");
        }
        if (b.Parts.Length > 0)
        {   // every part must be told apart by its state, so breaking any part finds the others
            var words = b.Parts.Select(p => b.State.Apply(0, p.State)).ToArray();
            if (words.Distinct().Count() != words.Length) throw new InvalidDataException($"{who}: multiblock parts need different \"state\" values (e.g. half: lower / upper)");
        }
        return b;
    }

    /// <summary>Which multiblock part a state word belongs to (-1 if none).</summary>
    public int PartOf(uint word)
    {
        for (int i = 0; i < Parts.Length; i++) if (Parts[i].State.ValueKind == JsonValueKind.Object && State.Matches(word, Parts[i].State)) return i;
        return -1;
    }

    static ModelBox[] ParseBoxes(JsonElement arr, bool free = false) => arr.EnumerateArray().Select(be =>
    {
        var mb = new ModelBox { Box = BlockBox.Parse(be, free) };
        if (Get(be, "textures") is { ValueKind: JsonValueKind.Object } bt) { mb.Tex = new(); Flatten(mb.Tex, bt, ""); }
        return mb;
    }).ToArray();
    static BlockBox[] FullToNull(BlockBox[] l) => l != null && l.Length == 1 && l[0].Equals(BlockBox.Full) ? null : l;
    public static Dictionary<string, int> Merge(Dictionary<string, int> a, Dictionary<string, int> b)
    {
        var m = new Dictionary<string, int>(a); foreach (var kv in b) m[kv.Key] = kv.Value; return m;
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
