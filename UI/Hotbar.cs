namespace CubePlanet;

/// <summary>Hotbar (the player inventory's 10 hotbar slots) and item icons.</summary>
public static partial class UI
{
    /// <summary>Selects hotbar slot i (1..10).</summary>
    public static void Pick(int i) => PlayerInventory.Selected = Math.Clamp(i - 1, 0, 9);
    public static void Note(string msg, double secs = 5) { toast = msg; toastT = secs; }

    static void DrawHotbar()
    {
        int s = S, isz = 30 * s, gap = 3 * s, bd = 2 * s, tot = 10 * (isz + 2 * bd) + 9 * gap; double x0 = (W - tot) / 2.0, y0 = H - 6 * s - isz - 2 * bd;
        for (int i = 0; i < 10; i++)
        {
            double x = x0 + i * (isz + 2 * bd + gap); bool sel = i == PlayerInventory.Selected;
            Rect(x, y0, isz + 2 * bd, isz + 2 * bd, 1, 1, 1, sel ? 1 : .27f);
            Rect(x + bd, y0 + bd, isz, isz, 0, 0, 0, .55f);
            var st = PlayerInventory.Hotbar[i];
            if (st != null && !st.IsEmpty)
            {
                ItemIcon(st, x + bd, y0 + bd, isz, false);
                if (screen == null && In(x, y0, isz + 2 * bd, isz + 2 * bd)) Text(StackName(st), mx + 12, my - 14 * s, s, 1, 1, 1);
            }
            if (screen == null && clicked && In(x, y0, isz + 2 * bd, isz + 2 * bd)) { Pick(i + 1); clicked = false; }
        }
        var held = PlayerInventory.Held;
        if (screen == null && held != null && !held.IsEmpty) Text(StackName(held), W / 2.0, y0 - 12 * s, s, 1, 1, 1, .85f, true);
    }

    /// <summary>Item icon, drawn live (see ItemIcons; texture row 0 is the bottom, so v is flipped), bucket fill bar, stack count.</summary>
    static readonly int[] QuadTris = { 0, 1, 2, 0, 2, 3 };
    public static void ItemIcon(ItemStack st, double x, double y, double s, bool count = true)
    {
        var d = st.Def;
        switch (d == null ? "texture" : ItemIcons.KindOf(d))
        {
            case "model": // the block's 3D model, face by face with the block textures (back to front)
                foreach (var q in ItemIcons.Quads(d))
                {
                    if (q.Tex <= 0) continue; double k = s / 32; float sh = q.Shade;
                    foreach (int c in QuadTris) V(x + q.X[c] * k, y + q.Y[c] * k, q.U[c], q.V[c], sh, sh, sh, 1, 2, q.Tex);
                }
                break;
            case "sprite": // the block's texture, flat
                { int bt = ItemIcons.SpriteTexture(d);
                  V(x, y, 0, 1, 1, 1, 1, 1, 2, bt); V(x + s, y, 1, 1, 1, 1, 1, 1, 2, bt); V(x + s, y + s, 1, 0, 1, 1, 1, 1, 2, bt);
                  V(x, y, 0, 1, 1, 1, 1, 1, 2, bt); V(x + s, y + s, 1, 0, 1, 1, 1, 1, 2, bt); V(x, y + s, 0, 0, 1, 1, 1, 1, 2, bt); }
                break;
            default: // the item's own picture from the item atlas
                { int t = st.Item;
                  V(x, y, 0, 1, 1, 1, 1, 1, 3, t); V(x + s, y, 1, 1, 1, 1, 1, 1, 3, t); V(x + s, y + s, 1, 0, 1, 1, 1, 1, 3, t);
                  V(x, y, 0, 1, 1, 1, 1, 1, 3, t); V(x + s, y + s, 1, 0, 1, 1, 1, 1, 3, t); V(x, y + s, 0, 0, 1, 1, 1, 1, 3, t); }
                break;
        }
        if (d?.Behavior == "bucket" && d.State.Find("fill") is { } f)
        {
            double fill = f.Value(st.State) / (double)Math.Max(1, d.Capacity), k = s / 16;
            if (fill > 0) Rect(x + 4 * k, y + (13 - 7 * fill) * k, 8 * k, 7 * fill * k, .24f, .55f, 1f, 1);
        }
        if (count && st.Count > 1) { string c = st.Count.ToString(); double sc = Math.Max(1, s / 18); Text(c, x + s - TextW(c, sc) - 1, y + s - GH * sc, sc, 1, 1, 1); }
    }
}
