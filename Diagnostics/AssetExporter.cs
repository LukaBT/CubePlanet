using System.Text.Json;
using System.Text.Json.Nodes;

namespace CubePlanet;

/// <summary>
/// One-time exporter (--export-assets): writes the 117 procedurally generated blocks as the 7 AssetEditor files into Assets/, then loads them
/// back and checks that the tables and texture bytes are identical to the procedural ones. Removed once the game loads everything from Assets/.
/// </summary>
public static class AssetExporter
{
    const int NT = 118, BUCKET_ITEM = 200;

    public static void Run(string dir)
    {
        Directory.CreateDirectory(dir);
        // ---- the hard-coded tables as they were (Blocks static constructor, shader layer ranges, mesher special cases)
        var opq = new byte[256]; var spc = new byte[256]; var tgr = new byte[256]; var uwp = new bool[256]; var logid = new bool[256]; var leafs = new bool[256];
        Array.Fill(opq, (byte)1);
        foreach (int i in new[] { 0, 9, 5, 23, 24, 25, 26, 38, 6 }) opq[i] = 0;
        for (int i = 43; i <= 55; i++) { spc[i] = 1; opq[i] = 0; }
        foreach (int i in new[] { 5, 23, 24, 25, 26 }) { tgr[i] = 1; leafs[i] = true; }
        tgr[38] = 2; tgr[6] = 3;
        foreach (int i in new[] { 4, 27, 28, 29, 30 }) logid[i] = true;
        foreach (int i in new[] { 94, 95, 96, 97, 98 }) { opq[i] = 0; tgr[i] = 1; leafs[i] = true; }
        for (int i = 104; i <= 117; i++) { spc[i] = 1; opq[i] = 0; }
        for (int i = 110; i <= 117; i++) uwp[i] = true;
        logid[90] = true; logid[92] = true;
        bool In(int i, params (int a, int b)[] r) => r.Any(q => i >= q.a && i <= q.b);
        string Sway(int i) => In(i, (5, 5), (23, 26), (51, 51), (94, 98)) ? "leaves" : In(i, (43, 50), (55, 55), (104, 109)) ? "plant" : In(i, (110, 117)) ? "underwater" : "none";
        bool Emissive(int i) => i == 75 || i == 85 || i == 88;
        bool NoShadow(int i) => In(i, (43, 55), (104, 117));
        bool NoJitter(int i) => i == 55 || i == 108 || i == 110 || i == 111;
        string[] names = OldNames();

        // ---- block textures (16x16): procedural atlas, image orientation in the PNG (row 0 of a tile = bottom of the face)
        byte[] tiles = Tex.BuildAtlas();
        var btd = new TextureDefinitionFile { Width = 16, Height = 16, PerRow = 16, Rows = 16 };
        var batlas = new byte[btd.PerRow * 16 * btd.Rows * 16 * 4]; int bw = btd.PerRow * 16;
        for (int id = 1; id < NT; id++)
        {
            var (cx, cy) = btd.Cell(id); btd.Entries.Add((id, names[id], cx, cy));
            for (int r = 0; r < 16; r++) Buffer.BlockCopy(tiles, (id * 16 + 15 - r) * 16 * 4, batlas, ((cy + r) * bw + cx) * 4, 64);
        }
        Png.WriteRgba(Path.Combine(dir, AssetPaths.BlockAtlas), batlas, bw, btd.Rows * 16);
        File.WriteAllText(Path.Combine(dir, AssetPaths.BlockTextureDefinitions), btd.ToText());

        // ---- block + item definitions
        var blocks = new JsonArray(); var items = new JsonArray();
        static JsonArray Arr(params JsonNode[] n) => new(n);
        for (int id = 1; id < NT; id++)
        {
            string n = names[id];
            bool water = id == 9;
            string model = water ? "liquid" : spc[id] != 0 ? (id == 51 || id == 52 ? "attached" : "cross") : "cube";
            string layer = opq[id] != 0 ? "opaque" : water ? "liquid" : tgr[id] >= 2 ? "translucent" : "cutout";
            string group = tgr[id] switch { 1 => "leaves", 2 => "glass", 3 => "ice", _ => "" };
            var render = new JsonObject { ["layer"] = layer };
            if (group != "") render["connect_group"] = group;
            if (Sway(id) != "none") render["sway"] = Sway(id);
            if (Emissive(id)) render["emissive"] = 1.0;
            if (NoShadow(id)) render["casts_shadow"] = false;
            var def = new JsonObject { ["model"] = model };
            if (id == 51) def["attach"] = "vine";
            if (id == 52) def["attach"] = "pod";
            if (model == "cross" && NoJitter(id)) def["random_offset"] = false;
            if (uwp[id]) def["waterlogged"] = true;
            def["render"] = render;
            var c = Tex.TileColor(id); def["lod_color"] = Arr(c[0], c[1], c[2]);
            def["default_state"] = "normal"; def["hardness"] = 1.0;
            bool solid = model == "cube";
            var box = new JsonObject { ["x"] = 0, ["y"] = 0, ["width"] = 1, ["height"] = 1 };
            def["geometry"] = new JsonObject { ["collision"] = solid ? Arr(box.DeepClone()) : new JsonArray(), ["selection"] = water ? new JsonArray() : Arr(box.DeepClone()), ["state_overrides"] = new JsonArray() };
            def["drops"] = water ? new JsonArray() : Arr(new JsonObject { ["namespace"] = "base", ["id_name"] = n, ["count"] = 1 });
            var tags = new JsonArray(); if (logid[id]) tags.Add("log"); if (leafs[id]) tags.Add("leaves");
            blocks.Add(new JsonObject
            {
                ["type"] = "block", ["namespace"] = "base", ["id_name"] = n, ["id"] = id, ["name"] = Title(n), ["stateless"] = true, ["group_tags"] = tags,
                ["textures"] = new JsonObject { ["all"] = id }, ["properties"] = new JsonObject { ["definition"] = def, ["instance_state"] = new JsonObject() },
            });
            items.Add(new JsonObject
            {
                ["type"] = "item", ["namespace"] = "base", ["id_name"] = n, ["id"] = id, ["name"] = Title(n), ["group_tags"] = new JsonArray(),
                ["properties"] = new JsonObject
                {
                    ["definition"] = new JsonObject { ["stack_size"] = 64, ["places_block"] = new JsonObject { ["namespace"] = "base", ["id_name"] = n } },
                    ["instance_state"] = new JsonObject(),
                },
            });
        }
        items.Add(new JsonObject
        {
            ["type"] = "item", ["namespace"] = "base", ["id_name"] = "bucket", ["id"] = BUCKET_ITEM, ["name"] = "Bucket", ["group_tags"] = new JsonArray(),
            ["properties"] = new JsonObject
            {
                ["definition"] = new JsonObject { ["stack_size"] = 1, ["capacity"] = 8 },
                ["instance_state"] = new JsonObject { ["fill"] = new JsonObject { ["type"] = "integer", ["default"] = 0, ["min"] = 0, ["max_from"] = "definition.capacity" } },
            },
        });
        var jo = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        File.WriteAllText(Path.Combine(dir, AssetPaths.BlockDefinitions), new JsonObject { ["blocks"] = blocks }.ToJsonString(jo) + "\n");
        File.WriteAllText(Path.Combine(dir, AssetPaths.ItemDefinitions), new JsonObject { ["items"] = items }.ToJsonString(jo) + "\n");
        File.WriteAllText(Path.Combine(dir, AssetPaths.UiDefinitions), new JsonObject { ["uis"] = new JsonArray() }.ToJsonString(jo) + "\n");

        // ---- item icons (32x32): cubes rendered isometrically, sprites scaled up, plus the bucket
        var itd = new TextureDefinitionFile { Width = 32, Height = 32, PerRow = 16, Rows = 16 };
        int iw = itd.PerRow * 32; var iatlas = new byte[iw * itd.Rows * 32 * 4];
        void PutIcon(int id, string name, byte[] icon)
        {
            var (cx, cy) = itd.Cell(id); itd.Entries.Add((id, name, cx, cy));
            for (int r = 0; r < 32; r++) Buffer.BlockCopy(icon, r * 32 * 4, iatlas, ((cy + r) * iw + cx) * 4, 128);
        }
        for (int id = 1; id < NT; id++)
        {
            var tile = new byte[16 * 16 * 4]; // image orientation (row 0 = top)
            for (int r = 0; r < 16; r++) Buffer.BlockCopy(tiles, (id * 16 + 15 - r) * 64, tile, r * 64, 64);
            PutIcon(id, names[id], spc[id] != 0 ? Flat(tile) : Iso(tile));
        }
        PutIcon(BUCKET_ITEM, "bucket", Bucket());
        Png.WriteRgba(Path.Combine(dir, AssetPaths.ItemAtlas), iatlas, iw, itd.Rows * 32);
        File.WriteAllText(Path.Combine(dir, AssetPaths.ItemTextureDefinitions), itd.ToText());
        Console.WriteLine($"exported {NT - 1} blocks, {items.Count} items to {dir}");

        // ---- load everything back and compare with the procedural data
        AssetPaths.Directory = dir; GameAssets.Load(); int bad = 0;
        void Cmp(string what, int i, object a, object b) { if (!Equals(a, b)) { if (bad++ < 20) Console.WriteLine($"MISMATCH {what}[{i}]: old {a}, loaded {b}"); } }
        for (int i = 0; i < 256; i++)
        {
            Cmp("OPQ", i, opq[i], Blocks.OPQ[i]); Cmp("SPC", i, spc[i], Blocks.SPC[i]); Cmp("TGR", i, tgr[i], Blocks.TGR[i]); Cmp("UWP", i, uwp[i], Blocks.UWP[i]);
            Cmp("LOGID", i, logid[i], Blocks.LOGID[i]); Cmp("LEAFS", i, leafs[i], Blocks.LEAFS[i]);
            if (i < NT) { Cmp("TEX", i, i, Blocks.TEX[i]); Cmp("EMI", i, Emissive(i), Blocks.EMI[i]); if (spc[i] != 0 && i != 51 && i != 52) Cmp("JIT", i, !NoJitter(i), Blocks.JIT[i]); }
            if (i > 0 && i < NT) { Cmp("BN", i, names[i], Blocks.BN[i]); Cmp("LODC", i, string.Join(",", Tex.TileColor(i)), string.Join(",", Blocks.TileColor(i))); }
        }
        for (int i = 1; i < NT; i++) for (int k = 0; k < 1024; k++) if (tiles[i * 1024 + k] != GameAssets.BlockLayers[i * 1024 + k]) { Cmp("texel", i * 1024 + k, tiles[i * 1024 + k], GameAssets.BlockLayers[i * 1024 + k]); break; }
        Console.WriteLine(bad == 0 ? "EXPORT CHECK OK: definitions rebuild identical tables, textures are byte-identical" : $"EXPORT CHECK FAILED: {bad} mismatches");
    }

