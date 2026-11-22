using System.Diagnostics;
using Silk.NET.Vulkan;

namespace CubePlanet;

/// <summary>Sun shadows: up to 3 cascades following the camera.</summary>
public static class Shadows
{
    // ---------- sun shadows: up to 3 cascades following the camera ----------
    // 0: 112-block box (voxel terrain, 5x5 PCF), 1: covers the whole chunk range (voxel terrain + distant LOD terrain),
    // 2: 1800-block box (LOD terrain only) so mountains far away still cast shadows. Each cascade is snapped to a coarse multiple
    // of its texel size and cached: it is re-rendered only when its snap cell / the sun step changes, or (throttled) when a chunk
    // mesh inside it changes. Far cascades are staggered so at most one of them is redrawn per frame.
    internal sealed class Casc { public readonly float[] LV = new float[16]; public long kx = long.MinValue, ky, kz; public double qa, ox, oy, cX, cY, E, D; public bool valid, dirty; public int last; public V3 sx, sy, fl, cam; }
    internal static readonly Casc[] cas = { new(), new(), new() };
    internal static int shN; internal static readonly bool[] shNeed = new bool[3];
    internal static bool ShadowTouches(Chunk ch)
    {
        bool any = false;
        for (int i = 0; i < 2; i++)
        {
            var c = cas[i]; if (!c.valid) continue;
            V3 p = new(ch.mx, ch.my, ch.mz);
            if (Math.Abs(M.Dot(p, c.sx) - c.cX) <= c.E + ch.cr && Math.Abs(M.Dot(p, c.sy) - c.cY) <= c.E + ch.cr && Math.Abs(M.Dot(p - c.cam, c.fl)) <= c.D + ch.cr) { c.dirty = true; any = true; }
        }
        return any;
    }
    internal static void ShadowPrep()
    {
        shOn = false; shN = 0; shNeed[0] = shNeed[1] = shNeed[2] = false;
        int want = (int)Math.Round(SET.shadows);
        if (want <= 0 || C.orbit) { foreach (var c in cas) c.valid = false; return; }
        double r = Math.Sqrt(C.x * C.x + C.y * C.y + C.z * C.z); V3 g = new(C.x / r, C.y / r, C.z / r);
        if (r - R > HMAX * 1.6 + 80 || M.Dot(SUN, g) < -.1) { foreach (var c in cas) c.valid = false; return; }
        double stp = SET.shInt > 0 ? SET.shInt * 2 * PI / SET.day : 0, qa = stp != 0 ? M.JsRound(SUNA / stp) * stp : SUNA, qc = Math.Cos(qa), qs = Math.Sin(qa);
        V3 fl = new(-(SA.X * qc + SB.X * qs), -(SA.Y * qc + SB.Y * qs), -(SA.Z * qc + SB.Z * qs));
        V3 refv = Math.Abs(fl.Y) < .95 ? new V3(0, 1, 0) : new V3(1, 0, 0), sx = M.Nrm(M.Cross(fl, refv)), sy = M.Cross(sx, fl);
        double[] ext = { SHE, Math.Max(SHE * 2, RD * CS + 48), 1800 };
        bool farDone = false;
        for (int i = 0; i < 3; i++)
        {
            var c = cas[i];
            if (i >= want) { c.valid = false; continue; }
            double E = ext[i], D = E * 1.5 + HMAX * .8 + 60, stepw = 2 * E / SMS * 16;
            double lx = C.x * sx.X + C.y * sx.Y + C.z * sx.Z, ly = C.x * sy.X + C.y * sy.Y + C.z * sy.Z, lz = C.x * fl.X + C.y * fl.Y + C.z * fl.Z;
            long kx = (long)M.JsRound(lx / stepw), ky = (long)M.JsRound(ly / stepw), kz = (long)M.JsRound(lz / stepw);
            bool moved = !c.valid || c.kx != kx || c.ky != ky || c.kz != kz || c.qa != qa || c.E != E;
            bool redo = moved || (c.dirty && (i == 0 || frameNo - c.last >= 20));
            if (redo && i > 0 && farDone) redo = false; // stagger the far cascades
            if (redo)
            {
                if (i > 0) farDone = true;
                double ox = lx - kx * stepw, oy = ly - ky * stepw, oz = lz - kz * stepw;
                c.sx = sx; c.sy = sy; c.fl = fl; c.E = E; c.D = D; c.ox = ox; c.oy = oy; c.cX = kx * stepw; c.cY = ky * stepw; c.cam = new V3(C.x, C.y, C.z);
                c.kx = kx; c.ky = ky; c.kz = kz; c.qa = qa;
                shNeed[i] = true;
            }
            if (!c.valid && !shNeed[i]) break;
            // light matrix relative to the CURRENT camera (vertex positions are camera-relative)
            double ccx = c.cX, ccy = c.cY, ccz = M.Dot(c.cam, c.fl);
            double rox = (C.x * c.sx.X + C.y * c.sx.Y + C.z * c.sx.Z) - ccx, roy = (C.x * c.sy.X + C.y * c.sy.Y + C.z * c.sy.Z) - ccy, roz = (C.x * c.fl.X + C.y * c.fl.Y + C.z * c.fl.Z) - ccz;
            // depth is centred on the camera position at render time, so keep that reference
            var L = c.LV;
            L[0] = (float)(c.sx.X / c.E); L[1] = (float)(c.sy.X / c.E); L[2] = (float)(c.fl.X / c.D); L[3] = 0;
            L[4] = (float)(c.sx.Y / c.E); L[5] = (float)(c.sy.Y / c.E); L[6] = (float)(c.fl.Y / c.D); L[7] = 0;
            L[8] = (float)(c.sx.Z / c.E); L[9] = (float)(c.sy.Z / c.E); L[10] = (float)(c.fl.Z / c.D); L[11] = 0;
            L[12] = (float)(rox / c.E); L[13] = (float)(roy / c.E); L[14] = (float)(roz / c.D); L[15] = 1;
            shN = i + 1;
        }
        shOn = shN > 0;
    }
    internal static bool InCasc(Casc c, double x, double y, double z, double rad)
    {
        V3 p = new(x, y, z);
        return Math.Abs(M.Dot(p, c.sx) - c.cX) <= c.E + rad && Math.Abs(M.Dot(p, c.sy) - c.cY) <= c.E + rad && Math.Abs(M.Dot(p - c.cam, c.fl)) <= c.D + rad;
    }
    // LOD patches for the far cascades: same level selection as the view, but no frustum test (off-screen mountains cast shadows too)
    internal static readonly List<LodMesh> lodSh = new();
    internal static void LodShadowWalk(Casc cs, int f, int lv, int i, int j, double minDist, double hor, V3 cd)
    {
        double a = PI / 2 / (1 << lv); V3 c = GpA(f, -PI / 4 + (i + .5) * a, -PI / 4 + (j + .5) * a, R);
        double cr = R * a * 1.1 + 3, ang = Math.Acos(M.Clamp((c.X * cd.X + c.Y * cd.Y + c.Z * cd.Z) / R, -1, 1));
        if (ang - cr / R > hor + HA || !InCasc(cs, c.X, c.Y, c.Z, cr + HMAX)) return;
        double dist = Math.Max(Math.Sqrt((c.X - C.x) * (c.X - C.x) + (c.Y - C.y) * (c.Y - C.y) + (c.Z - C.z) * (c.Z - C.z)) - cr, 1), edge = R * a * 1.2;
        if (lv < MAXL && (lv < MINL || edge / dist * LODK > .6))
        {
            for (int di = 0; di < 2; di++) for (int dj = 0; dj < 2; dj++) LodShadowWalk(cs, f, lv + 1, i * 2 + di, j * 2 + dj, minDist, hor, cd);
            return;
        }
        if (dist + 2 * cr < minDist) return; // fully inside the voxel range: the voxel terrain casts there
        var m = TryLod(f, lv, i, j); if (m == null) RequestLod(f, lv, i, j); else lodSh.Add(m);
    }
    /// <summary>Builds the LOD patch lists for the far cascades (may create + upload patches, so it runs before any render pass).</summary>
    internal static readonly List<LodMesh>[] lodShQ = { new(), new(), new() };
    internal static void ShadowCollect()
    {
        for (int k = 1; k < 3; k++)
        {
            lodShQ[k].Clear(); if (!shNeed[k] || !LODON) continue;
            double r = Math.Sqrt(C.x * C.x + C.y * C.y + C.z * C.z), hor = r > R ? Math.Acos(R / r) : PI; V3 cd = new(C.x / r, C.y / r, C.z / r);
            lodSh.Clear(); double minD = Math.Max(0, (RD - 1) * CS);
            for (int f = 0; f < 6; f++) for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) LodShadowWalk(cas[k], f, 2, i, j, minD, hor, cd);
            lodShQ[k].AddRange(lodSh);
        }
    }
    internal static void ShadowDraw()
    {
        for (int k = 0; k < 3; k++)
        {
            if (!shNeed[k]) continue;
            var cs = cas[k];
            Gpu.BeginShadow(k);
            if (k < 2)
            {
                Gpu.Bind(pShadow);
                foreach (var ch in chunks.Values)
                {
                    if (ch.mesh == null || !InCasc(cs, ch.mx, ch.my, ch.mz, ch.cr)) continue;
                    Gpu.Push((float)(ch.mesh.org.X - C.x), (float)(ch.mesh.org.Y - C.y), (float)(ch.mesh.org.Z - C.z), k);
                    DrawM(ch.mesh, (uint)ch.mesh.n);
                }
            }
            if (k > 0 && lodShQ[k].Count > 0)
            {
                Gpu.Bind(pLodShadow);
                foreach (var m in lodShQ[k]) { Gpu.Push((float)(m.o.X - C.x), (float)(m.o.Y - C.y), (float)(m.o.Z - C.z), k); Gpu.DrawShared(m.a, lodIdx, IndexType.Uint16, lodN); }
            }
            Gpu.EndPass(); cs.valid = true; cs.dirty = false; cs.last = frameNo;
        }
    }
}
