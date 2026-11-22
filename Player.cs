using static CubePlanet.Cube;
using static CubePlanet.Blocks;

namespace CubePlanet;

public class Cam
{
    public double x, y, z; public V3 f = new(0, 0, -1), u = new(0, 1, 0), rt = new(1, 0, 0), up = new(0, 1, 0);
    public bool orbit, lvl; public double lon, lat = .4, d, fov = PI / 3, near = .1, far = 1000, asp = 1;
    public float[] vp = new float[16];
    const double PI = Math.PI;
}
public class Plr
{
    public bool on = true, fly, gnd, wet, head, ready, core; public V3 p, vh, gv = new(0, 1, 0); public double vu; public int rf;
}
public class RayHit { public double x, y, z, px, py, pz; public bool core; public int id, ci, cj, ck, bi, f, bx, by, bz; }

public static partial class W
{
    public static Cam C = new();
    public static Plr P = new();
    public static double SENS = 1, FLYS = 20, spd = 1;
    public static bool sprint;
    static double visTan = 1;
    public static int sel = 2, slot = 2;

    // ---------- camera ----------
    public static bool CoreView() => P.on && P.ready ? P.core : Math.Sqrt(C.x * C.x + C.y * C.y + C.z * C.z) < CORE;
    public static void Turn(double yaw, double pitch)
    {
        C.f = M.Rot(C.f, C.u, -yaw); V3 r = M.Nrm(M.Cross(C.f, C.u));
        if (!C.orbit && CoreView()) // inside the core there is no fixed horizon: look anywhere, pitch is not clamped
        {
            C.f = M.Nrm(M.Rot(C.f, r, pitch)); C.u = M.Nrm(M.Rot(C.u, r, pitch));
            double dd = M.Dot(C.u, C.f); C.u = M.Nrm(C.u - C.f * dd); return;
        }
        V3 p = M.Rot(C.f, r, pitch); if (Math.Abs(M.Dot(p, C.u)) < .995) C.f = p; C.f = M.Nrm(C.f);
    }
    public static void CamMove(double fw, double sd, double up, bool level)
    {
        V3 f = C.f;
        if (level) { f = C.f - C.u * M.Dot(C.f, C.u); if (f.Len < 1e-6) f = C.f; f = M.Nrm(f); }
        V3 r = M.Nrm(M.Cross(C.f, C.u)); V3 mv = f * fw + r * sd + C.u * up;
        C.x += mv.X; C.y += mv.Y; C.z += mv.Z;
    }
    public static void SetOrbit(bool on)
    {
        if (on)
        {
            double r = Math.Sqrt(C.x * C.x + C.y * C.y + C.z * C.z); V3 n = new(C.x / r, C.y / r, C.z / r);
            C.lat = Math.Asin(M.Clamp(n.Y, -1, 1)); C.lon = Math.Atan2(n.X, n.Z); C.d = Math.Max(R * 3, r); C.orbit = true;
        }
        else
        {
            double cl = Math.Cos(C.lat); V3 n = new(cl * Math.Sin(C.lon), Math.Sin(C.lat), cl * Math.Cos(C.lon)); double p = R + 40;
            C.x = n.X * p; C.y = n.Y * p; C.z = n.Z * p; C.u = n;
            V3 t = new V3(0, 1, 0) - n * n.Y; if (t.Len < .01) t = new V3(0, 0, 1) - n * n.Z;
            C.f = M.Nrm(M.Nrm(t) * .95 - n * .3); C.orbit = false;
            if (P.on && P.ready) ToPlayer();
        }
    }
    public static void CamUpdate(double dt)
    {
        double r = Math.Sqrt(C.x * C.x + C.y * C.y + C.z * C.z), alt = r - R;
        if (C.orbit)
        {
            double cl = Math.Cos(C.lat); V3 n = new(cl * Math.Sin(C.lon), Math.Sin(C.lat), cl * Math.Cos(C.lon));
            C.x = n.X * C.d; C.y = n.Y * C.d; C.z = n.Z * C.d; C.f = -n; C.u = new V3(0, 1, 0);
            C.near = Math.Max(1, (C.d - R) * .05); C.far = C.d + R * 1.5;
        }
        else
        {
            V3? gT = null;
            if (P.on && P.ready) { if (!P.core) gT = P.gv; }
            else if (r >= CORE) gT = new V3(C.x / r, C.y / r, C.z / r);
            if (gT.HasValue)
            {
                V3 g = gT.Value; double ang = Math.Acos(M.Clamp(M.Dot(C.u, g), -1, 1));
                if (ang > 1e-9)
                {
                    V3 ax = M.Cross(C.u, g); if (M.Dot(ax, ax) < 1e-18) ax = M.Cross(C.f, C.u); ax = M.Nrm(ax);
                    double st = ang < 1e-4 ? ang : ang * Math.Min(1, dt * 12);
                    C.f = M.Nrm(M.Rot(C.f, ax, st)); C.u = M.Nrm(M.Rot(C.u, ax, st));
                }
            }
            C.near = P.on ? (P.fly ? .2 : .1) : Math.Max(.1, Math.Abs(alt) * .015); if (r < CORE + 40) C.near = .1; C.far = r + R + 100;
        }
        C.rt = M.Nrm(M.Cross(C.f, C.u)); C.up = M.Cross(C.rt, C.f);
        visTan = Math.Tan(C.fov / 2);
        C.vp = M.Mul(M.VkPersp(C.fov, C.asp, C.near), M.Look(C.f, C.u));
    }
    public static bool Vis(double wx, double wy, double wz, double rad)
    {
        V3 rel = new(wx - C.x, wy - C.y, wz - C.z); double dp = M.Dot(rel, C.f);
        if (dp < -rad || dp > C.far + rad) return false;
        double th = visTan, sl = rad * 2, d = Math.Max(dp, 0);
        return Math.Abs(M.Dot(rel, C.up)) <= d * th + sl && Math.Abs(M.Dot(rel, C.rt)) <= d * th * C.asp + sl;
    }