    static string Title(string n) => string.Join(" ", n.Split('_').Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    // sprite: 16x16 scaled to 32x32 (nearest)
    static byte[] Flat(byte[] t)
    {
        var o = new byte[32 * 32 * 4];
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) Buffer.BlockCopy(t, ((y >> 1) * 16 + (x >> 1)) * 4, o, (y * 32 + x) * 4, 4);
        return o;
    }
    // isometric cube: top face lit, left side .78, right side .6
    static byte[] Iso(byte[] t)
    {
        var o = new byte[32 * 32 * 4];
        double tx = 16, ty = 2;
        for (int py = 0; py < 32; py++)
            for (int px = 0; px < 32; px++)
            {
                double x = px + .5 - tx, y = py + .5 - ty; int u = -1, v = -1; double shade = 1;
                // top: P = T + a*(14,7) + b*(-14,7)
                double a = (x / 14 + y / 7) / 2, b = (y / 7 - x / 14) / 2;
                if (a >= 0 && a < 1 && b >= 0 && b < 1) { u = (int)(a * 16); v = (int)(b * 16); }
                else
                {
                    // left: P = T + (-14,7) + a*(14,7) + w*(0,15); right: P = T + (0,14) + a*(14,-7) + w*(0,15)
                    double la = (x + 14) / 14, lw = (y - 7 - la * 7) / 15, ra = x / 14, rw = (y - 14 + ra * 7) / 15;
                    if (la >= 0 && la < 1 && lw >= 0 && lw < 1) { u = (int)(la * 16); v = (int)(lw * 16); shade = .78; }
                    else if (ra >= 0 && ra < 1 && rw >= 0 && rw < 1) { u = (int)(ra * 16); v = (int)(rw * 16); shade = .6; }
                }
                if (u < 0) continue;
                int s = (v * 16 + u) * 4, d = (py * 32 + px) * 4;
                for (int c = 0; c < 3; c++) o[d + c] = (byte)Math.Round(t[s + c] * shade);
                o[d + 3] = t[s + 3];
            }
        return o;
    }
    // the bucket icon of the hotbar (UI.BucketIcon, empty), 2 pixels per unit
    static byte[] Bucket()
    {
        var o = new byte[32 * 32 * 4];
        void R(int x, int y, int w, int h, byte r, byte g, byte b) { for (int yy = y * 2; yy < (y + h) * 2; yy++) for (int xx = x * 2; xx < (x + w) * 2; xx++) { int d = (yy * 32 + xx) * 4; o[d] = r; o[d + 1] = g; o[d + 2] = b; o[d + 3] = 255; } }
        R(3, 5, 10, 9, 154, 160, 166); R(3, 5, 10, 1, 107, 112, 117); R(3, 13, 10, 1, 107, 112, 117); R(3, 5, 1, 9, 107, 112, 117); R(12, 5, 1, 9, 107, 112, 117);
        R(4, 6, 8, 7, 27, 29, 32);
        return o;
    }

