namespace CubePlanet;

/// <summary>Start screen (radius + seed).</summary>
public static partial class UI
{
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
