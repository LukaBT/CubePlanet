namespace CubePlanet;

/// <summary>
/// Moving parts of blocks (properties.definition.moving_parts: blades, shafts, rotors): built every frame on the CPU, turned to the block
/// entity's current angle (rotation network, see Kinetics; numbers "angle" + "speed" since the last tick), and drawn with the terrain pipeline
/// in one batch (same lighting as the world). Only blocks with a block entity and within <see cref="Range"/> blocks are drawn.
/// </summary>
public static class MovingParts
{
    public const double Range = 96;
    const int MaxQuads = 16000; // below 65536 / 4 so 16-bit indices reach every vertex
    static GAlloc quadIx; static readonly List<float> vs = new();

    public static void Draw()
    {
        if (BlockStates.entities.Count == 0 || C.orbit) return;
        vs.Clear(); int quads = 0;
        V3 org = new(Math.Floor(C.x), Math.Floor(C.y), Math.Floor(C.z)); double since = Kinetics.SinceTick;
        foreach (var e in BlockStates.entities.Values)
        {
            var d = DEFS[e.BlockId]; if (d == null || d.MovingParts.Length == 0 || e.Pos.Core) continue;
            var p = e.Pos; V3 c = p.Center; if ((c - new V3(C.x, C.y, C.z)).Len > Range || !p.Loaded) continue;
            var v = HASVAR[e.BlockId] ? Variant(e.BlockId, p.State) : null;
            var parts = v?.Moving ?? d.MovingParts;
            double ang = e.Num("angle") + e.Num("speed") * since;
            foreach (var m in parts) foreach (var mb in m.Boxes) { if (quads + 12 > MaxQuads) break; AddBox(p, mb, m, ang * m.Speed, org); quads += 12; }
        }
        if (quads == 0) return;
        var md = Mesher.Pack(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(vs), ReadOnlySpan<uint>.Empty);
        if (quadIx == null)
        {
            var ix = new ushort[65536 / 4 * 6]; // 16384 quads x 6 indices (vertices stay below 65536)
            for (int q = 0, k = 0; q < 65536 / 4; q++) { int b = q * 4; ix[k++] = (ushort)b; ix[k++] = (ushort)(b + 1); ix[k++] = (ushort)(b + 2); ix[k++] = (ushort)b; ix[k++] = (ushort)(b + 2); ix[k++] = (ushort)(b + 3); }
            quadIx = Gpu.Upload(Array.Empty<byte>(), 0, ix, ix.Length);
        }
        Gpu.Push((float)(org.X - C.x), (float)(org.Y - C.y), (float)(org.Z - C.z), 0);
        Gpu.DrawDynamicIndexedBytes(md.vb, md.vb.Length, quadIx, (uint)(Math.Min(quads, 65536 / 4) * 6));
    }

    // block-local point -> world (the curved block grid), relative to org
    static V3 World(BlockPos p, double lx, double ly, double lz, V3 org)
    {
        Band B = bands[p.B]; V3 w = Gp(B, p.F, p.Z + lz, p.X + lx, B.inner + p.Y + ly); return w - org;
    }
    /// <summary>A part's axis as the world turns it: always pointing toward +x / +y / +z, so every part of a line (shafts placed either way
    /// round, turbines facing each other) turns the same way for the same network angle.</summary>
    public static V3 WorldAxis(V3 axis) { var a = M.Nrm(axis); return a.X + a.Y + a.Z < 0 ? a * -1 : a; }
    static V3 Rot(V3 q, V3 axis, double a)
    {
        double c = Math.Cos(a), s = Math.Sin(a); V3 k = axis; double d = M.Dot(k, q);
        return q * c + M.Cross(k, q) * s + k * (d * (1 - c));
    }
    static void AddBox(BlockPos p, BlockDefinition.ModelBox mb, BlockDefinition.MovingPart m, double ang, V3 org)
    {
        var b = mb.Box; V3 ax = WorldAxis(m.Axis);
        V3 C8(int i)
        {
            var q = new V3((i & 1) != 0 ? b.X1 : b.X0, (i & 2) != 0 ? b.Y1 : b.Y0, (i & 4) != 0 ? b.Z1 : b.Z0) - m.Pivot;
            q = Rot(q, ax, ang) + m.Pivot; return World(p, q.X, q.Y, q.Z, org);
        }
        var pts = new V3[8]; for (int i = 0; i < 8; i++) pts[i] = C8(i);
        // faces in the mesher's order (+x, -x, +y, -y, +z, -z), corners wound outward
        int[][] F = { new[] { 1, 3, 7, 5 }, new[] { 4, 6, 2, 0 }, new[] { 2, 6, 7, 3 }, new[] { 4, 0, 1, 5 }, new[] { 5, 7, 6, 4 }, new[] { 0, 2, 3, 1 } };
        float[] cu = { 0, 0, 1, 1 }, cv = { 0, 1, 1, 0 };
        for (int fc = 0; fc < 6; fc++)
        {
            var f = F[fc]; V3 a = pts[f[0]], bb = pts[f[1]], c = pts[f[2]], dd = pts[f[3]];
            V3 n = M.Nrm(M.Cross(c - a, dd - bb)); float lay = LayTex(p.Id, mb.Faces[fc]);
            // both windings (two-sided): the terrain pipeline culls back faces, and a turned box's winding is not worth getting wrong
            for (int side = 0; side < 2; side++)
                for (int kk = 0; kk < 4; kk++)
                {
                    int k = side == 0 ? kk : 3 - kk; V3 q = k == 0 ? a : k == 1 ? bb : k == 2 ? c : dd; double sg = side == 0 ? 1 : -1;
                    vs.Add((float)q.X); vs.Add((float)q.Y); vs.Add((float)q.Z); vs.Add(cu[k] * 16); vs.Add(cv[k] * 16); vs.Add(lay); vs.Add(.9f);
                    vs.Add((float)(n.X * sg)); vs.Add((float)(n.Y * sg)); vs.Add((float)(n.Z * sg));
                }
        }
    }
}
