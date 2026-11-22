using System.Text.Json;
using static CubePlanet.DefinitionFile;

namespace CubePlanet;

/// <summary>
/// Windows built from ui_definitions.txt: "window" {width, height} and "widgets" (label, slot, slot_grid, progress_bar, ui_panel), positions in UI
/// units (1 unit = 2 * UI scale pixels). Slot storage: {"scope": "block", "key": "fuel"} / "slots[3]" (the opened block's entity inventory) or
/// "scope": "player" with "hotbar[i]" / "main[i]"; slot_grid uses "key_pattern" with {index} (plus optional "start_index"). Progress bars:
/// "current_from": "block.progress" and "max" or "max_from": "block.cook_time". Mouse: left click picks up / puts down / swaps a stack, right click
/// takes half / puts one, shift + left click moves a stack between the block and the player inventory. E opens the player inventory with the
/// creative item palette next to it.
/// </summary>
public static partial class UI
{
    sealed class Widget
    {
        public string Type; public double X, Y, W, H; public string Text;
        public ItemStack[] Store; public int Index; public bool OutputOnly, PlayerSlot;
        public string Current, MaxFrom; public double Max = 1; public bool Vertical; public float R = .95f, G = .55f, B = .15f;
    }
    sealed class Screen { public string Title; public double W, H; public readonly List<Widget> Widgets = new(); public BlockPos Pos; public int BlockId; public BlockEntity Entity; public bool Palette; }

    static Screen screen;
    /// <summary>The stack held by the mouse while a window is open.</summary>
    public static ItemStack cursor;
    public static bool shift;
    public static bool invOpen => screen != null;
    static double paletteScroll;

    public static void OpenInventory()
    {
        settingsOpen = false;
        screen = Build(GameAssets.UiReg.Get("player_inventory"), null, default, 0); screen.Palette = true;
    }
    public static void OpenBlockUi(string ui, BlockPos p, int id)
    {
        settingsOpen = false;
        var e = BlockStates.EntityOrNew(p, id);
        screen = Build(GameAssets.UiReg.Get(ui), e, p, id);
    }
    public static void CloseScreen()
    {
        if (cursor != null && !cursor.IsEmpty) PlayerInventory.Add(cursor); // what does not fit is lost (creative)
        cursor = null; screen = null;
    }
    public static void ToggleInventory() { if (screen != null) CloseScreen(); else OpenInventory(); }

