using System.Text;

namespace CubePlanet;

/// <summary>
/// Atlas index written by the editor:
///   texture_size: W H / textures_per_row: N / texture_rows: N, then one "id name x y" line per texture (x, y = top-left pixel of its cell).
/// ID 0 is reserved (air), so cell 0 stays empty.
/// </summary>
public sealed class TextureDefinitionFile
{
    public int Width = 16, Height = 16, PerRow = 16, Rows = 16;
    public readonly List<(int id, string name, int x, int y)> Entries = new();

    public static TextureDefinitionFile Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Asset file missing: " + path);
        var t = new TextureDefinitionFile(); int ln = 0;
        foreach (var raw in File.ReadAllLines(path))
        {
            ln++; var line = raw.Trim(); if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
            var p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            int I(int k) => int.TryParse(p[k], out int v) ? v : throw new InvalidDataException($"{Path.GetFileName(path)} line {ln}: '{raw}' is not a number");
            if (p[0] == "texture_size:") { t.Width = I(1); t.Height = I(2); }
            else if (p[0] == "textures_per_row:") t.PerRow = I(1);
            else if (p[0] == "texture_rows:") t.Rows = I(1);
            else if (p.Length >= 4) t.Entries.Add((I(0), p[1], I(2), I(3)));
            else throw new InvalidDataException($"{Path.GetFileName(path)} line {ln}: expected 'id name x y', got '{raw}'");
        }
        return t;
    }

    public string ToText()
    {
        var sb = new StringBuilder();
        sb.Append($"texture_size: {Width} {Height}\n").Append($"textures_per_row: {PerRow}\n").Append($"texture_rows: {Rows}\n");
        foreach (var (id, name, x, y) in Entries.OrderBy(e => e.id)) sb.Append($"{id} {name} {x} {y}\n");
        return sb.ToString();
    }

    /// <summary>Top-left pixel of a texture's cell, as the editor computes it.</summary>
    public (int x, int y) Cell(int id) => (id % PerRow * Width, id / PerRow * Height);
}
