namespace CubePlanet;

/// <summary>Turns chunk block data into packed vertices: faces, skylight, ambient occlusion, water surfaces.</summary>
public static class Mesher
{
    internal static readonly int[][] OPN_PR =
    {
        new[]{1,0,3}, new[]{-1,0,3}, new[]{0,1,3}, new[]{0,-1,3}, new[]{1,0,6}, new[]{-1,0,6}, new[]{0,1,6}, new[]{0,-1,6},
        new[]{1,1,4}, new[]{1,-1,4}, new[]{-1,1,4}, new[]{-1,-1,4}
    };
    internal static bool Cul(int v, bool wt, int id) => wt ? (v == Water || UWP[v] || OPQ[v] != 0 || TGR[v] >= 2) : (OPQ[v] != 0 || (TGR[id] != 0 && TGR[v] == TGR[id]));
    // scratch buffers reused by every Build on the same thread (meshing runs on the worker threads) so meshing doesn't churn the GC
    [ThreadStatic] internal static List<float> pVs, pWv, pGv;
    [ThreadStatic] internal static List<uint> pIx, pWi, pGi;
    [ThreadStatic] internal static byte[] sOp, sLt; [ThreadStatic] internal static int[] sQ;
    [ThreadStatic] internal static List<long> tWait;
    internal static bool WLGet(long k, out int lv) { if (BlockStates.TryGet(k, out ushort s)) { lv = LevelOf(s); return true; } lv = 0; return false; } // water level (state of a water cell)

    /// <summary>Meshes a chunk (thread-safe for non-core chunks). wait = neighbour chunk keys that were missing (the chunk is re-lit when they appear).</summary>
    public static BuildResult Build(Chunk ch, out List<long> wait)
    {
        tWait = null; var r = BuildImpl(ch); wait = tWait; tWait = null; return r;
    }

    internal static short Q16(double v) => (short)Math.Clamp(Math.Round(v), -32767, 32767);
    /// <summary>Converts the mesher's 10-float vertices (x,y,z,u,v,layer,sky,nx,ny,nz; u and v in 1/16 block (0..16), u also carries 32*(3-ao), layer carries texture | material flags &lt;&lt; 16, see Blocks.Lay) into the packed GPU format.</summary>
    internal static unsafe MeshData Pack(ReadOnlySpan<float> v, ReadOnlySpan<uint> ix)
    {
        int nv = v.Length / 10; var vb = new byte[nv * 16];
        fixed (byte* pb = vb)
            for (int k = 0; k < nv; k++)
            {
                int o = k * 10; short* s = (short*)(pb + k * 16);
                s[0] = Q16(v[o] * 256); s[1] = Q16(v[o + 1] * 256); s[2] = Q16(v[o + 2] * 256); int lf = (int)v[o + 5]; s[3] = (short)(lf & 0xFFFF);
                ushort* u = (ushort*)(pb + k * 16 + 8);
                u[0] = (ushort)Math.Clamp(Math.Round(v[o + 6] * 120), 0, 65535);
                int ui = (int)v[o + 3], ao = 3 - ((ui >> 5) & 3);
                int fl = (ui >> 7) & 63; // water flow (water faces only; the water pass has no ambient occlusion): replaces the ao bits, 6 bits
                u[1] = (ushort)((ui & 31) | (((int)v[o + 4] & 31) << 5) | (fl != 0 ? fl << 10 : ao << 10)); // bits: u (5, 1/16 block), v (5), ao (2) or flow (6)
                sbyte* nn = (sbyte*)(pb + k * 16 + 12);
                nn[0] = (sbyte)Math.Round(v[o + 7] * 127); nn[1] = (sbyte)Math.Round(v[o + 8] * 127); nn[2] = (sbyte)Math.Round(v[o + 9] * 127); ((byte*)nn)[3] = (byte)(lf >> 16); // 4th byte: material flags (Blocks.Mat*)
            }
        var md = new MeshData { vb = vb, n = ix.Length };
        if (nv < 65536) { md.i16 = new ushort[ix.Length]; for (int i = 0; i < ix.Length; i++) md.i16[i] = (ushort)ix[i]; }
        else md.i32 = ix.ToArray();
        return md;
    }
    internal static bool Sup(int b) => OPQ[b] != 0 || TGR[b] == 1;
    // which block-local axis the texture u / v of each face follows (and whether it runs backwards), derived from CN / CU / CV, so a box face
    // shows the same part of the texture as that region of a full cube face
    static readonly int[] UAX = new int[6], VAX = new int[6]; static readonly bool[] UFL = new bool[6], VFL = new bool[6];
    static Mesher()
    {
        for (int fc = 0; fc < 6; fc++)
            for (int a = 0; a < 3; a++)
            {
                bool su = true, fu = true, sv = true, fv = true;
                for (int i = 0; i < 4; i++)
                {
                    int q = CN[fc][i * 3 + a];
                    su &= q == CU[i]; fu &= q == 1 - CU[i]; sv &= q == CV[i]; fv &= q == 1 - CV[i];
                }
                if (su || fu) { UAX[fc] = a; UFL[fc] = fu; }
                if (sv || fv) { VAX[fc] = a; VFL[fc] = fv; }
            }
    }
    /// <summary>Corners of one face of a box (block-local, 0..1) and their texture coordinates.</summary>
    static void BoxFace(BlockBox bx, int fc, double[][] q, float[] uv)
    {
        var c = CN[fc];
        for (int i = 0; i < 4; i++)
        {
            q[i][0] = c[i * 3] != 0 ? bx.X1 : bx.X0; q[i][1] = c[i * 3 + 1] != 0 ? bx.Y1 : bx.Y0; q[i][2] = c[i * 3 + 2] != 0 ? bx.Z1 : bx.Z0;
            double u = q[i][UAX[fc]], v = q[i][VAX[fc]]; uv[i * 2] = (float)(UFL[fc] ? 1 - u : u); uv[i * 2 + 1] = (float)(VFL[fc] ? 1 - v : v);
        }
    }
    /// <summary>False for box faces of zero area; edge = the face lies on the block boundary (an opaque neighbour hides it).</summary>
    static bool BoxFaceInfo(BlockBox bx, int fc, out bool edge)
    {
        int ax = DX[fc] != 0 ? 0 : DY[fc] != 0 ? 1 : 2; int sg = DX[fc] + DY[fc] + DZ[fc];
        float lo = ax == 0 ? bx.X0 : ax == 1 ? bx.Y0 : bx.Z0, hi = ax == 0 ? bx.X1 : ax == 1 ? bx.Y1 : bx.Z1;
        edge = sg > 0 ? hi >= 1 : lo <= 0;
        bool areaX = bx.X1 > bx.X0, areaY = bx.Y1 > bx.Y0, areaZ = bx.Z1 > bx.Z0;
        return ax == 0 ? areaY && areaZ : ax == 1 ? areaX && areaZ : areaX && areaY;
    }

