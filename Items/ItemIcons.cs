namespace CubePlanet;

/// <summary>
/// Inventory icons (one texture-array layer per item ID, <see cref="Size"/> px, with mipmaps), built when the assets load:
/// <list type="bullet">
/// <item>"model" icons: the placed block's 3D model seen from above at the front-right corner, like Minecraft's block items (its GUI view is
/// rotation 30 / 225, an orthographic 2:1 look). Every pixel is the average of 4x4 rays through the model, so edges are smooth at 128 px; rays
/// pass through transparent texels (leaves, glass) and show the faces behind. Shading: top 1, south / north .78, east / west .6, bottom .5.
/// Multiblocks (beds) show all their parts, scaled to fit.</item>
/// <item>"sprite" icons: the block's texture drawn flat (flowers, saplings, vines), like Minecraft's item/generated models.</item>
/// <item>"texture" icons: the item's own picture from the item atlas (ingots, the door).</item>
/// </list>
/// Flat pictures are scaled up by whole pixels, so their pixels stay square; the UI samples them sharply (see ui.frag).
/// </summary>
public static class ItemIcons
{
    public const int Size = 128, SS = 4;
    /// <summary>Icon kind per item ID: 0 = flat picture (sharp sampling), 1 = 3D model (smooth sampling).</summary>
    public static byte[] Smooth = Array.Empty<byte>();

    public static string KindOf(ItemDefinition it)
    {
        if (it.Icon.Length > 0) return it.PlacesBlockId == 0 && it.Icon != "texture" ? "texture" : it.Icon;
        if (it.PlacesBlockId == 0) return "texture";
        string m = DEFS[it.PlacesBlockId]?.Model ?? "cube";
        return m == "cube" || m == "boxes" ? "model" : "sprite";
    }

    /// <summary>Mip chain of all icon layers (mips[0] = Size x Size per layer; rows bottom-up like every texture array here).</summary>
    public static byte[][] Build(byte[] itemLayers, int itemW, int itemH, int itemLayerCount, out int layers)
    {
        var items = GameAssets.ItemReg.All;
        layers = Math.Max(itemLayerCount, items.Count == 0 ? 1 : items.Max(i => i.Id) + 1);
        int L = layers, S = Size; var top = new byte[S * S * 4 * L];
        Smooth = new byte[L];
        // atlas pictures first (also the checkerboard of missing ones), scaled up by whole pixels
        for (int l = 0; l < Math.Min(L, itemLayerCount); l++) Blit(itemLayers, l, itemW, itemH, top, l);
        Parallel.ForEach(items, it =>
        {
            string kind = KindOf(it);
            if (kind != "texture") Array.Clear(top, it.Id * S * S * 4, S * S * 4); // drawn here, not from the atlas (whose missing-picture checkerboard is there)
            switch (kind)
            {
                case "model": Model(it, top, it.Id); Smooth[it.Id] = 1; break;
                case "sprite":
                    var d = DEFS[it.PlacesBlockId]; int t = d.Faces[4] > 0 ? d.Faces[4] : d.Faces[2];
                    if (t > 0 && t < GameAssets.BlockLayerCount) Blit(GameAssets.BlockLayers, t, GameAssets.BlockTexW, GameAssets.BlockTexH, top, it.Id);
                    break;
            }
        });
        var mips = new List<byte[]> { top };
        for (int s = S; s > 1; s /= 2) mips.Add(Half(mips[^1], s, L));
        return mips.ToArray();
    }

    // nearest-neighbour scale of one w x h layer to Size x Size (both bottom-up)
    static void Blit(byte[] src, int sl, int w, int h, byte[] dst, int dl)
    {
        int S = Size;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
                Buffer.BlockCopy(src, ((sl * h + y * h / S) * w + x * w / S) * 4, dst, ((dl * S + y) * S + x) * 4, 4);
    }