    static string[] OldNames() =>
        ("air grass stone dirt oak_log oak_leaves ice sand snow water gravel sandstone red_sand terracotta orange_terracotta yellow_terracotta white_terracotta red_terracotta brown_terracotta jungle_grass savanna_grass taiga_grass swamp_grass spruce_leaves jungle_leaves acacia_leaves birch_leaves birch_log spruce_log jungle_log acacia_log oak_planks spruce_planks acacia_planks jungle_planks cobblestone mossy_cobblestone stone_bricks glass cut_sandstone cactus hay_bale red_sandstone tall_grass dry_grass fern dandelion poppy cornflower allium dead_bush vine cocoa red_mushroom brown_mushroom sugar_cane pumpkin melon coal_ore iron_ore gold_ore diamond_ore redstone_ore lapis_ore copper_ore emerald_ore deepslate deepslate_coal_ore deepslate_iron_ore deepslate_gold_ore deepslate_diamond_ore deepslate_redstone_ore deepslate_lapis_ore deepslate_copper_ore deepslate_emerald_ore lava lush_grass moss_block podzol packed_ice blue_ice clay coral_sand basalt volcanic_ash magma_block obsidian prismarine_bricks sea_lantern dead_coral_block cherry_log cherry_planks palm_log palm_planks cherry_leaves flowering_azalea_leaves azalea_leaves palm_leaves snowy_spruce_leaves tube_coral_block brain_coral_block bubble_coral_block fire_coral_block horn_coral_block pink_petals pink_tulip bluebell lavender bamboo sweet_berry_bush seagrass kelp sea_pickle tube_coral_fan brain_coral_fan bubble_coral_fan fire_coral_fan horn_coral_fan").Split(' ');
}