    internal static BuildResult BuildImpl(Chunk ch)
    {
        if (ch.core) return BuildCore(ch);
        pVs ??= new(1 << 16); pWv ??= new(1 << 12); pGv ??= new(1 << 12); pIx ??= new(1 << 16); pWi ??= new(1 << 12); pGi ??= new(1 << 12);
        bool aoOn = SET.ao > .5;
        Band B = bands[ch.band]; double R0 = B.inner + ch.cy * CS; int f = ch.face; V3 n = N[f], u = U[f], v = V[f];
        bool ed; lock (EditLock) ed = ecount.ContainsKey(ch.key);
        if (!ed && R0 > R + HMAX + 1) return null;
        Ensure(ch);
        if (ch.emp && !ed) return null;
        if (ch.solid && !ed && R0 + CS <= R - CAVB - 1) return null; // buried stone: no visible faces
        ushort[] d = ch.d;
        var tu = new double[CS + 1]; var tv = new double[CS + 1];
        for (int i = 0; i <= CS; i++) { tu[i] = Math.Tan(-PI / 4 + (ch.cz * CS + i) * B.d); tv[i] = Math.Tan(-PI / 4 + (ch.cx * CS + i) * B.d); }
        V3 Pt(double x, double y, double z)
        {
            double a = (z == Math.Floor(z) && z >= 0 && z <= CS) ? tu[(int)z] : Math.Tan(-PI / 4 + (ch.cz * CS + z) * B.d);
            double b = (x == Math.Floor(x) && x >= 0 && x <= CS) ? tv[(int)x] : Math.Tan(-PI / 4 + (ch.cx * CS + x) * B.d);
            double X = n.X + u.X * a + v.X * b, Y = n.Y + u.Y * a + v.Y * b, Z = n.Z + u.Z * a + v.Z * b;
            double s = (R0 + y) / Math.Sqrt(X * X + Y * Y + Z * Z);
            return new V3(X * s, Y * s, Z * s);
        }
        // ---- skylight: open columns are level 15, light spreads into covered cells losing 2 per block and cannot pass solid blocks (sealed rooms stay dark)
        // reads real block data from neighbouring chunks (ensuring them when needed) and across cube-face seams via Blk(); chunks waiting on a missing neighbour are re-lit when it appears
        Col cols = ColsOf(B, f, ch.cx, ch.cz);
        byte[] LT = null; const int LM = 8, LY = 8; int LW = CS + 2 * LM, LH = CS + 8 + LY;
        void LightInit()
        {
            int N3 = LW * LW * LH;
            if (sOp == null || sOp.Length < N3) { sOp = new byte[N3]; sLt = new byte[N3]; sQ = new int[N3]; } else Array.Clear(sLt, 0, N3);
            var op = sOp; var lt = sLt; var q = sQ; int qh = 0, qt = 0;
            var n27 = new Chunk[27]; var n27s = new bool[27];
            LT = lt; tWait = new List<long>();
            if (R0 + CS + 8 < R + cols.lo - 8) return;
            if (R0 - LY > R + cols.hi + 20) { Array.Fill(lt, (byte)15); return; }
            var nch = new Dictionary<long, Chunk>(); var hc = new Dictionary<long, Col>(); int bw = B.cols, nrow = B.rows / CS;
            double HAt(int gx, int gz)
            {
                if (gx >= 0 && gx < CS && gz >= 0 && gz < CS) return cols.h[gx + CS * gz];
                int bx = ch.cx * CS + gx, bz = ch.cz * CS + gz, ff = f;
                if (bx < 0 || bz < 0 || bx >= bw || bz >= bw)
                {
                    if (!Wrap(B, f, bx, bz)) return cols.h[Math.Min(CS - 1, Math.Max(0, gx)) + CS * Math.Min(CS - 1, Math.Max(0, gz))];
                    ff = WF; bx = WX; bz = WZ;
                }
                long key = ((long)ff * 1048576 + (bx >> 5)) * 1048576 + (bz >> 5);
                if (!hc.TryGetValue(key, out var c)) { c = ColsOf(B, ff, bx >> 5, bz >> 5); hc[key] = c; }
                return c.h[(bx & 31) + 32 * (bz & 31)];
            }
            bool tpB = ch.band < BC - 1 && (ch.cy + 1) * CS >= B.rows;
            double HUp(int gx, int gz)
            {
                int bx = ch.cx * CS + gx, bz = ch.cz * CS + gz;
                if (bx < 0 || bz < 0 || bx >= bw || bz >= bw) return 1e9;
                double m = 1e9; Band B2 = bands[ch.band + 1];
                for (int a = 0; a < 2; a++) for (int c = 0; c < 2; c++)
                    {
                        int X = bx * 2 + a, Z = bz * 2 + c; Col cc = ColsOf(B2, f, X >> 5, Z >> 5); double h = cc.h[(X & 31) + 32 * (Z & 31)]; if (h < m) m = h;
                    }
                return m;
            }
            Chunk Nc(long key)
            {
                if (!nch.TryGetValue(key, out var nc))
                {
                    chunkMap.TryGetValue(key, out nc);
                    if (nc != null) { if (nc.d == null) Ensure(nc); } else tWait.Add(key);
                    nch[key] = nc;
                }
                return nc;
            }
            for (int gy = -LY; gy < CS + 8; gy++)
            {
                double rr = R0 + gy + .5; int ky = ch.cy + (gy < 0 ? -1 : gy >= CS ? 1 : 0), ly = gy < 0 ? gy + CS : gy >= CS ? gy - CS : gy, gi = gy + LY;
                for (int gz = -LM; gz < CS + LM; gz++)
                    for (int gx = -LM; gx < CS + LM; gx++)
                    {
                        int o = 0;
                        if (gx >= 0 && gx < CS && gz >= 0 && gz < CS && gy >= 0 && gy < CS) o = OPQ[d[gx + CS * (gz + CS * gy)]];
                        else
                        {
                            int bx = ch.cx * CS + gx, bz = ch.cz * CS + gz; bool known = false;
                            if (bx < 0 || bz < 0 || bx >= bw || bz >= bw || ky < 0 || ky >= nrow)
                            {
                                known = true;
                                // above the band's top row each cell has 2x2 cells of the next band: it only blocks light if all four are opaque
                                if (ky >= nrow && bx >= 0 && bz >= 0 && bx < bw && bz < bw && ch.band < BC - 1)
                                {
                                    int yy = ch.cy * CS + gy - B.rows; Nc(ck(ch.band + 1, f, (bx * 2) >> 5, yy >> 5, (bz * 2) >> 5));
                                    o = 1;
                                    for (int a2 = 0; a2 < 2 && o != 0; a2++) for (int c2 = 0; c2 < 2; c2++) if (OPQ[Blk(ch.band + 1, f, bx * 2 + a2, yy, bz * 2 + c2)] == 0) { o = 0; break; }
                                }
                                else if (Norm(ch.band, f, bx, ch.cy * CS + gy, bz))
                                {
                                    long kk = ck(NB, NF, NX >> 5, NY >> 5, NZ >> 5); int nx = NX, ny = NY, nz = NZ;
                                    Chunk nc = Nc(kk);
                                    o = nc != null && nc.d != null ? OPQ[nc.d[(nx & 31) + 32 * ((nz & 31) + 32 * (ny & 31))]] : OPQ[Blk(ch.band, f, bx, ch.cy * CS + gy, bz)];
                                }
                                else o = OPQ[NCV];
                            }
                            else
                            {
                                int ci = (((gy < 0 ? 0 : gy >= CS ? 2 : 1) * 3) + (gz < 0 ? 0 : gz >= CS ? 2 : 1)) * 3 + (gx < 0 ? 0 : gx >= CS ? 2 : 1); Chunk nc;
                                if (n27s[ci]) nc = n27[ci]; else { nc = Nc(ck(ch.band, f, bx >> 5, ky, bz >> 5)); n27[ci] = nc; n27s[ci] = true; }
                                if (nc != null && nc.d != null) { o = OPQ[nc.d[(bx & 31) + 32 * ((bz & 31) + 32 * ly)]]; known = true; }
                            }
                            if (!known) o = rr < R + HAt(gx, gz) ? 1 : 0;
                        }
                        op[(gi * LW + gz + LM) * LW + gx + LM] = (byte)o;
                    }
            }
            for (int gz = -LM; gz < CS + LM; gz++)
                for (int gx = -LM; gx < CS + LM; gx++)
                {
                    if (!(R0 + CS + 7.5 > R + Math.Min(HAt(gx, gz), tpB ? HUp(gx, gz) : 1e9))) continue;
                    for (int gi = LH - 1; gi >= 0; gi--)
                    {
                        int i = (gi * LW + gz + LM) * LW + gx + LM;
                        if (op[i] != 0) break;
                        lt[i] = 15; q[qt++] = i;
                    }
                }
            int SY = LW * LW;
            void Tn(int j, int val) { if (op[j] == 0 && lt[j] < val) { lt[j] = (byte)val; q[qt++] = j; } }
            while (qh < qt)
            {
                int i = q[qh++], val = lt[i] - 2; if (val <= 0) continue;
                int gx = i % LW, gz = (i / LW) % LW, gi = i / SY;
                if (gx > 0) Tn(i - 1, val); if (gx < LW - 1) Tn(i + 1, val);
                if (gz > 0) Tn(i - LW, val); if (gz < LW - 1) Tn(i + LW, val);
                if (gi > 0) Tn(i - SY, val); if (gi < LH - 1) Tn(i + SY, val);
            }
        }
        double LightAt(int nx, int ny, int nz)
        {
            if (LT == null) LightInit();
            ny += LY; if (ny < 0) ny = 0;
            if (ny >= LH || nx < -LM || nx >= CS + LM || nz < -LM || nz >= CS + LM) return 1;
            return LT[(ny * LW + nz + LM) * LW + nx + LM] / 15.0;
        }

        bool topB = ch.band < BC - 1 && (ch.cy + 1) * CS >= B.rows;
        V3 org = Pt(0, 0, 0);
        var vs = pVs; var ix = pIx; var wv = pWv; var wi = pWi; var gv = pGv; var gi2 = pGi; vs.Clear(); ix.Clear(); wv.Clear(); wi.Clear(); gv.Clear(); gi2.Clear();
        V3 Pw(double a, double b, double c) { V3 t = Pt(a, b, c); return new V3(t.X - org.X, t.Y - org.Y, t.Z - org.Z); }
        void PQ(V3[] p, bool rev, float lay, double sky, V3 nn)
        {
            uint first = (uint)(vs.Count / 10);
            for (int i = 0; i < 4; i++)
            {
                int uvi = rev ? 3 - i : i;
                vs.Add((float)p[i].X); vs.Add((float)p[i].Y); vs.Add((float)p[i].Z); vs.Add(CU[uvi] * 16); vs.Add(CV[uvi] * 16); vs.Add(lay); vs.Add((float)sky);
                vs.Add((float)nn.X); vs.Add((float)nn.Y); vs.Add((float)nn.Z);
            }
            ix.Add(first); ix.Add(first + 1); ix.Add(first + 2); ix.Add(first); ix.Add(first + 2); ix.Add(first + 3);
        }
        void PQUV(V3[] p, float lay, double sky, V3 nn, float[] uv)
        {
            uint first = (uint)(vs.Count / 10);
            for (int i = 0; i < 4; i++)
            {
                vs.Add((float)p[i].X); vs.Add((float)p[i].Y); vs.Add((float)p[i].Z); vs.Add(MathF.Round(uv[i * 2] * 16)); vs.Add(MathF.Round(uv[i * 2 + 1] * 16)); vs.Add(lay); vs.Add((float)sky);
                vs.Add((float)nn.X); vs.Add((float)nn.Y); vs.Add((float)nn.Z);
            }
            ix.Add(first); ix.Add(first + 1); ix.Add(first + 2); ix.Add(first); ix.Add(first + 2); ix.Add(first + 3);
        }
        void Two(V3[] p, float lay, double sky, V3 nn) { PQ(p, false, lay, sky, nn); PQ(new[] { p[3], p[2], p[1], p[0] }, true, lay, sky, nn); }
        int StAt(int x, int y, int z) => ch.st?[x + CS * (z + CS * y)] ?? 0; // state word of a cell of this chunk
        int Gb(int x, int y, int z) => x >= 0 && x < CS && y >= 0 && y < CS && z >= 0 && z < CS ? d[x + CS * (z + CS * y)] : Blk(ch.band, f, ch.cx * CS + x, ch.cy * CS + y, ch.cz * CS + z);

        // "boxes" models (slabs, stairs, fences, ...): every box face goes through the same curved corner mapping as the cubes;
        // faces on the block boundary are hidden by opaque neighbours, faces inside the block are always drawn
        void BoxModel(int id, int x, int y, int z)
        {
            var q = new[] { new double[3], new double[3], new double[3], new double[3] }; var uv = new float[8];
            foreach (var mb in HASVAR[id] ? Variant(id, StAt(x, y, z)).Boxes : MBOX[id])
                for (int fc = 0; fc < 6; fc++)
                {
                    if (!BoxFaceInfo(mb.Box, fc, out bool edge)) continue;
                    int nx = x + DX[fc], ny = y + DY[fc], nz = z + DZ[fc];
                    if (edge && OPQ[Gb(nx, ny, nz)] != 0) continue;
                    BoxFace(mb.Box, fc, q, uv); var p = new V3[4];
                    for (int i = 0; i < 4; i++) p[i] = Pw(x + q[i][0], y + q[i][1], z + q[i][2]);
                    double sky = EMI[id] ? 1 : edge ? LightAt(nx, ny, nz) : LightAt(x, y, z);
                    double ax = p[2].X - p[0].X, ay = p[2].Y - p[0].Y, az = p[2].Z - p[0].Z, bx = p[3].X - p[1].X, by = p[3].Y - p[1].Y, bz = p[3].Z - p[1].Z;
                    PQUV(p, LayTex(id, mb.Faces[fc]), sky, M.Nrm(new V3(ay * bz - az * by, az * bx - ax * bz, ax * by - ay * bx)), uv);
                }
        }
        // plants (crossed quads), vines (thin planes on supporting sides) and cocoa pods (small boxes)
        void Special(int id, int x, int y, int z)
        {
            double sky = LightAt(x, y, z); V3 up = M.Nrm(Pt(x + .5, y + .5, z + .5));
            // plants with state (crop stages): the texture of their current state, not the base one
            float lay = HASVAR[id] ? LayTex(id, Variant(id, StAt(x, y, z)).Faces[2]) : Lay(id);
            if (ATT[id] == 1) // vine
            {
                bool any = false;
                foreach (int fc in new[] { 0, 1, 4, 5 })
                {
                    bool ok = false;
                    for (int k = 0; k < 8; k++) { if (k != 0 && Gb(x, y + k, z) != id) break; if (Sup(Gb(x + DX[fc], y + k, z + DZ[fc]))) { ok = true; break; } }
                    if (ok)
                    {
                        any = true; var c = CN[fc]; var p = new V3[4];
                        for (int i = 0; i < 4; i++) p[i] = Pw(x + c[i * 3] - DX[fc] * .06, y + c[i * 3 + 1], z + c[i * 3 + 2] - DZ[fc] * .06);
                        Two(p, lay, sky, up);
                    }
                }
                if (Sup(Gb(x, y + 1, z)))
                {
                    any = true; var c = CN[2]; var p = new V3[4];
                    for (int i = 0; i < 4; i++) p[i] = Pw(x + c[i * 3], y + c[i * 3 + 1] - .06, z + c[i * 3 + 2]);
                    Two(p, lay, sky, up);
                }
                if (!any)
                {
                    var c = CN[0]; var p = new V3[4];
                    for (int i = 0; i < 4; i++) p[i] = Pw(x + c[i * 3] - .06, y + c[i * 3 + 1], z + c[i * 3 + 2]);
                    Two(p, lay, sky, up);
                }
            }
            else if (ATT[id] == 2) // pod (cocoa)
            {
                double sx = 0, sz = 0;
                foreach (int fc in new[] { 0, 1, 4, 5 }) if (LOGID[Gb(x + DX[fc], y, z + DZ[fc])]) { sx = DX[fc] * .25; sz = DZ[fc] * .25; break; }
                double x0 = .25 + sx, x1 = .75 + sx, z0 = .25 + sz, z1 = .75 + sz, y0 = .3, y1 = .85;
                for (int fc = 0; fc < 6; fc++)
                {
                    var c = CN[fc]; var p = new V3[4];
                    for (int i = 0; i < 4; i++) p[i] = Pw(x + (c[i * 3] != 0 ? x1 : x0), y + (c[i * 3 + 1] != 0 ? y1 : y0), z + (c[i * 3 + 2] != 0 ? z1 : z0));
                    double ax = p[2].X - p[0].X, ay = p[2].Y - p[0].Y, az = p[2].Z - p[0].Z, bx = p[3].X - p[1].X, by = p[3].Y - p[1].Y, bz = p[3].Z - p[1].Z;
                    PQ(p, false, lay, sky, M.Nrm(new V3(ay * bz - az * by, az * bx - ax * bz, ax * by - ay * bx)));
                }
            }
            else
            {
                double jit = JIT[id] ? .3 : 0, X = (HH(f, ch.cx * CS + x, ch.cz * CS + z, 77) - .5) * jit, Z = (HH(f, ch.cx * CS + x, ch.cz * CS + z, 78) - .5) * jit, lo = .1, hi = .9;
                Two(new[] { Pw(x + lo + X, y, z + lo + Z), Pw(x + hi + X, y, z + hi + Z), Pw(x + hi + X, y + 1, z + hi + Z), Pw(x + lo + X, y + 1, z + lo + Z) }, lay, sky, up);
                Two(new[] { Pw(x + lo + X, y, z + hi + Z), Pw(x + hi + X, y, z + lo + Z), Pw(x + hi + X, y + 1, z + lo + Z), Pw(x + lo + X, y + 1, z + hi + Z) }, lay, sky, up);
            }
        }
        // water surface openness (used to fade waves in small ponds): ocean-like if most of the surrounding surface is water
        var oc = new Dictionary<int, int>();
        int Opn(int vx, int vy, int vz)
        {
            int k = ((vx + 16) * 128 + vz + 16) * 64 + vy;
            if (oc.TryGetValue(k, out int vv)) return vv;
            // waves move only surface vertices: pin every corner that touches something other than open water (a block, ice, a capped or
            // higher water column, air on the surface row), otherwise the moving surface pulls away from the faces next to it and leaves gaps
            for (int a = 0; a < 2; a++)
                for (int b = 0; b < 2; b++)
                {
                    int cx = vx - 1 + a, cz = vz - 1 + b, w = Gb(cx, vy - 1, cz), u = Gb(cx, vy, cz);
                    if (!Wet(w) || Wet(u) || OPQ[u] != 0 || TGR[u] >= 2) { oc[k] = 0; return 0; }
                }
            int cnt = 0;
            foreach (var pr in OPN_PR) { int dx = pr[0], dz = pr[1], r = pr[2]; if (Gb(vx + dx * r - (dx < 0 ? 1 : 0), vy - 1, vz + dz * r - (dz < 0 ? 1 : 0)) == Water) cnt++; }
            vv = (int)M.JsRound(8 * M.Sm(.7, 1, cnt / 12.0));
            oc[k] = vv; return vv;
        }
        // flow drawn on a water face (WaterFlow snapshot): top faces get the direction (8 steps, measured in the shader's tangent frame
        // t1 = normalize(cross(up, (.31, .83, .47))), t2 = cross(up, t1)) and strength; side faces get the falling strength
        int FlowBits(int x, int y, int z, int fc)
        {
            if (!Norm(ch.band, f, ch.cx * CS + x, ch.cy * CS + y, ch.cz * CS + z)) return 0;
            int code = WaterFlow.CodeAt(ek()); if (code == 0) return 0;
            if (DY[fc] == 0) { int fs = (code >> 6) & 7; return fs == 0 ? 0 : fs << 3; }
            if (fc != 2) return 0;
            int s = (code >> 3) & 7; if (s == 0) return 0;
            double ang = (code & 7) * Math.PI / 4, gx = Math.Cos(ang), gz = Math.Sin(ang);
            V3 c0 = Pt(x + .5, y + 1, z + .5), ex = Pt(x + 1, y + 1, z + .5) - Pt(x, y + 1, z + .5), ez = Pt(x + .5, y + 1, z + 1) - Pt(x + .5, y + 1, z);
            V3 w = ex * gx + ez * gz, upv = M.Nrm(c0), t1 = M.Nrm(M.Cross(upv, new V3(.31, .83, .47))), t2 = M.Cross(upv, t1);
            int bin = ((int)Math.Round(Math.Atan2(M.Dot(w, t2), M.Dot(w, t1)) / (Math.PI / 4)) + 8) & 7;
            return bin | s << 3;
        }
        const double WSURF = .8; // surface height of a full (8/8) water cell
        // disturbed water cells carry a level 1..8 (their state word); the surface vertex height is the average of the surrounding surface cells
        int Wlv(int x, int y, int z) => Norm(ch.band, f, ch.cx * CS + x, ch.cy * CS + y, ch.cz * CS + z) && WLGet(ek(), out int lv) ? lv : 0;
        double Vh(int vx, int vy, int vz)
        {
            if (BlockStates.Count == 0) return WSURF;
            double sm2 = 0; int cnt = 0;
            for (int a = 0; a < 2; a++) for (int b = 0; b < 2; b++)
                {
                    int cx = vx - 1 + a, cz = vz - 1 + b;
                    if (!Wet(Gb(cx, vy - 1, cz)) || Wet(Gb(cx, vy, cz))) continue;
                    int l = Wlv(cx, vy - 1, cz); sm2 += l != 0 ? .1 * l : .8; cnt++;
                }
            return cnt != 0 ? sm2 / cnt : .8;
        }
        var qd = new double[12]; var aoq = new int[4];
        for (int y = 0; y < CS; y++)
        {
            if (!ed) { if (R0 + y > R + HMAX) break; if (R0 + y + 1 < R - DEP) continue; }
            for (int z = 0; z < CS; z++)
                for (int x = 0; x < CS; x++)
                {
                    int id = d[x + CS * (z + CS * y)];
                    if (id == 0) continue;
                    if (MODEL[id] == ModelBoxes) { BoxModel(id, x, y, z); continue; }
                    if (SPC[id] != 0)
                    {
                        Special(id, x, y, z);
                        if (!UWP[id]) continue;
                        id = Water; // an underwater plant sits inside water: give its cell the water faces too (otherwise it leaves a hole in the surface)
                    }
                    if (ch.solid && x > 0 && x < CS - 1 && y > 0 && y < CS - 1 && z > 0 && z < CS - 1) continue;
                    bool wt = id == Water;
                    for (int fc = 0; fc < 6; fc++)
                    {
                        int nx = x + DX[fc], ny = y + DY[fc], nz = z + DZ[fc];
                        int nb = nx >= 0 && nx < CS && ny >= 0 && ny < CS && nz >= 0 && nz < CS ? d[nx + CS * (nz + CS * ny)] : Blk(ch.band, f, ch.cx * CS + nx, ch.cy * CS + ny, ch.cz * CS + nz);
                        // band seam: the next band has 2x2 cells above one cell of this band; Blk() only sees one of them, so look at all four and keep the face if any is open
                        if (fc == 2 && ny == CS && topB && Cul(nb, wt, id))
                        {
                            bool op = false;
                            for (int a = 0; a < 2 && !op; a++) for (int c2 = 0; c2 < 2; c2++)
                                {
                                    int vv = Blk(ch.band + 1, f, (ch.cx * CS + x) * 2 + a, 0, (ch.cz * CS + z) * 2 + c2);
                                    if (!Cul(vv, wt, id)) { nb = vv; op = true; break; }
                                }
                        }
                        bool wallW = false;
                        // step between a full-height water cell (water, ice or a block above it) and a neighbouring water cell whose surface is open
                        // (lower, at ~0.8): water faces between water cells are normally hidden, so draw the strip from the neighbour's surface up
                        if (wt && DY[fc] == 0 && Wet(nb))
                        {
                            int ux = Gb(x, y + 1, z), u2 = Gb(nx, y + 1, nz);
                            bool xFull = Wet(ux) || OPQ[ux] != 0 || TGR[ux] >= 2;
                            wallW = xFull && !Wet(u2) && OPQ[u2] == 0 && TGR[u2] < 2;
                        }
                        if (Cul(nb, wt, id) && !wallW) continue;
                        bool isT = TGR[id] >= 2; // glass / ice
                        var A = wt ? wv : isT ? gv : vs; var I = wt ? wi : isT ? gi2 : ix;
                        uint first = (uint)(A.Count / 10); var c = CN[fc];
                        int ua = wt ? Gb(x, y + 1, z) : 0;
                        bool ab = wt && !Wet(ua) && OPQ[ua] == 0 && TGR[ua] < 2;
                        bool led = wt && DY[fc] == 0 && !wallW && OPQ[Gb(x, y - 1, z)] != 0 && Wet(Gb(nx, y - 1, nz));
                        for (int i = 0; i < 4; i++)
                        {
                            int c1 = c[i * 3 + 1]; double yy = y + c1;
                            if (ab && c1 == 1) yy = y + Vh(x + c[i * 3], y + 1, z + c[i * 3 + 2]);
                            else if (c1 == 0) { if (wallW) yy = y + Vh(x + c[i * 3], y + 1, z + c[i * 3 + 2]); else if (led) yy = y - 1 + Vh(x + c[i * 3], y, z + c[i * 3 + 2]); }
                            V3 t = Pt(x + c[i * 3], yy, z + c[i * 3 + 2]);
                            qd[i * 3] = t.X - org.X; qd[i * 3 + 1] = t.Y - org.Y; qd[i * 3 + 2] = t.Z - org.Z;
                        }
                        // world-space face normal from the quad diagonals (vertices wind counter-clockwise seen from outside)
                        double ax = qd[6] - qd[0], ay = qd[7] - qd[1], az = qd[8] - qd[2], bx = qd[9] - qd[3], by = qd[10] - qd[4], bz = qd[11] - qd[5];
                        double fx = ay * bz - az * by, fy = az * bx - ax * bz, fz = ax * by - ay * bx;
                        double fl2 = Math.Sqrt(fx * fx + fy * fy + fz * fz); double il = 1 / (fl2 != 0 ? fl2 : 1); fx *= il; fy *= il; fz *= il;
                        double sky;
                        if (wt)
                        {
                            if (fc == 2)
                            {
                                int k = 1, gp2 = 0;
                                for (int j = 1; j <= 8; j++) { if (Wet(Gb(x, y - j, z))) { k++; gp2 = 0; } else if (++gp2 > 3) break; }
                                sky = Math.Min(k, 8) / 8.0;
                            }
                            else sky = .5;
                        }
                        else sky = LightAt(nx, ny, nz);
                        if (EMI[id]) sky = 1; // self-lit blocks (lava, magma, sea lanterns)
                        float lay = HASVAR[id] ? LayTex(id, Variant(id, StAt(x, y, z)).Faces[fc]) : Lay(id, fc);
                        int flow = wt && WaterFlow.Cells.Count > 0 ? FlowBits(x, y, z, fc) : 0;
                        // ambient occlusion: per corner, count the opaque blocks touching it in the layer in front of the face (2 sides + diagonal)
                        bool doAO = aoOn && !wt && !isT && OPQ[id] != 0;
                        if (doAO)
                        {
                            int axn = DX[fc] != 0 ? 0 : DY[fc] != 0 ? 1 : 2, t1 = axn == 0 ? 1 : 0, t2 = axn == 2 ? 1 : 2;
                            for (int i = 0; i < 4; i++)
                            {
                                int s1 = c[i * 3 + t1] == 1 ? 1 : -1, s2 = c[i * 3 + t2] == 1 ? 1 : -1;
                                int e1x = t1 == 0 ? s1 : 0, e1y = t1 == 1 ? s1 : 0, e1z = t1 == 2 ? s1 : 0, e2x = t2 == 0 ? s2 : 0, e2y = t2 == 1 ? s2 : 0, e2z = t2 == 2 ? s2 : 0;
                                int sA = OPQ[Gb(nx + e1x, ny + e1y, nz + e1z)] != 0 ? 1 : 0, sB = OPQ[Gb(nx + e2x, ny + e2y, nz + e2z)] != 0 ? 1 : 0;
                                int cr = OPQ[Gb(nx + e1x + e2x, ny + e1y + e2y, nz + e1z + e2z)] != 0 ? 1 : 0;
                                aoq[i] = sA + sB == 2 ? 0 : 3 - (sA + sB + cr);
                            }
                        }
                        else { aoq[0] = aoq[1] = aoq[2] = aoq[3] = 3; }
                        for (int i = 0; i < 4; i++)
                        {
                            A.Add((float)qd[i * 3]); A.Add((float)qd[i * 3 + 1]); A.Add((float)qd[i * 3 + 2]); A.Add(CU[i] * 16 + 32 * (3 - aoq[i]) + 128 * flow); A.Add(CV[i] * 16); A.Add(lay);
                            A.Add((float)(sky + (ab && c[i * 3 + 1] == 1 ? 2 + 2 * Opn(x + c[i * 3], y + 1, z + c[i * 3 + 2]) : 0)));
                            A.Add((float)fx); A.Add((float)fy); A.Add((float)fz);
                        }
                        // split the quad along the diagonal that keeps the occlusion gradient smooth
                        if (aoq[0] + aoq[2] < aoq[1] + aoq[3]) { I.Add(first + 1); I.Add(first + 2); I.Add(first + 3); I.Add(first + 1); I.Add(first + 3); I.Add(first); }
                        else { I.Add(first); I.Add(first + 1); I.Add(first + 2); I.Add(first); I.Add(first + 2); I.Add(first + 3); }
                    }
                }
        }
        int nw = wi.Count, goff = wv.Count / 10;
        if (ix.Count == 0 && nw == 0 && gi2.Count == 0) return null;
        var res = new BuildResult { org = org };
        if (ix.Count > 0) res.o = Pack(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(vs), System.Runtime.InteropServices.CollectionsMarshal.AsSpan(ix));
        if (nw > 0 || gi2.Count > 0)
        {
            var wvs = new float[wv.Count + gv.Count]; wv.CopyTo(wvs, 0); gv.CopyTo(wvs, wv.Count);
            var wix = new uint[nw + gi2.Count]; wi.CopyTo(wix, 0);
            for (int i = 0; i < gi2.Count; i++) wix[nw + i] = gi2[i] + (uint)goff;
            res.w = Pack(wvs, wix); res.nw = nw;
        }
        return res;
    }

