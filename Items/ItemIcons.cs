namespace CubePlanet;

/// <summary>
/// Inventory icons, drawn live like Minecraft's (no icon pictures are stored):
/// <list type="bullet">
/// <item>"model": the placed block's 3D model, seen from above at the front-right corner (Minecraft's GUI view, rotation 30 / 225: an
/// orthographic 2:1 look). Each icon is a cached list of flat faces (<see cref="Quads"/>): screen corners in a 32 x 32 unit box, texture
/// coordinates into the block texture array, shading (top 1, south / north .78, east / west .6, bottom .5) and depth. The UI draws them back
/// to front with the block textures every frame (only the visible slots), so icons are sharp at any size and cost no memory. See-through cubes
/// (glass, leaves) also draw the inside of their far faces; multiblocks (beds) show all parts, scaled to fit.</item>
/// <item>"sprite": the block's texture drawn flat (flowers, saplings, vines), like Minecraft's item/generated models.</item>
/// <item>"texture": the item's own picture from the item atlas (ingots, the door).</item>
/// </list>
/// </summary>
public static class ItemIcons
{
    public static string KindOf(ItemDefinition it)
    {
        if (it.Icon.Length > 0) return it.PlacesBlockId == 0 && it.Icon != "texture" ? "texture" : it.Icon;
        if (it.PlacesBlockId == 0) return "texture";
        string m = DEFS[it.PlacesBlockId]?.Model ?? "cube";
        return m == "cube" || m == "boxes" ? "model" : "sprite";
    }
    /// <summary>Block texture of a "sprite" icon.</summary>
    public static int SpriteTexture(ItemDefinition it) { var d = DEFS[it.PlacesBlockId]; return d.Faces[4] > 0 ? d.Faces[4] : d.Faces[2]; }

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

    /// <summary>One face of an icon: corners in a 32 x 32 unit box (y down), texture coordinates (v up, like the texture arrays), block
    /// texture, shading, and depth (larger = nearer the viewer).</summary>
    public sealed class Quad { public readonly double[] X = new double[4], Y = new double[4], U = new double[4], V = new double[4]; public int Tex; public float Shade; public double Depth; }

    static readonly Dictionary<int, Quad[]> cache = new();
    public static void ClearCache() { lock (cache) cache.Clear(); }
    static readonly float[] Shade = { .6f, .6f, 1f, .5f, .78f, .78f };

    /// <summary>The faces of a "model" icon, in drawing order (back to front). Cached per item.</summary>
    public static Quad[] Quads(ItemDefinition it)
    {
        lock (cache) if (cache.TryGetValue(it.Id, out var q)) return q;
        var res = BuildQuads(it);
        lock (cache) cache[it.Id] = res;
        return res;
    }

