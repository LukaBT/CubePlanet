namespace CubePlanet;

/// <summary>Settings panel (Esc / Tab).</summary>
public static partial class UI
{
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
        var SET = Settings.SET; panelW = Math.Min(W - 16, 300 * S + 40); panelX = W - panelW - 8; double top = 8, ph = H - 16;
        Rect(panelX, top, panelW, ph, 0, 0, 0, .8f);
        scrollY = M.Clamp(scrollY - scroll * 24, 0, Math.Max(0, contentH - ph)); scroll = 0; sy0 = top + 6 - scrollY; wid = 0; double startY = sy0;
        Head("SETTINGS  (ESC: CLOSE)");
        Check("VSync ON/OFF  (F10)", ref SET.vsync);
        Slider("Render distance", ref SET.rd, 2, 12, 1); Slider("LOD detail", ref SET.lod, .3, 3, .1); Check("Distant terrain (LOD)", ref SET.lodOn);
        Slider("Field of view", ref SET.fov, 50, 110, 1); Slider("Mouse sensitivity", ref SET.sens, .2, 3, .1); Slider("Fly speed", ref SET.fly, 5, 300, 5);
        Slider("Clouds", ref SET.clouds, 0, 3, 1, v => new[] { "off", "low", "high", "ultra" }[(int)v]); Slider("Fog", ref SET.fog, 0, 2, .1);
        double hour = Sky.LocalHour(); if (Slider("Time of day", ref hour, 0, 23.99, .25, v => $"{(int)v:00}:{(int)((v % 1) * 60):00}")) Sky.SetHour(hour);
        Slider("Day length (s)", ref SET.day, 60, 1800, 30); Slider("Tick speed (farming)", ref SET.tick, 1, 50, 1, v => $"x{v:0}"); Check("Freeze time", ref SET.pause); Check("Realistic gravity (9.81 m/s2, floatier jumps)", ref SET.realgrav); Slider("Axial tilt (Earth=23.5)", ref SET.tilt, 0, 90, .5);
        Head("WIND");
        { double nat = Kinetics.IsNatural ? 1 : 0, spd = Kinetics.WindSpeed, from = Kinetics.FromBearing;
          if (Check("Natural wind (changes by itself)", ref nat)) { if (nat > .5) Kinetics.Natural(); else Kinetics.SetWind(Math.Round(spd), Math.Round(from / 5) * 5 % 360); }
          // moving a slider sets the wind (natural off)
          if (Slider("Wind speed", ref spd, 0, 20, .5, v => $"{v:0.0} m/s")) Kinetics.SetWind(spd, from);
          if (Slider("Wind from", ref from, 0, 355, 5, v => $"{v:0} deg ({Kinetics.WindFrom})")) Kinetics.SetWind(spd, from); }
        Head("GRAPHICS");
        Slider("Render resolution", ref SET.rscale, .3, 1, .05, v => $"{v * 100:0}% {(int)Math.Round(W * v)}x{(int)Math.Round(H * v)}");
        { int pr = SET.Preset(); if (Btn("Preset (click to change)", pr < 0 ? "CUSTOM" : Settings.PresetNames[pr])) SET.ApplyPreset(pr < 0 ? 2 : (pr + 1) % Settings.PresetNames.Length); }
        Slider("Shadows", ref SET.shadows, 0, 3, 1, v => new[] { "off", "near", "medium", "far" }[(int)v]); Slider("Shadow update (s)", ref SET.shInt, 0, 2, .05, v => v == 0 ? "every frame" : v.ToString("0.00") + "s");
        Check("Ambient occlusion (block corners)", ref SET.ao);
        Check(Renderer.AnisoOk ? "Anisotropic texture filtering" : "Anisotropic filtering (unsupported)", ref SET.aniso);
        Check("Bloom (glow)", ref SET.bloom); Check("God rays", ref SET.godrays); Check("Water reflections (SSR)", ref SET.ssr); Check("FXAA anti-aliasing", ref SET.fxaa);
        if (Btn($"Renderer (now {(Dx.On ? "DirectX 11" : "Vulkan")}; applies on restart)", SET.renderer > .5 ? "DIRECTX 11" : "VULKAN")) SET.renderer = SET.renderer > .5 ? 0 : 1;
        if (Btn("MSAA (applies on restart)", SET.msaa < 1 ? "OFF" : (int)SET.msaa + "X")) SET.msaa = SET.msaa < 1 ? 2 : SET.msaa < 3 ? 4 : SET.msaa < 6 ? 8 : 0;
        Head("WATER");
        Slider("Wave height", ref SET.wvA, 0, 2.5, .05); Slider("Wave speed", ref SET.wvSpd, 0, 4, .05); Slider("Wave length", ref SET.wvLen, .4, 3, .05);
        Slider("Crest sharpness", ref SET.wvK, 1, 3.5, .05); Slider("Wave direction", ref SET.wvDir, 0, 360, 5); Slider("Ripple detail", ref SET.wvRip, 0, 3, .05);
        Slider("Foam", ref SET.wvFoam, 0, 2, .05); Slider("Water opacity", ref SET.wvOp, .4, 1.5, .05);
        if (Btn("Water defaults", "RESET")) { SET.wvA = 1; SET.wvSpd = 1; SET.wvLen = 1; SET.wvK = 1.8; SET.wvDir = 0; SET.wvRip = 1; SET.wvFoam = 1; SET.wvOp = 1; }
        if (Btn("Display", "FULLSCREEN")) OnFullscreen?.Invoke();
        if (WorldSave.Current != null && Btn("World: " + WorldSave.CurrentName, "SAVE NOW")) { try { WorldSave.Save(); Note("World saved", 2); } catch (Exception ex) { Note("Saving failed: " + ex.Message, 6); } }
        if (Btn("Mode", PlayerPhysics.P.on ? "PLAYER" : "FREE CAMERA")) PlayerPhysics.SetMode(!PlayerPhysics.P.on);
        string[] help = { "WASD MOVE, SPACE JUMP (DOUBLE TAP = FLY)", "CTRL / DOUBLE TAP W SPRINT, SHIFT SNEAK/DOWN", "WHEEL OR 1-9,0 HOTBAR, LMB BREAK, RMB PLACE (BUCKET: SCOOP / POUR)", "MMB PICK BLOCK, V PLAYER/FREE CAM, F FLY", "O ORBIT VIEW, E BLOCK PALETTE, F10 VSYNC, F11 FULLSCREEN", "ESC OR TAB = THIS MENU. CLICK WORLD TO RESUME." };
        foreach (var l in help) { Text(l, panelX + 8, sy0, S * .75, .6f, .7f, .8f); sy0 += 9 * S; }
        contentH = sy0 - startY + 12;
    }
    static double contentH;
}
