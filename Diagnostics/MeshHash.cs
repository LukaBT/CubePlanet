namespace CubePlanet;

/// <summary>--meshhash (no window): generates the chunks around spawn (render distance 8), meshes them and prints one hash over every
/// mesh's vertex and index bytes, so a mesher change can be checked to produce exactly the same meshes.</summary>
public static class MeshHash
{
    public static void Run()
    {
        PlayerPhysics.P.on = false; World.InitWorldData(1200, 1337); ChunkGenerator.mainTid = -1;
        double cx = Camera.C.x, cy = Camera.C.y, cz = Camera.C.z; int rd = 8;
        var list = new List<Chunk>(); var seen = new HashSet<long>(); int half = 16; double range = rd * World.CS; int n = (int)Math.Ceiling(range / half);
        for (int i = -n; i <= n; i++) for (int j = -n; j <= n; j++) for (int k = -n; k <= n; k++)
                {
                    int ox = i * half, oy = j * half, oz = k * half; if (Math.Sqrt((double)ox * ox + (double)oy * oy + (double)oz * oz) > range) continue;
                    double px = cx + ox, py = cy + oy, pz = cz + oz, rr = Math.Sqrt(px * px + py * py + pz * pz);
                    if (rr > World.R + World.HMAX + World.CS || rr < World.CORE - World.CS) continue;
                    int bi = BlockAccess.P2b(px, py, pz); if (bi < 0) continue;
                    int f = BlockAccess.PF, ccx = BlockAccess.PX >> 5, ccy = BlockAccess.PY >> 5, ccz = BlockAccess.PZ >> 5; long key = BlockAccess.ck(bi, f, ccx, ccy, ccz);
                    if (!seen.Add(key)) continue; double rMin = World.bands[bi].inner + ccy * World.CS;
                    if (rMin > World.R + World.HMAX || rMin + World.CS < World.R - World.DEP || rMin + World.CS <= World.R - World.CAVB - 2) continue;
                    Band B = World.bands[bi]; if (ccx >= B.fc || ccz >= B.fc || ccy >= B.rows / World.CS) continue;
                    list.Add(new Chunk { key = key, band = bi, face = f, cx = ccx, cy = ccy, cz = ccz });
                }
        list.Sort((p, q) => p.key.CompareTo(q.key));
        foreach (var ch in list) { BlockAccess.ResetBlkCache(); World.AddChunk(ch); ChunkGenerator.Ensure(ch); }
        ulong h = 14695981039346656037UL; void Add(byte v) { h ^= v; h *= 1099511628211UL; } long verts = 0;
        void AddMesh(MeshData m)
        {
            if (m == null) { Add(0); return; }
            foreach (var b in m.vb) Add(b); verts += m.vb.Length / 16;
            if (m.i16 != null) foreach (var x in m.i16) { Add((byte)x); Add((byte)(x >> 8)); }
            if (m.i32 != null) foreach (var x in m.i32) { Add((byte)x); Add((byte)(x >> 8)); Add((byte)(x >> 16)); }
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var ch in list) { BlockAccess.ResetBlkCache(); var r = Mesher.Build(ch, out _); for (int s = 0; s < 64; s += 8) Add((byte)(ch.key >> s)); if (r == null) { Add(9); continue; } AddMesh(r.o); AddMesh(r.w); }
        Console.WriteLine($"MESHHASH {list.Count} chunks, {verts} vertices, hash {h:X16} ({sw.ElapsedMilliseconds} ms meshing)");
    }
}