    // ---------- editing ----------
    /// <summary>March along the view ray; returns the first solid block hit plus the empty cell just before it.</summary>
    public static RayHit Raycast(double reach)
    {
        double px = C.x, py = C.y, pz = C.z;
        for (double t = 0; t <= reach; t += .07)
        {
            double x = C.x + C.f.X * t, y = C.y + C.f.Y * t, z = C.z + C.f.Z * t;
            if (x * x + y * y + z * z < CORE * CORE)
            {
                int id = CoreAt(x, y, z);
                if (id != 0 && id != 9) return new RayHit { x = x, y = y, z = z, px = px, py = py, pz = pz, core = true, id = id, ci = (int)Math.Floor(x), cj = (int)Math.Floor(y), ck = (int)Math.Floor(z) };
                px = x; py = y; pz = z; continue;
            }
            int bi = P2b(x, y, z);
            if (bi >= 0)
            {
                int f = PF, bx = PX, by = PY, bz = PZ, hb = Blk(bi, f, bx, by, bz);
                if (hb != 0 && hb != 9) return new RayHit { x = x, y = y, z = z, px = px, py = py, pz = pz, bi = bi, f = f, bx = bx, by = by, bz = bz };
            }
            px = x; py = y; pz = z;
        }
        return null;
    }
    public static void Edit(bool brk, bool plc)
    {
        if (sel == BK) { BucketUse(brk, plc); return; }
        var T = Raycast(P.on ? 6 : 10); if (T == null) return;
        if (brk)
        {
            int old0 = GetAt(T.x, T.y, T.z);
            SetAt(T.x, T.y, T.z, 0);
            if (UWP[old0]) PlaceWater(T.x, T.y, T.z); // underwater plants leave water behind, not an air pocket
            V3 g = M.Nrm(new V3(T.x, T.y, T.z));
            for (int k = 1; k < 6; k++)
            {
                double X = T.x + g.X * k, Y = T.y + g.Y * k, Z = T.z + g.Z * k; int b = GetAt(X, Y, Z);
                if (SPC[b] != 0 && b != 51 && b != 52) { SetAt(X, Y, Z, 0); if (UWP[b]) PlaceWater(X, Y, Z); } else break;
            }
        }
        else if (plc)
        {
            bool rp = T.core ? SPC[T.id] != 0 : SPC[Blk(T.bi, T.f, T.bx, T.by, T.bz)] != 0;
            double X = rp ? T.x : T.px, Y = rp ? T.y : T.py, Z = rp ? T.z : T.pz;
            if ((X - C.x) * (X - C.x) + (Y - C.y) * (Y - C.y) + (Z - C.z) * (Z - C.z) > 1)
            {
                int prev = GetAt(X, Y, Z); if (sel == 9) PlaceWater(X, Y, Z); else SetAt(X, Y, Z, sel);
                if (P.on && Pcol(P.p, HW)) SetAt(X, Y, Z, prev);
            }
        }
    }
    public static void PickBlock()
    {
        var T = Raycast(P.on ? 6 : 10); if (T == null) return;
        int id = T.core ? T.id : Blk(T.bi, T.f, T.bx, T.by, T.bz);
        if (id == 0 || id == 9) return;
        UI.SetSlotBlock(id);
    }

