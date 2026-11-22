using System.Diagnostics;
using Silk.NET.Vulkan;

namespace CubePlanet;

/// <summary>Distant terrain: quadtree of curved LOD patches per cube face.</summary>
public static class LodTerrain
{
    // ---------- LOD: quadtree of curved terrain patches per cube face ----------
    internal static readonly Dictionary<long, LodMesh> lodMap = new();
    internal static readonly List<long> lodDrop = new();

    // LOD patches are built on thread-pool threads (each needs 121 terrain samples, and turning around used to create hundreds of them
    // in one frame); until a patch is ready its parent is drawn instead.
    internal static readonly HashSet<long> lodPending = new();
    internal static readonly System.Collections.Concurrent.ConcurrentQueue<(long k, float[] vd, V3 o)> lodDone = new();
    internal const int LOD_INFLIGHT = 48;
    internal static long LodKey(int f, int lv, int i, int j) => (((long)f * 16 + lv) * 4096 + i) * 4096 + j;
    internal static LodMesh TryLod(int f, int lv, int i, int j)
    {
        if (lodMap.TryGetValue(LodKey(f, lv, i, j), out var m)) { m.t = frameNo; return m; }
        return null;
    }
    internal static void RequestLod(int f, int lv, int i, int j)
    {
        long k = LodKey(f, lv, i, j);
        if (lodPending.Count >= LOD_INFLIGHT || !lodPending.Add(k)) return;
        ThreadPool.UnsafeQueueUserWorkItem(_ => { var (vd, o) = BuildLodData(f, lv, i, j); lodDone.Enqueue((k, vd, o)); }, null);
    }
    /// <summary>Uploads finished LOD patches (before any render pass of the frame).</summary>
    internal static void LodUploads(double t0)
    {
        while (NowMs - t0 < 12 && lodDone.TryDequeue(out var d))
        {
            lodPending.Remove(d.k);
            if (lodMap.ContainsKey(d.k)) continue;
            lodMap[d.k] = new LodMesh { a = Gpu.Upload(d.vd, d.vd.Length, Array.Empty<byte>(), 0), o = d.o, t = frameNo };
        }
    }
    internal static (float[] vd, V3 o) BuildLodData(int f, int lv, int i, int j)
    {
        double a = PI / 2 / (1 << lv), u0 = -PI / 4 + i * a, v0 = -PI / 4 + j * a; const int G = 8, S = G + 3;
        double sk = a * R * .12 + 1, lw = a * R / G * 1.2, seg = a * R / G; int seaSk = (int)Math.Ceiling(seg * seg / (8 * R) * 2 + 2);
        V3 o = GpA(f, u0 + a / 2, v0 + a / 2, R);
        var vd = new float[S * S * 12]; var pp = new double[S * S * 3]; int p = 0;
        for (int gi = 0; gi < S; gi++)
            for (int gj = 0; gj < S; gj++)
            {
                int ci = Math.Min(Math.Max(gi - 1, 0), G), cj = Math.Min(Math.Max(gj - 1, 0), G); bool edge = gi == 0 || gj == 0 || gi == S - 1 || gj == S - 1;
                double au = u0 + ci * a / G, av = v0 + cj * a / G; V3 t = GpA(f, au, av, 1);
                Terrain(t.X, t.Y, t.Z, lw);
                double rd = R + gH - 2.2 - (edge ? sk : 0); var c = Tex.TileColor(gT);
                double kk = (gB == 6 || gB == 14 || gB == 26 || gB == 28) ? .8 : (gB == 2 || gB == 3 || gB == 4 || gB == 10 || gB == 19) ? .86 : 1;
                double x = t.X * rd - o.X, y = t.Y * rd - o.Y, z = t.Z * rd - o.Z; int q = (gi * S + gj) * 3;
                pp[q] = x; pp[q + 1] = y; pp[q + 2] = z;
                vd[p++] = (float)x; vd[p++] = (float)y; vd[p++] = (float)z; vd[p++] = (float)au; vd[p++] = (float)av;
                // the ice flag also carries the sea skirt depth for edge vertices (ice + 4*depth): the LOD sea is projected onto the sea sphere,
                // so without this its skirts collapse and patches of different detail leave cracks
                vd[p++] = (float)(c[0] / 255.0 * kk); vd[p++] = (float)(c[1] / 255.0 * kk); vd[p++] = (float)(c[2] / 255.0 * kk); vd[p++] = gI + (edge ? 4 * seaSk : 0); p += 3;
            }
        // smooth per-vertex normals from the height grid (one-sided at the patch border); skirt vertices copy their neighbour
        for (int gi = 1; gi < S - 1; gi++)
            for (int gj = 1; gj < S - 1; gj++)
            {
                int a0 = Math.Max(gi - 1, 1), a1 = Math.Min(gi + 1, S - 2), b0 = Math.Max(gj - 1, 1), b1 = Math.Min(gj + 1, S - 2);
                int pA = (a1 * S + gj) * 3, pB = (a0 * S + gj) * 3, pC = (gi * S + b1) * 3, pD = (gi * S + b0) * 3;
                double ux = pp[pA] - pp[pB], uy = pp[pA + 1] - pp[pB + 1], uz = pp[pA + 2] - pp[pB + 2], vx = pp[pC] - pp[pD], vy = pp[pC + 1] - pp[pD + 1], vz = pp[pC + 2] - pp[pD + 2];
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx; double l = Math.Sqrt(nx * nx + ny * ny + nz * nz); if (l == 0) l = 1; nx /= l; ny /= l; nz /= l;
                int q = (gi * S + gj) * 3;
                if (nx * (pp[q] + o.X) + ny * (pp[q + 1] + o.Y) + nz * (pp[q + 2] + o.Z) < 0) { nx = -nx; ny = -ny; nz = -nz; }
                int w = (gi * S + gj) * 12 + 9; vd[w] = (float)nx; vd[w + 1] = (float)ny; vd[w + 2] = (float)nz;
            }
        for (int gi = 0; gi < S; gi++)
            for (int gj = 0; gj < S; gj++)
            {
                if (gi > 0 && gi < S - 1 && gj > 0 && gj < S - 1) continue;
                int s = (Math.Min(Math.Max(gi, 1), S - 2) * S + Math.Min(Math.Max(gj, 1), S - 2)) * 12 + 9, w = (gi * S + gj) * 12 + 9;
                vd[w] = vd[s]; vd[w + 1] = vd[s + 1]; vd[w + 2] = vd[s + 2];
            }
        return (vd, o);
    }
    public static int lodDrawn;
    internal static readonly List<(LodMesh m, float x, float y, float z)> lodQ = new();
    // walk the quadtree and collect the visible patches; missing ones are requested from the workers and covered by their parent
    // meanwhile. Returns false when this node could not be covered at all (so the caller falls back to its own patch).
    internal static bool LodWalk(int f, int lv, int i, int j, double hor, V3 cd)
    {
        double a = PI / 2 / (1 << lv); V3 c = GpA(f, -PI / 4 + (i + .5) * a, -PI / 4 + (j + .5) * a, R);
        double cr = R * a * 1.1 + 3, ang = Math.Acos(M.Clamp((c.X * cd.X + c.Y * cd.Y + c.Z * cd.Z) / R, -1, 1));
        if (ang - cr / R > hor + HA || !Vis(c.X, c.Y, c.Z, cr + HMAX)) return true;
        double dist = Math.Max(Math.Sqrt((c.X - C.x) * (c.X - C.x) + (c.Y - C.y) * (c.Y - C.y) + (c.Z - C.z) * (c.Z - C.z)) - cr, 1), edge = R * a * 1.2;
        if (lv < MAXL && (lv < MINL || edge / dist * LODK > .6))
        {
            int mark = lodQ.Count; bool ok = true;
            for (int di = 0; di < 2; di++) for (int dj = 0; dj < 2; dj++) ok &= LodWalk(f, lv + 1, i * 2 + di, j * 2 + dj, hor, cd);
            if (ok) return true;
            var own = TryLod(f, lv, i, j);
            if (own == null) return false;
            lodDrawn -= lodQ.Count - mark; lodQ.RemoveRange(mark, lodQ.Count - mark);
            lodQ.Add((own, (float)(own.o.X - C.x), (float)(own.o.Y - C.y), (float)(own.o.Z - C.z))); lodDrawn++;
            return true;
        }
        var m = TryLod(f, lv, i, j);
        if (m == null) { RequestLod(f, lv, i, j); return false; }
        lodQ.Add((m, (float)(m.o.X - C.x), (float)(m.o.Y - C.y), (float)(m.o.Z - C.z))); lodDrawn++;
        return true;
    }
    internal static void CollectLod()
    {
        lodDrawn = 0; lodQ.Clear(); if (!LODON) return;
        double r = Math.Sqrt(C.x * C.x + C.y * C.y + C.z * C.z), hor = r > R ? Math.Acos(R / r) : PI; V3 cd = new(C.x / r, C.y / r, C.z / r);
        for (int f = 0; f < 6; f++) for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) LodWalk(f, 2, i, j, hor, cd);
        // keep the coarse level-2 patches (48 of them) around so there is always something to fall back to
        for (int f = 0; f < 6; f++) for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) if (TryLod(f, 2, i, j) == null) RequestLod(f, 2, i, j);
    }
    internal static void DrawLod(Pipe p)
    {
        if (lodQ.Count == 0) return;
        Gpu.Bind(p);
        foreach (var (m, x, y, z) in lodQ) { Gpu.Push(x, y, z, 0); Gpu.DrawShared(m.a, lodIdx, IndexType.Uint16, lodN); }
    }
}
