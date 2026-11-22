namespace CubePlanet;

/// <summary>Player movement: radial gravity, walking, swimming, flying, core movement.</summary>
public static class PlayerPhysics
{
    public static Plr P = new();
    public static bool sprint;

    // ---------- player physics (radial gravity, voxel collision) ----------
    public const double EYE = 1.62, GRAV = 30, JUMP = 9, HW = .24;
    /// <summary>Highest ledge the player walks up without jumping (stairs, slabs), like Minecraft's step height.</summary>
    public const double STEP = .6;

    // Gravity always points at the planet centre. Its strength follows the shell theorem for a uniform planet: inverse-square above the surface (R/r)^2, linear in depth below it (r/R)
    public static double GravK(double r) => r >= R ? (R / r) * (R / r) : r / R;
    internal static void UpdMode(V3 p)
    {
        double r = p.Len;
        if (P.core) { if (r > CORE + 1) P.core = false; } else if (r < CORE - 1) P.core = true;
        if (P.core)
        {
            V3 g = UpVec(p); double x = Math.Abs(g.X), y = Math.Abs(g.Y), z = Math.Abs(g.Z); int k = x <= y && x <= z ? 0 : y <= z ? 1 : 2;
            if (Math.Abs(g[P.rf]) > Math.Abs(g[k]) + .15) P.rf = k;
        }
    }
    internal static V3 UpVec(V3 p) { double r = p.Len; if (r < 3) return P.gv; return new V3(p.X / r, p.Y / r, p.Z / r); }

    public static void SyncCam()
    {
        double m = Math.Max(P.p.Len, 12), k = EYE / m; C.x = P.p.X + P.p.X * k; C.y = P.p.Y + P.p.Y * k; C.z = P.p.Z + P.p.Z * k;
    }
    public static void ToPlayer()
    {
        V3 g = M.Nrm(new V3(C.x, C.y, C.z)); P.p = new V3(C.x, C.y, C.z) - g * EYE; UpdMode(P.p); P.vh = new V3(); P.vu = 0; P.gnd = false;
        for (int i = 0; i < 400 && Pcol(P.p, HW); i++) { V3 q = M.Nrm(P.p); P.p = P.p + q * .5; }
        SyncCam();
    }
    public static void SetMode(bool on) { P.on = on; if (P.on && !C.orbit && P.ready) ToPlayer(); }