    static Screen Build(UiRegistry.UiDefinition def, BlockEntity e, BlockPos p, int id)
    {
        var s = new Screen { Entity = e, Pos = p, BlockId = id, W = Num(def.Json, "window.width", 196), H = Num(def.Json, "window.height", 166) };
        s.Title = DEFS[id]?.Name ?? "Inventory";
        AddWidgets(s, def, 0, 0, 0);
        return s;
    }
    static void AddWidgets(Screen s, UiRegistry.UiDefinition def, double ox, double oy, int depth)
    {
        if (Get(def.Json, "widgets") is not { ValueKind: JsonValueKind.Array } ws) return;
        foreach (var w in ws.EnumerateArray())
        {
            string type = Str(w, "type"); double x = ox + Num(w, "position.x"), y = oy + Num(w, "position.y");
            switch (type)
            {
                case "label": s.Widgets.Add(new Widget { Type = type, X = x, Y = y, Text = Str(w, "text") }); break;
                case "slot":
                    {
                        var (store, idx, pl) = Storage(s, Str(w, "storage.scope", "block"), Str(w, "storage.key"), 0);
                        s.Widgets.Add(new Widget { Type = "slot", X = x, Y = y, W = 18, H = 18, Store = store, Index = idx, PlayerSlot = pl, OutputOnly = Str(w, "mode") == "output_only", Text = Str(w, "name") });
                        break;
                    }
                case "slot_grid":
                    {
                        int rows = (int)Num(w, "rows", 1), cols = (int)Num(w, "columns", 1), start = (int)Num(w, "start_index"); double size = Num(w, "slot_size", 18);
                        string scope = Str(w, "storage.scope", "block"), pat = Str(w, "storage.key_pattern");
                        for (int r = 0; r < rows; r++) for (int c = 0; c < cols; c++)
                            {
                                var (store, idx, pl) = Storage(s, scope, pat, start + r * cols + c);
                                s.Widgets.Add(new Widget { Type = "slot", X = x + c * size, Y = y + r * size, W = 18, H = 18, Store = store, Index = idx, PlayerSlot = pl });
                            }
                        break;
                    }
                case "progress_bar":
                    s.Widgets.Add(new Widget { Type = type, X = x, Y = y, W = Num(w, "size.width", 24), H = Num(w, "size.height", 8), Current = Str(w, "current_from"), MaxFrom = Str(w, "max_from"), Max = Num(w, "max", 1), Vertical = Str(w, "orientation") == "vertical", Text = Str(w, "label") });
                    if (Get(w, "color") is { ValueKind: JsonValueKind.Array } col && col.GetArrayLength() >= 3) { var cw = s.Widgets[^1]; var cv = col.EnumerateArray().Select(v => (float)(v.GetDouble() / 255)).ToArray(); cw.R = cv[0]; cw.G = cv[1]; cw.B = cv[2]; }
                    break;
                case "ui_panel":
                    if (depth < 4) AddWidgets(s, GameAssets.UiReg.Get(Str(w, "ui_id.namespace", "base") + ":" + Str(w, "ui_id.id_name")), x, y, depth + 1);
                    break;
            }
        }
    }
    /// <summary>Resolves a storage key ("fuel", "slots[{index}]", "hotbar[3]") to an inventory array + index.</summary>
    static (ItemStack[] store, int idx, bool player) Storage(Screen s, string scope, string key, int index)
    {
        key = key.Replace("{index}", index.ToString());
        if (key.StartsWith("inventory.")) key = key[10..]; // the editor's default "inventory.slot" form
        string name = key; int idx = 0; int b = key.IndexOf('[');
        if (b >= 0) { name = key[..b]; int.TryParse(key[(b + 1)..].TrimEnd(']'), out idx); }
        if (scope == "player") { var a = PlayerInventory.Slots(name); return (a, a != null && idx < a.Length ? idx : -1, true); }
        var inv = s.Entity?.Inv(name); return (inv, inv != null && idx < inv.Length ? idx : -1, false);
    }

    static double EntNum(BlockEntity e, string from) => from.StartsWith("block.") && e != null ? e.Num(from[6..]) : 0;

