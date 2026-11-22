using System.Text.Json;

namespace CubePlanet;

/// <summary>
/// An axis-aligned box in block-local units (0..1; x / z along the block grid, y = up, away from the planet centre).
/// JSON: {"x","y","z","w","h","d"} (3D) or the AssetEditor's 2D form {"x","y","width","height"}, which spans the full depth.
/// </summary>
public readonly struct BlockBox
{
    public readonly float X0, Y0, Z0, X1, Y1, Z1;
    public BlockBox(double x0, double y0, double z0, double x1, double y1, double z1) { X0 = (float)x0; Y0 = (float)y0; Z0 = (float)z0; X1 = (float)x1; Y1 = (float)y1; Z1 = (float)z1; }
    public static readonly BlockBox Full = new(0, 0, 0, 1, 1, 1);
    public bool Contains(double x, double y, double z) => x >= X0 && x < X1 && y >= Y0 && y < Y1 && z >= Z0 && z < Z1;

    public static BlockBox Parse(JsonElement e)
    {
        double N(string a, string b, double def) => DefinitionFile.Get(e, a) is { ValueKind: JsonValueKind.Number } v ? v.GetDouble() : DefinitionFile.Get(e, b) is { ValueKind: JsonValueKind.Number } v2 ? v2.GetDouble() : def;
        double x = N("x", "x", 0), y = N("y", "y", 0), z = N("z", "z", 0), w = N("w", "width", 1), h = N("h", "height", 1), d = N("d", "depth", 1);
        return new BlockBox(Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1), Math.Clamp(z, 0, 1), Math.Clamp(x + w, 0, 1), Math.Clamp(y + h, 0, 1), Math.Clamp(z + d, 0, 1));
    }
    /// <summary>A JSON list of boxes, or null when the key is missing.</summary>
    public static BlockBox[] ParseList(JsonElement? e) => e is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray().Select(Parse).ToArray() : null;

    /// <summary>Bounding box of a list (Full for null / empty).</summary>
    public static BlockBox Bounds(BlockBox[] l)
    {
        if (l == null || l.Length == 0) return Full;
        double x0 = 1, y0 = 1, z0 = 1, x1 = 0, y1 = 0, z1 = 0;
        foreach (var b in l) { x0 = Math.Min(x0, b.X0); y0 = Math.Min(y0, b.Y0); z0 = Math.Min(z0, b.Z0); x1 = Math.Max(x1, b.X1); y1 = Math.Max(y1, b.Y1); z1 = Math.Max(z1, b.Z1); }
        return new BlockBox(x0, y0, z0, x1, y1, z1);
    }
}
