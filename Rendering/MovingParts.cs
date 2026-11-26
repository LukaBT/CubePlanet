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
        if ((BlockStates.entities.Count == 0 && Wires.Links.Count == 0 && Tanks.All.Count == 0) || C.orbit) return;
        vs.Clear(); int quads = 0;
        V3 org = new(Math.Floor(C.x), Math.Floor(C.y), Math.Floor(C.z)); double since = Kinetics.SinceTick;
        foreach (var e in BlockStates.entities.Values)
        {
            var d = DEFS[e.BlockId]; if (d == null || d.MovingParts.Length == 0 || e.Pos.Core) continue;
            var p = e.Pos; V3 c = p.Center; if ((c - new V3(C.x, C.y, C.z)).Len > Range || !p.Loaded) continue;
            var v = HASVAR[e.BlockId] ? Variant(e.BlockId, p.State) : null;
            var parts = v?.Moving ?? d.MovingParts;
            double ang = e.Num("angle") + e.Num("speed") * since;
            double ang2 = e.Num("angle2");
            foreach (var m in parts) foreach (var mb in m.Boxes) { if (quads + 12 > MaxQuads) break; AddBox(p, mb, m, ang * m.Speed, org, ang2); quads += 12; }
        }
        quads = AddWires(org, quads);
        quads = AddTanks(org, quads);
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
    static void AddBox(BlockPos p, BlockDefinition.ModelBox mb, BlockDefinition.MovingPart m, double ang, V3 org, double ang2 = 0)
    {
        var b = mb.Box; V3 ax = WorldAxis(m.Axis);
        V3 C8(int i)
        {
            var q = new V3((i & 1) != 0 ? b.X1 : b.X0, (i & 2) != 0 ? b.Y1 : b.Y0, (i & 4) != 0 ? b.Z1 : b.Z0) - m.Pivot;
            if (m.Axis2 is V3 a2 && ang2 != 0) q = Rot(q, WorldAxis(a2), ang2); // the second turn first (a 2-axis tracker's tilt)
            q = Rot(q, ax, ang) + m.Pivot; return World(p, q.X, q.Y, q.Z, org);
        }
        var pts = new V3[8]; for (int i = 0; i < 8; i++) pts[i] = C8(i);
        // faces in the mesher's order (+x, -x, +y, -y, +z, -z), corners wound outward
        int[][] F = { new[] { 1, 3, 7, 5 }, new[] { 4, 6, 2, 0 }, new[] { 2, 6, 7, 3 }, new[] { 4, 0, 1, 5 }, new[] { 5, 7, 6, 4 }, new[] { 0, 2, 3, 1 } };
        for (int fc = 0; fc < 6; fc++)
        {
            var f = F[fc]; V3 a = pts[f[0]], bb = pts[f[1]], c = pts[f[2]], dd = pts[f[3]];
            Quad(a, bb, c, dd, M.Nrm(M.Cross(c - a, dd - bb)), LayTex(p.Id, mb.Faces[fc]));
        }
    }
    static readonly float[] cu = { 0, 0, 1, 1 }, cv = { 0, 1, 1, 0 };
    // one quad, both windings (two-sided): the terrain pipeline culls back faces, and a turned box's winding is not worth getting wrong
    static void Quad(V3 a, V3 bb, V3 c, V3 dd, V3 n, float lay)
    {
        for (int side = 0; side < 2; side++)
            for (int kk = 0; kk < 4; kk++)
            {
                int k = side == 0 ? kk : 3 - kk; V3 q = k == 0 ? a : k == 1 ? bb : k == 2 ? c : dd; double sg = side == 0 ? 1 : -1;
                vs.Add((float)q.X); vs.Add((float)q.Y); vs.Add((float)q.Z); vs.Add(cu[k] * 16); vs.Add(cv[k] * 16); vs.Add(lay); vs.Add(.9f);
                vs.Add((float)(n.X * sg)); vs.Add((float)(n.Y * sg)); vs.Add((float)(n.Z * sg));
            }
    }

    /// <summary>The water inside tanks (seen through tank windows): a box of the room's footprint up to the water's depth.</summary>
    static int AddTanks(V3 org, int quads)
    {
        if (Tanks.All.Count == 0) return quads;
        V3 cam = new(C.x, C.y, C.z); float lay = TEXF[Water * 6 + 2];
        foreach (var t in Tanks.All.Values)
        {
            if (t.Water < 1 || quads + 12 > MaxQuads) continue;
            var a = t.Anchor; if (!a.Loaded || (a.Center - cam).Len > Range * 2) continue;
            double h = Math.Max(.02, t.Depth - .02), w = t.W - .02, d = t.D - .02;
            V3 P(double x, double y, double z) => World(a, .01 + x, y, .01 + z, org);
            V3[] c = { P(0, 0, 0), P(w, 0, 0), P(0, h, 0), P(w, h, 0), P(0, 0, d), P(w, 0, d), P(0, h, d), P(w, h, d) };
            int[][] F = { new[] { 1, 3, 7, 5 }, new[] { 4, 6, 2, 0 }, new[] { 2, 6, 7, 3 }, new[] { 4, 0, 1, 5 }, new[] { 5, 7, 6, 4 }, new[] { 0, 2, 3, 1 } };
            foreach (var f in F) { V3 q0 = c[f[0]], q1 = c[f[1]], q2 = c[f[2]], q3 = c[f[3]]; Quad(q0, q1, q2, q3, M.Nrm(M.Cross(q2 - q0, q3 - q1)), lay); }
            quads += 12;
        }
        return quads;
    }

    /// <summary>Power line wires (<see cref="Wires"/>): a sagging line between the two attach points, as two crossed thin ribbons in 16 pieces.</summary>
    static int AddWires(V3 org, int quads)
    {
        if (Wires.Links.Count == 0) return quads;
        V3 cam = new(C.x, C.y, C.z); float lay = LayTex(0, Wires.WireSpec.Texture);
        foreach (var l in Wires.Links)
        {
            if ((!l.A.Loaded && !l.B.Loaded) || !Wires.IsAnchor(l.A, out var la) || !Wires.IsAnchor(l.B, out var lb)) continue; // one end may be in unloaded land
            V3 a = Wires.AttachPoint(l.A, la), b = Wires.AttachPoint(l.B, lb);
            if (((a + b) * .5 - cam).Len > Range * 2) continue;
            V3 up = M.Nrm((a + b) * .5); double len = (b - a).Len, sag = .03 * len + .1; const int N = 16;
            V3 prev = a - org;
            for (int i = 1; i <= N; i++)
            {
                if (quads + 4 > MaxQuads) return quads;
                double t = i / (double)N; V3 cur = a + (b - a) * t - up * (sag * 4 * t * (1 - t)) - org;
                V3 dir = cur - prev; V3 s1 = M.Nrm(M.Cross(dir, up)) * .035, s2 = M.Nrm(M.Cross(dir, s1)) * .035;
                Quad(prev - s1, prev + s1, cur + s1, cur - s1, M.Nrm(s2), lay);
                Quad(prev - s2, prev + s2, cur + s2, cur - s2, M.Nrm(s1), lay);
                quads += 4; prev = cur;
            }
        }
        return quads;
    }
}