    // ---------- drawing + mouse ----------
    static void DrawScreen()
    {
        var s = screen; if (s == null) return;
        if (s.Entity != null && (s.Pos.Id != s.BlockId)) { CloseScreen(); return; } // the block was removed
        Rect(0, 0, W, H, 0, 0, 0, .45f);
        double k = Math.Max(1, Math.Min(2 * S, Math.Min(W * .9 / (s.W + (s.Palette ? 230 : 0)), H * .9 / s.H)));
        double pw = s.Palette ? 220 * k : 0, wx = (W - s.W * k - pw) / 2 + pw, wy = (H - s.H * k) / 2;
        Rect(wx - 2, wy - 2, s.W * k + 4, s.H * k + 4, .55f, .55f, .6f, 1); Rect(wx, wy, s.W * k, s.H * k, .16f, .16f, .19f, .97f);
        Widget hover = null;
        foreach (var w in s.Widgets)
        {
            double x = wx + w.X * k, y = wy + w.Y * k;
            switch (w.Type)
            {
                case "label": Text(w.Text, x, y, k * .75, .9f, .9f, .9f); break;
                case "slot":
                    {
                        bool hv = In(x, y, 18 * k, 18 * k); if (hv) hover = w;
                        Rect(x, y, 18 * k, 18 * k, .32f, .32f, .36f, 1); Rect(x + k, y + k, 16 * k, 16 * k, hv ? .3f : .1f, hv ? .3f : .1f, hv ? .34f : .12f, 1);
                        var st = w.Index >= 0 ? w.Store[w.Index] : null;
                        if (st != null && !st.IsEmpty) ItemIcon(st, x + k, y + k, 16 * k);
                        if (w.Store != null && w.Store == PlayerInventory.Hotbar && w.Index == PlayerInventory.Selected) Rect(x, y + 17 * k, 18 * k, k, 1, 1, 1, .8f);
                        break;
                    }
                case "progress_bar":
                    {
                        double max = w.MaxFrom.Length > 0 ? EntNum(s.Entity, w.MaxFrom) : w.Max, t = max > 0 ? Math.Clamp(EntNum(s.Entity, w.Current) / max, 0, 1) : 0;
                        Rect(x, y, w.W * k, w.H * k, .08f, .08f, .1f, 1);
                        if (w.Vertical) Rect(x, y + w.H * k * (1 - t), w.W * k, w.H * k * t, w.R, w.G, w.B, 1); else Rect(x, y, w.W * k * t, w.H * k, w.R, w.G, w.B, 1);
                        if (!string.IsNullOrEmpty(w.Text)) Text(w.Text, x, y - 7 * k, k * .55, .7f, .7f, .75f);
                        break;
                    }
            }
        }
        Text(s.Title.ToUpperInvariant() + "   (E / ESC: CLOSE)", wx, wy - 10 * k, k * .7, .9f, .85f, .5f);
        if (s.Palette) { double ph = Math.Min(H * .85, Math.Max(s.H, 200) * k); DrawPalette(wx - pw, (H - ph) / 2, 210 * k, ph, k); }
        if (hover != null && (clicked || rclicked)) { SlotClick(s, hover, clicked); clicked = rclicked = false; }
        // tooltip and the stack on the mouse
        var hs = hover != null && hover.Index >= 0 ? hover.Store[hover.Index] : null;
        if (cursor != null && !cursor.IsEmpty) ItemIcon(cursor, mx - 8 * k, my - 8 * k, 16 * k);
        else if (hs != null && !hs.IsEmpty) Text(StackName(hs), mx + 12, my - 12 * S, S, 1, 1, 1);
        if (clicked && !In(wx, wy, s.W * k, s.H * k)) clicked = false; // clicks beside the window do nothing
    }

    static void SlotClick(Screen s, Widget w, bool left)
    {
        if (w.Index < 0) return;
        ref ItemStack slot = ref w.Store[w.Index];
        bool empty = slot == null || slot.IsEmpty, hand = cursor != null && !cursor.IsEmpty;
        if (left && shift && !empty && !hand)
        {   // quick move between the block inventory and the player inventory
            if (w.PlayerSlot && s.Entity != null) { var target = s.Widgets.Where(o => o.Type == "slot" && !o.PlayerSlot && !o.OutputOnly && o.Index >= 0).Select(o => o).ToList(); MoveInto(ref slot, target); }
            else if (!w.PlayerSlot) { var mv = slot.Clone(); PlayerInventory.Add(mv); slot = mv.Count > 0 ? mv : null; }
            return;
        }
        if (left)
        {
            if (!hand) { cursor = slot; slot = null; }
            else if (w.OutputOnly) { if (!empty && slot.Stacks(cursor) && cursor.Count + slot.Count <= cursor.MaxStack) { cursor.Count += slot.Count; slot = null; } }
            else if (empty) { slot = cursor; cursor = null; }
            else if (slot.Stacks(cursor)) { int n = Math.Min(cursor.Count, slot.MaxStack - slot.Count); slot.Count += n; cursor.Count -= n; if (cursor.Count <= 0) cursor = null; }
            else (slot, cursor) = (cursor, slot);
        }
        else
        {
            if (!hand && !empty) { int half = (slot.Count + 1) / 2; cursor = new ItemStack(slot.Item, half, slot.State); slot.Count -= half; if (slot.Count <= 0) slot = null; }
            else if (hand && !w.OutputOnly && (empty || (slot.Stacks(cursor) && slot.Count < slot.MaxStack)))
            {
                if (empty) slot = new ItemStack(cursor.Item, 1, cursor.State); else slot.Count++;
                if (--cursor.Count <= 0) cursor = null;
            }
        }
    }
    static void MoveInto(ref ItemStack src, List<Widget> targets)
    {
        foreach (var t in targets) { var d = t.Store[t.Index]; if (d != null && d.Stacks(src)) { int n = Math.Min(src.Count, d.MaxStack - d.Count); d.Count += n; src.Count -= n; if (src.Count == 0) { src = null; return; } } }
        foreach (var t in targets) if (t.Store[t.Index] == null || t.Store[t.Index].IsEmpty) { t.Store[t.Index] = src; src = null; return; }
    }

