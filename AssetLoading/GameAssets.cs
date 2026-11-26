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
    public static RecipeRegistry RecipeReg;
    public static SoundRegistry SoundReg;
    public static EntityRegistry EntityReg;
    /// <summary>Block texture array: RGBA, layer-major, row 0 = bottom of the face.</summary>
    public static byte[] BlockLayers; public static int BlockLayerCount, BlockTexW, BlockTexH;
    /// <summary>Item icons: RGBA, layer = item ID (= item texture ID).</summary>
    /// <summary>The conveyor's moving belt surface (block texture "conveyor_belt").</summary>
    public static int BeltTexture;
    static readonly Dictionary<string, int> blockTexByName = new();
    /// <summary>A block texture id by its name in block_texture_definitions.txt (0 if missing).</summary>
    public static int BlockTex(string name) => blockTexByName.TryGetValue(name, out int id) ? id : 0;
    /// <summary>Item pictures are also copied into the block texture array (after the block textures, scaled to the block texture size), so
    /// items can be drawn in the world (on conveyor belts): layer = <see cref="ItemWorldBase"/> + item texture id.</summary>
    public static int ItemWorldBase;
    public static float ItemWorldLayer(int item) => item > 0 && item < ItemLayerCount ? ItemWorldBase + item : 0;
    static void AppendItemLayers()
    {
        int bw = BlockTexW, iw = ItemTexW, n = ItemLayerCount; if (bw <= 0 || iw <= 0 || iw % bw != 0 || BlockTexH != bw || ItemTexH != iw) return;
        int f = iw / bw; var res = new byte[BlockLayers.Length + n * bw * bw * 4]; Buffer.BlockCopy(BlockLayers, 0, res, 0, BlockLayers.Length);
        ItemWorldBase = BlockLayerCount;
        for (int l = 0; l < n; l++)
            for (int y = 0; y < bw; y++)
                for (int x = 0; x < bw; x++)
                {
                    int r = 0, g = 0, b = 0, a = 0;
                    for (int sy = 0; sy < f; sy++) for (int sx = 0; sx < f; sx++)
                        {
                            int s = ((l * iw + y * f + sy) * iw + x * f + sx) * 4; int al = ItemLayers[s + 3];
                            r += ItemLayers[s] * al; g += ItemLayers[s + 1] * al; b += ItemLayers[s + 2] * al; a += al;
                        }
                    int o = BlockLayers.Length + ((l * bw + y) * bw + x) * 4;
                    if (a > 0) { res[o] = (byte)(r / a); res[o + 1] = (byte)(g / a); res[o + 2] = (byte)(b / a); }
                    res[o + 3] = (byte)(a / (f * f) >= 128 ? 255 : 0); // cut-out like the block textures
                }
        BlockLayers = res; BlockLayerCount += n;
    }
    public static byte[] ItemLayers; public static int ItemLayerCount, ItemTexW, ItemTexH;

    public static void Load()
    {
        var bdefs = DefinitionFile.Load(AssetPaths.Get(AssetPaths.BlockDefinitions), "blocks").Select(e => BlockDefinition.Parse(e, AssetPaths.BlockDefinitions));
        BlockReg = new BlockRegistry(bdefs, AssetPaths.BlockDefinitions);
        var idefs = DefinitionFile.Load(AssetPaths.Get(AssetPaths.ItemDefinitions), "items").Select(e => ItemDefinition.Parse(e, AssetPaths.ItemDefinitions));
        ItemReg = new ItemRegistry(idefs, BlockReg, AssetPaths.ItemDefinitions);
        UiReg = new UiRegistry(DefinitionFile.Load(AssetPaths.Get(AssetPaths.UiDefinitions), "uis"), AssetPaths.UiDefinitions);
        RecipeReg = new RecipeRegistry(DefinitionFile.Load(AssetPaths.Get(AssetPaths.RecipeDefinitions), "recipes"), ItemReg, AssetPaths.RecipeDefinitions);
        SoundReg = new SoundRegistry(DefinitionFile.Load(AssetPaths.Get(AssetPaths.SoundDefinitions), "sounds"), AssetPaths.SoundDefinitions);
        EntityReg = new EntityRegistry(DefinitionFile.Load(AssetPaths.Get(AssetPaths.EntityDefinitions), "entities"));
        foreach (var b in BlockReg.All)
        {
            foreach (var (k, snd) in b.Sounds) if (!SoundReg.Has(snd)) throw new InvalidDataException($"{AssetPaths.BlockDefinitions}: block {b.FullName}: sound '{k}' = '{snd}' is not in {AssetPaths.SoundDefinitions}");
            if (b.UiId.Length > 0) UiReg.Get(b.UiId); // throws if the UI does not exist
        }

        var tdef = TextureDefinitionFile.Load(AssetPaths.Get(AssetPaths.BlockTextureDefinitions));
        BlockTexW = tdef.Width; BlockTexH = tdef.Height;
        BlockLayers = AtlasLoader.LoadLayers(AssetPaths.Get(AssetPaths.BlockAtlas), tdef, out BlockLayerCount);
        foreach (var b in BlockReg.All)
            foreach (var (slot, t) in b.Textures)
                if (t < 1 || t >= BlockLayerCount || tdef.Entries.All(e => e.id != t))
                    Console.Error.WriteLine($"warning: block {b.FullName} texture '{slot}' = {t} is not in {AssetPaths.BlockTextureDefinitions} (shows as a checkerboard)");
        var itdef = TextureDefinitionFile.Load(AssetPaths.Get(AssetPaths.ItemTextureDefinitions));
        ItemTexW = itdef.Width; ItemTexH = itdef.Height;
        ItemLayers = AtlasLoader.LoadLayers(AssetPaths.Get(AssetPaths.ItemAtlas), itdef, out ItemLayerCount);
        AppendItemLayers();
        BeltTexture = tdef.Entries.FirstOrDefault(e => e.name == "conveyor_belt").id;
        foreach (var en in tdef.Entries) blockTexByName.TryAdd(en.name, en.id);
        Blocks.Build(BlockReg);
    }
}
