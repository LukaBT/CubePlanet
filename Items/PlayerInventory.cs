namespace CubePlanet;

/// <summary>
/// The player's items: a 10-slot hotbar (keys 1-9, 0 and the mouse wheel select) and 27 more slots. Creative rules for now: placing blocks does
/// not use them up and the E palette hands out any item.
/// </summary>
public static class PlayerInventory
{
    public static readonly ItemStack[] Hotbar = new ItemStack[10], Main = new ItemStack[27];
    /// <summary>Selected hotbar slot, 0..9.</summary>
    public static int Selected = 1;
    public static ItemStack Held => Hotbar[Selected];
    /// <summary>Block the held item places (0 if none).</summary>
    public static int HeldBlock => Held is { IsEmpty: false } h ? h.Def?.PlacesBlockId ?? 0 : 0;

    /// <summary>Starting hotbar: the same blocks the old block hotbar had, plus the bucket.</summary>
    public static void Init()
    {
        string[] start = { "grass", "stone", "dirt", "oak_log", "oak_leaves", "ice", "sand", "snow", "bucket", "water" };
        for (int i = 0; i < start.Length; i++)
            if (GameAssets.ItemReg.Has(start[i])) { var d = GameAssets.ItemReg.Get(GameAssets.ItemReg.Id(start[i])); Hotbar[i] = new ItemStack(d.Id, d.StackSize); }
    }
    public static ItemStack[] Slots(string name) => name switch { "hotbar" => Hotbar, "main" => Main, _ => null };

    /// <summary>Adds a stack (merging into matching stacks first, hotbar before main). What does not fit stays in s; true if all fit.</summary>
    public static bool Add(ItemStack s) => AddTo(s, Hotbar, Main);
    public static bool AddTo(ItemStack s, params ItemStack[][] lists)
    {
        if (s == null || s.IsEmpty) return true;
        foreach (var l in lists) for (int i = 0; i < l.Length && s.Count > 0; i++)
                if (l[i] != null && l[i].Stacks(s)) { int n = Math.Min(s.Count, l[i].MaxStack - l[i].Count); l[i].Count += n; s.Count -= n; }
        foreach (var l in lists) for (int i = 0; i < l.Length && s.Count > 0; i++)
                if (l[i] == null || l[i].IsEmpty) { int n = Math.Min(s.Count, s.MaxStack); l[i] = new ItemStack(s.Item, n, s.State); s.Count -= n; }
        return s.Count == 0;
    }
    /// <summary>Puts an item for a block into the selected slot (middle click), or selects the hotbar slot that already holds it.</summary>
    public static void PickBlock(int blockId)
    {
        int item = GameAssets.ItemReg.ItemForBlock(blockId); if (item == 0) return;
        for (int i = 0; i < Hotbar.Length; i++) if (Hotbar[i] != null && Hotbar[i].Item == item) { Selected = i; return; }
        var d = GameAssets.ItemReg.Get(item); Hotbar[Selected] = new ItemStack(item, d.StackSize);
    }
}