    /// <summary>Creative palette: every item; click = a full stack on the mouse (right click: one), click with a stack on the mouse = delete it.</summary>
    static void DrawPalette(double x0, double y0, double pw, double ph, double k)
    {
        Rect(x0 - 2, y0 - 2, pw + 4, ph + 4, .55f, .55f, .6f, 1); Rect(x0, y0, pw, ph, .12f, .12f, .14f, .97f);
        Text("ITEMS (CLICK: TAKE A STACK)", x0 + 4 * k, y0 + 3 * k, k * .6, .8f, .9f, 1);
        var items = GameAssets.ItemReg.All; int cols = 11; double cs = (pw - 8 * k) / cols; int rows = (items.Count + cols - 1) / cols;
        double top = y0 + 12 * k, view = ph - 16 * k, maxScroll = Math.Max(0, rows * cs - view);
        if (In(x0, y0, pw, ph)) { paletteScroll = Math.Clamp(paletteScroll - scroll * cs, 0, maxScroll); scroll = 0; }
        bool inside = In(x0, top, pw, view);
        if (inside && (clicked || rclicked) && cursor != null && !cursor.IsEmpty) { cursor = null; clicked = rclicked = false; } // trash
        for (int i = 0; i < items.Count; i++)
        {
            double x = x0 + 4 * k + (i % cols) * cs, y = top + (i / cols) * cs - paletteScroll;
            if (y < top - 1 || y + cs > top + view + 1) continue;
            bool hv = In(x, y, cs, cs); if (hv) Rect(x, y, cs, cs, 1, 1, 1, .25f);
            ItemIcon(new ItemStack(items[i].Id, 1), x + 1, y + 1, cs - 2, false);
            if (hv) { Text(items[i].Name, mx + 12, my - 12 * S, S, 1, 1, 1); if (clicked || rclicked) { cursor = new ItemStack(items[i].Id, clicked ? items[i].StackSize : 1); clicked = rclicked = false; } }
        }
    }

    /// <summary>Self-test hook: clicks the n-th slot of the open window that shows inventory 'store' ("hotbar", "main" or a block inventory name).</summary>
    public static bool TestClick(string store, int index, bool left, bool shiftKey = false)
    {
        if (screen == null) return false;
        var arr = PlayerInventory.Slots(store) ?? screen.Entity?.Inv(store);
        var w = screen.Widgets.FirstOrDefault(o => o.Type == "slot" && o.Store == arr && o.Index == index); if (w == null) return false;
        bool old = shift; shift = shiftKey; SlotClick(screen, w, left); shift = old; return true;
    }
    static string StackName(ItemStack s)
    {
        var d = s.Def; string n = d?.Name ?? "?";
        if (d?.Behavior == "bucket" && d.State.Find("fill") is { } f) n += $" ({f.Value(s.State)}/{Math.Max(1, d.Capacity)})";
        return s.Count > 1 ? $"{n} x{s.Count}" : n;
    }
}
