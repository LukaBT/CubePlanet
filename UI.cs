using Silk.NET.Vulkan;
using static CubePlanet.Blocks;
using W_ = CubePlanet.W;

namespace CubePlanet;

/// <summary>Minimal immediate-mode 2D layer: coloured quads, 5x7 bitmap text, block icons, plus the game's HUD / hotbar / palette / settings / compass.</summary>
public static class UI
{
    // ---------- font (5x7, column bytes, LSB = top row) ----------
    static readonly Dictionary<char, byte[]> FONT = new()
    {
        [' '] = new byte[] { 0, 0, 0, 0, 0 }, ['!'] = new byte[] { 0, 0, 0x5F, 0, 0 }, ['#'] = new byte[] { 0x14, 0x7F, 0x14, 0x7F, 0x14 }, ['%'] = new byte[] { 0x23, 0x13, 0x08, 0x64, 0x62 },
        ['\''] = new byte[] { 0, 0x05, 0x03, 0, 0 }, ['('] = new byte[] { 0, 0x1C, 0x22, 0x41, 0 }, [')'] = new byte[] { 0, 0x41, 0x22, 0x1C, 0 }, ['*'] = new byte[] { 0x14, 0x08, 0x3E, 0x08, 0x14 },
        ['+'] = new byte[] { 0x08, 0x08, 0x3E, 0x08, 0x08 }, [','] = new byte[] { 0, 0x50, 0x30, 0, 0 }, ['-'] = new byte[] { 0x08, 0x08, 0x08, 0x08, 0x08 }, ['.'] = new byte[] { 0, 0x60, 0x60, 0, 0 },
        ['/'] = new byte[] { 0x20, 0x10, 0x08, 0x04, 0x02 }, ['0'] = new byte[] { 0x3E, 0x51, 0x49, 0x45, 0x3E }, ['1'] = new byte[] { 0, 0x42, 0x7F, 0x40, 0 }, ['2'] = new byte[] { 0x42, 0x61, 0x51, 0x49, 0x46 },
        ['3'] = new byte[] { 0x21, 0x41, 0x45, 0x4B, 0x31 }, ['4'] = new byte[] { 0x18, 0x14, 0x12, 0x7F, 0x10 }, ['5'] = new byte[] { 0x27, 0x45, 0x45, 0x45, 0x39 }, ['6'] = new byte[] { 0x3C, 0x4A, 0x49, 0x49, 0x30 },
        ['7'] = new byte[] { 0x01, 0x71, 0x09, 0x05, 0x03 }, ['8'] = new byte[] { 0x36, 0x49, 0x49, 0x49, 0x36 }, ['9'] = new byte[] { 0x06, 0x49, 0x49, 0x29, 0x1E }, [':'] = new byte[] { 0, 0x36, 0x36, 0, 0 },
        ['<'] = new byte[] { 0x08, 0x14, 0x22, 0x41, 0 }, ['='] = new byte[] { 0x14, 0x14, 0x14, 0x14, 0x14 }, ['>'] = new byte[] { 0, 0x41, 0x22, 0x14, 0x08 }, ['?'] = new byte[] { 0x02, 0x01, 0x51, 0x09, 0x06 },
        ['A'] = new byte[] { 0x7E, 0x11, 0x11, 0x11, 0x7E }, ['B'] = new byte[] { 0x7F, 0x49, 0x49, 0x49, 0x36 }, ['C'] = new byte[] { 0x3E, 0x41, 0x41, 0x41, 0x22 }, ['D'] = new byte[] { 0x7F, 0x41, 0x41, 0x22, 0x1C },
        ['E'] = new byte[] { 0x7F, 0x49, 0x49, 0x49, 0x41 }, ['F'] = new byte[] { 0x7F, 0x09, 0x09, 0x09, 0x01 }, ['G'] = new byte[] { 0x3E, 0x41, 0x49, 0x49, 0x7A }, ['H'] = new byte[] { 0x7F, 0x08, 0x08, 0x08, 0x7F },
        ['I'] = new byte[] { 0, 0x41, 0x7F, 0x41, 0 }, ['J'] = new byte[] { 0x20, 0x40, 0x41, 0x3F, 0x01 }, ['K'] = new byte[] { 0x7F, 0x08, 0x14, 0x22, 0x41 }, ['L'] = new byte[] { 0x7F, 0x40, 0x40, 0x40, 0x40 },
        ['M'] = new byte[] { 0x7F, 0x02, 0x0C, 0x02, 0x7F }, ['N'] = new byte[] { 0x7F, 0x04, 0x08, 0x10, 0x7F }, ['O'] = new byte[] { 0x3E, 0x41, 0x41, 0x41, 0x3E }, ['P'] = new byte[] { 0x7F, 0x09, 0x09, 0x09, 0x06 },
        ['Q'] = new byte[] { 0x3E, 0x41, 0x51, 0x21, 0x5E }, ['R'] = new byte[] { 0x7F, 0x09, 0x19, 0x29, 0x46 }, ['S'] = new byte[] { 0x46, 0x49, 0x49, 0x49, 0x31 }, ['T'] = new byte[] { 0x01, 0x01, 0x7F, 0x01, 0x01 },
        ['U'] = new byte[] { 0x3F, 0x40, 0x40, 0x40, 0x3F }, ['V'] = new byte[] { 0x1F, 0x20, 0x40, 0x20, 0x1F }, ['W'] = new byte[] { 0x3F, 0x40, 0x38, 0x40, 0x3F }, ['X'] = new byte[] { 0x63, 0x14, 0x08, 0x14, 0x63 },
        ['Y'] = new byte[] { 0x07, 0x08, 0x70, 0x08, 0x07 }, ['Z'] = new byte[] { 0x61, 0x51, 0x49, 0x45, 0x43 }, ['['] = new byte[] { 0, 0x7F, 0x41, 0x41, 0 }, [']'] = new byte[] { 0, 0x41, 0x41, 0x7F, 0 },
        ['_'] = new byte[] { 0x40, 0x40, 0x40, 0x40, 0x40 }, ['\u00B0'] = new byte[] { 0x06, 0x09, 0x09, 0x06, 0 },
    };
    const int GW = 6, GH = 8, GN = 128;

