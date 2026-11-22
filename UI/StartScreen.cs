namespace CubePlanet;

/// <summary>Start screen: saved worlds (play / delete) and a new world (name, radius, seed).</summary>
public static partial class UI
{
    public static string nameStr = "";
    /// <summary>Set by the window: start a saved world, or a new one (null) from nameStr / radiusStr / seedStr.</summary>
    public static Action<WorldSave.Info> OnStart;
    static List<WorldSave.Info> worlds; static int armedDelete = -1; static double armedT;
    public static void RefreshWorlds() { worlds = WorldSave.List(); armedDelete = -1; }

    static void DrawStart()
    {
        worlds ??= WorldSave.List();
        Rect(0, 0, W, H, 13 / 255f, 18 / 255f, 38 / 255f, 1); double cx = W / 2.0, y = H * .07, s = S;
        Text("CUBE PLANET", cx, y, s * 4, 1, 1, 1, 1, true); y += 42 * s;
        // ---- saved worlds
        double lw = Math.Min(W - 40, 620 * s), lx = cx - lw / 2, rh = 16 * s;
        Text(worlds.Count > 0 ? "WORLDS" : "NO SAVED WORLDS YET", lx, y, s * 1.4, 1, .85f, .4f); y += 18 * s;
        if (armedDelete >= 0 && (armedT -= 1 / 60.0) < 0) armedDelete = -1;
        for (int i = 0; i < worlds.Count && i < 8; i++)
        {
            var w = worlds[i]; bool hov = In(lx, y, lw, rh);
            Rect(lx, y, lw, rh - 2 * s, 1, 1, 1, hov ? .14f : .06f);
            Text($"{w.Name}", lx + 6 * s, y + 3 * s, s * 1.2, 1, 1, 1);
            Text($"R {w.Radius:0}  SEED {w.Seed}  {w.LastPlayed:yyyy-MM-dd HH:mm}", lx + lw * .42, y + 4 * s, s, .65f, .75f, .85f);
            double bw = 54 * s, bx = lx + lw - 2 * bw - 8 * s;
            if (Button("PLAY", bx, y, bw, rh - 2 * s, .3f, .6f, .35f)) { OnStart?.Invoke(w); return; }
            if (Button(armedDelete == i ? "SURE?" : "DELETE", bx + bw + 4 * s, y, bw, rh - 2 * s, .6f, .25f, .25f))
            {
                if (armedDelete == i) { WorldSave.Delete(w); RefreshWorlds(); return; }
                armedDelete = i; armedT = 3;
            }
            y += rh;
        }
        y += 14 * s;
        // ---- new world
        Text("NEW WORLD", lx, y, s * 1.4, 1, .85f, .4f); y += 18 * s;
        string[] labels = { "NAME", "RADIUS", "SEED" }, vals = { nameStr.Length > 0 ? nameStr : (field == 0 ? "" : "(automatic)"), radiusStr, seedStr };
        for (int i = 0; i < 3; i++)
        {
            bool on = field == i; double fx = lx + 90 * s, fw = lw * .5;
            Text(labels[i], lx + 6 * s, y + 3 * s, s * 1.2, .8f, .8f, .9f);
            Rect(fx, y, fw, rh - 2 * s, 1, 1, 1, on ? .2f : .08f);
            Text(vals[i] + (on ? "_" : ""), fx + 4 * s, y + 3 * s, s * 1.2, on ? 1f : .7f, on ? 1f : .7f, 1);
            if (clicked && In(fx, y, fw, rh)) { field = i; clicked = false; }
            y += rh;
        }
        if (Button("CREATE", lx + 90 * s, y + 2 * s, 80 * s, rh, .3f, .5f, .8f)) { OnStart?.Invoke(null); return; }
        y += rh + 14 * s;
        Text("TAB SWITCHES FIELD, ENTER CREATES (RADIUS 200-10000000). WORLDS SAVE EVERY MINUTE AND ON EXIT.", cx, y, s, .6f, .8f, .6f, 1, true); y += 20 * s;
        string[] help = { "WASD WALK  SPACE JUMP (DOUBLE TAP = FLY)  CTRL OR DOUBLE TAP W SPRINT  SHIFT SNEAK/DOWN",
            "WHEEL OR 1-9,0 HOTBAR   LMB BREAK   RMB PLACE / USE   MMB PICK BLOCK   E INVENTORY", "V PLAYER/FREE CAMERA   F FLY   O PLANET ORBIT VIEW",
            "ESC / TAB = SETTINGS (RENDER DISTANCE, LOD, FOV, TIME, WATER...)   F11 FULLSCREEN" };
        foreach (var l in help) { Text(l, cx, y, s * 1.0, .6f, .7f, .8f, 1, true); y += 13 * s; }
    }
    static bool Button(string label, double x, double y, double w, double h, float r, float g, float b)
    {
        bool hov = In(x, y, w, h);
        Rect(x, y, w, h, r, g, b, hov ? 1 : .7f); Text(label, x + w / 2, y + (h - GH * S) / 2, S, 1, 1, 1, 1, true);
        if (hov && clicked) { clicked = false; return true; }
        return false;
    }
}
