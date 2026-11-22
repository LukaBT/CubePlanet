using System.IO.Compression;

namespace CubePlanet;

/// <summary>
/// One region file: up to 8x8x8 chunks of one band face ("r.{band*6+face}.{cx>>3}.{cy>>3}.{cz>>3}.bin"). Each chunk record is stored whole:
/// 2-byte block IDs, the state words (if any) and its block entities, deflate-compressed per chunk.
/// Layout: "CPRG", int version, int count, then per chunk: int band, face, cx, cy, cz, int byteLength, compressed bytes.
/// Compressed payload: 32768 ushort block IDs, byte hasState (+ 32768 ushort state words), int entity count, entities (see WorldSave).
/// </summary>
public static class RegionFile
{
    const int Version = 1;
    public sealed class Record { public int Band, Face, Cx, Cy, Cz; public ushort[] D, St; public List<BlockEntity> Entities = new(); public byte[] EntityBytes; public int EntityCount; } // EntityBytes: entities serialized in advance (background save)

    public static string NameOf(int band, int face, int cx, int cy, int cz) => $"r.{band * 6 + face}.{cx >> 3}.{cy >> 3}.{cz >> 3}.bin";

    public static void Write(string path, IEnumerable<Record> recs, Action<BinaryWriter, BlockEntity> writeEntity)
    {
        var list = recs.ToList();
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true))
        {
            w.Write("CPRG".ToCharArray()); w.Write(Version); w.Write(list.Count);
            foreach (var r in list)
            {
                using var raw = new MemoryStream();
                using (var z = new ZLibStream(raw, CompressionLevel.Fastest, true))
                using (var pw = new BinaryWriter(z))
                {
                    var bytes = new byte[32768 * 2]; Buffer.BlockCopy(r.D, 0, bytes, 0, bytes.Length); pw.Write(bytes);
                    pw.Write((byte)(r.St != null ? 1 : 0));
                    if (r.St != null) { Buffer.BlockCopy(r.St, 0, bytes, 0, bytes.Length); pw.Write(bytes); }
                    if (r.EntityBytes != null) { pw.Write(r.EntityCount); pw.Write(r.EntityBytes); }
                    else { pw.Write(r.Entities.Count); foreach (var e in r.Entities) writeEntity(pw, e); }
                }
                w.Write(r.Band); w.Write(r.Face); w.Write(r.Cx); w.Write(r.Cy); w.Write(r.Cz);
                w.Write((int)raw.Length); w.Write(raw.GetBuffer(), 0, (int)raw.Length);
            }
        }
        WorldSave.WriteAtomic(path, ms.ToArray());
    }

    public static List<Record> Read(string path, Func<BinaryReader, BlockPos, BlockEntity> readEntity)
    {
        var list = new List<Record>();
        using var r = new BinaryReader(File.OpenRead(path));
        if (new string(r.ReadChars(4)) != "CPRG") throw new InvalidDataException(Path.GetFileName(path) + " is not a region file");
        int ver = r.ReadInt32(); if (ver > Version) throw new InvalidDataException($"{Path.GetFileName(path)} was saved by a newer version ({ver})");
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            var rec = new Record { Band = r.ReadInt32(), Face = r.ReadInt32(), Cx = r.ReadInt32(), Cy = r.ReadInt32(), Cz = r.ReadInt32() };
            var comp = r.ReadBytes(r.ReadInt32());
            using var z = new ZLibStream(new MemoryStream(comp), CompressionMode.Decompress);
            using var pr = new BinaryReader(z);
            rec.D = new ushort[32768]; Buffer.BlockCopy(pr.ReadBytes(65536), 0, rec.D, 0, 65536);
            if (pr.ReadByte() != 0) { rec.St = new ushort[32768]; Buffer.BlockCopy(pr.ReadBytes(65536), 0, rec.St, 0, 65536); }
            int ne = pr.ReadInt32();
            for (int k = 0; k < ne; k++) { var e = readEntity(pr, default); if (e != null) rec.Entities.Add(e); }
            list.Add(rec);
        }
        return list;
    }
}