    internal static BuildResult BuildCore(Chunk ch)
    {
        CoreEnsure(ch);
        if (ch.empty) return null;
        ushort[] d = ch.d; int a = ch.cc[0] * 32, b = ch.cc[1] * 32, c = ch.cc[2] * 32; var org = new V3(a + 16, b + 16, c + 16);
        var vs = new List<float>(); var ix = new List<uint>(); const float SK = .3f;
        void Quad(double[][] p, float lay, double nx, double ny, double nz, bool rev, float[] uv = null)
        {
            uint f0 = (uint)(vs.Count / 10);
            for (int i = 0; i < 4; i++)
            {
                int uu = rev ? 3 - i : i;
                vs.Add((float)p[i][0]); vs.Add((float)p[i][1]); vs.Add((float)p[i][2]);
                if (uv != null) { vs.Add(MathF.Round(uv[i * 2] * 16)); vs.Add(MathF.Round(uv[i * 2 + 1] * 16)); } else { vs.Add(CU[uu] * 16); vs.Add(CV[uu] * 16); }
                vs.Add(lay); vs.Add(SK);
                vs.Add((float)nx); vs.Add((float)ny); vs.Add((float)nz);
            }
            ix.Add(f0); ix.Add(f0 + 1); ix.Add(f0 + 2); ix.Add(f0); ix.Add(f0 + 2); ix.Add(f0 + 3);
        }
        int Nb(int nx, int ny, int nz)
        {
            if (nx >= 0 && nx < 32 && ny >= 0 && ny < 32 && nz >= 0 && nz < 32) return d[nx + 32 * (nz + 32 * ny)];
            int gi = a + nx, gj = b + ny, gk = c + nz, nb = CoreCell(gi, gj, gk);
            if (nb == 0 && !coreEd.ContainsKey(cEk(gi, gj, gk)) && CoreOpqFar(gi, gj, gk)) nb = Stone;
            return nb;
        }
        var bq = new[] { new double[3], new double[3], new double[3], new double[3] }; var buv = new float[8];
        for (int y = 0; y < 32; y++)
            for (int z = 0; z < 32; z++)
                for (int x = 0; x < 32; x++)
                {
                    int id = d[x + 32 * (z + 32 * y)];
                    if (id == 0 || id == Water) continue;
                    if (MODEL[id] == ModelBoxes)
                    {
                        foreach (var mb in HASVAR[id] ? Variant(id, ch.st?[x + 32 * (z + 32 * y)] ?? 0).Boxes : MBOX[id])
                            for (int fc = 0; fc < 6; fc++)
                            {
                                if (!BoxFaceInfo(mb.Box, fc, out bool edge) || (edge && OPQ[Nb(x + DX[fc], y + DY[fc], z + DZ[fc])] != 0)) continue;
                                BoxFace(mb.Box, fc, bq, buv); var pp = new double[4][];
                                for (int i = 0; i < 4; i++) pp[i] = new[] { x + bq[i][0] - 16, y + bq[i][1] - 16, z + bq[i][2] - 16 };
                                Quad(pp, LayTex(id, mb.Faces[fc]), DX[fc], DY[fc], DZ[fc], false, buv);
                            }
                        continue;
                    }
                    if (SPC[id] != 0)
                    {
                        int X = x - 16, Y = y - 16, Z = z - 16; double lo = .1, hi = .9;
                        void Pl(double[][] qq) { Quad(qq, Lay(id), 0, 1, 0, false); Quad(new[] { qq[3], qq[2], qq[1], qq[0] }, Lay(id), 0, 1, 0, true); }
                        Pl(new[] { new[] { X + lo, (double)Y, Z + lo }, new[] { X + hi, (double)Y, Z + hi }, new[] { X + hi, Y + 1.0, Z + hi }, new[] { X + lo, Y + 1.0, Z + lo } });
                        Pl(new[] { new[] { X + lo, (double)Y, Z + hi }, new[] { X + hi, (double)Y, Z + lo }, new[] { X + hi, Y + 1.0, Z + lo }, new[] { X + lo, Y + 1.0, Z + hi } });
                        continue;
                    }
                    for (int fc = 0; fc < 6; fc++)
                    {
                        int nb = Nb(x + DX[fc], y + DY[fc], z + DZ[fc]);
                        if (OPQ[nb] != 0 || (TGR[id] != 0 && TGR[nb] == TGR[id])) continue;
                        var cn = CN[fc]; var pp = new double[4][];
                        for (int i = 0; i < 4; i++) pp[i] = new double[] { x + cn[i * 3] - 16, y + cn[i * 3 + 1] - 16, z + cn[i * 3 + 2] - 16 };
                        Quad(pp, HASVAR[id] ? LayTex(id, Variant(id, ch.st?[x + 32 * (z + 32 * y)] ?? 0).Faces[fc]) : Lay(id, fc), DX[fc], DY[fc], DZ[fc], false);
                    }
                }
        if (ix.Count == 0) return null;
        return new BuildResult { o = Pack(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(vs), System.Runtime.InteropServices.CollectionsMarshal.AsSpan(ix)), org = org };
    }
}
