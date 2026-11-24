using System.Numerics;

namespace CubePlanet;

/// <summary>Cube-sphere block addressing: position <-> band/face/block coordinates, seam wrapping, chunk keys, reading and writing blocks.</summary>
public static class BlockAccess
{
    // results of Wrap / P2b / Norm (globals as in the original)
    // [ThreadStatic]: chunk generation, meshing and the loader scan run on worker threads too
    [ThreadStatic] internal static long blkKey; [ThreadStatic] internal static Chunk blkCh;
    [ThreadStatic] public static int WF, WX, WZ;
    [ThreadStatic] public static int PF, PX, PY, PZ;
    [ThreadStatic] public static double PFX, PFY, PFZ; // P2b: position inside the block (0..1, block-local x / y (up) / z)
    [ThreadStatic] public static int NB, NF, NX, NY, NZ, NCV;
    public static void ResetBlkCache() { blkCh = null; blkKey = 0; }

    // ---------- geometry ----------
    public static V3 GpA(int f, double a, double b, double rad)
    {
        double ta = Math.Tan(a), tb = Math.Tan(b);
        V3 n = N[f], u = U[f], v = V[f];
        double x = n.X + u.X * ta + v.X * tb, y = n.Y + u.Y * ta + v.Y * tb, z = n.Z + u.Z * ta + v.Z * tb;
        double s = rad / Math.Sqrt(x * x + y * y + z * z);
        return new V3(x * s, y * s, z * s);
    }
    public static V3 Gp(Band B, int f, double ui, double vi, double rad) => GpA(f, -PI / 4 + ui * B.d, -PI / 4 + vi * B.d, rad);

    /// <summary>Wrap block coords across a cube-face seam. Result in WF/WX/WZ; false when outside (corner).</summary>
    public static bool Wrap(Band B, int f, int bx, int bz)
    {
        int n = B.cols; bool oU = bz < 0 || bz >= n, oV = bx < 0 || bx >= n;
        WF = f; WX = bx; WZ = bz;
        if (!oU && !oV) return true;
        if (oU && oV) return false;
        int s, m, al;
        if (oU) { s = bz >= n ? 0 : 1; m = bz >= n ? bz - n : -1 - bz; al = bx; }
        else { s = bx >= n ? 2 : 3; m = bx >= n ? bx - n : -1 - bx; al = bz; }
        if (m >= n) return false;
        Link l = LK[f * 4 + s];
        int ac = l.af ? n - 1 - m : m, ai = l.al ? n - 1 - al : al;
        WF = l.g;
        if (l.acU) { WZ = ac; WX = ai; } else { WX = ac; WZ = ai; }
        return true;
    }

    public static readonly object EditLock = new(); // guards edits / ecount between the main thread (writer) and the chunk workers (readers)

