using System.Text.Json;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace CubePlanet;

public static partial class W
{
    public static bool Started;
    static double fps;

    // ---------- world creation ----------
    public static void InitWorld(double radius, int seed)
    {
        R = Math.Round(radius); SeedNoise(seed); SEEDV = seed; ClearFeatCaches();
        double rr0 = R / 1200; BSC = Math.Max(1, Math.Sqrt(rr0)); LSC = Math.Max(1, Math.Pow(rr0, .3)); LCN = R <= 1000 ? R * .9 : 900 * Math.Pow(R / 1000, .85);
        HS = R <= 1200 ? Math.Max(.4, R / 1000) : Math.Min(2.5, 1.2 * Math.Pow(rr0, .2)); OD = 24 * Math.Max(1, HS); HMAX = Math.Ceiling(96 * HS + 12); MH = 70 * HS; HA = Math.Acos(R / (R + HMAX)) + .02; DEP = Math.Max(48, R - CORE);
        BC = bandOf(R + HMAX + 2) + 1; OUTER = CORE << BC; bands.Clear();
        for (int b = 0; b < BC; b++) { int fc = FC0 << b, cols = fc * CS; bands.Add(new Band { b = b, fc = fc, cols = cols, rows = (CORE / CS) * (1 << b) * CS, inner = CORE << b, d = PI / 2 / cols }); }
        lodDelta = bands[bandOf(R)].d; spd = Math.Max(1, R / 1200); Spawn(); MakeAtmosphereResources(); SetHour(9); C.d = R * 3;
        Started = true; P.ready = true; if (P.on) ToPlayer();
    }
    static void Spawn()
    {
        int n0 = 900; double ga = PI * (3 - Math.Sqrt(5)); double[] best = null, any = null;
        for (int i = 0; i < n0; i++)
        {
            double y = 1 - 2 * (i + .5) / n0, rr = Math.Sqrt(1 - y * y), th = ga * i, x = Math.Cos(th) * rr, z = Math.Sin(th) * rr;
            Terrain(x, y, z, 200);
            if (gH > 3 && gH < 14 && gT == 1 && Math.Abs(y) < .6) { best = new[] { x, y, z, gH }; break; }
            if (any == null && gH > 2) any = new[] { x, y, z, gH };
        }
        var s = best ?? any ?? new[] { 0, 1, 0, 0.0 };
        C.lat = Math.Asin(s[1]); C.lon = Math.Atan2(s[0], s[2]); SetOrbit(false);
        double p = R + Math.Max(s[3], 0) + (P.on ? 4 : 30); C.x = s[0] * p; C.y = s[1] * p; C.z = s[2] * p;
    }
    public static void Zoom(double k) { C.d = Math.Min(R * 14, C.d * k); if (C.d < R + 60) SetOrbit(false); }
    public static void Drag(double dx, double dy, double s)
    {
        if (C.orbit) { C.lon -= dx * .004 * s; C.lat = M.Clamp(C.lat + dy * .004 * s, -1.5, 1.5); }
        else Turn(dx * .0025 * s * SENS, -dy * .0025 * s * SENS);
    }
    static readonly string[] FN = { "+X", "-X", "+Y", "-Y", "+Z", "-Z" };
    static string LocInfo()
    {
        V3 p = P.on && !C.orbit ? P.p : new V3(C.x, C.y, C.z); int bi = P2b(p.X, p.Y, p.Z);
        if (bi < 0)
        {
            int i = (int)Math.Floor(p.X), j = (int)Math.Floor(p.Y), k = (int)Math.Floor(p.Z);
            return p.Len2 < CORE * CORE ? $"CARTESIAN CORE (r<{CORE})\nBLOCK x{i} y{j} z{k}\nCHUNK {i >> 5},{j >> 5},{k >> 5}  LOCAL {i & 31},{j & 31},{k & 31}" : "OUTSIDE WORLD";
        }
        Band B = bands[bi]; int pf = PF, px = PX, py = PY, pz = PZ;
        return $"FACE {FN[pf]} (#{pf})  BAND {bi} (r {B.inner}..{B.inner + B.rows})\nBLOCK x{px} y{py} z{pz}\nCHUNK {px >> 5},{py >> 5},{pz >> 5}  LOCAL {px & 31},{py & 31},{pz & 31}\nBIOME {BiomeName(p)}";
    }
    public static string HudText()
    {
        double r = Math.Sqrt(C.x * C.x + C.y * C.y + C.z * C.z); double h = LocalHour();
        string mode = C.orbit ? "ORBIT" : P.on ? (P.fly ? "PLAYER FLY" : P.wet ? "SWIM" : P.gnd ? "WALK" : "AIR") : (C.lvl ? "FREECAM LEVEL" : "FREECAM");
        return $"{mode}  ALT {(r - R):0.0}  R {R}  {(P.on && !C.orbit ? "" : "SPEED x" + spd.ToString("0.00"))}\n" +
               $"LAT {Math.Asin(C.y / r) * 180 / PI:0.0}  LON {Math.Atan2(C.x, C.z) * 180 / PI:0.0}  {(int)h:00}:{(int)((h % 1) * 60):00}\n" +
               $"RD {RD}  G {GravK(r) * GRAV:0.0}  CHUNKS {chunks.Count}  LOD {lodDrawn}  {fps:0} FPS {(SET.vsync > .5 ? "VSYNC" : "UNCAPPED")}\n{LocInfo()}";
    }
    public static void UpdateFps(double dt) { fps += (1 / Math.Max(dt, 1e-3) - fps) * .05; }
}

