using System.Text.Json;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace CubePlanet;

public class Game : GameWindow
{
    static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CubePlanet", "settings.json");
    double lastT, editT, lastSpace, lastW, lastRender; bool blockClick; int hudFrame; string hudCache = "";
    bool wDown, autoLow, vkFallback; int startFrames;

    public Game() : base(GameWindowSettings.Default, new NativeWindowSettings
    {
        ClientSize = new Vector2i(1280, 720), Title = "Cube Planet", API = ContextAPI.NoAPI, // Vulkan / Direct3D 11: GLFW creates no GL context
    }) { }

    protected override void OnLoad()
    {
        base.OnLoad();
        bool firstStart = !File.Exists(SettingsPath);
        try { if (File.Exists(SettingsPath)) Settings.SET = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath), new JsonSerializerOptions { IncludeFields = true }) ?? new Settings(); } catch { }
        if (Program.NoVsync) Settings.SET.vsync = 0;
        Renderer.VW = FramebufferSize.X; Renderer.VH = FramebufferSize.Y;
        foreach (var o in Program.Overrides)
        {
            var kv = o.Split('='); var fld = typeof(Settings).GetField(kv[0]);
            if (fld != null && kv.Length == 2 && double.TryParse(kv[1], System.Globalization.CultureInfo.InvariantCulture, out double ov)) fld.SetValue(Settings.SET, ov);
        }
        if (Settings.SET.ver < 2) { if (Settings.SET.shadows >= 1) Settings.SET.shadows = 3; Settings.SET.ver = 2; } // older settings files had a single on/off shadow switch
        unsafe
        {
            Func<bool, int> pick = integrated =>
            {
                // first start on integrated graphics (school / office PCs, laptops): start from the LOW preset at 75 % resolution and a shorter view
                if (firstStart && integrated && Program.Overrides.Count == 0) { Settings.SET.ApplyPreset(0); Settings.SET.rscale = .75; Settings.SET.rd = Math.Min(Settings.SET.rd, 6); autoLow = true; }
                return Program.Msaa >= 0 ? Program.Msaa : (int)Settings.SET.msaa;
            };
            // renderer: Vulkan unless the settings or --dx11 ask for DirectX 11; a PC without Vulkan (Intel HD 4000 / 4400 / 5500 ...) gets DirectX 11
            bool dx = Program.ForceDx || (!Program.ForceVk && Settings.SET.renderer > .5);
            if (!dx)
                try { Gpu.Init(WindowPtr, pick, Settings.SET.vsync > .5); }
                catch (Exception ex) { Console.Error.WriteLine("Vulkan is not available (" + ex.Message + "): using DirectX 11"); dx = true; vkFallback = true; }
            if (dx) { Dx.Init(WindowPtr, pick, Settings.SET.vsync > .5); Gpu.DeviceName = Dx.DeviceName; Gpu.Integrated = Dx.Integrated; }
        }
        Gpu.SetRenderScale(Settings.SET.rscale);
        Renderer.InitGpu(); UI.Init(); Gpu.WriteDescriptors();
        if (autoLow) UI.Note("Integrated graphics detected: LOW preset at 75% resolution (change it in Esc > GRAPHICS)", 8);
        Title = $"Cube Planet ({(Dx.On ? "DirectX 11" : "Vulkan")}, {Gpu.DeviceName})";
        if (vkFallback) UI.Note("This PC has no Vulkan: running on DirectX 11", 6);
        UI.OnFullscreen = () => WindowState = WindowState == WindowState.Fullscreen ? WindowState.Normal : WindowState.Fullscreen;
        PlayerInventory.Init(); UI.Pick(2);
        Renderer.VW = FramebufferSize.X; Renderer.VH = FramebufferSize.Y;
        ChunkGenerator.mainTid = Environment.CurrentManagedThreadId;
        UI.OnStart = StartGame;
        if (Program.Bench > 0)
        {   // bench: fixed free camera above the spawn point; --world loads a saved world (saved again at the end only with --benchsave)
            var bw = Program.WorldArg == null ? null : WorldSave.List().FirstOrDefault(i => i.Name == Program.WorldArg);
            if (bw == null) { PlayerPhysics.P.on = false; StartGame(null); }
            else { StartGame(bw); PlayerPhysics.P.on = false; if (!Program.BenchSave) WorldSave.Current = null; }
        }
        else if (Program.WorldArg != null) { var w = WorldSave.List().FirstOrDefault(i => i.Name == Program.WorldArg); if (w == null) UI.nameStr = Program.WorldArg; StartGame(w); }
    }
    protected override void OnUnload()
    {
        if (WorldSave.Current != null) try { WorldSave.Save(); } catch (Exception ex) { Console.Error.WriteLine("Saving the world failed: " + ex.Message); }
        if (Program.Overrides.Count == 0 && Program.Bench <= 0) try { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)); File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Settings.SET, new JsonSerializerOptions { IncludeFields = true })); } catch { }
        Gpu.Shutdown();
        base.OnUnload();
    }
    protected override void OnFramebufferResize(FramebufferResizeEventArgs e)
    {
        base.OnFramebufferResize(e); Renderer.VW = e.Width; Renderer.VH = e.Height; Gpu.Resize();
    }

    bool Captured => CursorState == CursorState.Grabbed;
    void UpdateCursor()
    {
        bool want = !(UI.startScreen || UI.settingsOpen || UI.invOpen);
        if (want && !Captured) CursorState = CursorState.Grabbed; else if (!want && Captured) CursorState = CursorState.Normal;
    }
    /// <summary>Starts a saved world (w) or a new one (w == null, from the start screen fields; bench runs are never saved).</summary>
    void StartGame(WorldSave.Info w)
    {
        // while playing, the garbage collector avoids blocking full collections (concurrent GC is on in the project; those pauses were the
        // occasional 50-100 ms freezes when flying fast)
        System.Runtime.GCSettings.LatencyMode = System.Runtime.GCLatencyMode.SustainedLowLatency;
        Thread.CurrentThread.Priority = ThreadPriority.AboveNormal; // the game / render thread goes first; all background work is below normal
        if (w != null)
        {
            var j = WorldSave.ReadInfo(w.Folder);
            UI.startScreen = false; World.InitWorld((double)j["radius"], (int)j["seed"]);
            try { WorldSave.LoadInto(w.Folder); WorldSave.Current = w.Folder; WorldSave.CurrentName = w.Name; UI.Note("Loaded " + w.Name, 3); }
            catch (Exception ex) { UI.Note("Could not load the world (it will not be saved): " + ex.Message, 10); }
        }
        else
        {
            double rad = double.TryParse(UI.radiusStr, out var r) ? r : 1200; int seed = int.TryParse(UI.seedStr, out var s) ? s : 1337; if (seed == 0) seed = 1337;
            rad = Math.Max(200, Math.Min(10000000, rad));
            UI.startScreen = false; World.InitWorld(rad, seed);
            if (Program.Bench <= 0)
            {
                string name = UI.nameStr.Trim(); var existing = WorldSave.List().Select(i => i.Folder).ToHashSet();
                if (name.Length == 0) { int n = 1; while (existing.Contains(WorldSave.FolderFor("World " + n))) n++; name = "World " + n; }
                string folder = WorldSave.FolderFor(name), baseName = name;
                for (int n = 2; existing.Contains(folder); n++) { name = baseName + " " + n; folder = WorldSave.FolderFor(name); }
                WorldSave.Current = folder; WorldSave.CurrentName = name;
                try { WorldSave.Save(); UI.Note("New world: " + name, 3); } catch (Exception ex) { UI.Note("Cannot save to " + folder + ": " + ex.Message, 10); WorldSave.Current = null; }
            }
        }
        UpdateCursor();
    }
    void ToggleSettings()
    {
        if (UI.invOpen) { UI.CloseScreen(); return; } // Esc closes an open window first
        UI.settingsOpen = !UI.settingsOpen;
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (UI.searchFocus && UI.invOpen) { char ch = (char)e.Unicode; if (UI.search.Length < 24 && (char.IsLetterOrDigit(ch) || ch == ' ' || ch == '_')) UI.search += ch; return; }
        if (!UI.startScreen) return;
        char c = (char)e.Unicode;
        if (UI.field == 0) { if (UI.nameStr.Length < 24 && (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_')) UI.nameStr += c; }
        else if (c >= '0' && c <= '9') { if (UI.field == 1) { if (UI.radiusStr.Length < 8) UI.radiusStr += c; } else if (UI.seedStr.Length < 9) UI.seedStr += c; }
        else if (c == '-' && UI.field == 2 && UI.seedStr.Length == 0) UI.seedStr = "-";
    }
    protected override void OnKeyDown(KeyboardKeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Program.Bench > 0) return;
        if (UI.searchFocus && UI.invOpen)
        {   // the palette search box has the keyboard: E, numbers etc. are text, Esc / Enter leave the box
            if (e.Key == Keys.Backspace && UI.search.Length > 0) UI.search = UI.search[..^1];
            else if (e.Key == Keys.Escape || e.Key == Keys.Enter || e.Key == Keys.KeyPadEnter) UI.searchFocus = false;
            return;
        }
        if (UI.startScreen)
        {
            if (e.Key == Keys.Enter || e.Key == Keys.KeyPadEnter) StartGame(null);
            else if (e.Key == Keys.Tab) UI.field = (UI.field + 1) % 3;
            else if (e.Key == Keys.Backspace)
            {
                static string Cut(string t) => t.Length > 0 ? t[..^1] : t;
                if (UI.field == 0) UI.nameStr = Cut(UI.nameStr); else if (UI.field == 1) UI.radiusStr = Cut(UI.radiusStr); else UI.seedStr = Cut(UI.seedStr);
            }
            else if (e.Key == Keys.Escape) Close();
            return;
        }
        if (e.Key == Keys.F11) { UI.OnFullscreen?.Invoke(); return; }
        if (e.Key == Keys.F10 && !e.IsRepeat) { Settings.SET.vsync = Settings.SET.vsync > .5 ? 0 : 1; UI.Note(Settings.SET.vsync > .5 ? "VSync ON" : "VSync OFF (uncapped FPS)", 2); return; }
        if (e.IsRepeat) return;
        double t = Renderer.NowMs;
        switch (e.Key)
        {
            case Keys.F: if (PlayerPhysics.P.on) PlayerPhysics.P.fly = !PlayerPhysics.P.fly; else Camera.C.lvl = !Camera.C.lvl; break;
            case Keys.V: PlayerPhysics.SetMode(!PlayerPhysics.P.on); break;
            case Keys.Space: if (PlayerPhysics.P.on && t - lastSpace < 300) { PlayerPhysics.P.fly = !PlayerPhysics.P.fly; PlayerPhysics.P.vu = 0; } lastSpace = t; break;
            case Keys.W: if (t - lastW < 300) PlayerPhysics.sprint = true; lastW = t; wDown = true; break;
            case Keys.O: Camera.SetOrbit(!Camera.C.orbit); break;
            case Keys.E: UI.settingsOpen = false; UI.ToggleInventory(); break;
            case Keys.Tab: case Keys.Escape: ToggleSettings(); break;
        }
        if (e.Key >= Keys.D0 && e.Key <= Keys.D9) UI.Pick(e.Key == Keys.D0 ? 10 : (int)e.Key - (int)Keys.D0);
    }
    protected override void OnKeyUp(KeyboardKeyEventArgs e)
    {
        base.OnKeyUp(e); if (e.Key == Keys.W) { PlayerPhysics.sprint = false; wDown = false; }
    }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Program.Bench > 0) return;
        if (UI.startScreen) return;
        double deltaY = -e.OffsetY * 100;
        if (UI.settingsOpen || UI.invOpen) UI.scroll += e.OffsetY; // settings panel / item palette scroll; the hotbar stays put
        else if (Camera.C.orbit) Camera.Zoom(Math.Pow(1.002, deltaY));
        else if (PlayerPhysics.P.on) UI.Pick((PlayerInventory.Selected + (deltaY > 0 ? 1 : 9)) % 10 + 1);
        else Camera.spd = Math.Max(.05, Math.Min(5000, Camera.spd * Math.Pow(1.2, -Math.Sign(deltaY))));
    }
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButton.Left) UI.clicked = true; // the start screen needs clicks too (PLAY / DELETE / CREATE)
        if (e.Button == MouseButton.Right) UI.rclicked = true;
        if (UI.startScreen) return;
        if (Captured && e.Button == MouseButton.Middle) BlockEditing.PickBlock();
    }
    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        base.OnMouseMove(e);
        if (Captured && !UI.startScreen && Program.Bench == 0) Camera.Drag(e.DeltaX, e.DeltaY, 1);
    }

    double benchT0 = -1, benchLast; int benchFrames; double benchWorst; bool shotDone, turned; double turnWorst; double loadDoneT = -1;
    // --benchedits (with --bench): 3..8 s no edits, then a block placed / broken every 0.15 s (like holding a mouse button) near the camera;
    // frame times of both phases are reported (--editsync: edited chunks meshed on the main thread, the old way, to compare)
    readonly List<double> calmFrames = new(), editFrames = new(); double nextEdit; int editNo; BlockPos hole; bool holeOk;
    void BenchEdit(double now, double ft)
    {
        double el = (now - benchT0) / 1000;
        if (el < 3) return;
        if (el < 8) { calmFrames.Add(ft); return; }
        editFrames.Add(ft);
        if (el < nextEdit) return; nextEdit = el + .15;
        V3 c = new(Camera.C.x, Camera.C.y, Camera.C.z), up = M.Nrm(c), f = Camera.C.f;
        V3 fl = M.Nrm(f - up * M.Dot(f, up)); if (fl.Len < .1) fl = M.Nrm(M.Cross(up, new V3(.3, .8, .5)));
        V3 side = M.Nrm(M.Cross(fl, up));
        int k = editNo / 2; double a = (k * 7) % 25 - 12, d = 4 + (k * 3) % 9; // spread along a line across chunk borders
        if (editNo % 2 == 1)
        {   // fill the hole dug by the last edit
            if (holeOk && hole.Loaded && hole.Id == 0) BlockActions.Place(hole, Blocks.Stone, f, out _);
            editNo++; return;
        }
        V3 p = c + fl * d + side * a;
        for (int i = 0; i < 80 && BlockAccess.GetAt(p.X, p.Y, p.Z) == 0; i++) p -= up; // down to the ground: dig into it (dense chunks)
        holeOk = BlockPos.TryAt(p.X, p.Y, p.Z, out hole) && hole.Loaded && hole.Id != 0 && !BlockActions.Replaceable(hole.Id) && !BlockStates.entities.ContainsKey(hole.Key);
        if (holeOk) BlockActions.Break(hole);
        editNo++;
    }
    static string Stats(List<double> fr)
    {
        if (fr.Count == 0) return "no frames"; var s = fr.OrderBy(x => x).ToList();
        return $"{fr.Count} frames, avg {fr.Average():0.0} ms, p99 {s[(int)(s.Count * .99)]:0.0} ms, worst {s[^1]:0.0} ms, over 20 ms {fr.Count(x => x > 20)}, over 33 ms {fr.Count(x => x > 33)}";
    }
    long profT, frameEndT; readonly double[] prevPhase = new double[11]; int g0Last = -1, g1Last, g2Last; readonly List<string> slow = new(); int slow25, slow50;
    void BenchStep(double now)
    {
        if (benchT0 < 0)
        {
            benchT0 = now; benchLast = now;
            if (Program.Hour >= 0) { Sky.SetHour(Program.Hour); Settings.SET.pause = 1; }
            if (Program.Up != 0) Camera.CamMove(0, 0, Program.Up, false);
            if (Program.Yaw != 0 || Program.Pitch != 0) Camera.Turn(Program.Yaw * Math.PI / 180, Program.Pitch * Math.PI / 180);
            if (Program.Showcase) SelfTest.Showcase(); // example blocks in front of the camera
            if (Program.ShowUi != null) SelfTest.ShowUi(Program.ShowUi);
            return;
        }
        double ft = now - benchLast; benchLast = now;
        // --flyspeed V: fly the free camera forward at V blocks/s (level with the ground) and record what each slow frame spent its time on
        if (Program.FlySpeed > 0) Camera.CamMove(Program.FlySpeed * Math.Min(ft, 100) / 1000, 0, 0, true);
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        if (g0Last >= 0 && now - benchT0 > 3000 && ft > 25)
        {
            slow25++; if (ft > 50) slow50++;
            if (slow.Count < 25)
                slow.Add($"SLOW {(now - benchT0) / 1000:0.0}s {ft:0} ms: " + string.Join(" ", Renderer.PhaseName.Select((n, i) => (n, v: i >= 9 ? Renderer.Phase[i] : prevPhase[i])).Where(p => p.v >= 1).Select(p => $"{p.n} {p.v:0}")) +
                         $" | gc0 +{g0 - g0Last} gc1 +{g1 - g1Last} gc2 +{g2 - g2Last} | chunks {World.chunks.Count}");
        }
        g0Last = g0; g1Last = g1; g2Last = g2;
        if (Program.BenchEdits) BenchEdit(now, ft);
        if (Program.TurnAt >= 0)
        {
            double el = (now - benchT0) / 1000;
            if (!turned && el >= Program.TurnAt) { turned = true; Camera.Turn(Math.PI, 0); }
            else if (turned && el < Program.TurnAt + 2 && ft > turnWorst) turnWorst = ft;
        }
        if (loadDoneT < 0) { bool any = false; foreach (var c in World.chunks.Values) if (!c.built) { any = true; break; } if (!any && World.chunks.Count > 100) loadDoneT = (now - benchT0) / 1000; }
        if (now - benchT0 > 3000) { benchFrames++; benchWorst = Math.Max(benchWorst, ft); } else benchT0 = benchT0 + 0 * ft; // first 3 s = warm-up / loading
        if (Program.Shot != null && now - benchT0 >= Program.Bench * 1000 + 2900 && !shotDone) { Gpu.ShotPath = Program.Shot; shotDone = true; }
        if (now - benchT0 >= Program.Bench * 1000 + 3000)
        {
            double sec = (now - benchT0 - 3000) / 1000;
            Close();
            Console.WriteLine($"BENCH {Gpu.DeviceName}: {benchFrames / sec:0.0} avg FPS, worst frame {benchWorst:0.0} ms, chunks {World.chunks.Count}, LOD {LodTerrain.lodDrawn}, mesh heap {Gpu.HeapUsed / 1048576.0:0.0} MB");
            double ms(long t) => t * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (Program.BenchEdits) { Console.WriteLine($"EDITS {(Program.EditSync ? "main-thread meshing (old)" : "worker meshing (new)")}: {editNo / 2} edits"); Console.WriteLine("  no edits:   " + Stats(calmFrames)); Console.WriteLine("  with edits: " + Stats(editFrames)); }
            if (Program.TurnAt >= 0) Console.WriteLine($"TURN worst frame in the 2 s after a 180 degree turn: {turnWorst:0.0} ms");
            Console.WriteLine($"PROF gen {ChunkWorkers.profGenN} chunks, {ms(ChunkWorkers.profGenTicks) / Math.Max(1, ChunkWorkers.profGenN):0.00} ms avg (worker), {ChunkWorkers.profSyncGen} generated synchronously on main thread");
            Console.WriteLine($"PROF mesh (workers) {ChunkWorkers.profMeshN} meshes, {ms(ChunkWorkers.profMeshTicks) / Math.Max(1, ChunkWorkers.profMeshN):0.00} ms avg");
            Console.WriteLine($"PROF mesh (main thread) {ChunkWorkers.profBuildN} builds, {ms(ChunkWorkers.profBuildTicks) / Math.Max(1, ChunkWorkers.profBuildN):0.00} ms avg, {ChunkWorkers.profRebuild} were rebuilds");
            Console.WriteLine($"PROF scan {ChunkWorkers.profScanN}x, {ms(ChunkWorkers.profScanTicks) / Math.Max(1, ChunkWorkers.profScanN):0.0} ms avg (background)");
            if (Program.FlySpeed > 0) { Console.WriteLine($"FLY {Program.FlySpeed} blocks/s: {slow25} frames over 25 ms, {slow50} over 50 ms"); foreach (var s in slow) Console.WriteLine(s); }
            Console.WriteLine($"PROF total main-thread mesh time {ms(ChunkWorkers.profBuildTicks):0} ms, wall {(now - benchT0) / 1000:0.0} s, first-load done at {loadDoneT:0.0} s");
        }
    }
    protected override void OnRenderFrame(FrameEventArgs args)
    {
        base.OnRenderFrame(args);
        double now = Renderer.NowMs; double dt = Math.Min((now - lastT) / 1000.0, .1); if (dt <= 0) dt = .016; lastT = now;
        int w = FramebufferSize.X, h = FramebufferSize.Y; Renderer.VW = w; Renderer.VH = h; if (w == 0 || h == 0) { Thread.Sleep(10); return; } // minimised
        Gpu.SetRenderScale(Settings.SET.rscale);
        profT = System.Diagnostics.Stopwatch.GetTimestamp(); if (frameEndT != 0) Renderer.Phase[10] = (profT - frameEndT) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        if (!Gpu.BeginFrame(Settings.SET.vsync > .5)) return;
        Renderer.Prof(9, ref profT);
        double sx = (double)w / Math.Max(1, ClientSize.X), sy = (double)h / Math.Max(1, ClientSize.Y);
        UI.mx = MousePosition.X * sx; UI.my = MousePosition.Y * sy; UI.down = MouseState.IsButtonDown(MouseButton.Left);

        if (!World.Started && Program.StartShot != null && ++startFrames == 30) Gpu.ShotPath = Program.StartShot; // --startshot: picture of the start screen, then quit
        if (!World.Started && Program.StartShot != null && startFrames == 40) { Close(); return; }
        if (!World.Started) { Gpu.BeginFinal(13 / 255f, 18 / 255f, 38 / 255f); UI.Begin(w, h); UI.Draw(dt); UI.End(); UI.clicked = false; Gpu.EndFrame(); return; }

        var SET = Settings.SET; var kb = KeyboardState;
        // apply settings
        int rd = (int)SET.rd; if (rd != World.RD) { World.RD = rd; ChunkLoader.lastScan = new V3(1e9, 0, 0); }
        Renderer.LODK = SET.lod; Renderer.LODON = SET.lodOn > .5; Camera.C.fov = SET.fov * Math.PI / 180; Camera.SENS = SET.sens; Camera.FLYS = SET.fly; Sky.SetTilt(SET.tilt);

        ChunkLoader.frameNo++; if (SET.pause < .5) Sky.SUNA += dt * 2 * Math.PI / SET.day; Sky.SunVec(); Hud.UpdateFps(dt); Sky.AtmosphereStep(dt);
        Camera.C.asp = (double)w / h;
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
        if (Camera.C.orbit)
        {
            Camera.Drag(-sd * dt * 180, 0, 1); if (yaw != 0 || pit != 0) Camera.Drag(yaw * dt * 450, -pit * dt * 450, 1);
            double z = -fw * dt * 1.2; if (z != 0) Camera.Zoom(Math.Exp(z)); if (up != 0) Camera.Zoom(Math.Exp(-up * dt * 1.2));
        }
        else
        {
            if (yaw != 0 || pit != 0) Camera.Turn(yaw * 1.8 * dt, pit * 1.8 * dt);
            if (PlayerPhysics.P.on) PlayerPhysics.PlayerUpdate(dt, fw, sd, jump, sneak, ctrl);
            else { double alt = Math.Sqrt(Camera.C.x * Camera.C.x + Camera.C.y * Camera.C.y + Camera.C.z * Camera.C.z) - World.R, s = 12 * Camera.spd * Math.Max(1, alt / 25) * dt * (ctrl ? 5 : 1); Camera.CamMove(fw * s, sd * s, up * s, Camera.C.lvl); }
        }
        Camera.CamUpdate(dt); if (!Camera.C.orbit) ChunkLoader.LoadStep(); WaterSim.WaterTick(dt); BlockActions.Tick(dt); if (Program.Bench <= 0) WorldSave.Tick(dt);
        UI.shift = kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift);
        if (ChunkLoader.frameNo % 6 == 0) UI.underwater = !Camera.C.orbit && Blocks.Wet(BlockAccess.GetAt(Camera.C.x, Camera.C.y, Camera.C.z));
        // block editing
        editT -= dt; bool L = Captured && Program.Bench == 0 && MouseState.IsButtonDown(MouseButton.Left), Rt = Captured && Program.Bench == 0 && MouseState.IsButtonDown(MouseButton.Right);
        if (blockClick) { if (!MouseState.IsButtonDown(MouseButton.Left)) blockClick = false; L = false; }
        if ((L || Rt) && editT <= 0 && !Camera.C.orbit) { BlockEditing.Edit(L, Rt && !L, sneak, UI.rclicked); editT = .15; }
        if (Camera.C.orbit) Highlight.TG = null; else if (ChunkLoader.frameNo % 2 == 0) Highlight.TG = BlockEditing.Raycast(PlayerPhysics.P.on ? 6 : 10);

        Renderer.Prof(6, ref profT);
        long sceneT = System.Diagnostics.Stopwatch.GetTimestamp(); Renderer.Render(); Renderer.Phase[5] = 0; Renderer.Prof(5, ref sceneT);
        Renderer.Phase[5] -= Renderer.Phase[0] + Renderer.Phase[1] + Renderer.Phase[2] + Renderer.Phase[3] + Renderer.Phase[4]; profT = System.Diagnostics.Stopwatch.GetTimestamp();
        if (Program.Bench > 0) BenchStep(now);

        // UI
        if (++hudFrame % 10 == 0) hudCache = Hud.HudText(); UI.hud = hudCache;
        UI.Begin(w, h); UI.Draw(dt); UI.End();
        // click on the world while a menu is open: close it and capture the mouse again
        if (UI.clicked && !UI.startScreen && UI.settingsOpen && !UI.PanelHit) { UI.settingsOpen = false; blockClick = true; }
        UI.clicked = false; UI.rclicked = false;
        UpdateCursor(); Renderer.Prof(7, ref profT);
        Gpu.EndFrame(); Renderer.Prof(8, ref profT); frameEndT = profT; Array.Copy(Renderer.Phase, prevPhase, prevPhase.Length);
    }
}
