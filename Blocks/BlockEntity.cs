namespace CubePlanet;

/// <summary>
/// The big part of a block's instance state: inventories and numbers (furnace slots + cook progress, a chest's 27 slots, a machine's energy
/// and tanks). Created from the block's entity fields in instance_state; numbers start at their "default".
/// </summary>
public sealed class BlockEntity
{
    public readonly BlockPos Pos; public readonly int BlockId;
    public readonly Dictionary<string, ItemStack[]> Inventories = new();
    public readonly Dictionary<string, double> Numbers = new();

    public BlockEntity(BlockPos pos, int blockId, StateSchema schema)
    {
        Pos = pos; BlockId = blockId;
        foreach (var f in schema.Entity)
        {
            if (f.Type == "inventory") Inventories[f.Name] = new ItemStack[f.Slots];
            else Numbers[f.Name] = f.DefNumber;
        }
    }
    public double Num(string name) => Numbers.TryGetValue(name, out double v) ? v : 0;
    public void SetNum(string name, double v) => Numbers[name] = v;
    public ItemStack[] Inv(string name) => Inventories.TryGetValue(name, out var a) ? a : null;
    /// <summary>Every item stack this entity holds (dropped / lost when the block is broken).</summary>
    public IEnumerable<ItemStack> AllItems() => Inventories.Values.SelectMany(a => a).Where(s => s != null && !s.IsEmpty);
}
