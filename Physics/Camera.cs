namespace CubePlanet;

/// <summary>Camera movement and orientation (player, free camera, orbit view) and the view-frustum test.</summary>
public static class Camera
{
    public static Cam C = new();
    public static double SENS = 1, FLYS = 20, spd = 1; // mouse sensitivity, fly speed, free-camera speed
    internal static double visTan = 1;

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

    public static void Zoom(double k) { C.d = Math.Min(R * 14, C.d * k); if (C.d < R + 60) SetOrbit(false); }
    public static void Drag(double dx, double dy, double s)
    {
        if (C.orbit) { C.lon -= dx * .004 * s; C.lat = M.Clamp(C.lat + dy * .004 * s, -1.5, 1.5); }
        else Turn(dx * .0025 * s * SENS, -dy * .0025 * s * SENS);
    }
}
