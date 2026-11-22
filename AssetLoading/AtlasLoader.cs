using StbImageSharp;

namespace CubePlanet;

/// <summary>
/// Cuts an editor atlas into texture-array layers (layer = texture ID). Rows are flipped so row 0 is the BOTTOM of a block face, which is how the
/// mesher's v coordinate runs. IDs without a texture get a magenta / black checkerboard so missing art is obvious in game.
/// </summary>
public static class AtlasLoader
{
    /// <summary>RGBA bytes of all layers (layer-major); layers = highest texture ID + 1.</summary>
    public static byte[] LoadLayers(string pngPath, TextureDefinitionFile defs, out int layers)
    {
        if (!File.Exists(pngPath)) throw new FileNotFoundException("Asset file missing: " + pngPath);
        ImageResult img;
        using (var s = File.OpenRead(pngPath)) img = ImageResult.FromStream(s, ColorComponents.RedGreenBlueAlpha);
        int w = defs.Width, h = defs.Height, max = 0;
        foreach (var e in defs.Entries) max = Math.Max(max, e.id);
        layers = max + 1;
        var data = new byte[w * h * 4 * layers];
        for (int l = 0; l < layers; l++) Checker(data, l, w, h);
        foreach (var (id, name, x, y) in defs.Entries)
        {
            if (id <= 0 || id > 65535) throw new InvalidDataException($"texture '{name}' has id {id}, ids must be 1..65535");
            if (x < 0 || y < 0 || x + w > img.Width || y + h > img.Height) throw new InvalidDataException($"texture {id} '{name}' at {x},{y} lies outside {Path.GetFileName(pngPath)} ({img.Width}x{img.Height})");
            for (int r = 0; r < h; r++)
                Buffer.BlockCopy(img.Data, ((y + h - 1 - r) * img.Width + x) * 4, data, ((id * h + r) * w) * 4, w * 4);
        }
        return data;
    }

    /// <summary>Same cut-out, but rows stay top-down (image orientation), e.g. for item icons.</summary>
    public static byte[] Cell(ImageResult img, int x, int y, int w, int h)
    {
        var o = new byte[w * h * 4];
        for (int r = 0; r < h; r++) Buffer.BlockCopy(img.Data, ((y + r) * img.Width + x) * 4, o, r * w * 4, w * 4);
        return o;
    }

    static void Checker(byte[] d, int layer, int w, int h)
    {
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = ((layer * h + y) * w + x) * 4; bool m = ((x * 2 / w) + (y * 2 / h)) % 2 == 0;
                d[o] = (byte)(m ? 255 : 0); d[o + 1] = 0; d[o + 2] = (byte)(m ? 255 : 0); d[o + 3] = 255;
            }
    }
}
