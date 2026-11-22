namespace CubePlanet;

/// <summary>Procedural 16x16 tiles for the biome blocks (ids 76-117). Texture row 0 is the BOTTOM of a block face.</summary>
public static partial class Tex
{
    static void Pxl(byte[] img, int i, int x, int y, double[] c, double a = 255)
    {
        if (x < 0 || x > 15 || y < 0 || y > 15) return;
        int o = ((i * 16 + y) * 16 + x) * 4; img[o] = B8(c[0]); img[o + 1] = B8(c[1]); img[o + 2] = B8(c[2]); img[o + 3] = B8(a);
    }
    static void ClrTile(byte[] img, int i) { for (int k = 0; k < 256; k++) img[(i * 256 + k) * 4 + 3] = 0; }
    static double[] Shd(double[] c, double k) => new[] { Math.Clamp(c[0] * k, 0, 255), Math.Clamp(c[1] * k, 0, 255), Math.Clamp(c[2] * k, 0, 255) };
    static double[] Rgb(double r, double g, double b) => new[] { r, g, b };
    static void Line2(byte[] img, int i, int x0, int y0, int x1, int y1, double[] c)
    {
        int n = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)); if (n == 0) n = 1;
        for (int k = 0; k <= n; k++) Pxl(img, i, (int)M.JsRound(x0 + (x1 - x0) * (double)k / n), (int)M.JsRound(y0 + (y1 - y0) * (double)k / n), c);
    }

    static void PaintBiomes(byte[] img)
    {
        // ---------- terrain blocks ----------
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                double h;
                double[] c;
                // 76 lush grass: deep saturated green, darker clumps, light flecks
                h = Hash(x, y, 76); c = Shd(Rgb(60, 152, 50), .9 + h * .22);
                if (Hash(x >> 1, y >> 1, 176) < .18) c = Shd(c, .8);
                if (h > .93) c = Rgb(118, 190, 72);
                if (y < 3) c = Shd(c, .92);
                Pxl(img, 76, x, y, c);
                // 77 moss block: fluffy, soft blotches
                c = Shd(Rgb(88, 114, 42), 1 + (Hash(x >> 1, y >> 1, 77) - .5) * .55 + (Hash(x, y, 177) - .5) * .2);
                if (Hash(x, y, 277) < .06) c = Rgb(132, 152, 62);
                Pxl(img, 77, x, y, c);
                // 78 podzol: dark brown with orange and black flecks
                h = Hash(x, y, 78); c = Shd(Rgb(112, 78, 40), .85 + h * .3);
                if (h < .14) c = Rgb(64, 44, 24); else if (h > .92) c = Rgb(158, 108, 48);
                if (Hash(x >> 1, y >> 1, 178) < .15) c = Shd(c, .8);
                Pxl(img, 78, x, y, c);
                // 79 packed ice
                c = Shd(Rgb(148, 188, 238), .92 + Hash(x, y, 79) * .16);
                if ((x + y * 2) % 9 == 0) c = Rgb(200, 226, 255);
                if (Hash(x >> 2, y >> 2, 179) < .2) c = Shd(c, .9);
                Pxl(img, 79, x, y, c);
                // 80 blue ice: deep blue with bright veins
                c = Shd(Rgb(84, 148, 238), .9 + Hash(x, y, 80) * .2);
                if ((x * 2 + y * 3) % 11 == 0 || (x * 3 - y * 2 + 64) % 13 == 0) c = Rgb(190, 226, 255);
                if (Hash(x, y, 180) > .93) c = Rgb(228, 244, 255);
                Pxl(img, 80, x, y, c);
                // 81 clay
                c = Shd(Rgb(160, 166, 182), .95 + Hash(x >> 1, y >> 1, 81) * .1 + (Hash(x, y, 181) - .5) * .06);
                Pxl(img, 81, x, y, c);
                // 82 coral sand: pale tropical sand with pink and grey grains
                c = Shd(Rgb(240, 230, 200), .96 + Hash(x, y, 82) * .08);
                if (Hash(x, y, 182) < .05) c = Rgb(236, 190, 190); else if (Hash(x, y, 282) < .04) c = Rgb(206, 196, 170);
                Pxl(img, 82, x, y, c);
                // 83 basalt: dark columns
                c = Shd(Rgb(58, 58, 64), (x % 4 < 1 ? .82 : 1) * (.9 + Hash(x, y, 83) * .2));
                if (y % 6 == 0) c = Shd(c, .85);
                Pxl(img, 83, x, y, c);
                // 84 volcanic ash
                c = Shd(Rgb(100, 96, 98), .8 + Hash(x, y, 84) * .4);
                if (Hash(x, y, 184) < .07) c = Rgb(150, 146, 148); else if (Hash(x, y, 284) < .05) c = Rgb(52, 50, 52);
                Pxl(img, 84, x, y, c);
                // 85 magma block: dark crust with glowing cracks
                double hh = Hash(x >> 1, y >> 1, 85), g = Hash(x, y, 185);
                bool crack = hh < .34;
                c = crack ? Shd(Rgb(255, 150, 40), .8 + g * .4) : Shd(Rgb(96, 34, 18), .8 + g * .4);
                if (crack && g > .7) c = Rgb(255, 210, 90);
                Pxl(img, 85, x, y, c);
                // 86 obsidian
                c = Shd(Rgb(24, 16, 40), .8 + Hash(x, y, 86) * .5);
                if ((x + y) % 7 == 0 && Hash(x, y, 186) < .7) c = Rgb(70, 40, 120);
                if (Hash(x, y, 286) > .95) c = Rgb(110, 70, 170);
                Pxl(img, 86, x, y, c);
                // 87 prismarine bricks
                bool mortar = y % 4 == 3 || (x + ((y >> 2) & 1) * 4) % 8 == 7;
                c = mortar ? Shd(Rgb(50, 120, 108), .9 + Hash(x, y, 187) * .2) : Shd(Rgb(86, 172, 152), .88 + Hash(x, y, 87) * .24);
                Pxl(img, 87, x, y, c);
                // 88 sea lantern (self-lit): bright panels with a glowing centre
                bool edge = (x & 7) == 0 || (x & 7) == 7 || (y & 7) == 0 || (y & 7) == 7;
                c = edge ? Rgb(130, 190, 184) : Shd(Rgb(232, 252, 246), .94 + Hash(x, y, 88) * .1);
                if ((x & 7) >= 3 && (x & 7) <= 4 && (y & 7) >= 3 && (y & 7) <= 4) c = Rgb(250, 255, 252);
                Pxl(img, 88, x, y, c);
                // 89 dead (bleached) coral block
                c = Shd(Rgb(130, 124, 118), .85 + Hash(x >> 1, y >> 1, 89) * .3);
                if (Hash(x, y, 189) < .14) c = Rgb(70, 66, 62); else if (Hash(x, y, 289) > .93) c = Rgb(170, 164, 158);
                Pxl(img, 89, x, y, c);
                // 90 cherry log: dark purple-brown bark with pale streaks
                c = Shd(Rgb(84, 50, 58), .85 + Hash(x, y, 90) * .3);
                if (x % 4 == 0) c = Shd(c, .7);
                if (Hash(x, y, 190) > .92) c = Rgb(150, 110, 120);
                Pxl(img, 90, x, y, c);
                // 92 palm log: light brown trunk with ring marks
                c = Shd(Rgb(150, 118, 74), .85 + Hash(x, y, 92) * .3);
                if (x % 4 == 0) c = Shd(c, .75);
                if (y % 4 == 3) c = Shd(c, .82);
                Pxl(img, 92, x, y, c);
            }

        // ---------- leaves (same 25% holes as the other leaf tiles) ----------
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                double a = Hash(x, y, 9) < .25 ? 0 : 255, h = Hash(x, y, 94);
                // 94 cherry leaves: pale and deep pink
                double[] c = h < .2 ? Rgb(255, 214, 228) : h > .85 ? Rgb(224, 118, 164) : Shd(Rgb(244, 170, 198), .9 + Hash(x, y, 194) * .2);
                Pxl(img, 94, x, y, c, a);
                // 95 flowering azalea leaves: green with magenta blossoms
                c = Hash(x, y, 195) < .15 ? Rgb(238, 120, 190) : Shd(Rgb(98, 146, 58), .85 + Hash(x, y, 295) * .3);
                Pxl(img, 95, x, y, c, a);
                // 96 azalea leaves
                c = Shd(Rgb(78, 128, 52), .85 + Hash(x, y, 96) * .3);
                Pxl(img, 96, x, y, c, a);
                // 97 palm leaves: frond veins
                c = Shd(Rgb(54, 152, 46), .85 + Hash(x, y, 97) * .3);
                if ((x + y) % 5 == 0) c = Rgb(112, 192, 72);
                Pxl(img, 97, x, y, c, a);
                // 98 snowy spruce leaves: dark needles under a snow layer
                c = Shd(Rgb(40, 86, 60), .8 + Hash(x, y, 98) * .4);
                double sn = Hash(x, y, 198);
                if ((y >= 7 && sn < .55) || sn < .12) c = Rgb(238, 244, 250);
                Pxl(img, 98, x, y, c, a);
            }

        // ---------- coral blocks ----------
        void Coral(int i, double[] col)
        {
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    double h = Hash(x >> 1, y >> 1, i), g = Hash(x, y, i + 300);
                    var c = Shd(col, .8 + h * .35);
                    if (g < .1) c = Shd(col, .55); else if (g > .92) c = Shd(col, 1.3);
                    if (((x & 3) == 1 || (x & 3) == 2) && ((y & 3) == 1 || (y & 3) == 2) && Hash(x >> 2, y >> 2, i + 500) < .35) c = Shd(col, .5);
                    Pxl(img, i, x, y, c);
                }
        }
        Coral(99, Rgb(48, 96, 220)); Coral(100, Rgb(222, 100, 160)); Coral(101, Rgb(150, 60, 200)); Coral(102, Rgb(200, 40, 40)); Coral(103, Rgb(240, 200, 60));

        // ---------- land plants (transparent crossed quads) ----------
        { // 104 pink petals: scattered petals lying at the foot of the block
            ClrTile(img, 104);
            for (int k = 0; k < 16; k++)
            {
                int px = 1 + (int)Math.Floor(Hash(k, 1, 104) * 13), py = (int)Math.Floor(Hash(k, 2, 104) * 3);
                double t = Hash(k, 3, 104); var pc = t < .33 ? Rgb(244, 160, 200) : t < .66 ? Rgb(232, 120, 170) : Rgb(255, 206, 226);
                Pxl(img, 104, px, py, pc); Pxl(img, 104, px + 1, py, Shd(pc, .9)); if (t > .5) Pxl(img, 104, px, py + 1, Shd(pc, 1.05));
            }
            for (int k = 0; k < 4; k++) Pxl(img, 104, 2 + k * 4, 0, Rgb(70, 140, 52));
        }
        { // 105 pink tulip
            ClrTile(img, 105); var gr = Rgb(55, 130, 45);
            for (int y = 0; y < 9; y++) { Pxl(img, 105, 7, y, Shd(gr, .8 + Hash(7, y, 105) * .3)); Pxl(img, 105, 8, y, Shd(gr, .7 + Hash(8, y, 105) * .3)); }
            Pxl(img, 105, 5, 2, gr); Pxl(img, 105, 6, 3, gr); Pxl(img, 105, 9, 4, gr); Pxl(img, 105, 10, 5, gr);
            int[][] rows = { new[] { 9, 7, 8 }, new[] { 10, 6, 9 }, new[] { 11, 6, 9 }, new[] { 12, 6, 9 }, new[] { 13, 6, 6 }, new[] { 13, 9, 9 } };
            foreach (var r in rows) for (int x = r[1]; x <= r[2]; x++) Pxl(img, 105, x, r[0], Shd(Rgb(238, 120, 170), .85 + Hash(x, r[0], 105) * .3));
        }
        { // 106 bluebell: arching stem with hanging blue bells
            ClrTile(img, 106); var gr = Rgb(55, 130, 45);
            int[][] stem = { new[] { 7, 0 }, new[] { 7, 1 }, new[] { 7, 2 }, new[] { 7, 3 }, new[] { 7, 4 }, new[] { 8, 5 }, new[] { 8, 6 }, new[] { 8, 7 }, new[] { 9, 8 }, new[] { 9, 9 }, new[] { 10, 10 }, new[] { 10, 11 }, new[] { 11, 12 } };
            foreach (var s in stem) Pxl(img, 106, s[0], s[1], Shd(gr, .8 + Hash(s[0], s[1], 106) * .3));
            Line2(img, 106, 7, 1, 4, 4, gr); Line2(img, 106, 7, 2, 10, 4, gr);
            int[][] bells = { new[] { 9, 7 }, new[] { 10, 9 }, new[] { 11, 10 }, new[] { 12, 12 }, new[] { 9, 11 }, new[] { 7, 9 }, new[] { 12, 14 } };
            foreach (var b in bells)
            {
                Pxl(img, 106, b[0], b[1], Rgb(95, 112, 232)); Pxl(img, 106, b[0] + 1, b[1], Rgb(120, 136, 245)); Pxl(img, 106, b[0], b[1] - 1, Rgb(70, 84, 200)); Pxl(img, 106, b[0] + 1, b[1] - 1, Rgb(84, 98, 214));
            }
        }
        { // 107 lavender: three purple spikes
            ClrTile(img, 107); var gr = Rgb(88, 130, 64);
            foreach (int sx in new[] { 4, 8, 12 })
            {
                int top = 12 + (int)Math.Floor(Hash(sx, 1, 107) * 3);
                for (int y = 0; y <= 7; y++) Pxl(img, 107, sx, y, Shd(gr, .8 + Hash(sx, y, 107) * .3));
                for (int y = 6; y <= top; y++)
                {
                    var pc = Shd((y + sx) % 3 == 0 ? Rgb(190, 140, 238) : Rgb(150, 100, 215), .88 + Hash(sx, y, 207) * .25);
                    Pxl(img, 107, sx, y, pc); if (y % 2 == 0) { Pxl(img, 107, sx - 1, y, Shd(pc, .9)); Pxl(img, 107, sx + 1, y, Shd(pc, .95)); }
                }
            }
        }
        { // 108 bamboo: segmented stalk with leaf pairs
            ClrTile(img, 108);
            for (int y = 0; y < 16; y++)
            {
                bool node = y % 5 == 4;
                Pxl(img, 108, 7, y, node ? Rgb(78, 128, 36) : Shd(Rgb(126, 186, 58), .9 + Hash(7, y, 108) * .2));
                Pxl(img, 108, 8, y, node ? Rgb(66, 112, 30) : Shd(Rgb(96, 150, 44), .9 + Hash(8, y, 108) * .2));
                if (node) { Pxl(img, 108, 6, y, Rgb(70, 118, 32)); Pxl(img, 108, 9, y, Rgb(60, 104, 28)); }
            }
            var lf = Rgb(72, 156, 44);
            Line2(img, 108, 6, 7, 3, 9, lf); Line2(img, 108, 9, 12, 12, 14, lf); Line2(img, 108, 6, 1, 4, 3, Shd(lf, .9)); Line2(img, 108, 9, 3, 11, 5, Shd(lf, 1.1));
            Line2(img, 108, 6, 12, 4, 13, Shd(lf, 1.1)); Line2(img, 108, 9, 8, 12, 9, Shd(lf, .9));
        }
        { // 109 sweet berry bush
            ClrTile(img, 109);
            for (int y = 0; y < 11; y++) for (int x = 1; x < 15; x++)
                {
                    double dx = (x - 7.5) / 6.5, dy = (y - 2.0) / 9.0;
                    if (dx * dx + dy * dy > 1) continue;
                    var c = Shd(Rgb(56, 118, 48), .8 + Hash(x, y, 109) * .4);
                    if (Hash(x, y, 209) < .1) c = Rgb(200, 40, 52);
                    Pxl(img, 109, x, y, c);
                }
        }

        // ---------- underwater plants ----------
        { // 110 seagrass
            ClrTile(img, 110);
            foreach (int bx in new[] { 3, 5, 7, 9, 11, 12 })
            {
                int hgt = 8 + (int)Math.Floor(Hash(bx, 1, 110) * 7);
                for (int y = 0; y <= hgt; y++)
                {
                    int x = bx + (int)M.JsRound(Math.Sin(y * .5 + bx) * 1.0);
                    Pxl(img, 110, x, y, Shd(Rgb(40, 152, 76), .72 + .5 * y / hgt + (Hash(x, y, 210) - .5) * .15));
                }
            }
        }
        { // 111 kelp: wavy stalk with fronds and bladders (period 16 so stacked blocks line up)
            ClrTile(img, 111);
            for (int y = 0; y < 16; y++)
            {
                int cx = 7 + (int)M.JsRound(Math.Sin(y * Math.PI / 8) * 1.5);
                var sc = Shd(Rgb(86, 118, 38), .85 + Hash(cx, y, 111) * .3);
                Pxl(img, 111, cx, y, sc); Pxl(img, 111, cx + 1, y, Shd(sc, .88));
                if (y % 4 == 1) { Pxl(img, 111, cx - 1, y, Rgb(150, 172, 62)); Pxl(img, 111, cx + 2, y, Rgb(136, 160, 56)); }
                if (y % 5 == 2) { int dir = (y / 5) % 2 == 0 ? 1 : -1; Line2(img, 111, cx + (dir > 0 ? 2 : -1), y, cx + dir * 5, y + 2, Rgb(114, 148, 48)); }
            }
        }
        { // 112 sea pickle: glowing green pickles
            ClrTile(img, 112);
            int[][] pk = { new[] { 5, 7 }, new[] { 8, 9 }, new[] { 11, 6 } };
            foreach (var p in pk)
                for (int y = 0; y < p[1]; y++)
                {
                    int w = (y < 2 || y > p[1] - 3) ? 2 : 3, x0 = p[0] - (w >> 1);
                    for (int x = x0; x < x0 + w; x++)
                    {
                        var c = Shd(Rgb(78, 128, 40), .8 + Hash(x, y, 112) * .4);
                        if (y >= p[1] - 2) c = Rgb(190, 228, 100);
                        else if (Hash(x, y, 212) < .15) c = Rgb(40, 80, 24);
                        Pxl(img, 112, x, y, c);
                    }
                }
        }
        void Fan(int i, double[] col)
        {
            ClrTile(img, i);
            for (int y = 0; y < 3; y++) { Pxl(img, i, 7, y, Shd(col, .6)); Pxl(img, i, 8, y, Shd(col, .5)); }
            for (int y = 3; y <= 14; y++)
            {
                int hw = 1 + (int)Math.Floor((y - 3) * .55);
                for (int x = 8 - hw; x <= 7 + hw; x++)
                {
                    if (y == 14 && x % 2 != 0) continue;
                    var c = (x * 5 + y * 3) % 4 == 0 ? Shd(col, 1.25) : Shd(col, .85 + Hash(x, y, i) * .25);
                    Pxl(img, i, x, y, c);
                }
            }
        }
        Fan(113, Rgb(60, 100, 230)); Fan(114, Rgb(230, 110, 170)); Fan(115, Rgb(160, 70, 210)); Fan(116, Rgb(210, 50, 50)); Fan(117, Rgb(240, 210, 70));
    }
}