    // Projection (32 units = the icon): screen x = 16 + 14 (x - z), y = 2 + 15 (1 - y) + 7 (x + z); a full block spans 2..30 x 2..31.
    static Quad[] BuildQuads(ItemDefinition it)
    {
        var boxes = BoxesOf(it); var d = DEFS[it.PlacesBlockId];
        bool backs = d.Model == "cube" && d.RenderLayer != "opaque"; // see-through cubes show the inside of their far faces
        // multiblocks: scale and centre to fit a block's room; single blocks keep their place (a slab sits at the bottom)
        double sc = 1, cx = 16, cy = 16.5;
        if (d.Parts.Length > 0)
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
        var front = new List<Quad>(); var back = new List<Quad>();
        foreach (var B in boxes)
        {
            double fx = Math.Floor(B.X0), fy = Math.Floor(B.Y0), fz = Math.Floor(B.Z0); // texture coordinates are block-local
            foreach (int face in backs ? new[] { 0, 1, 2, 3, 4, 5 } : new[] { 0, 2, 4 })
            {
                var corners = Corners(B, face); if (corners == null) continue;
                var q = new Quad { Tex = B.Faces[face], Shade = Shade[face] }; double depth = 0;
                for (int k = 0; k < 4; k++)
                {
                    var (x, y, z) = corners[k];
                    double sx = 16 + 14 * (x - z), sy = 2 + 15 * (1 - y) + 7 * (x + z);
                    q.X[k] = 16 + (sx - cx) * sc; q.Y[k] = 16.5 + (sy - cy) * sc;
                    // texture coordinates as the world's box faces map them (u, v down from the top of the face), v flipped for the arrays
                    double lx = x - fx, ly = y - fy, lz = z - fz, u, vd;
                    switch (face)
                    {
                        case 2: u = lx; vd = lz; break;
                        case 3: u = lx; vd = 1 - lz; break;
                        case 0: u = 1 - lz; vd = 1 - ly; break;
                        case 1: u = lz; vd = 1 - ly; break;
                        case 4: u = lx; vd = 1 - ly; break;
                        default: u = 1 - lx; vd = 1 - ly; break;
                    }
                    q.U[k] = u; q.V[k] = 1 - vd; depth += x + y * 14 / 15 + z;
                }
                q.Depth = depth / 4;
                (face == 0 || face == 2 || face == 4 ? front : back).Add(q);
            }
        }
        back.Sort((a, b) => a.Depth.CompareTo(b.Depth)); front.Sort((a, b) => a.Depth.CompareTo(b.Depth));
        return back.Concat(front).ToArray();
    }

    // corners of a box face, in an order that winds around it (null for an empty face)
    static (double, double, double)[] Corners(Box b, int face)
    {
        if (b.X1 <= b.X0 || b.Y1 <= b.Y0 || b.Z1 <= b.Z0) return null;
        return face switch
        {
            2 => new[] { (b.X0, b.Y1, b.Z0), (b.X1, b.Y1, b.Z0), (b.X1, b.Y1, b.Z1), (b.X0, b.Y1, b.Z1) },
            3 => new[] { (b.X0, b.Y0, b.Z0), (b.X1, b.Y0, b.Z0), (b.X1, b.Y0, b.Z1), (b.X0, b.Y0, b.Z1) },
            0 => new[] { (b.X1, b.Y1, b.Z0), (b.X1, b.Y1, b.Z1), (b.X1, b.Y0, b.Z1), (b.X1, b.Y0, b.Z0) },
            1 => new[] { (b.X0, b.Y1, b.Z0), (b.X0, b.Y1, b.Z1), (b.X0, b.Y0, b.Z1), (b.X0, b.Y0, b.Z0) },
            4 => new[] { (b.X0, b.Y1, b.Z1), (b.X1, b.Y1, b.Z1), (b.X1, b.Y0, b.Z1), (b.X0, b.Y0, b.Z1) },
            _ => new[] { (b.X0, b.Y1, b.Z0), (b.X1, b.Y1, b.Z0), (b.X1, b.Y0, b.Z0), (b.X0, b.Y0, b.Z0) },
        };
    }

    // ---------- CPU rasterizer of the same faces (no window): --iconsheet and the self-test check what the UI draws ----------