    // 2x2 box filter weighted by alpha (transparent texels don't darken the edges)
    static byte[] Half(byte[] a, int s, int layers)
    {
        int h = s / 2; var o = new byte[h * h * 4 * layers];
        for (int l = 0; l < layers; l++)
            for (int y = 0; y < h; y++)
                for (int x = 0; x < h; x++)
                {
                    double r = 0, g = 0, b = 0, al = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        int i = ((l * s + y * 2 + (k >> 1)) * s + x * 2 + (k & 1)) * 4; double w = a[i + 3];
                        r += a[i] * w; g += a[i + 1] * w; b += a[i + 2] * w; al += w;
                    }
                    int j = ((l * h + y) * h + x) * 4;
                    if (al > 0) { o[j] = (byte)Math.Round(r / al); o[j + 1] = (byte)Math.Round(g / al); o[j + 2] = (byte)Math.Round(b / al); }
                    o[j + 3] = (byte)Math.Round(al / 4);
                }
        return o;
    }

    internal readonly record struct Box(double X0, double Y0, double Z0, double X1, double Y1, double Z1, int[] Faces);

    /// <summary>The boxes an item's icon shows: the block in its icon state (front to the viewer), all multiblock parts at their offsets.</summary>
    internal static List<Box> BoxesOf(ItemDefinition it)
    {
        int id = it.PlacesBlockId; var d = DEFS[id]; var list = new List<Box>();
        uint word = 0;
        if (it.IconState.ValueKind == System.Text.Json.JsonValueKind.Object) word = d.State.Apply(word, it.IconState);
        var facing = d.Rotation != "none" ? d.State.Find("facing") : null;
        bool setFacing = facing != null && !(it.IconState.ValueKind == System.Text.Json.JsonValueKind.Object && it.IconState.TryGetProperty("facing", out _));
        // the front (north side) turned to the viewer, who looks from the south-east above
        if (setFacing) word = facing.With(word, Array.IndexOf(facing.Values, "south"));
        string face = facing != null ? facing.Values[facing.Index(word)] : "north";
        var parts = d.Parts.Length > 0 ? d.Parts : new[] { new BlockDefinition.Part() };
        foreach (var p in parts)
        {
            uint w = p.State.ValueKind == System.Text.Json.JsonValueKind.Object ? d.State.Apply(word, p.State) : word;
            var (ox, oy, oz) = BlockVariant.TurnOffset(face, p.X, p.Y, p.Z);
            var v = Variant(id, (int)w);
            if (d.Model == "boxes")
                foreach (var mb in v?.Boxes ?? d.Boxes) { var b = mb.Box; list.Add(new Box(b.X0 + ox, b.Y0 + oy, b.Z0 + oz, b.X1 + ox, b.Y1 + oy, b.Z1 + oz, mb.Faces)); }
            else list.Add(new Box(ox, oy, oz, ox + 1, oy + 1, oz + 1, v?.Faces ?? d.Faces));
        }
        return list;
    }

    // Projection in "32 units" (an icon is 32 x 32 units): screen x = 16 + 14 (x - z), y = 2 + 15 (1 - y) + 7 (x + z) (y down). A full block
    // spans 2..30 x 2..31, like the AssetEditor's pictures. The view ray is (1, 14/15, 1) (towards the viewer); depth along it = x + 14/15 y + z.
    static readonly double[] Shade = { .6, .6, 1, .5, .78, .78 };
    static void Model(ItemDefinition it, byte[] dst, int layer)
    {
        var boxes = BoxesOf(it); if (boxes.Count == 0) return;
        // multiblocks: scale and centre the picture to fit a block's room; single blocks keep their place (a slab sits at the bottom)
        double sc = 1, cx = 0, cy = 0;
        if (DEFS[it.PlacesBlockId].Parts.Length > 0)
        {
            double x0 = 1e9, x1 = -1e9, y0 = 1e9, y1 = -1e9;
            foreach (var b in boxes)
                for (int c = 0; c < 8; c++)
                {
                    double x = (c & 1) != 0 ? b.X1 : b.X0, y = (c & 2) != 0 ? b.Y1 : b.Y0, z = (c & 4) != 0 ? b.Z1 : b.Z0;
                    double sx = 16 + 14 * (x - z), sy = 2 + 15 * (1 - y) + 7 * (x + z);
                    x0 = Math.Min(x0, sx); x1 = Math.Max(x1, sx); y0 = Math.Min(y0, sy); y1 = Math.Max(y1, sy);
                }
            sc = Math.Min(1, Math.Min(28 / (x1 - x0), 29 / (y1 - y0))); cx = (x0 + x1) / 2; cy = (y0 + y1) / 2;
        }
        // see-through cubes (glass, leaves) show the inside of their far faces through the holes, like Minecraft's glass item; box models
        // (beds, fences) only their outside, as in the world
        var bd = DEFS[it.PlacesBlockId]; bool backs = bd.Model == "cube" && bd.RenderLayer != "opaque";
        int S = Size; double k = S / 32.0, inv = 1.0 / (SS * SS);
        var tex = GameAssets.BlockLayers; int tw = GameAssets.BlockTexW, th = GameAssets.BlockTexH, tl = GameAssets.BlockLayerCount;
        for (int py = 0; py < S; py++)
            for (int px = 0; px < S; px++)
            {
                double r = 0, g = 0, bl = 0, al = 0;
                for (int s = 0; s < SS * SS; s++)
                {
                    double sx = (px + (s % SS + .5) / SS) / k, sy = (py + (s / SS + .5) / SS) / k;
                    if (sc != 1) { sx = cx + (sx - 16) / sc; sy = cy + (sy - 16.5) / sc; }
                    if (!Sample(boxes, sx, sy, tex, tw, th, tl, backs, out var c)) continue;
                    double a = c.a / 255.0; r += c.r * a; g += c.g * a; bl += c.b * a; al += a;
                }
                if (al <= 0) continue;
                int o = ((layer * S + (S - 1 - py)) * S + px) * 4; // rows bottom-up
                dst[o] = (byte)Math.Round(r / al); dst[o + 1] = (byte)Math.Round(g / al); dst[o + 2] = (byte)Math.Round(bl / al); dst[o + 3] = (byte)Math.Round(255 * al * inv);
            }
    }

    // nearest non-transparent texel along the view ray through screen point (sx, sy), front faces and (through holes) the inside of back faces
    static bool Sample(List<Box> boxes, double sx, double sy, byte[] tex, int tw, int th, int tl, bool backs, out (double r, double g, double b, double a) col)
    {
        col = default;
        // point on the ray with y = 0, then the ray p + t * (1, 14/15, 1)
        double a = (sx - 16) / 14, s = (sy - 17) / 7, bx = (a + s) / 2, bz = (s - a) / 2; const double dy = 14.0 / 15;
        Span<(double t, int face, int box)> hits = stackalloc (double, int, int)[2 * Math.Min(boxes.Count, 64)];
        int n = 0;
        for (int i = 0; i < boxes.Count && i < 64; i++)
        {
            var B = boxes[i];
            // slab intersection; the ray direction is positive on every axis
            double tx0 = B.X0 - bx, tx1 = B.X1 - bx, ty0 = B.Y0 / dy, ty1 = B.Y1 / dy, tz0 = B.Z0 - bz, tz1 = B.Z1 - bz;
            double tin = Math.Max(tx0, Math.Max(ty0, tz0)), tout = Math.Min(tx1, Math.Min(ty1, tz1));
            if (tin >= tout) continue;
            // front: the face where the ray enters coming from the viewer (largest t) = the min of the far planes
            int ff = tout == tx1 ? 0 : tout == ty1 ? 2 : 4;
            int bf = tin == tx0 ? 1 : tin == ty0 ? 3 : 5;
            hits[n++] = (tout, ff, i); if (backs) hits[n++] = (tin, bf, i);
        }
        if (n == 0) return false;
        var hs = hits[..n]; hs.Sort((p, q) => q.t.CompareTo(p.t));
        foreach (var (t, face, bi) in hs)
        {
            var B = boxes[bi]; double x = bx + t, y = t * dy, z = bz + t;
            double u, v; // texture coordinates (v down from the top of the face), as the AssetEditor maps them
            switch (face)
            {
                case 2: u = x - Math.Floor(B.X0); v = z - Math.Floor(B.Z0); break;
                case 3: u = x - Math.Floor(B.X0); v = 1 - (z - Math.Floor(B.Z0)); break;
                case 0: u = 1 - (z - Math.Floor(B.Z0)); v = 1 - (y - Math.Floor(B.Y0)); break;
                case 1: u = z - Math.Floor(B.Z0); v = 1 - (y - Math.Floor(B.Y0)); break;
                case 4: u = x - Math.Floor(B.X0); v = 1 - (y - Math.Floor(B.Y0)); break;
                default: u = 1 - (x - Math.Floor(B.X0)); v = 1 - (y - Math.Floor(B.Y0)); break;
            }
            int tid = B.Faces[face]; double sh = Shade[face];
            double r, g, b, al;
            if (tid <= 0 || tid >= tl) { bool m = ((int)(u * 2) + (int)(v * 2)) % 2 == 0; r = m ? 255 : 0; g = 0; b = r; al = 255; }
            else
            {
                int ix = Math.Clamp((int)(u * tw), 0, tw - 1), iy = Math.Clamp((int)(v * th), 0, th - 1);
                int o = ((tid * th + (th - 1 - iy)) * tw + ix) * 4; // layers are stored bottom-up
                r = tex[o]; g = tex[o + 1]; b = tex[o + 2]; al = tex[o + 3];
            }
            if (al < 8) continue;
            col = (r * sh, g * sh, b * sh, al); return true;
        }
        return false;
    }

    /// <summary>--iconsheet file.png: every item icon (16 per row, at full size over a grey background), for checking them without a window.</summary>
    public static void WriteSheet(string path)
    {
        var items = GameAssets.ItemReg.All.OrderBy(i => i.Id).ToList(); int S = Size, cols = 16, rows = (items.Count + cols - 1) / cols;
        var img = new byte[cols * S * rows * S * 4]; var top = GameAssets.IconMips[0];
        for (int i = 0; i < img.Length; i += 4) { img[i] = 60; img[i + 1] = 60; img[i + 2] = 70; img[i + 3] = 255; }
        for (int n = 0; n < items.Count; n++)
        {
            int l = items[n].Id, ox = n % cols * S, oy = n / cols * S;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int s = ((l * S + (S - 1 - y)) * S + x) * 4, d = ((oy + y) * cols * S + ox + x) * 4; double a = top[s + 3] / 255.0;
                    for (int c = 0; c < 3; c++) img[d + c] = (byte)Math.Round(top[s + c] * a + img[d + c] * (1 - a));
                }
        }
        Png.WriteRgba(path, img, cols * S, rows * S);
        Console.WriteLine($"ICONSHEET {items.Count} icons -> {path}");
    }
}