    // ---------- position <-> block ----------
    /// <summary>Position to band index (or -1 for core/outside); result block coords in PF/PX/PY/PZ.</summary>
    public static int P2b(double x, double y, double z)
    {
        double r = Math.Sqrt(x * x + y * y + z * z);
        if (r < CORE || r >= OUTER) return -1;
        int bi = Math.Min(bandOf(r), BC - 1); Band B = bands[bi]; int f = FaceOf(x, y, z);
        V3 n = N[f], u = U[f], v = V[f];
        double pn = x * n.X + y * n.Y + z * n.Z;
        double aU = Math.Atan2(x * u.X + y * u.Y + z * u.Z, pn), aV = Math.Atan2(x * v.X + y * v.Y + z * v.Z, pn);
        double gz = (aU + PI / 4) / B.d, gx = (aV + PI / 4) / B.d, gy = r - B.inner;
        PZ = (int)M.Clamp(Math.Floor(gz), 0, B.cols - 1);
        PX = (int)M.Clamp(Math.Floor(gx), 0, B.cols - 1);
        PY = (int)M.Clamp(Math.Floor(gy), 0, (double)B.rows * CS - 1);
        PFX = Math.Clamp(gx - PX, 0, 1); PFY = Math.Clamp(gy - PY, 0, 1); PFZ = Math.Clamp(gz - PZ, 0, 1);
        PF = f; return bi;
    }
    // chunk key: 7 bits band*6+face, 19 bits cx, 18 bits cy, 19 bits cz (enough for radius 10,000,000)
    public static long ck(int bi, int f, int cx, int cy, int cz) => ((((long)(bi * 6 + f) * 524288 + cx) * 262144 + cy) * 524288) + cz;
    // 64-bit mixing key for per-block tables (edits, water, cave nodes): block coordinates are too big to pack exactly
    public static long Mix(int bf, int x, int y, int z)
    {
        unchecked
        {
            ulong h = (ulong)(bf + 1) * 0x9E3779B97F4A7C15UL;
            h = (h ^ (ulong)(uint)x) * 0xC2B2AE3D27D4EB4FUL; h ^= h >> 29;
            h = (h ^ (ulong)(uint)z) * 0x165667B19E3779F9UL; h ^= h >> 32;
            h = (h ^ (ulong)(uint)y) * 0xD6E8FEB86659FD93UL; h ^= h >> 31;
            return (long)h;
        }
    }
    public static long ek() => Mix(NB * 6 + NF, NX, NY, NZ);
    public static long ekOf(int b, int f, int x, int y, int z) => Mix(b * 6 + f, x, y, z);

    /// <summary>Normalise block coords: wrap across face seams, move into the proper band when above/below. Result in NB..NZ.</summary>
    public static bool Norm(int bi, int f, int bx, int by, int bz)
    {
        NCV = 0; Band B = bands[bi];
        if (!Wrap(B, f, bx, bz)) return false;
        f = WF; bx = WX; bz = WZ;
        if (by < 0 || by >= B.rows)
        {
            V3 t = Gp(B, f, bz + .5, bx + .5, B.inner + by + .5);
            bi = P2b(t.X, t.Y, t.Z);
            if (bi < 0) { NCV = t.Len2 < CORE * CORE ? CoreAt(t.X, t.Y, t.Z) : 0; return false; }
            f = PF; bx = PX; by = PY; bz = PZ;
        }
        NB = bi; NF = f; NX = bx; NY = by; NZ = bz; return true;
    }
    public static int Blk(int bi, int f, int bx, int by, int bz)
    {
        if (!Norm(bi, f, bx, by, bz)) return NCV;
        long k = ck(NB, NF, NX >> 5, NY >> 5, NZ >> 5); Chunk ch;
        if (k == blkKey && blkCh != null) ch = blkCh; // one-entry cache: consecutive lookups almost always hit the same chunk
        else { chunkMap.TryGetValue(k, out ch); if (ch != null) { blkKey = k; blkCh = ch; } }
        if (ch != null && ch.d != null) return ch.d[(NX & 31) + 32 * ((NZ & 31) + 32 * (NY & 31))];
        ushort e; bool he; lock (EditLock) he = edits.TryGetValue(ek(), out e);
        if (he) return e;
        if (WorldSave.TryGetSaved(k, out var sd)) return sd[(NX & 31) + 32 * ((NZ & 31) + 32 * (NY & 31))]; // saved chunk that is not loaded
        return Gen(bands[NB], NF, NX, NY, NZ);
    }