public class Game : GameWindow
{
    static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CubePlanet", "settings.json");
    double lastT, editT, lastSpace, lastW, lastRender; bool blockClick; int hudFrame; string hudCache = "";
    bool wDown, autoLow;

    public Game() : base(GameWindowSettings.Default, new NativeWindowSettings
    {
        ClientSize = new Vector2i(1280, 720), Title = "Cube Planet (Vulkan)", API = ContextAPI.NoAPI, // Vulkan: GLFW creates no GL context
    }) { }

    protected override void OnLoad()
    {
        base.OnLoad();
        bool firstStart = !File.Exists(SettingsPath);
        try { if (File.Exists(SettingsPath)) W.SET = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath), new JsonSerializerOptions { IncludeFields = true }) ?? new Settings(); } catch { }
        if (Program.NoVsync) W.SET.vsync = 0;
        W.VW = FramebufferSize.X; W.VH = FramebufferSize.Y;
        foreach (var o in Program.Overrides)
        {
            var kv = o.Split('='); var fld = typeof(Settings).GetField(kv[0]);
            if (fld != null && kv.Length == 2 && double.TryParse(kv[1], System.Globalization.CultureInfo.InvariantCulture, out double ov)) fld.SetValue(W.SET, ov);
        }
        if (W.SET.ver < 2) { if (W.SET.shadows >= 1) W.SET.shadows = 3; W.SET.ver = 2; } // older settings files had a single on/off shadow switch
        unsafe
        {
            Gpu.Init(WindowPtr, integrated =>
            {
                // first start on integrated graphics (school / office PCs, laptops): start from the LOW preset at 75 % resolution and a shorter view
                if (firstStart && integrated && Program.Overrides.Count == 0) { W.SET.ApplyPreset(0); W.SET.rscale = .75; W.SET.rd = Math.Min(W.SET.rd, 6); autoLow = true; }
                return Program.Msaa >= 0 ? Program.Msaa : (int)W.SET.msaa;
            }, W.SET.vsync > .5);
        }
        Gpu.SetRenderScale(W.SET.rscale);
        W.InitGpu(); UI.Init(); Gpu.WriteDescriptors();
        if (autoLow) UI.Note("Integrated graphics detected: LOW preset at 75% resolution (change it in Esc > GRAPHICS)", 8);
        Title = $"Cube Planet (Vulkan, {Gpu.DeviceName})";
        UI.OnFullscreen = () => WindowState = WindowState == WindowState.Fullscreen ? WindowState.Normal : WindowState.Fullscreen;
        UI.Pick(2);
        W.VW = FramebufferSize.X; W.VH = FramebufferSize.Y;
        W.mainTid = Environment.CurrentManagedThreadId;
        if (Program.Bench > 0) { W.P.on = false; StartGame(); } // bench: fixed free camera above the spawn point
    }
    protected override void OnUnload()
    {
        if (Program.Overrides.Count == 0 && Program.Bench <= 0) try { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)); File.WriteAllText(SettingsPath, JsonSerializer.Serialize(W.SET, new JsonSerializerOptions { IncludeFields = true })); } catch { }
        Gpu.Shutdown();
        base.OnUnload();
    }
    protected override void OnFramebufferResize(FramebufferResizeEventArgs e)
    {
        base.OnFramebufferResize(e); W.VW = e.Width; W.VH = e.Height; Gpu.Resize();
    }

    bool Captured => CursorState == CursorState.Grabbed;
    void UpdateCursor()
    {
        bool want = !(UI.startScreen || UI.settingsOpen || UI.invOpen);
        if (want && !Captured) CursorState = CursorState.Grabbed; else if (!want && Captured) CursorState = CursorState.Normal;
    }
    void StartGame()
    {
        double rad = double.TryParse(UI.radiusStr, out var r) ? r : 1200; int seed = int.TryParse(UI.seedStr, out var s) ? s : 1337; if (seed == 0) seed = 1337;
        rad = Math.Max(200, Math.Min(10000000, rad));
        UI.startScreen = false; W.InitWorld(rad, seed); UpdateCursor();
    }
    void ToggleSettings()
    {
        UI.invOpen = false; UI.settingsOpen = !UI.settingsOpen;
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (!UI.startScreen) return;
        if (e.Unicode >= '0' && e.Unicode <= '9') { if (UI.field == 0) { if (UI.radiusStr.Length < 8) UI.radiusStr += (char)e.Unicode; } else if (UI.seedStr.Length < 9) UI.seedStr += (char)e.Unicode; }
        else if (e.Unicode == '-' && UI.field == 1 && UI.seedStr.Length == 0) UI.seedStr = "-";
    }
    protected override void OnKeyDown(KeyboardKeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Program.Bench > 0) return;
        if (UI.startScreen)
        {
            if (e.Key == Keys.Enter || e.Key == Keys.KeyPadEnter) StartGame();
            else if (e.Key == Keys.Tab) UI.field ^= 1;
            else if (e.Key == Keys.Backspace) { if (UI.field == 0) { if (UI.radiusStr.Length > 0) UI.radiusStr = UI.radiusStr[..^1]; } else if (UI.seedStr.Length > 0) UI.seedStr = UI.seedStr[..^1]; }
            else if (e.Key == Keys.Escape) Close();
            return;
        }
        if (e.Key == Keys.F11) { UI.OnFullscreen?.Invoke(); return; }
        if (e.Key == Keys.F10 && !e.IsRepeat) { W.SET.vsync = W.SET.vsync > .5 ? 0 : 1; UI.Note(W.SET.vsync > .5 ? "VSync ON" : "VSync OFF (uncapped FPS)", 2); return; }
        if (e.IsRepeat) return;
        double t = W.NowMs;
        switch (e.Key)
        {
            case Keys.F: if (W.P.on) W.P.fly = !W.P.fly; else W.C.lvl = !W.C.lvl; break;
            case Keys.V: W.SetMode(!W.P.on); break;
            case Keys.Space: if (W.P.on && t - lastSpace < 300) { W.P.fly = !W.P.fly; W.P.vu = 0; } lastSpace = t; break;
            case Keys.W: if (t - lastW < 300) W.sprint = true; lastW = t; wDown = true; break;
            case Keys.O: W.SetOrbit(!W.C.orbit); break;
            case Keys.E: UI.settingsOpen = false; UI.invOpen = !UI.invOpen; break;
            case Keys.Tab: case Keys.Escape: ToggleSettings(); break;
        }
        if (e.Key >= Keys.D0 && e.Key <= Keys.D9) UI.Pick(e.Key == Keys.D0 ? 10 : (int)e.Key - (int)Keys.D0);
    }
    protected override void OnKeyUp(KeyboardKeyEventArgs e)
    {
        base.OnKeyUp(e); if (e.Key == Keys.W) { W.sprint = false; wDown = false; }
    }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Program.Bench > 0) return;
        if (UI.startScreen) return;
        double deltaY = -e.OffsetY * 100;
        if (UI.settingsOpen) UI.scroll += e.OffsetY;
        else if (W.C.orbit) W.Zoom(Math.Pow(1.002, deltaY));
        else if (W.P.on) UI.Pick((W.slot - 1 + (deltaY > 0 ? 1 : 9)) % 10 + 1);
        else W.spd = Math.Max(.05, Math.Min(5000, W.spd * Math.Pow(1.2, -Math.Sign(deltaY))));
    }
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (UI.startScreen) return;
        if (e.Button == MouseButton.Left) UI.clicked = true;
        if (e.Button == MouseButton.Right) UI.rclicked = true;
        if (Captured && e.Button == MouseButton.Middle) W.PickBlock();
    }
    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        base.OnMouseMove(e);
        if (Captured && !UI.startScreen && Program.Bench == 0) W.Drag(e.DeltaX, e.DeltaY, 1);
    }

    double benchT0 = -1, benchLast; int benchFrames; double benchWorst; bool shotDone, turned; double turnWorst; double loadDoneT = -1;
    void BenchStep(double now)
    {
        if (benchT0 < 0)
        {
            benchT0 = now; benchLast = now;
            if (Program.Hour >= 0) { W.SetHour(Program.Hour); W.SET.pause = 1; }
            if (Program.Up != 0) W.CamMove(0, 0, Program.Up, false);
            if (Program.Yaw != 0 || Program.Pitch != 0) W.Turn(Program.Yaw * Math.PI / 180, Program.Pitch * Math.PI / 180);
            return;
        }
        double ft = now - benchLast; benchLast = now;
        if (Program.TurnAt >= 0)
        {
            double el = (now - benchT0) / 1000;
            if (!turned && el >= Program.TurnAt) { turned = true; W.Turn(Math.PI, 0); }
            else if (turned && el < Program.TurnAt + 2 && ft > turnWorst) turnWorst = ft;
        }
        if (loadDoneT < 0) { bool any = false; foreach (var c in W.chunks.Values) if (!c.built) { any = true; break; } if (!any && W.chunks.Count > 100) loadDoneT = (now - benchT0) / 1000; }
        if (now - benchT0 > 3000) { benchFrames++; benchWorst = Math.Max(benchWorst, ft); } else benchT0 = benchT0 + 0 * ft; // first 3 s = warm-up / loading
        if (Program.Shot != null && now - benchT0 >= Program.Bench * 1000 + 2900 && !shotDone) { Gpu.ShotPath = Program.Shot; shotDone = true; }
        if (now - benchT0 >= Program.Bench * 1000 + 3000)
        {
            double sec = (now - benchT0 - 3000) / 1000;
            Close();
            Console.WriteLine($"BENCH {Gpu.DeviceName}: {benchFrames / sec:0.0} avg FPS, worst frame {benchWorst:0.0} ms, chunks {W.chunks.Count}, LOD {W.lodDrawn}, mesh heap {Gpu.HeapUsed / 1048576.0:0.0} MB");
            double ms(long t) => t * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (Program.TurnAt >= 0) Console.WriteLine($"TURN worst frame in the 2 s after a 180 degree turn: {turnWorst:0.0} ms");
            Console.WriteLine($"PROF gen {W.profGenN} chunks, {ms(W.profGenTicks) / Math.Max(1, W.profGenN):0.00} ms avg (worker), {W.profSyncGen} generated synchronously on main thread");
            Console.WriteLine($"PROF mesh (workers) {W.profMeshN} meshes, {ms(W.profMeshTicks) / Math.Max(1, W.profMeshN):0.00} ms avg");
            Console.WriteLine($"PROF mesh (main thread) {W.profBuildN} builds, {ms(W.profBuildTicks) / Math.Max(1, W.profBuildN):0.00} ms avg, {W.profRebuild} were rebuilds");
            Console.WriteLine($"PROF scan {W.profScanN}x, {ms(W.profScanTicks) / Math.Max(1, W.profScanN):0.0} ms avg (background)");
            Console.WriteLine($"PROF total main-thread mesh time {ms(W.profBuildTicks):0} ms, wall {(now - benchT0) / 1000:0.0} s, first-load done at {loadDoneT:0.0} s");
        }
    }
    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);
        double now = W.NowMs; double dt = Math.Min((now - lastT) / 1000.0, .1); if (dt <= 0) dt = .016; lastT = now;
        int w = FramebufferSize.X, h = FramebufferSize.Y; W.VW = w; W.VH = h; if (w == 0 || h == 0) { Thread.Sleep(10); return; } // minimised
        Gpu.SetRenderScale(W.SET.rscale);
        if (!Gpu.BeginFrame(W.SET.vsync > .5)) return;
        double sx = (double)w / Math.Max(1, ClientSize.X), sy = (double)h / Math.Max(1, ClientSize.Y);
        UI.mx = MousePosition.X * sx; UI.my = MousePosition.Y * sy; UI.down = MouseState.IsButtonDown(MouseButton.Left);

        if (!W.Started) { Gpu.BeginFinal(13 / 255f, 18 / 255f, 38 / 255f); UI.Begin(w, h); UI.Draw(dt); UI.End(); UI.clicked = false; Gpu.EndFrame(); return; }

        var SET = W.SET; var kb = KeyboardState;
        // apply settings
        int rd = (int)SET.rd; if (rd != W.RD) { W.RD = rd; W.lastScan = new V3(1e9, 0, 0); }
        W.LODK = SET.lod; W.LODON = SET.lodOn > .5; W.C.fov = SET.fov * Math.PI / 180; W.SENS = SET.sens; W.FLYS = SET.fly; W.SetTilt(SET.tilt);

        W.frameNo++; if (SET.pause < .5) W.SUNA += dt * 2 * Math.PI / SET.day; W.SunVec(); W.UpdateFps(dt); W.AtmosphereStep(dt);
        W.C.asp = (double)w / h;
        bool menu = UI.settingsOpen || UI.invOpen || Program.Bench > 0; // bench: ignore input so runs are repeatable
        double yaw = (kb.IsKeyDown(Keys.Right) ? 1 : 0) - (kb.IsKeyDown(Keys.Left) ? 1 : 0), pit = (kb.IsKeyDown(Keys.Up) ? 1 : 0) - (kb.IsKeyDown(Keys.Down) ? 1 : 0);
        if (Program.Bench > 0) { yaw = 0; pit = 0; }
        double fw = 0, sd = 0, up = 0; bool jump = false, sneak = false, ctrl = false;
        if (!menu)
        {
            fw = (kb.IsKeyDown(Keys.W) ? 1 : 0) - (kb.IsKeyDown(Keys.S) ? 1 : 0); sd = (kb.IsKeyDown(Keys.D) ? 1 : 0) - (kb.IsKeyDown(Keys.A) ? 1 : 0);
            jump = kb.IsKeyDown(Keys.Space); sneak = kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift); ctrl = kb.IsKeyDown(Keys.LeftControl) || kb.IsKeyDown(Keys.RightControl);
            up = (jump ? 1 : 0) - (kb.IsKeyDown(Keys.LeftShift) ? 1 : 0);
        }
        if (W.C.orbit)
        {
            W.Drag(-sd * dt * 180, 0, 1); if (yaw != 0 || pit != 0) W.Drag(yaw * dt * 450, -pit * dt * 450, 1);
            double z = -fw * dt * 1.2; if (z != 0) W.Zoom(Math.Exp(z)); if (up != 0) W.Zoom(Math.Exp(-up * dt * 1.2));
        }
        else
        {
            if (yaw != 0 || pit != 0) W.Turn(yaw * 1.8 * dt, pit * 1.8 * dt);
            if (W.P.on) W.PlayerUpdate(dt, fw, sd, jump, sneak, ctrl);
            else { double alt = Math.Sqrt(W.C.x * W.C.x + W.C.y * W.C.y + W.C.z * W.C.z) - W.R, s = 12 * W.spd * Math.Max(1, alt / 25) * dt * (ctrl ? 5 : 1); W.CamMove(fw * s, sd * s, up * s, W.C.lvl); }
        }
        W.CamUpdate(dt); if (!W.C.orbit) W.LoadStep(); W.WaterTick(dt);
        if (W.frameNo % 6 == 0) UI.underwater = !W.C.orbit && Blocks.Wet(W.GetAt(W.C.x, W.C.y, W.C.z));
        // block editing
        editT -= dt; bool L = Captured && Program.Bench == 0 && MouseState.IsButtonDown(MouseButton.Left), Rt = Captured && Program.Bench == 0 && MouseState.IsButtonDown(MouseButton.Right);
        if (blockClick) { if (!MouseState.IsButtonDown(MouseButton.Left)) blockClick = false; L = false; }
        if ((L || Rt) && editT <= 0 && !W.C.orbit) { W.Edit(L, Rt && !L); editT = .15; }
        if (W.C.orbit) W.TG = null; else if (W.frameNo % 2 == 0) W.TG = W.Raycast(W.P.on ? 6 : 10);

        W.Render();
        if (Program.Bench > 0) BenchStep(now);

        // UI
        if (++hudFrame % 10 == 0) hudCache = W.HudText(); UI.hud = hudCache;
        UI.Begin(w, h); UI.Draw(dt); UI.End();
        // click on the world while a menu is open: close it and capture the mouse again
        if (UI.clicked && !UI.startScreen && (UI.settingsOpen || UI.invOpen) && !UI.PanelHit) { UI.settingsOpen = false; UI.invOpen = false; blockClick = true; }
        UI.clicked = false; UI.rclicked = false;
        UpdateCursor();
        Gpu.EndFrame();
    }
}
