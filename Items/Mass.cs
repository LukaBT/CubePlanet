namespace CubePlanet;

/// <summary>
/// Every block and item has a mass (kg), for rockets and anything else that has to be lifted. The rule for content:
/// <list type="bullet">
/// <item>A block is 1 m^3. Its mass is the density of what it is made of x the volume it really fills: a stone block is 2,700 kg, an iron
/// block would be 7,870 kg (so nobody builds a rocket out of them); a steel plate one pixel (1/16 block) thick is 491 kg; a slab is half
/// its block; leaves, plants and machines are what they would weigh (a machine is mostly air inside a steel case).</item>
/// <item>A block's mass is properties.definition.mass (the whole block; a multiblock: all its parts). The item that places it weighs the
/// same; any other item has its own properties.definition.mass.</item>
/// <item>No machine or recipe makes mass: what comes out (items, gases into pipes) never weighs more than what goes in (items, gases and
/// liquids taken from pipes; burnt fuel does not count). The rest is what a real process loses (gases into the air, water, dust).
/// The self-test checks every recipe.</item>
/// </list>
/// Common sizes: ingot 1/64 m^3 (a 25 cm cube: steel 123 kg, copper 140 kg), plate 1/16 block thick (4 ingots), rod 1/16 x 1/16 x 1
/// (1/4 ingot), crushed ore / concentrate / slag / tailings 250 kg, gold bar 1 kg; liquids 1,000 kg per full cell of water (125 kg a unit).
/// </summary>
public static class Mass
{
    public static double OfBlock(int id) => id > 0 && id < DEFS.Length && DEFS[id] != null ? DEFS[id].Mass : 0;
    public static double OfItem(int id)
    {
        var d = GameAssets.ItemReg?.Get(id); if (d == null) return 0;
        return d.Mass > 0 ? d.Mass : d.PlacesBlockId > 0 ? OfBlock(d.PlacesBlockId) : 0;
    }
    public static double Of(ItemStack s) => s == null || s.IsEmpty ? 0 : OfItem(s.Item) * s.Count;
    /// <summary>"12 G", "491 KG", "7.87 T".</summary>
    public static string Text(double kg) => kg < 1 ? $"{kg * 1000:0.#} G" : kg < 1000 ? $"{kg:0.#} KG" : kg < 1e6 ? $"{kg / 1000:0.##} T" : $"{kg / 1e6:0.##} KT";
}