    static Pipe pipe; static readonly List<float> vs = new();
    public static int W, H, S = 1; // screen size, integer UI scale

    // ---------- input state fed by the window ----------
    public static double mx, my, scroll; public static bool down, clicked, rclicked;
    public static bool settingsOpen, invOpen, startScreen = true; public static string toast = ""; public static double toastT;
    public static string radiusStr = "1200", seedStr = "1337"; public static int field;

    public static readonly int[] HB = { 1, 2, 3, 4, 5, 6, 7, 8, W_.BK, 9 };

    public static void Init()
    {
        pipe = Gpu.MakePipe("ui", @"layout(location=0)in vec2 aP;layout(location=1)in vec2 aUv;layout(location=2)in vec4 aC;layout(location=3)in vec2 aM;
out vec2 vUv;out vec4 vC;flat out vec2 vM;
void main(){vec2 p=aP/uScr*2.-1.;gl_Position=vec4(p.x,p.y,0.,1.);vUv=aUv;vC=aC;vM=aM;}",
@"in vec2 vUv;in vec4 vC;flat in vec2 vM;out vec4 o;
void main(){vec4 c=vC;if(vM.x>1.5){vec4 t=texture(uT,vec3(vUv,vM.y));c*=t;}else if(vM.x>.5){c.a*=texture(uFont,vUv).r;}o=c;}",
            new PipeCfg { layout = VL.UI, pass = RP.Final, depthTest = false, depthWrite = false, blend = BlendMode.Alpha });
        var px = new byte[GW * GN * GH];
        foreach (var kv in FONT)
        {
            int cell = kv.Key; if (cell >= GN) continue;
            for (int c = 0; c < 5; c++) for (int r = 0; r < 7; r++) if (((kv.Value[c] >> r) & 1) != 0) px[r * (GW * GN) + cell * GW + c] = 255;
        }
        Gpu.texFont = Gpu.MakeTexture(ImageType.Type2D, ImageViewType.Type2D, Format.R8Unorm, 1, GW * GN, GH, 1, 1, new[] { px }, Filter.Nearest, Filter.Nearest, SamplerMipmapMode.Nearest, SamplerAddressMode.ClampToEdge);
    }

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
        int h = (int)M.JsRound(W_.bk / 8.0 * 7); if (h > 0) { Rr(4, 13 - h, 8, h, .239f, .545f, 1f); Rr(4, 13 - h, 8, 1, .561f, .761f, 1f); }
    }
    static void ItemIcon(int id, double x, double y, double s) { if (id == W_.BK) BucketIcon(x, y, s); else Icon(id, x, y, s); }
    static string ItemName(int id) => id == W_.BK ? "bucket " + W_.bk + "/8" : Blocks.BN[id].Replace('_', ' ');
    public static bool In(double x, double y, double w, double h) => mx >= x && mx < x + w && my >= y && my < y + h;

    public static void Begin(int w, int h) { W = w; H = h; S = Math.Max(1, (int)Math.Round(h / 600.0)); vs.Clear(); }
    public static void End()
    {
        if (vs.Count == 0) return;
        // drawn into the main render pass that W.Render (or the start screen) opened
        Gpu.UboSet(60, W); Gpu.UboSet(61, H); Gpu.Bind(pipe);
        Gpu.DrawDynamic(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(vs), (uint)(vs.Count / 10));
    }

    // ---------- hotbar / palette ----------
    public static void SetSlotBlock(int id)
    {
        int i = Array.IndexOf(HB, id);
        if (i >= 0) Pick(i + 1); else { HB[W_.slot - 1] = id; W_.sel = id; }
    }
    public static void Pick(int i) { W_.slot = i; W_.sel = HB[i - 1]; }
    public static void Note(string msg, double secs = 5) { toast = msg; toastT = secs; }

    static void DrawHotbar()
    {
        int s = S, isz = 30 * s, gap = 3 * s, bd = 2 * s, tot = 10 * (isz + 2 * bd) + 9 * gap; double x0 = (W - tot) / 2.0, y0 = H - 6 * s - isz - 2 * bd;
        for (int i = 0; i < 10; i++)
        {
            double x = x0 + i * (isz + 2 * bd + gap); bool sel = i == W_.slot - 1;
            Rect(x, y0, isz + 2 * bd, isz + 2 * bd, 1, 1, 1, sel ? 1 : .27f);
            ItemIcon(HB[i], x + bd, y0 + bd, isz);
            if (In(x, y0, isz + 2 * bd, isz + 2 * bd)) Text(ItemName(HB[i]), mx + 12, my - 14 * s, s, 1, 1, 1);
            if (clicked && In(x, y0, isz + 2 * bd, isz + 2 * bd)) { Pick(i + 1); clicked = false; }
        }
    }
    static void DrawInventory()
    {
        int s = S, cell = 36 * s + 4 * s, cols = Math.Max(4, Math.Min(16, (int)((W * .92) / cell))); int rows = (W_.NT + cols - 1) / cols;
        double pw = cols * cell + 12 * s, ph = rows * cell + 12 * s + 14 * s, x0 = (W - pw) / 2, y0 = (H - ph) / 2;
        Rect(x0, y0, pw, ph, 0, 0, 0, .85f); Text("BLOCK PALETTE (E TO CLOSE) - CLICK TO ASSIGN TO SELECTED SLOT", x0 + 6 * s, y0 + 4 * s, s * .8, .8f, .9f, 1);
        for (int id = 1; id < W_.NT; id++)
        {
            int k = id - 1; double x = x0 + 6 * s + (k % cols) * cell, y = y0 + 16 * s + (k / cols) * cell;
            bool hov = In(x, y, 36 * s, 36 * s); if (hov) Rect(x - 2 * s, y - 2 * s, 40 * s, 40 * s, 1, 1, 1, .5f);
            Icon(id, x, y, 36 * s);
            if (hov && clicked) { HB[W_.slot - 1] = id; W_.sel = id; invOpen = false; clicked = false; }
            if (hov) Text(Blocks.BN[id].Replace('_', ' '), mx + 12, my + 12, s, 1, 1, 1);
        }
        { // the bucket sits after the last block
            int k = W_.NT - 1; double x = x0 + 6 * s + (k % cols) * cell, y = y0 + 16 * s + (k / cols) * cell; bool hov = In(x, y, 36 * s, 36 * s);
            if (hov) Rect(x - 2 * s, y - 2 * s, 40 * s, 40 * s, 1, 1, 1, .5f);
            BucketIcon(x, y, 36 * s);
            if (hov) Text("bucket (break = scoop water, place = pour)", mx + 12, my + 12, s, 1, 1, 1);
            if (hov && clicked) { HB[W_.slot - 1] = W_.BK; W_.sel = W_.BK; invOpen = false; clicked = false; }
        }
    }

    // ---------- settings panel ----------
    static int wid, activeW = -1; static double sy0, panelX, panelW; static double scrollY;
    static double RowH => 10 * S + 8;
    static bool Slider(string label, ref double v, double mn, double mx_, double st, Func<double, string> fmt = null)
    {
        int id = wid++; double y = sy0; sy0 += RowH; bool ch = false;
        double tx = panelX + panelW * .50, tw = panelW * .30, ty = y + 2 * S;
        Text(label, panelX + 8, y + 2 * S, S * .9, .95f, .95f, .95f);
        Rect(tx, ty + 3 * S, tw, 4 * S, 1, 1, 1, .3f);
        double t = (v - mn) / (mx_ - mn); Rect(tx + t * tw - 3 * S, ty, 6 * S, 10 * S, 1, 1, 1, activeW == id ? 1 : .8f);
        if (clicked && In(tx - 4 * S, y, tw + 8 * S, RowH)) { activeW = id; clicked = false; }
        if (activeW == id)
        {
            if (!down) activeW = -1;
            else
            {
                double nt = M.Clamp((mx - tx) / tw, 0, 1), nv = mn + nt * (mx_ - mn);
                if (st > 0) nv = mn + Math.Round((nv - mn) / st) * st; nv = M.Clamp(nv, mn, mx_);
                if (nv != v) { v = nv; ch = true; }
            }
        }
        string txt = fmt != null ? fmt(v) : (Math.Abs(v - Math.Round(v)) < 1e-9 ? ((int)Math.Round(v)).ToString() : v.ToString("0.##"));
        Text(txt, tx + tw + 8, y + 2 * S, S * .9, .7f, .85f, 1);
        return ch;
    }
    static bool Check(string label, ref double v)
    {
        double y = sy0; sy0 += RowH; bool on = v > .5; double bx = panelX + panelW - 24 * S;
        Text(label, panelX + 8, y + 2 * S, S * .9, .95f, .95f, .95f); Rect(bx, y + 1 * S, 10 * S, 10 * S, 1, 1, 1, .35f); if (on) Rect(bx + 2 * S, y + 3 * S, 6 * S, 6 * S, .4f, 1, .5f, 1);
        if (clicked && In(panelX, y, panelW, RowH)) { v = on ? 0 : 1; clicked = false; return true; }
        return false;
    }
    static bool Btn(string label, string text)
    {
        double y = sy0; sy0 += RowH + 2 * S; double bw = TextW(text, S * .9) + 12 * S, bx = panelX + panelW - bw - 8;
        Text(label, panelX + 8, y + 3 * S, S * .9, .95f, .95f, .95f); bool hov = In(bx, y, bw, RowH);
        Rect(bx, y, bw, RowH - 1, 1, 1, 1, hov ? .45f : .25f); Text(text, bx + 6 * S, y + 3 * S, S * .9);
        if (hov && clicked) { clicked = false; return true; }
        return false;
    }
    static void Head(string t) { double y = sy0; sy0 += RowH; Text(t, panelX + 8, y + 2 * S, S, 1, .85f, .4f); }

    public static Action OnFullscreen;
    public static bool PanelHit => settingsOpen && mx >= panelX;
    static void DrawSettings()
    {
        var SET = W_.SET; panelW = Math.Min(W - 16, 300 * S + 40); panelX = W - panelW - 8; double top = 8, ph = H - 16;
        Rect(panelX, top, panelW, ph, 0, 0, 0, .8f);
        scrollY = M.Clamp(scrollY - scroll * 24, 0, Math.Max(0, contentH - ph)); scroll = 0; sy0 = top + 6 - scrollY; wid = 0; double startY = sy0;
        Head("SETTINGS  (ESC: CLOSE)");
        Check("VSync ON/OFF  (F10)", ref SET.vsync);
        Slider("Render distance", ref SET.rd, 2, 12, 1); Slider("LOD detail", ref SET.lod, .3, 3, .1); Check("Distant terrain (LOD)", ref SET.lodOn);
        Slider("Field of view", ref SET.fov, 50, 110, 1); Slider("Mouse sensitivity", ref SET.sens, .2, 3, .1); Slider("Fly speed", ref SET.fly, 5, 300, 5);
        Slider("Clouds", ref SET.clouds, 0, 3, 1, v => new[] { "off", "low", "high", "ultra" }[(int)v]); Slider("Fog", ref SET.fog, 0, 2, .1);
        double hour = W_.LocalHour(); if (Slider("Time of day", ref hour, 0, 23.99, .25, v => $"{(int)v:00}:{(int)((v % 1) * 60):00}")) W_.SetHour(hour);
        Slider("Day length (s)", ref SET.day, 60, 1800, 30); Check("Freeze time", ref SET.pause); Slider("Axial tilt (Earth=23.5)", ref SET.tilt, 0, 90, .5);
        Head("GRAPHICS");
        Slider("Render resolution", ref SET.rscale, .3, 1, .05, v => $"{v * 100:0}% {(int)Math.Round(W * v)}x{(int)Math.Round(H * v)}");
        { int pr = SET.Preset(); if (Btn("Preset (click to change)", pr < 0 ? "CUSTOM" : Settings.PresetNames[pr])) SET.ApplyPreset(pr < 0 ? 2 : (pr + 1) % Settings.PresetNames.Length); }
        Slider("Shadows", ref SET.shadows, 0, 3, 1, v => new[] { "off", "near", "medium", "far" }[(int)v]); Slider("Shadow update (s)", ref SET.shInt, 0, 2, .05, v => v == 0 ? "every frame" : v.ToString("0.00") + "s");
        Check("Ambient occlusion (block corners)", ref SET.ao);
        Check(W_.AnisoOk ? "Anisotropic texture filtering" : "Anisotropic filtering (unsupported)", ref SET.aniso);
        Check("Bloom (glow)", ref SET.bloom); Check("God rays", ref SET.godrays); Check("Water reflections (SSR)", ref SET.ssr); Check("FXAA anti-aliasing", ref SET.fxaa);
        if (Btn("MSAA (applies on restart)", SET.msaa < 1 ? "OFF" : (int)SET.msaa + "X")) SET.msaa = SET.msaa < 1 ? 2 : SET.msaa < 3 ? 4 : SET.msaa < 6 ? 8 : 0;
        Head("WATER");
        Slider("Wave height", ref SET.wvA, 0, 2.5, .05); Slider("Wave speed", ref SET.wvSpd, 0, 4, .05); Slider("Wave length", ref SET.wvLen, .4, 3, .05);
        Slider("Crest sharpness", ref SET.wvK, 1, 3.5, .05); Slider("Wave direction", ref SET.wvDir, 0, 360, 5); Slider("Ripple detail", ref SET.wvRip, 0, 3, .05);
        Slider("Foam", ref SET.wvFoam, 0, 2, .05); Slider("Water opacity", ref SET.wvOp, .4, 1.5, .05);
        if (Btn("Water defaults", "RESET")) { SET.wvA = 1; SET.wvSpd = 1; SET.wvLen = 1; SET.wvK = 1.8; SET.wvDir = 0; SET.wvRip = 1; SET.wvFoam = 1; SET.wvOp = 1; }
        if (Btn("Display", "FULLSCREEN")) OnFullscreen?.Invoke();
        if (Btn("Mode", W_.P.on ? "PLAYER" : "FREE CAMERA")) W_.SetMode(!W_.P.on);
        string[] help = { "WASD MOVE, SPACE JUMP (DOUBLE TAP = FLY)", "CTRL / DOUBLE TAP W SPRINT, SHIFT SNEAK/DOWN", "WHEEL OR 1-9,0 HOTBAR, LMB BREAK, RMB PLACE (BUCKET: SCOOP / POUR)", "MMB PICK BLOCK, V PLAYER/FREE CAM, F FLY", "O ORBIT VIEW, E BLOCK PALETTE, F10 VSYNC, F11 FULLSCREEN", "ESC OR TAB = THIS MENU. CLICK WORLD TO RESUME." };
        foreach (var l in help) { Text(l, panelX + 8, sy0, S * .75, .6f, .7f, .8f); sy0 += 9 * S; }
        contentH = sy0 - startY + 12;
    }
    static double contentH;

    // ---------- compass / UP-DOWN markers ----------
    static void NavMark(V3 d, string label, string sub, float[] col, double al)
    {
        double x = M.Dot(d, W_.C.rt), y = M.Dot(d, W_.C.up), z = M.Dot(d, W_.C.f), t = Math.Tan(W_.C.fov / 2); double sx = 0, sy = 0, ex = 0, ey = -1; bool on = false; double w = W, h = H;
        if (z > .02) { double px = x / (z * t * W_.C.asp), py = y / (z * t); if (Math.Abs(px) < .9 && Math.Abs(py) < .88) { on = true; sx = w / 2 + px * w / 2; sy = h / 2 - py * h / 2; } }
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
        var C = W_.C; if (C.orbit || !W_.Started) return;
        double r = Math.Sqrt(C.x * C.x + C.y * C.y + C.z * C.z); if (r < .5) return; V3 g = new(C.x / r, C.y / r, C.z / r);
        if (W_.CoreView())
        {
            double al = Math.Min(1, r / 6);
            NavMark(g, "UP", "surface " + Math.Max(0, Math.Round(W_.R - r)), new[] { .56f, .89f, .53f }, al);
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

    // ---------- whole frame ----------
    public static string hud = "";
    public static bool underwater;
    public static void Draw(double dt)
    {
        if (startScreen) { DrawStart(); return; }
        if (underwater) Rect(0, 0, W, H, 31 / 255f, 79 / 255f, 168 / 255f, .33f);
        DrawNav();
        // crosshair
        if (!W_.C.orbit) { double cx = W / 2.0, cy = H / 2.0, L = 8 * S; Rect(cx - L - 1, cy - 1.5, 2 * L + 2, 3, 0, 0, 0, .5f); Rect(cx - 1.5, cy - L - 1, 3, 2 * L + 2, 0, 0, 0, .5f); Rect(cx - L, cy - 1, 2 * L, 2, 1, 1, 1, 1); Rect(cx - 1, cy - L, 2, 2 * L, 1, 1, 1, 1); }
        // HUD text
        var lines = hud.Split('\n'); double hs = S * 1.0, lw = lines.Max(l => l.Length) * GW * hs + 12, lh = lines.Length * (GH + 1.5) * hs + 8;
        Rect(6, 6, lw, lh, 0, 0, 0, .45f);
        for (int i = 0; i < lines.Length; i++) Text(lines[i], 12, 10 + i * (GH + 1.5) * hs, hs);
        DrawHotbar();
        if (toastT > 0) { toastT -= dt; double tw = TextW(toast, S) + 20; Rect(W / 2.0 - tw / 2, H - 64 * S - 10, tw, 12 * S + 8, 0, 0, 0, .7f); Text(toast, W / 2.0, H - 64 * S - 6, S, 1, 1, 1, 1, true); }
        if (invOpen) DrawInventory();
        if (settingsOpen) DrawSettings();
    }
    static void DrawStart()
    {
        Rect(0, 0, W, H, 13 / 255f, 18 / 255f, 38 / 255f, 1); double cx = W / 2.0, y = H * .12, s = S;
        Text("CUBE PLANET", cx, y, s * 4, 1, 1, 1, 1, true); y += 44 * s;
        Text("RADIUS: " + radiusStr + (field == 0 ? "_" : ""), cx, y, s * 2, field == 0 ? 1f : .6f, field == 0 ? 1f : .6f, 1, 1, true); y += 22 * s;
        Text("SEED: " + seedStr + (field == 1 ? "_" : ""), cx, y, s * 2, field == 1 ? 1f : .6f, field == 1 ? 1f : .6f, 1, 1, true); y += 30 * s;
        Text("TYPE DIGITS, TAB SWITCHES FIELD, ENTER STARTS (RADIUS 200-10000000)", cx, y, s * 1.2, .6f, .8f, .6f, 1, true); y += 34 * s;
        string[] help = { "WASD WALK  SPACE JUMP (DOUBLE TAP = FLY)  CTRL OR DOUBLE TAP W SPRINT  SHIFT SNEAK/DOWN",
            "WHEEL OR 1-9,0 HOTBAR   LMB BREAK   RMB PLACE   MMB PICK BLOCK", "V PLAYER/FREE CAMERA   F FLY   O PLANET ORBIT VIEW   E BLOCK PALETTE",
            "ESC / TAB = SETTINGS (RENDER DISTANCE, LOD, FOV, TIME, WATER...)   F11 FULLSCREEN", "IN ORBIT VIEW: DRAG ROTATES, WHEEL/W/S ZOOMS, ZOOM IN TO LAND" };
        foreach (var l in help) { Text(l, cx, y, s * 1.0, .6f, .7f, .8f, 1, true); y += 14 * s; }
    }
}
