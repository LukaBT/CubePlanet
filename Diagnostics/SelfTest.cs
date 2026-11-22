namespace CubePlanet;

/// <summary>
/// Automated checks of game rules that need no window (--selftest; exit code 1 on failure), and the --showcase bench helper that places
/// example blocks in front of the bench camera so screenshots show them. Later phases add their tests here (multiblocks, containers, saving).
/// </summary>
public static class SelfTest
{
    static int fails, checks;
    static void Check(bool ok, string what) { checks++; if (!ok) { fails++; Console.WriteLine("FAIL " + what); } }

    public static int Run()
    {
        World.InitWorldData(1200, 1337); ChunkGenerator.mainTid = Environment.CurrentManagedThreadId;
        var (up, fwd, ground) = SpotInFront(6);

        // ---- slab: collision only in the lower half, selection too; placing on top goes into the cell above, not into the slab
        V3 slabAt = ground + up * .5;
        SetAt(slabAt.X, slabAt.Y, slabAt.Z, OakSlab);
        int id = LocalAt(slabAt.X, slabAt.Y, slabAt.Z, out _, out double ly, out _);
        Check(id == OakSlab, $"slab placed (got {BN[id]})");
        V3 lower = ground + up * .25, upper = ground + up * .75;
        Check(Collision.SolidAt(lower.X, lower.Y, lower.Z), "slab: lower half is solid");
        Check(!Collision.SolidAt(upper.X, upper.Y, upper.Z), "slab: upper half is free");
        // look straight down at the slab from 3 blocks above
        V3 eye = ground + up * 3.2; C.x = eye.X; C.y = eye.Y; C.z = eye.Z; C.f = -up; C.u = fwd;
        var hit = BlockEditing.Raycast(6);
        Check(hit != null && hit.id == OakSlab, $"raycast hits the slab (got {(hit == null ? "nothing" : BN[hit.id])})");
        if (hit != null)
        {
            Check(Vec(hit.x, hit.y, hit.z).Sub(ground).Dot(up) < .55, "raycast stops at the slab's top (0.5), not the cell top");
            V3 pc = Vec(hit.px, hit.py, hit.pz); int pid = LocalAt(pc.X, pc.Y, pc.Z, out _, out _, out _);
            Check(pid == 0 && pc.Sub(ground).Dot(up) > 1, "place position is the empty cell above the slab");
        }
        // ---- player dropped onto the slab lands on its top (0.5), not on the cell top (1.0)
        int pb = BlockAccess.P2b(slabAt.X, slabAt.Y, slabAt.Z); int pf = PF, pbx = PX, pby = PY, pbz = PZ; Band PB = bands[pb];
        for (int a = -1; a <= 1; a++) for (int c = -1; c <= 1; c++) { SetCell(pb, pf, pbx + a, pby, pbz + c, OakSlab); for (int k = 1; k <= 3; k++) SetCell(pb, pf, pbx + a, pby + k, pbz + c, 0); }
        V3 centre = Gp(PB, pf, pbz + .5, pbx + .5, PB.inner + pby); // bottom centre of the slab's cell
        P.on = true; P.ready = true; P.fly = false; P.core = false; P.vu = 0; P.vh = new V3(); P.p = centre + up * 2.5; ground = centre;
        for (int i = 0; i < 80; i++) PlayerPhysics.PlayerUpdate(.05, 0, 0, false, false, false);
        double feet = M.Dot(P.p - ground, up);
        Check(Math.Abs(feet - .5) < .06 && P.gnd, $"player stands on the slab top (feet at {feet:0.000}, on ground {P.gnd})");
        P.on = false;
        // ---- flower: no collision, selectable
        V3 fl = ground + up * .5 + fwd * 1; SetAt(fl.X, fl.Y, fl.Z, Poppy);
        Check(!Collision.SolidAt(fl.X, fl.Y, fl.Z), "flower has no collision");
        // ---- water: neither solid nor selectable
        V3 wa = ground + up * .5 + fwd * 2; SetAt(wa.X, wa.Y, wa.Z, Water);
        Check(!Collision.SolidAt(wa.X, wa.Y, wa.Z) && !Selects(Water, .5, .5, .5), "water: no collision, not selectable");
        // ---- stairs: back upper quarter solid, front upper quarter free
        V3 st = ground + up * .5 + fwd * 3; SetAt(st.X, st.Y, st.Z, OakStairs);
        // stairs boxes: the upper half covers z 0..0.5 (turning with the block's facing comes with block state in phase 4)
        Check(Collides(OakStairs, .5, .75, .25) && !Collides(OakStairs, .5, .75, .75), "stairs: upper box only at the back");
        Check(Collides(OakStairs, .5, .25, .75), "stairs: lower half solid");
        // ---- per-face textures and box meshing
        Check(TEXF[OakLog * 6 + 2] != TEXF[OakLog * 6 + 0], "oak log top texture differs from its side");
        Check(TEXF[OakLog * 6 + 3] == TEXF[OakLog * 6 + 2], "oak log bottom = top");
        int sb = BlockAccess.P2b(st.X, st.Y, st.Z);
        var chunk = new Chunk { key = ck(sb, PF, PX >> 5, PY >> 5, PZ >> 5), band = sb, face = PF, cx = PX >> 5, cy = PY >> 5, cz = PZ >> 5 };
        ChunkGenerator.Ensure(chunk);
        var r = Mesher.Build(chunk, out _);
        bool frac = false;
        if (r?.o != null)
            for (int k = 0; k < r.o.vb.Length / 16; k++) { int u = BitConverter.ToUInt16(r.o.vb, k * 16 + 10); int uu = u & 31, vv = (u >> 5) & 31; if (uu % 16 != 0 || vv % 16 != 0) { frac = true; break; } }
        Check(frac, "box faces get fractional texture coordinates");
        Console.WriteLine(fails == 0 ? $"SELFTEST OK ({checks} checks)" : $"SELFTEST FAILED: {fails} of {checks} checks");
        return fails == 0 ? 0 : 1;
    }

