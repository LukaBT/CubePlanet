namespace CubePlanet;

/// <summary>2D drawing primitives, text and item icons.</summary>
public static partial class UI
{
    // ---------- primitives ----------
    static void V(double x, double y, double u, double v, float r, float g, float b, float a, float mode, float layer)
    { vs.Add((float)x); vs.Add((float)y); vs.Add((float)u); vs.Add((float)v); vs.Add(r); vs.Add(g); vs.Add(b); vs.Add(a); vs.Add(mode); vs.Add(layer); }
    public static void Rect(double x, double y, double w, double h, float r, float g, float b, float a)
    {
        V(x, y, 0, 0, r, g, b, a, 0, 0); V(x + w, y, 0, 0, r, g, b, a, 0, 0); V(x + w, y + h, 0, 0, r, g, b, a, 0, 0);
        V(x, y, 0, 0, r, g, b, a, 0, 0); V(x + w, y + h, 0, 0, r, g, b, a, 0, 0); V(x, y + h, 0, 0, r, g, b, a, 0, 0);
    }
    public static void Tri(double x0, double y0, double x1, double y1, double x2, double y2, float r, float g, float b, float a)
    { V(x0, y0, 0, 0, r, g, b, a, 0, 0); V(x1, y1, 0, 0, r, g, b, a, 0, 0); V(x2, y2, 0, 0, r, g, b, a, 0, 0); }
    public static void Line(double x0, double y0, double x1, double y1, double w, float r, float g, float b, float a)
    {
        double dx = x1 - x0, dy = y1 - y0, l = Math.Sqrt(dx * dx + dy * dy); if (l < 1e-6) return; double nx = -dy / l * w / 2, ny = dx / l * w / 2;
        Tri(x0 + nx, y0 + ny, x1 + nx, y1 + ny, x1 - nx, y1 - ny, r, g, b, a); Tri(x0 + nx, y0 + ny, x1 - nx, y1 - ny, x0 - nx, y0 - ny, r, g, b, a);
    }
    public static void Circle(double cx, double cy, double rad, bool fill, double lw, float r, float g, float b, float a)
    {
        const int N = 20;
        for (int i = 0; i < N; i++)
        {
            double a0 = i * 2 * Math.PI / N, a1 = (i + 1) * 2 * Math.PI / N;
            if (fill) Tri(cx, cy, cx + Math.Cos(a0) * rad, cy + Math.Sin(a0) * rad, cx + Math.Cos(a1) * rad, cy + Math.Sin(a1) * rad, r, g, b, a);
            else Line(cx + Math.Cos(a0) * rad, cy + Math.Sin(a0) * rad, cx + Math.Cos(a1) * rad, cy + Math.Sin(a1) * rad, lw, r, g, b, a);
        }
    }
    public static double TextW(string s, double sc) => s.Length * GW * sc;
    public static void Text(string s, double x, double y, double sc, float r = 1, float g = 1, float b = 1, float a = 1, bool center = false)
    {
        if (center) x -= TextW(s, sc) / 2;
        foreach (char ch0 in s)
        {
            char ch = char.ToUpperInvariant(ch0); if (ch >= GN || !FONT.ContainsKey(ch)) ch = '?';
            double u0 = (double)(ch * GW) / (GW * GN), u1 = (double)(ch * GW + GW) / (GW * GN);
            double w = GW * sc, h = GH * sc;
            V(x, y, u0, 0, r, g, b, a, 1, 0); V(x + w, y, u1, 0, r, g, b, a, 1, 0); V(x + w, y + h, u1, 1, r, g, b, a, 1, 0);
            V(x, y, u0, 0, r, g, b, a, 1, 0); V(x + w, y + h, u1, 1, r, g, b, a, 1, 0); V(x, y + h, u0, 1, r, g, b, a, 1, 0);
            x += w;
        }
    }
    /// <summary>Block icon: texture row 0 is the bottom of the face, so v is flipped.</summary>
    public static void Icon(int id, double x, double y, double s)
    {
        Rect(x, y, s, s, 0, 0, 0, 1);
        V(x, y, 0, 1, 1, 1, 1, 1, 2, id); V(x + s, y, 1, 1, 1, 1, 1, 1, 2, id); V(x + s, y + s, 1, 0, 1, 1, 1, 1, 2, id);
        V(x, y, 0, 1, 1, 1, 1, 1, 2, id); V(x + s, y + s, 1, 0, 1, 1, 1, 1, 2, id); V(x, y + s, 0, 0, 1, 1, 1, 1, 2, id);
    }
    /// <summary>Bucket item icon (grey bucket with a water level showing how much it holds).</summary>
    public static void BucketIcon(double x, double y, double s)
    {
        double k = s / 16; Rect(x, y, s, s, 0, 0, 0, 1);
        void Rr(double rx, double ry, double rw, double rh, float r, float g, float b) => Rect(x + rx * k, y + ry * k, rw * k, rh * k, r, g, b, 1);
        Rr(3, 5, 10, 9, .604f, .627f, .651f); Rr(3, 5, 10, 1, .42f, .44f, .46f); Rr(3, 13, 10, 1, .42f, .44f, .46f); Rr(3, 5, 1, 9, .42f, .44f, .46f); Rr(12, 5, 1, 9, .42f, .44f, .46f);
        Rr(4, 6, 8, 7, .106f, .114f, .125f);
        int h = (int)M.JsRound(WaterSim.bk / 8.0 * 7); if (h > 0) { Rr(4, 13 - h, 8, h, .239f, .545f, 1f); Rr(4, 13 - h, 8, 1, .561f, .761f, 1f); }
    }
    static void ItemIcon(int id, double x, double y, double s) { if (id == WaterSim.BK) BucketIcon(x, y, s); else Icon(id, x, y, s); }
    static string ItemName(int id) => id == WaterSim.BK ? "bucket " + WaterSim.bk + "/8" : Blocks.BN[id].Replace('_', ' ');
}