    // inside the Cartesian core walls are world-axis aligned but gravity is radial: move one world axis at a time (a blocked axis only cancels its own part -> sliding).
    internal static void CoreMove(double dt, V3 g, V3 vh, bool moving)
    {
        for (int i = 0; i < 10 && Pcol(P.p, HW); i++) P.p = P.p + g * .05;
        V3 Vv = vh + g * P.vu; double[] va = { Vv.X, Vv.Y, Vv.Z };
        for (int a = 0; a < 3; a++) { double dd = va[a] * dt; if (dd != 0 && !Mv(AXV[a], dd, false)) va[a] = 0; }
        Vv = new V3(va[0], va[1], va[2]);
        g = UpVec(P.p); P.gv = g; double vu = M.Dot(Vv, g); V3 h = Vv - g * vu;
        P.gnd = vu <= .5 && Pcol(P.p - g * .04, HW);
        P.vu = P.gnd && vu < 0 ? 0 : vu;
        if (P.gnd && !moving && h.Len < .6) h = new V3();
        P.vh = h;
    }
    internal static void PStep(double dt, double fw, double sd, bool jump, bool sneak, bool ctrl)
    {
        UpdMode(P.p); V3 g = UpVec(P.p); P.gv = g; double gs = GravK(P.p.Len);
        V3 Pj(V3 v) => v - g * M.Dot(v, g);
        V3 lf = Pj(C.f);
        if (lf.Len < .05) { lf = Pj(C.up); if (M.Dot(C.f, g) > 0) lf = -lf; }
        lf = M.Nrm(lf); V3 lr = M.Nrm(M.Cross(lf, g));
        V3 w = lf * fw + lr * sd; double wl = w.Len; if (wl > 1) w = w / wl;
        double sp = P.fly ? FLYS * (ctrl ? 3 : 1) : sneak ? 1.8 : (ctrl || sprint) && fw > 0 ? 5.8 : 4.3;
        if (P.wet && !P.fly) sp *= .55;
        double k = P.fly ? 8 : P.gnd ? 14 : P.wet ? 4 : 2.5, a = 1 - Math.Exp(-k * dt); V3 vh = Pj(P.vh);
        vh = vh + (w * sp - vh) * a;
        if (P.fly) P.vu += (((jump ? 1 : 0) - (sneak ? 1 : 0)) * FLYS * (ctrl ? 2 : 1) - P.vu) * (1 - Math.Exp(-8 * dt));
        else if (P.wet)
        {
            P.vu -= 7 * gs * dt; if (jump) P.vu += 24 * dt; P.vu *= Math.Exp(-2.2 * dt); P.vu = Math.Max(-5, Math.Min(5, P.vu));
            if (jump && !P.head) P.vu = Math.Max(P.vu, 6.5);
        }
        else { P.vu = Math.Max(-55, P.vu - GRAV * gs * dt); if (jump && P.gnd) P.vu = JUMP; }
        if (P.core) CoreMove(dt, g, vh, Math.Abs(fw) + Math.Abs(sd) > .01);
        else
        {
            var (tu, tv) = GridAx(P.p, g); Depenetrate(g, tu, tv); double uv = M.Dot(tu, tv), c = 1 - uv * uv, bu = M.Dot(vh, tu), bv = M.Dot(vh, tv), ca = (bu - uv * bv) / c, cb = (bv - uv * bu) / c;
            // velocity split along the block grid: a blocked axis only cancels its own part, so walking diagonally into a wall slides along it
            if (!StepMv(tu, ca * dt, g)) vh = vh - tu * ca;
            if (!StepMv(tv, cb * dt, g)) vh = vh - tv * cb;
            P.vh = vh; g = UpVec(P.p); P.gnd = false;
            if (!Mv(g, P.vu * dt, true)) { if (P.vu < 0) P.gnd = true; P.vu = 0; }
        }
        g = UpVec(P.p); P.gv = g;
        P.wet = Wet(GetAt(P.p.X + g.X * .9, P.p.Y + g.Y * .9, P.p.Z + g.Z * .9));
        P.head = Wet(GetAt(P.p.X + g.X * EYE, P.p.Y + g.Y * EYE, P.p.Z + g.Z * EYE));
    }
    /// <summary>
    /// Horizontal move that walks up low ledges: when blocked while standing on the ground (not flying / swimming), try the same move lifted
    /// by STEP; if that gets further, settle down onto the ledge. Full blocks (1 high) still stop the player.
    /// </summary>
    static bool StepMv(V3 ax, double dist, V3 g)
    {
        V3 start = P.p;
        if (Mv(ax, dist, false)) return true;
        if (!P.gnd || P.fly || P.wet || dist == 0) return false;
        V3 blocked = P.p;
        P.p = start;
        if (Pcol(P.p + g * STEP, HW)) { P.p = blocked; return false; } // no head room to step up
        P.p = P.p + g * STEP;
        bool free = Mv(ax, dist, false);
        if (M.Dot(P.p - blocked, ax) * Math.Sign(dist) < .01) { P.p = blocked; return false; } // the ledge is too high: no progress
        Mv(g, -STEP, true); // settle onto the step
        return free;
    }
    public static void PlayerUpdate(double dt, double fw, double sd, bool jump, bool sneak, bool ctrl)
    {
        int n = Math.Max(1, (int)Math.Ceiling(dt / .017)); double h = dt / n;
        for (int i = 0; i < n; i++) PStep(h, fw, sd, jump, sneak, ctrl);
        SyncCam();
    }
}
