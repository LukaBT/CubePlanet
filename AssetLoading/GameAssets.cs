namespace CubePlanet;

/// <summary>
/// Loads the 7 editor files from Assets/ at startup (before anything else runs): registries for blocks, items and UIs, and the block texture
/// layers. Then builds the per-block tables in <see cref="Blocks"/> and resolves the named block IDs code uses (Blocks.Stone, ...).
/// </summary>
public static class GameAssets
{
    public static BlockRegistry BlockReg;
    public static ItemRegistry ItemReg;
    public static UiRegistry UiReg;
    /// <summary>Block texture array: RGBA, layer-major, row 0 = bottom of the face.</summary>
    public static byte[] BlockLayers; public static int BlockLayerCount, BlockTexW, BlockTexH;

    public static void Load()
    {
        var bdefs = DefinitionFile.Load(AssetPaths.Get(AssetPaths.BlockDefinitions), "blocks").Select(e => BlockDefinition.Parse(e, AssetPaths.BlockDefinitions));
        BlockReg = new BlockRegistry(bdefs, AssetPaths.BlockDefinitions);
        var idefs = DefinitionFile.Load(AssetPaths.Get(AssetPaths.ItemDefinitions), "items").Select(e => ItemDefinition.Parse(e, AssetPaths.ItemDefinitions));
        ItemReg = new ItemRegistry(idefs, BlockReg, AssetPaths.ItemDefinitions);
        UiReg = new UiRegistry(DefinitionFile.Load(AssetPaths.Get(AssetPaths.UiDefinitions), "uis"), AssetPaths.UiDefinitions);

        var tdef = TextureDefinitionFile.Load(AssetPaths.Get(AssetPaths.BlockTextureDefinitions));
        BlockTexW = tdef.Width; BlockTexH = tdef.Height;
        BlockLayers = AtlasLoader.LoadLayers(AssetPaths.Get(AssetPaths.BlockAtlas), tdef, out BlockLayerCount);
        foreach (var b in BlockReg.All)
            foreach (var (slot, t) in b.Textures)
                if (t < 1 || t >= BlockLayerCount || tdef.Entries.All(e => e.id != t))
                    Console.Error.WriteLine($"warning: block {b.FullName} texture '{slot}' = {t} is not in {AssetPaths.BlockTextureDefinitions} (shows as a checkerboard)");
        Blocks.Build(BlockReg);
    }
}
