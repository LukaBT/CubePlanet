using System.Text.Json;
using System.Text.Json.Nodes;

namespace CubePlanet;

/// <summary>
/// Voltage levels of the energy grid (AC): LV (machines, x1), MV (x8), HV (x64). Every part of a network between transformers has one
/// level, set by the transformer sides touching it (LV if none). A cable carries a current: its capacity (definition key "cable":
/// {"capacity": E/s at LV, "loss"}) times the level, so the same copper carries 64 E/s at LV, 512 at MV, 4096 at HV. Losses are I^2 R:
/// a cable block at its full current loses "loss" (a share) of what it carries; at a higher level the same energy is a smaller current,
/// so it loses (level)^2 less. Machines run on LV only: on MV / HV they do nothing (HUD: WRONG VOLTAGE).
/// </summary>
public static class Voltage
{
    public static readonly string[] Names = { "LV", "MV", "HV" };
    public static readonly double[] Factor = { 1, 8, 64 };
    public static double Of(string name) { int i = Array.IndexOf(Names, name); return i < 0 ? 1 : Factor[i]; }
    public static string Name(double v) { int i = Array.IndexOf(Factor, v); return i < 0 ? $"x{v:0}" : Names[i]; }
}

/// <summary>
/// Transformer: joins two voltage levels; its back is the low side, its front the high side (definition key "transformer":
/// {"low": "LV", "high": "MV", "rating": E/s}). Passes at most its rating, losing 2 %. If a side touches a part of the grid at another
/// level (two transformers fighting), it passes nothing (HUD).
/// </summary>
public sealed class TransformerBehavior : BlockBehavior
{
    public const double Loss = .02;
    public sealed class Spec { public double Low = 1, High = 8, Rating = 1024; }
    static readonly Dictionary<int, Spec> specs = new();
    public static Spec SpecOf(int id)
    {
        lock (specs)
        {
            if (specs.TryGetValue(id, out var s)) return s;
            s = new Spec();
            if (DEFS[id] != null && DefinitionFile.Get(DEFS[id].Json, "properties.definition.transformer") is { ValueKind: JsonValueKind.Object } j)
            { s.Low = Voltage.Of(DefinitionFile.Str(j, "low", "LV")); s.High = Voltage.Of(DefinitionFile.Str(j, "high", "MV")); s.Rating = DefinitionFile.Num(j, "rating", 1024); }
            return specs[id] = s;
        }
    }
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
    public override string Describe(BlockEntity e)
    {
        var s = SpecOf(e.BlockId); string sides = $"BACK {Voltage.Name(s.Low)} -> FRONT {Voltage.Name(s.High)}";
        if (e.Num("ok") < 0) return sides + ": A SIDE TOUCHES ANOTHER VOLTAGE, NOT PASSING ANYTHING";
        return $"{sides}  {e.Num("flow"):0.0} / {s.Rating:0} E/S";
    }
}

/// <summary>
/// Power lines: wires strung between anchors (pylon tops, wire connectors: definition key "wire_anchor": {"x", "y", "z" block-local attach
/// point, optional "when": {state field: value}}). A wire spool (item behaviour "wire_spool", key "wire": {"capacity", "loss", "max_length"})
/// links two anchors: right click one, then the other (again on a linked pair: removes the wire). A wire is part of the energy network
/// like a cable whose length is the span: it carries capacity x the level and loses loss x length at full current. Saved with the world;
/// a wire whose anchor is gone disappears.
/// </summary>
public static class Wires
{
    public sealed class Link
    {
        public BlockPos A, B; public double Length;
        public double Flow, Cap, Level = 1, LossNow; // last energy step (HUD)
    }
    public static readonly List<Link> Links = new();
    public static BlockPos? Pending;
    public sealed class Spec { public double Capacity = 256, Loss = .001, MaxLength = 48; public int Item, Texture; }
    static Spec spec;
    public static Spec WireSpec
    {
        get
        {
            if (spec != null) return spec;
            var s = new Spec();
            foreach (var it in GameAssets.ItemReg.All)
                if (it?.Behavior == "wire_spool")
                {
                    s.Item = it.Id;
                    if (DefinitionFile.Get(it.Json, "properties.definition.wire") is { ValueKind: JsonValueKind.Object } j)
                    { s.Capacity = DefinitionFile.Num(j, "capacity", 256); s.Loss = DefinitionFile.Num(j, "loss", .001); s.MaxLength = DefinitionFile.Num(j, "max_length", 48); s.Texture = (int)DefinitionFile.Num(j, "texture", 0); }
                    break;
                }
            return spec = s;
        }
    }

