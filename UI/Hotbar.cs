namespace CubePlanet;

/// <summary>Hotbar and block palette.</summary>
public static partial class UI
{
    public static readonly int[] HB = { 1, 2, 3, 4, 5, 6, 7, 8, WaterSim.BK, 9 };

    // ---------- hotbar / palette ----------
    public static void SetSlotBlock(int id)
    {
        int i = Array.IndexOf(HB, id);
        if (i >= 0) Pick(i + 1); else { HB[BlockEditing.slot - 1] = id; BlockEditing.sel = id; }
    }
    public static void Pick(int i) { BlockEditing.slot = i; BlockEditing.sel = HB[i - 1]; }
    public static void Note(string msg, double secs = 5) { toast = msg; toastT = secs; }

    static void DrawHotbar()
    {
        int s = S, isz = 30 * s, gap = 3 * s, bd = 2 * s, tot = 10 * (isz + 2 * bd) + 9 * gap; double x0 = (W - tot) / 2.0, y0 = H - 6 * s - isz - 2 * bd;
        for (int i = 0; i < 10; i++)
        {
            double x = x0 + i * (isz + 2 * bd + gap); bool sel = i == BlockEditing.slot - 1;
            Rect(x, y0, isz + 2 * bd, isz + 2 * bd, 1, 1, 1, sel ? 1 : .27f);
            ItemIcon(HB[i], x + bd, y0 + bd, isz);
            if (In(x, y0, isz + 2 * bd, isz + 2 * bd)) Text(ItemName(HB[i]), mx + 12, my - 14 * s, s, 1, 1, 1);
            if (clicked && In(x, y0, isz + 2 * bd, isz + 2 * bd)) { Pick(i + 1); clicked = false; }
        }
    }
    static void DrawInventory()
    {
        int s = S, cell = 36 * s + 4 * s, cols = Math.Max(4, Math.Min(16, (int)((W * .92) / cell))); int rows = (World.NT + cols - 1) / cols;
        double pw = cols * cell + 12 * s, ph = rows * cell + 12 * s + 14 * s, x0 = (W - pw) / 2, y0 = (H - ph) / 2;
        Rect(x0, y0, pw, ph, 0, 0, 0, .85f); Text("BLOCK PALETTE (E TO CLOSE) - CLICK TO ASSIGN TO SELECTED SLOT", x0 + 6 * s, y0 + 4 * s, s * .8, .8f, .9f, 1);
        for (int id = 1; id < World.NT; id++)
        {
            int k = id - 1; double x = x0 + 6 * s + (k % cols) * cell, y = y0 + 16 * s + (k / cols) * cell;
            bool hov = In(x, y, 36 * s, 36 * s); if (hov) Rect(x - 2 * s, y - 2 * s, 40 * s, 40 * s, 1, 1, 1, .5f);
            Icon(id, x, y, 36 * s);
            if (hov && clicked) { HB[BlockEditing.slot - 1] = id; BlockEditing.sel = id; invOpen = false; clicked = false; }
            if (hov) Text(Blocks.BN[id].Replace('_', ' '), mx + 12, my + 12, s, 1, 1, 1);
        }
        { // the bucket sits after the last block
            int k = World.NT - 1; double x = x0 + 6 * s + (k % cols) * cell, y = y0 + 16 * s + (k / cols) * cell; bool hov = In(x, y, 36 * s, 36 * s);
            if (hov) Rect(x - 2 * s, y - 2 * s, 40 * s, 40 * s, 1, 1, 1, .5f);
            BucketIcon(x, y, 36 * s);
            if (hov) Text("bucket (break = scoop water, place = pour)", mx + 12, my + 12, s, 1, 1, 1);
            if (hov && clicked) { HB[BlockEditing.slot - 1] = WaterSim.BK; BlockEditing.sel = WaterSim.BK; invOpen = false; clicked = false; }
        }
    }
}