    /// <summary>Block at a world position and the position inside it (block-local 0..1: x / z along the block grid, y up; world axes in the core).</summary>
    public static int LocalAt(double x, double y, double z, out double lx, out double ly, out double lz)
    {
        int bi = P2b(x, y, z);
        if (bi < 0) { lx = x - Math.Floor(x); ly = y - Math.Floor(y); lz = z - Math.Floor(z); return x * x + y * y + z * z < CORE * CORE ? CoreAt(x, y, z) : 0; }
        lx = PFX; ly = PFY; lz = PFZ; return Blk(bi, PF, PX, PY, PZ);
    }
    public static int GetAt(double x, double y, double z)
    {
        int bi = P2b(x, y, z);
        return bi < 0 ? (x * x + y * y + z * z < CORE * CORE ? CoreAt(x, y, z) : 0) : Blk(bi, PF, PX, PY, PZ);
    }
    public static void SetAt(double x, double y, double z, int id)
    {
        if (x * x + y * y + z * z < CORE * CORE) { CoreSet(x, y, z, id); return; }
        int bi = P2b(x, y, z); if (bi < 0) return;
        SetCell(bi, PF, PX, PY, PZ, id);
    }
    /// <summary>Sets a block and its state word (lvl: water level 1..8, stored as the water block's "level" state).</summary>
    /// <param name="background">re-mesh on the worker threads instead of this frame (the water simulation: hundreds of changes a second)</param>
    public static void SetCell(int bi, int f, int bx, int by, int bz, int id, int lvl = 0, int state = 0, bool background = false)
    {
        if (!Norm(bi, f, bx, by, bz)) return;
        bi = NB; f = NF; bx = NX; by = NY; bz = NZ;
        long kk = ek();
        long k = ck(bi, f, bx >> 5, by >> 5, bz >> 5);
        lock (EditLock) { edits[kk] = (ushort)id; ecount[k] = (ecount.TryGetValue(k, out int ec) ? ec : 0) + 1; }
        if (!chunks.ContainsKey(k)) lastScan = new V3(1e9, 0, 0); // chunk was skipped as deep solid: make the loader create it now
        if (id == Water && lvl > 0) state = WaterSim.LevelState(lvl);
        lock (EditLock) { if (state != 0) BlockStates.states[kk] = (ushort)state; else BlockStates.states.Remove(kk); }
        if (BlockStates.entities.Count > 0 && BlockStates.entities.TryGetValue(kk, out var be) && be.BlockId != id) BlockStates.entities.Remove(kk);
        if (chunks.TryGetValue(k, out var ch)) { Ensure(ch); ch.solid = false; int ci = (bx & 31) + 32 * ((bz & 31) + 32 * (by & 31)); ch.d[ci] = (ushort)id; BlockStates.Store(ch, ci, state); ch.ver++; if (background) ch.relight = true; else ch.dirty = true; }
        // neighbour chunks: one touching the edited cell shows a new face at once (dirty: meshed on the main thread this frame); one only
        // within skylight reach (8 blocks) just gets re-lit by a worker thread. Meshing them all on the main thread froze the game for
        // tens of ms per edit when placing / breaking fast.
        for (int d = 0; d < 6; d++)
        {
            bool touch = Norm(bi, f, bx + DX[d], by + DY[d], bz + DZ[d]) && ck(NB, NF, NX >> 5, NY >> 5, NZ >> 5) != k;
            if (Norm(bi, f, bx + DX[d] * 8, by + DY[d] * 8, bz + DZ[d] * 8))
            {
                long k2 = ck(NB, NF, NX >> 5, NY >> 5, NZ >> 5);
                if (!ecount.ContainsKey(k2)) { lock (EditLock) ecount[k2] = 0; if (!chunks.ContainsKey(k2)) lastScan = new V3(1e9, 0, 0); }
                if (chunks.TryGetValue(k2, out var nc) && k2 != k) { nc.ver++; if (touch && !background) nc.dirty = true; else nc.relight = true; }
            }
            if (touch && Norm(bi, f, bx + DX[d], by + DY[d], bz + DZ[d]) && chunks.TryGetValue(ck(NB, NF, NX >> 5, NY >> 5, NZ >> 5), out var tc)) { tc.ver++; if (background) tc.relight = true; else tc.dirty = true; }
        }
        Wake(bi, f, bx, by, bz);
        // a change in a river's / lake's water makes that part of it live (simulated, with inflow and outflow: Hydrology.Tick)
        if (Hydrology.Built && !Hydrology.Quiet) { var hc = ColsOf(bands[bi], f, bx >> 5, bz >> 5); int rc = hc.rc[(bx & 31) + 32 * (bz & 31)]; if (rc >= 0) Hydrology.Touch(rc); }
    }
}