    sealed class Anchor { public V3 At; public (string field, JsonElement value)[] When = Array.Empty<(string, JsonElement)>(); }
    static readonly Dictionary<int, Anchor> anchors = new();
    static Anchor AnchorDef(int id)
    {
        if (id <= 0 || DEFS[id] == null) return null;
        lock (anchors)
        {
            if (anchors.TryGetValue(id, out var a)) return a;
            if (DefinitionFile.Get(DEFS[id].Json, "properties.definition.wire_anchor") is { ValueKind: JsonValueKind.Object } j)
            {
                a = new Anchor { At = new V3(DefinitionFile.Num(j, "x", .5), DefinitionFile.Num(j, "y", .5), DefinitionFile.Num(j, "z", .5)) };
                if (DefinitionFile.Get(j, "when") is { ValueKind: JsonValueKind.Object } w) a.When = w.EnumerateObject().Select(p => (p.Name, p.Value.Clone())).ToArray();
            }
            return anchors[id] = a;
        }
    }
    /// <summary>Is this cell a wire anchor (in its current state)? local = the attach point in the block.</summary>
    public static bool IsAnchor(BlockPos p, out V3 local)
    {
        local = default; int id = p.Id; var a = AnchorDef(id); if (a == null) return false;
        uint st = (uint)p.State; var d = DEFS[id];
        foreach (var (name, v) in a.When)
        {
            var f = d.State.Find(name); if (f == null) return false;
            bool ok = v.ValueKind switch
            {
                JsonValueKind.String => f.Values != null && f.Values[f.Index(st)] == v.GetString(),
                JsonValueKind.True => f.Index(st) == 1, JsonValueKind.False => f.Index(st) == 0,
                JsonValueKind.Number => f.Value(st) == v.GetInt32(), _ => false
            };
            if (!ok) return false;
        }
        local = a.At; return true;
    }
    public static bool IsAnchor(BlockPos p) => IsAnchor(p, out _);
    /// <summary>World position of an anchor's attach point.</summary>
    public static V3 AttachPoint(BlockPos p, V3 local)
    {
        if (p.Core) return new V3(p.X + local.X, p.Y + local.Y, p.Z + local.Z);
        Band B = bands[p.B]; return Gp(B, p.F, p.Z + local.Z, p.X + local.X, B.inner + p.Y + local.Y);
    }
    public static bool Same(BlockPos a, BlockPos b) => a.Key == b.Key;
    /// <summary>Span of a wire in blocks (counted in the block grid, like the player sees it; across a cube-face seam: world distance).</summary>
    static double Dist(BlockPos a, BlockPos b)
    {
        IsAnchor(a, out var la); IsAnchor(b, out var lb);
        if (a.Core == b.Core && a.B == b.B && a.F == b.F)
        {
            double dx = b.X + lb.X - a.X - la.X, dy = b.Y + lb.Y - a.Y - la.Y, dz = b.Z + lb.Z - a.Z - la.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
        return (AttachPoint(a, la) - AttachPoint(b, lb)).Len;
    }
    public static IEnumerable<Link> At(BlockPos p) => Links.Where(l => Same(l.A, p) || Same(l.B, p));

    /// <summary>Wire spool right click on a block.</summary>
    public static void Use(BlockPos p)
    {
        if (!IsAnchor(p)) { UI.Note("Wires attach to pylon tops and wire connectors", 2); return; }
        if (Pending is not { } a || !IsAnchor(a)) { Pending = p; UI.Note("Wire start set: now right click another pylon or connector", 3); return; }
        Pending = null;
        if (Same(a, p)) { UI.Note("Wire cancelled", 1.5); return; }
        var old = Links.FirstOrDefault(l => (Same(l.A, a) && Same(l.B, p)) || (Same(l.A, p) && Same(l.B, a)));
        if (old != null) { Links.Remove(old); UI.Note("Wire removed", 1.5); return; }
        double len = Dist(a, p);
        if (len > WireSpec.MaxLength) { UI.Note($"Too far for a wire: {len:0} blocks (at most {WireSpec.MaxLength:0})", 2.5); return; }
        Links.Add(new Link { A = a, B = p, Length = len }); UI.Note($"Wire strung: {len:0.0} blocks", 2);
    }
    public static bool Connect(BlockPos a, BlockPos b)
    {
        if (!IsAnchor(a) || !IsAnchor(b) || Same(a, b)) return false;
        double len = Dist(a, b); if (len > WireSpec.MaxLength) return false;
        Links.Add(new Link { A = a, B = b, Length = len }); return true;
    }
    /// <summary>Drops wires whose anchor was broken (only where both ends are loaded, so nothing is lost in unloaded land).</summary>
    public static void Clean() => Links.RemoveAll(l => l.A.Loaded && l.B.Loaded && (!IsAnchor(l.A) || !IsAnchor(l.B)));

    public static string Describe(BlockPos p)
    {
        var ls = At(p).ToList(); if (ls.Count == 0) return "NO WIRES (USE A WIRE SPOOL ON TWO ANCHORS)";
        double flow = ls.Sum(l => l.Flow), loss = ls.Sum(l => l.LossNow);
        return $"{ls.Count} WIRE{(ls.Count > 1 ? "S" : "")} {Voltage.Name(ls[0].Level)}  {flow:0.0} E/S (EACH UP TO {ls[0].Cap:0})  LOSS {loss:0.00} E/S";
    }

    static JsonArray P(BlockPos p) => new() { p.Core, p.B, p.F, p.X, p.Y, p.Z };
    static BlockPos P(JsonNode a) => new((bool)a[0], (int)a[1], (int)a[2], (int)a[3], (int)a[4], (int)a[5]);
    public static JsonArray Save() { var r = new JsonArray(); foreach (var l in Links) r.Add(new JsonObject { ["a"] = P(l.A), ["b"] = P(l.B), ["length"] = l.Length }); return r; }
    public static void Load(JsonNode n)
    {
        Links.Clear(); Pending = null; if (n is not JsonArray a) return;
        foreach (var x in a) Links.Add(new Link { A = P(x["a"]), B = P(x["b"]), Length = (double)x["length"] });
    }
    public static void Reset() { Links.Clear(); Pending = null; }
}