    /// <summary>An item's icon as RGBA (rows top-down), drawn from the same faces / textures the UI uses.</summary>
    public static byte[] Rasterize(ItemDefinition it, int size)
    {
        var img = new byte[size * size * 4]; var tex = GameAssets.BlockLayers; int tw = GameAssets.BlockTexW, th = GameAssets.BlockTexH, tl = GameAssets.BlockLayerCount;
        void Px(int x, int y, byte r, byte g, byte b, byte a)
        {
            if (a == 0) return; int o = (y * size + x) * 4; double al = a / 255.0;
            img[o] = (byte)(r * al + img[o] * (1 - al)); img[o + 1] = (byte)(g * al + img[o + 1] * (1 - al)); img[o + 2] = (byte)(b * al + img[o + 2] * (1 - al)); img[o + 3] = (byte)Math.Max(img[o + 3], a);
        }
        (byte, byte, byte, byte) Tx(byte[] data, int w, int h, int layer, double u, double v)
        {
            int ix = Math.Clamp((int)(u * w), 0, w - 1), iy = Math.Clamp((int)(v * h), 0, h - 1), o = ((layer * h + iy) * w + ix) * 4;
            return (data[o], data[o + 1], data[o + 2], data[o + 3]);
        }
        switch (KindOf(it))
        {
            case "model":
                foreach (var q in Quads(it))
                {
                    if (q.Tex <= 0 || q.Tex >= tl) continue;
                    for (int t = 0; t < 2; t++)
                    {
                        int a = 0, b = t + 1, c = t + 2; // triangles (0,1,2) and (0,2,3)
                        double k = size / 32.0, ax = q.X[a] * k, ay = q.Y[a] * k, bx = q.X[b] * k, by = q.Y[b] * k, cxp = q.X[c] * k, cyp = q.Y[c] * k;
                        double den = (by - cyp) * (ax - cxp) + (cxp - bx) * (ay - cyp); if (Math.Abs(den) < 1e-9) continue;
                        int minx = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cxp)))), maxx = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cxp))));
                        int miny = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cyp)))), maxy = Math.Min(size - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cyp))));
                        for (int y = miny; y <= maxy; y++) for (int x = minx; x <= maxx; x++)
                            {
                                double px = x + .5, py = y + .5;
                                double w0 = ((by - cyp) * (px - cxp) + (cxp - bx) * (py - cyp)) / den, w1 = ((cyp - ay) * (px - cxp) + (ax - cxp) * (py - cyp)) / den, w2 = 1 - w0 - w1;
                                if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                                double u = w0 * q.U[a] + w1 * q.U[b] + w2 * q.U[c], v = w0 * q.V[a] + w1 * q.V[b] + w2 * q.V[c];
                                var (r, g, bb, al) = Tx(tex, tw, th, q.Tex, u, v);
                                Px(x, y, (byte)(r * q.Shade), (byte)(g * q.Shade), (byte)(bb * q.Shade), al);
                            }
                    }
                }
                break;
            case "sprite":
                { int t = SpriteTexture(it); if (t <= 0 || t >= tl) break; for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) { var (r, g, b, a) = Tx(tex, tw, th, t, (x + .5) / size, 1 - (y + .5) / size); Px(x, y, r, g, b, a); } }
                break;
            default:
                { int l = it.Id; if (l >= GameAssets.ItemLayerCount) break; for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) { var (r, g, b, a) = Tx(GameAssets.ItemLayers, GameAssets.ItemTexW, GameAssets.ItemTexH, l, (x + .5) / size, 1 - (y + .5) / size); Px(x, y, r, g, b, a); } }
                break;
        }
        return img;
    }

    /// <summary>--iconsheet file.png: every item icon (16 per row, 128 px over a grey background), as the UI draws them.</summary>
    public static void WriteSheet(string path)
    {
        var items = GameAssets.ItemReg.All.OrderBy(i => i.Id).ToList(); int S = 128, cols = 16, rows = (items.Count + cols - 1) / cols;
        var img = new byte[cols * S * rows * S * 4];
        for (int i = 0; i < img.Length; i += 4) { img[i] = 60; img[i + 1] = 60; img[i + 2] = 70; img[i + 3] = 255; }
        for (int n = 0; n < items.Count; n++)
        {
            var ic = Rasterize(items[n], S); int ox = n % cols * S, oy = n / cols * S;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int s = (y * S + x) * 4, d = ((oy + y) * cols * S + ox + x) * 4; double a = ic[s + 3] / 255.0;
                    for (int c = 0; c < 3; c++) img[d + c] = (byte)Math.Round(ic[s + c] * a + img[d + c] * (1 - a));
                }
        }
        Png.WriteRgba(path, img, cols * S, rows * S);
        Console.WriteLine($"ICONSHEET {items.Count} icons -> {path}");
    }
}
