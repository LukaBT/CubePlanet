using System.Text.Json;
using static CubePlanet.DefinitionFile;

namespace CubePlanet;

/// <summary>
/// The instance state of a block or item, declared in properties.instance_state:
///   "open":   {"type": "boolean", "default": false}
///   "facing": {"type": "string", "values": ["north", "east", "south", "west"], "default": "north"}
///   "level":  {"type": "integer", "min": 1, "max": 8, "default": 8}
///   "slots":  {"type": "inventory", "slots": 27}          "progress": {"type": "number", "default": 0}
/// Booleans, strings with values and integers with min/max are packed into one state word (16 bits for blocks, 32 for items); inventories,
/// unbounded numbers and fields with "storage": "entity" live in a block entity. A packed field stores (index - default index) mod count,
/// so the word 0 always means "every field at its default" (chunks without state need no memory at all).
/// </summary>
public sealed class StateSchema
{
    public sealed class Field
    {
        public string Name, Type; public string[] Values; public int Min, Count, Shift, Bits, DefIndex;
        public bool Packed; public int Slots; public double DefNumber;
        public int Index(ushort word) => Index((uint)word);
        public int Index(uint word) => (int)(((word >> Shift) & ((1u << Bits) - 1)) + DefIndex) % Count;
        /// <summary>Integer value (min + index), boolean as 0/1, string as its index.</summary>
        public int Value(uint word) => Type == "integer" ? Min + Index(word) : Index(word);
        public uint With(uint word, int index)
        {
            uint m = ((1u << Bits) - 1) << Shift, enc = (uint)(((index - DefIndex) % Count + Count) % Count);
            return (word & ~m) | (enc << Shift);
        }
        public int IndexOf(JsonElement v) => Type switch
        {
            "boolean" => v.ValueKind == JsonValueKind.True ? 1 : 0,
            "integer" => Math.Clamp(v.GetInt32() - Min, 0, Count - 1),
            _ => Math.Max(0, Array.IndexOf(Values, v.GetString())),
        };
        public string Text(uint word) => Type switch { "boolean" => Index(word) != 0 ? "true" : "false", "integer" => Value(word).ToString(), _ => Values[Index(word)] };
    }

    public readonly List<Field> Packed = new(), Entity = new();
    public int Bits;
    public bool IsEmpty => Packed.Count == 0 && Entity.Count == 0;
    public Field Find(string name) => Packed.FirstOrDefault(f => f.Name == name) ?? Entity.FirstOrDefault(f => f.Name == name);

    public static StateSchema Parse(JsonElement? obj, string owner, int maxBits)
    {
        var s = new StateSchema();
        if (obj is not { ValueKind: JsonValueKind.Object } o) return s;
        foreach (var p in o.EnumerateObject())
        {
            var e = p.Value; var f = new Field { Name = p.Name, Type = Str(e, "type", "string") };
            bool entity = Str(e, "storage") == "entity";
            switch (f.Type)
            {
                case "boolean": f.Count = 2; break;
                case "string":
                    if (Get(e, "values") is { ValueKind: JsonValueKind.Array } vs) f.Values = vs.EnumerateArray().Select(v => v.GetString()).ToArray();
                    if (f.Values == null || f.Values.Length == 0) throw new InvalidDataException($"{owner}: state field '{p.Name}' (string) needs a values list");
                    f.Count = f.Values.Length; break;
                case "integer":
                    if (Get(e, "min") is { ValueKind: JsonValueKind.Number } mn && Get(e, "max") is { ValueKind: JsonValueKind.Number } mx) { f.Min = mn.GetInt32(); f.Count = mx.GetInt32() - f.Min + 1; }
                    else entity = true; // no range: too big to pack
                    break;
                case "number": entity = true; f.DefNumber = Num(e, "default"); break;
                case "inventory": entity = true; f.Slots = Math.Max(1, (int)Num(e, "slots", 1)); break;
                default: throw new InvalidDataException($"{owner}: state field '{p.Name}' has unknown type '{f.Type}' (boolean, string, integer, number, inventory)");
            }
            if (entity || f.Count <= 0) { f.Packed = false; if (f.Type == "integer") f.DefNumber = Num(e, "default"); s.Entity.Add(f); continue; }
            f.Packed = true; f.Bits = Math.Max(1, (int)Math.Ceiling(Math.Log2(f.Count)));
            if (Get(e, "default") is { } dv && dv.ValueKind != JsonValueKind.Null) f.DefIndex = f.IndexOf(dv);
            f.Shift = s.Bits; s.Bits += f.Bits; s.Packed.Add(f);
        }
        if (s.Bits > maxBits) throw new InvalidDataException($"{owner}: packed state fields need {s.Bits} bits, at most {maxBits} fit (mark big fields \"storage\": \"entity\")");
        return s;
    }

    /// <summary>State word with the given field values ({"half": "upper", "open": true}); other fields keep the values in word.</summary>
    public uint Apply(uint word, JsonElement values)
    {
        if (values.ValueKind != JsonValueKind.Object) return word;
        foreach (var p in values.EnumerateObject()) { var f = Find(p.Name); if (f != null && f.Packed) word = f.With(word, f.IndexOf(p.Value)); }
        return word;
    }
    /// <summary>Does the state word match all conditions of a "when" object?</summary>
    public bool Matches(uint word, JsonElement when)
    {
        if (when.ValueKind != JsonValueKind.Object) return true;
        foreach (var p in when.EnumerateObject()) { var f = Find(p.Name); if (f == null || !f.Packed || f.Index(word) != f.IndexOf(p.Value)) return false; }
        return true;
    }
    public string Describe(uint word) => string.Join(" ", Packed.Select(f => f.Name + "=" + f.Text(word)));
}
