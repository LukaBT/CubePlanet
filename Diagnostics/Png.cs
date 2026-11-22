using System.IO.Compression;

namespace CubePlanet;

/// <summary>Tiny PNG writer (no dependencies): RGB for --shot screenshots, RGBA for asset atlases.</summary>
public static class Png
{
    public static void Write(string path, byte[] px, int w, int h, bool bgra, bool flipY)
    {
        var raw = new byte[(w * 3 + 1) * h];
        for (int y = 0; y < h; y++)
        {
            int sy = flipY ? h - 1 - y : y, o = y * (w * 3 + 1); raw[o++] = 0;
            for (int x = 0; x < w; x++) { int s = (sy * w + x) * 4; raw[o++] = px[s + (bgra ? 2 : 0)]; raw[o++] = px[s + 1]; raw[o++] = px[s + (bgra ? 0 : 2)]; }
        }
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Fastest, true)) z.Write(raw);
        using var f = File.Create(path);
        f.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13]; BE(ihdr, 0, w); BE(ihdr, 4, h); ihdr[8] = 8; ihdr[9] = 2;
        Chunk(f, "IHDR", ihdr); Chunk(f, "IDAT", ms.ToArray()); Chunk(f, "IEND", Array.Empty<byte>());
    }
    /// <summary>RGBA PNG (rows top-down), used for the asset atlases.</summary>
    public static void WriteRgba(string path, byte[] rgba, int w, int h)
    {
        var raw = new byte[(w * 4 + 1) * h];
        for (int y = 0; y < h; y++) { raw[y * (w * 4 + 1)] = 0; Buffer.BlockCopy(rgba, y * w * 4, raw, y * (w * 4 + 1) + 1, w * 4); }
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(raw);
        using var f = File.Create(path);
        f.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13]; BE(ihdr, 0, w); BE(ihdr, 4, h); ihdr[8] = 8; ihdr[9] = 6;
        Chunk(f, "IHDR", ihdr); Chunk(f, "IDAT", ms.ToArray()); Chunk(f, "IEND", Array.Empty<byte>());
    }
    static void BE(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; BE(len, 0, data.Length); s.Write(len);
        var td = new byte[4 + data.Length]; for (int i = 0; i < 4; i++) td[i] = (byte)type[i]; data.CopyTo(td, 4); s.Write(td);
        uint c = 0xFFFFFFFF; foreach (var b in td) { c ^= b; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; }
        var cb = new byte[4]; BE(cb, 0, (int)~c); s.Write(cb);
    }
}