    /// <summary>Bench: a row of example blocks on the ground in front of the camera (slabs, stairs, fence posts, logs).</summary>
    public static void Showcase()
    {
        var (up, fwd, ground) = SpotInFront(7);
        V3 side = M.Nrm(M.Cross(fwd, up));
        int[] row = { OakSlab, StoneSlab, CobblestoneSlab, OakStairs, StoneBrickStairs, OakFence, OakFence, OakLog, BirchLog, SpruceLog, CherryLog };
        for (int i = 0; i < row.Length; i++)
        {
            V3 p = ground + side * ((i - row.Length / 2) * 1.0) + up * .5;
            p = Ground(p + up * 4, up) + up * .5;
            SetAt(p.X, p.Y, p.Z, row[i]);
            if (row[i] == OakLog || row[i] == BirchLog) { V3 q = p + up; SetAt(q.X, q.Y, q.Z, row[i]); }
        }
    }

    static (V3 up, V3 fwd, V3 ground) SpotInFront(double dist)
    {
        V3 c = new(C.x, C.y, C.z), up = M.Nrm(c);
        V3 fwd = C.f - up * M.Dot(C.f, up); if (fwd.Len < 1e-6) fwd = C.u - up * M.Dot(C.u, up); fwd = M.Nrm(fwd);
        return (up, fwd, Ground(c + fwd * dist, up));
    }
    /// <summary>Walks down from p until the cell below is solid; returns the bottom of the first free cell (on the ground).</summary>
    static V3 Ground(V3 p, V3 up)
    {
        for (int i = 0; i < 400; i++)
        {
            V3 below = p - up * .5; int b = GetAt(below.X, below.Y, below.Z);
            if (b != 0 && b != Water && SPC[b] == 0) { BlockAccess.P2b(p.X, p.Y, p.Z); return p - up * PFY; }
            p = p - up * .5;
        }
        return p;
    }
    static V3 Vec(double x, double y, double z) => new(x, y, z);
    static V3 Sub(this V3 a, V3 b) => a - b;
    static double Dot(this V3 a, V3 b) => M.Dot(a, b);
}
