using Silk.NET.Vulkan;

namespace CubePlanet;

/// <summary>Minimal immediate-mode 2D layer: coloured quads, 5x7 bitmap text, block icons, plus the game's HUD / hotbar / palette / settings / compass.</summary>
public static partial class UI
{
    static Pipe pipe; static readonly List<float> vs = new();
    public static int W, H, S = 1; // screen size, integer UI scale

    // ---------- input state fed by the window ----------
    public static double mx, my, scroll; public static bool down, clicked, rclicked;
    public static bool settingsOpen, startScreen = true; public static string toast = ""; public static double toastT;
    public static string radiusStr = "1200", seedStr = "1337"; public static int field;


    public static void Init()
    {
        pipe = Gpu.MakePipe("ui", Glsl.UI_VS, Glsl.UI_FS,
            new PipeCfg { layout = VL.UI, pass = RP.Final, depthTest = false, depthWrite = false, blend = BlendMode.Alpha });
        var px = new byte[GW * GN * GH];
        foreach (var kv in FONT)
        {
            int cell = kv.Key; if (cell >= GN) continue;
            for (int c = 0; c < 5; c++) for (int r = 0; r < 7; r++) if (((kv.Value[c] >> r) & 1) != 0) px[r * (GW * GN) + cell * GW + c] = 255;
        }
        Gpu.texFont = Gpu.MakeTexture(ImageType.Type2D, ImageViewType.Type2D, Format.R8Unorm, 1, GW * GN, GH, 1, 1, new[] { px }, Filter.Nearest, Filter.Nearest, SamplerMipmapMode.Nearest, SamplerAddressMode.ClampToEdge);
    }

    public static bool In(double x, double y, double w, double h) => mx >= x && mx < x + w && my >= y && my < y + h;

    public static void Begin(int w, int h) { W = w; H = h; S = Math.Max(1, (int)Math.Round(h / 600.0)); vs.Clear(); }
    public static void End()
    {
        if (vs.Count == 0) return;
        // drawn into the main render pass that Renderer.Render (or the start screen) opened
        Gpu.UboSet(60, W); Gpu.UboSet(61, H); Gpu.Bind(pipe);
        Gpu.DrawDynamic(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(vs), (uint)(vs.Count / 10));
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
        if (!Camera.C.orbit) { double cx = W / 2.0, cy = H / 2.0, L = 8 * S; Rect(cx - L - 1, cy - 1.5, 2 * L + 2, 3, 0, 0, 0, .5f); Rect(cx - 1.5, cy - L - 1, 3, 2 * L + 2, 0, 0, 0, .5f); Rect(cx - L, cy - 1, 2 * L, 2, 1, 1, 1, 1); Rect(cx - 1, cy - L, 2, 2 * L, 1, 1, 1, 1); }
        // HUD text
        var lines = hud.Split('\n'); double hs = S * 1.0, lw = lines.Max(l => l.Length) * GW * hs + 12, lh = lines.Length * (GH + 1.5) * hs + 8;
        Rect(6, 6, lw, lh, 0, 0, 0, .45f);
        for (int i = 0; i < lines.Length; i++) Text(lines[i], 12, 10 + i * (GH + 1.5) * hs, hs);
        DrawHotbar();
        if (toastT > 0) { toastT -= dt; double tw = TextW(toast, S) + 20; Rect(W / 2.0 - tw / 2, H - 64 * S - 10, tw, 12 * S + 8, 0, 0, 0, .7f); Text(toast, W / 2.0, H - 64 * S - 6, S, 1, 1, 1, 1, true); }
        DrawScreen();
        if (settingsOpen) DrawSettings();
    }
}
