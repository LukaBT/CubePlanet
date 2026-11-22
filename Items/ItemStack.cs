namespace CubePlanet;

/// <summary>A stack of items: item ID, count and the item's packed instance state (bucket fill, durability...; see StateSchema, 32 bits).</summary>
public sealed class ItemStack
{
    public int Item, Count; public uint State;
    public ItemStack(int item, int count, uint state = 0) { Item = item; Count = count; State = state; }
    public bool IsEmpty => Item == 0 || Count <= 0;
    public ItemDefinition Def => GameAssets.ItemReg.Get(Item);
    public int MaxStack => Math.Max(1, Def?.StackSize ?? 64);
    public ItemStack Clone() => new(Item, Count, State);
    /// <summary>Same item and state, so the two can share one stack.</summary>
    public bool Stacks(ItemStack o) => o != null && !o.IsEmpty && o.Item == Item && o.State == State;
    public override string ToString() => $"{Def?.IdName ?? "item_" + Item} x{Count}";
}
