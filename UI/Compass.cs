namespace CubePlanet;

/// <summary>Compass and UP / DOWN markers.</summary>
public static partial class UI
{
    // ---------- compass / UP-DOWN markers ----------
    static void NavMark(V3 d, string label, string sub, float[] col, double al)
    {
        double x = M.Dot(d, Camera.C.rt), y = M.Dot(d, Camera.C.up), z = M.Dot(d, Camera.C.f), t = Math.Tan(Camera.C.fov / 2); double sx = 0, sy = 0, ex = 0, ey = -1; bool on = false; double w = W, h = H;
        if (z > .02) { double px = x / (z * t * Camera.C.asp), py = y / (z * t); if (Math.Abs(px) < .9 && Math.Abs(py) < .88) { on = true; sx = w / 2 + px * w / 2; sy = h / 2 - py * h / 2; } }
        if (!on)
        {
            double l = Math.Sqrt(x * x + y * y); if (l > 1e-3) { ex = x / l; ey = y / l; }
            double mx_ = w / 2 - 46, my_ = h / 2 - 70, k = Math.Min(mx_ / (Math.Abs(ex) > 1e-6 ? Math.Abs(ex) : 1e-6), my_ / (Math.Abs(ey) > 1e-6 ? Math.Abs(ey) : 1e-6));
            sx = w / 2 + ex * k; sy = h / 2 - ey * k;
        }
        float r = col[0], g = col[1], b = col[2], a = (float)al;
        if (on) { Circle(sx, sy, 9 * S * .8, false, 2, r, g, b, a); Circle(sx, sy, 2.5 * S * .8, true, 0, r, g, b, a); }
        else
        {
            double ang = Math.Atan2(-ey, ex), c = Math.Cos(ang), s = Math.Sin(ang);
            (double, double) T(double px, double py) => (sx + px * c - py * s, sy + px * s + py * c);
            var p0 = T(12 * S * .8, 0); var p1 = T(-8 * S * .8, -9 * S * .8); var p2 = T(-4 * S * .8, 0); var p3 = T(-8 * S * .8, 9 * S * .8);
            Tri(p0.Item1, p0.Item2, p1.Item1, p1.Item2, p2.Item1, p2.Item2, r, g, b, a); Tri(p0.Item1, p0.Item2, p2.Item1, p2.Item2, p3.Item1, p3.Item2, r, g, b, a);
        }
        double ly = sy + (on ? 22 * S * .8 : (sy > h / 2 ? -24 * S : 24 * S));
        Text(label, sx, ly, S * 1.1, r, g, b, a, true); Text(sub, sx, ly + (sy > h / 2 && !on ? -14 * S : 14 * S), S * .8, r, g, b, a * .8f, true);
    }
    static readonly string[] BN8 = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
    static void DrawNav()
    {
        var C = Camera.C; if (C.orbit || !World.Started) return;
        double r = Math.Sqrt(C.x * C.x + C.y * C.y + C.z * C.z); if (r < .5) return; V3 g = new(C.x / r, C.y / r, C.z / r);
        if (Camera.CoreView())
        {
            double al = Math.Min(1, r / 6);
            NavMark(g, "UP", "surface " + Math.Max(0, Math.Round(World.R - r)), new[] { .56f, .89f, .53f }, al);
            NavMark(-g, "DOWN", "centre " + Math.Round(r), new[] { 1f, .7f, .42f }, al);
            if (r < 4) Text("PLANET CENTRE - WEIGHTLESS", W / 2.0, 34, S * 1.2, 1, 1, 1, 1, true);
            return;
        }
        double d = g.Y; V3 n = new(-g.X * d, 1 - g.Y * d, -g.Z * d); double nl = n.Len, cy = 34 * S * .7;
        if (nl < .02) { Text("POLE", W / 2.0, cy, S * 1.2, 1, 1, 1, 1, true); return; }
        n = n / nl; V3 e = M.Cross(n, g); double hd = ((Math.Atan2(M.Dot(C.f, e), M.Dot(C.f, n)) * 180 / Math.PI) + 360) % 360, sc = Math.Min(4, W * .42 / 80) * S * .6;
        Rect(W / 2.0 - 80 * sc, cy - 15 * S * .6, 160 * sc, 30 * S * .6, 0, 0, 0, .28f);
        for (int b = 0; b < 360; b += 15)
        {
            double dl = ((b - hd + 540) % 360) - 180; if (Math.Abs(dl) > 80) continue; double x = W / 2.0 + dl * sc, al = 1 - Math.Abs(dl) / 95;
            float[] col = b == 0 ? new[] { 1f, .42f, .35f } : new[] { 1f, 1f, 1f };
            if (b % 45 == 0) Text(BN8[b / 45], x, cy - 4 * S, b % 90 == 0 ? S * 1.2 : S * .9, col[0], col[1], col[2], (float)al, true); else Rect(x - .5, cy - 4 * S * .6, 1, 8 * S * .6, 1, 1, 1, (float)al);
        }
        Tri(W / 2.0, cy + 17 * S * .6, W / 2.0 - 5 * S * .6, cy + 25 * S * .6, W / 2.0 + 5 * S * .6, cy + 25 * S * .6, 1, 1, 1, 1);
        Text(BN8[(int)Math.Round(hd / 45) % 8] + " " + Math.Round(hd) + "\u00B0", W / 2.0, cy + 30 * S * .6, S * .9, 1, 1, 1, 1, true);
    }
}