    // ---------- player physics (radial gravity, voxel collision) ----------
    public const double EYE = 1.62, GRAV = 30, JUMP = 9, HW = .24;
    static readonly double[] PH = { .001, .6, 1.2, 1.75 };
    static readonly V3[] AXV = { new(1, 0, 0), new(0, 1, 0), new(0, 0, 1) };
    static readonly int[][] HOF = { new[] { -1, -1 }, new[] { 0, -1 }, new[] { 1, -1 }, new[] { -1, 0 }, new[] { 1, 0 }, new[] { -1, 1 }, new[] { 0, 1 }, new[] { 1, 1 } };
    static readonly double[] RCS = Enumerable.Range(0, 12).Select(k => Math.Cos(k * PI / 6)).ToArray(), RSN = Enumerable.Range(0, 12).Select(k => Math.Sin(k * PI / 6)).ToArray();

    static bool SolidAt(double x, double y, double z) { int b = GetAt(x, y, z); return b != 0 && b != 9 && SPC[b] == 0; }
    // Gravity always points at the planet centre. Its strength follows the shell theorem for a uniform planet: inverse-square above the surface (R/r)^2, linear in depth below it (r/R)
    public static double GravK(double r) => r >= R ? (R / r) * (R / r) : r / R;
    static void UpdMode(V3 p)
    {
        double r = p.Len;
        if (P.core) { if (r > CORE + 1) P.core = false; } else if (r < CORE - 1) P.core = true;
        if (P.core)
        {
            V3 g = UpVec(p); double x = Math.Abs(g.X), y = Math.Abs(g.Y), z = Math.Abs(g.Z); int k = x <= y && x <= z ? 0 : y <= z ? 1 : 2;
            if (Math.Abs(g[P.rf]) > Math.Abs(g[k]) + .15) P.rf = k;
        }
    }
    static V3 UpVec(V3 p) { double r = p.Len; if (r < 3) return P.gv; return new V3(p.X / r, p.Y / r, p.Z / r); }
    static (V3, V3) CoreFrame(V3 g)
    {
        V3 a = AXV[P.rf]; double d = M.Dot(a, g); V3 e1 = M.Nrm(a - g * d); return (e1, M.Cross(g, e1));
    }
    // unit tangent vectors along the two block-grid directions at p
    public static (V3, V3) GridAx(V3 p, V3 g)
    {
        int f = FaceOf(p.X, p.Y, p.Z); V3 u = U[f], v = V[f]; double du = M.Dot(u, g), dv = M.Dot(v, g);
        return (M.Nrm(u - g * du), M.Nrm(v - g * dv));
    }
    public static bool Pcol(V3 p, double hw)
    {
        V3 g = UpVec(p);
        if (P.core)
        {
            var (e1, e2) = CoreFrame(g); double rr = hw * 1.12; // round hitbox
            for (int k = 0; k < 12; k++)
            {
                double s1 = RCS[k] * rr, s2 = RSN[k] * rr; V3 b = p + e1 * s1 + e2 * s2;
                for (int i = 0; i < 4; i++) { double h = PH[i]; if (SolidAt(b.X + g.X * h, b.Y + g.Y * h, b.Z + g.Z * h)) return true; }
            }
            return false;
        }
        var (a1, a2) = GridAx(p, g);
        for (int k = 0; k < 8; k++)
        {
            double s1 = HOF[k][0] * hw, s2 = HOF[k][1] * hw; V3 b = p + a1 * s1 + a2 * s2;
            for (int i = 0; i < 4; i++) { double h = PH[i]; if (SolidAt(b.X + g.X * h, b.Y + g.Y * h, b.Z + g.Z * h)) return true; }
        }
        return false;
    }
    // move along a unit axis by dist, stopping at (and refining to) the first contact; false if blocked.
    // vert: vertical moves use a hitbox only a hair slimmer than horizontal ones (horizontal moves leave a .01 skin gap, so standing flush
    // against a wall never sticks). It used to be .03 slimmer, which let the player fall into gaps the walking box doesn't fit through
    // (digging down while standing near the block's edge, falling past a ledge) and end up inside the walls, unable to move.
    static bool Mv(V3 ax, double dist, bool vert)
    {
        double hw = vert ? HW - .004 : HW; int n = (int)Math.Ceiling(Math.Abs(dist) / .3); if (n == 0) return true; double st = dist / n;
        for (int i = 0; i < n; i++)
        {
            V3 np = P.p + ax * st;
            if (!Pcol(np, hw)) { P.p = np; continue; }
            double lo = 0, hi = st;
            for (int k = 0; k < 7; k++) { double m = (lo + hi) / 2; if (Pcol(P.p + ax * m, hw)) hi = m; else lo = m; }
            if (!vert)
            {
                double c = lo - Math.Sign(st) * .01; V3 qq = P.p + ax * c;
                if (!Pcol(qq, hw)) { P.p = qq; return false; }
            }
            P.p = P.p + ax * lo; return false;
        }
        return true;
    }
    /// <summary>
    /// Safety net: if the player overlaps a block (a chunk finished loading around them, a block edit, round-off on the curved grid),
    /// push them out by the smallest move that frees them: sideways along the block grid first, then straight up.
    /// </summary>
    static void Depenetrate(V3 g, V3 tu, V3 tv)
    {
        if (!Pcol(P.p, HW)) return;
        V3[] dirs = { tu, -tu, tv, -tv, M.Nrm(tu + tv), M.Nrm(tu - tv), M.Nrm(tv - tu), M.Nrm(-tu - tv) };
        foreach (double d in new[] { .015, .03, .05, .08, .12, .18, .26, .36, .5, .7, .95, 1.2 })
            foreach (var dir in dirs) { V3 q = P.p + dir * d; if (!Pcol(q, HW)) { P.p = q; return; } }
        // buried deeper (e.g. a tree finished loading around you): climb out on top
        for (double up = .05; up <= 16; up += up < 2 ? .05 : .25) { V3 q = P.p + g * up; if (!Pcol(q, HW)) { P.p = q; P.vu = Math.Max(P.vu, 0); return; } }
    }
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
    static void CoreMove(double dt, V3 g, V3 vh, bool moving)
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
    static void PStep(double dt, double fw, double sd, bool jump, bool sneak, bool ctrl)
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
            if (!Mv(tu, ca * dt, false)) vh = vh - tu * ca;
            if (!Mv(tv, cb * dt, false)) vh = vh - tv * cb;
            P.vh = vh; g = UpVec(P.p); P.gnd = false;
            if (!Mv(g, P.vu * dt, true)) { if (P.vu < 0) P.gnd = true; P.vu = 0; }
        }
        g = UpVec(P.p); P.gv = g;
        P.wet = Wet(GetAt(P.p.X + g.X * .9, P.p.Y + g.Y * .9, P.p.Z + g.Z * .9));
        P.head = Wet(GetAt(P.p.X + g.X * EYE, P.p.Y + g.Y * EYE, P.p.Z + g.Z * EYE));
    }
    public static void PlayerUpdate(double dt, double fw, double sd, bool jump, bool sneak, bool ctrl)
    {
        int n = Math.Max(1, (int)Math.Ceiling(dt / .017)); double h = dt / n;
        for (int i = 0; i < n; i++) PStep(h, fw, sd, jump, sneak, ctrl);
        SyncCam();
    }
}
