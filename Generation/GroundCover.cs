namespace CubePlanet;

/// <summary>Ground cover on land and under water, applied after features.</summary>
public static class GroundCover
{
    internal static readonly int[] FANS = { TubeCoralFan, BrainCoralFan, BubbleCoralFan, FireCoralFan, HornCoralFan };

    // ground cover: grass, ferns, flowers, mushrooms, dead bushes, sugar cane, pumpkins and melons (after trees/structures, only into free air)
    internal static void Cover(Chunk ch, ushort[] d, Col c, Band B, int f)
    {
        int cx0 = ch.cx * CS, cz0 = ch.cz * CS, cy0 = ch.cy * CS;
        for (int z = 0; z < CS; z++)
            for (int x = 0; x < CS; x++)
            {
                int i = x + CS * z; double h = c.h[i];
                if (h < .3 || c.ice[i] != 0) continue;
                bool hi = h >= 1.2;
                int gy = GyOf(B, h), y = gy + 1 - cy0;
                if (y < 0 || y >= CS) continue;
                int j = x + CS * (z + CS * y);
                if (d[j] != 0) continue;
                int bl = y > 0 ? d[j - CS * CS] : Gen(B, f, cx0 + x, gy, cz0 + z);
                if (bl == 0) continue;
                int bi = c.b[i]; double u = HH(f, cx0 + x, cz0 + z, 60), v = HH(f, cx0 + x, cz0 + z, 61); int id = 0, hg = 1;
                if (bi == 9 || bi == 17) { if (hi && u < .012) id = DeadBush; }
                else if ((bl == Sand || bl == Grass || bl == Dirt || bl == CoralSand || bl == LushGrass) && h < 3 && x > 0 && x < CS - 1 && z > 0 && z < CS - 1 && Math.Min(Math.Min(c.h[i - 1], c.h[i + 1]), Math.Min(c.h[i - CS], c.h[i + CS])) < 0)
                { if (u < .3) { id = SugarCane; hg = 1 + (int)Math.Floor(v * 3); } }
                else if (bl == Sand || bl == RedSand) { if (hi && (bi == 8 || bi == 17) && u < .012) id = DeadBush; }
                else if (Array.IndexOf(GR, bl) >= 0 && bl != Snow && hi)
                {
                    switch (bi)
                    {
                        case 1: id = u < .42 ? TallGrass : u < .46 ? FLW[(int)Math.Floor(v * 4)] : u < .4625 ? Pumpkin : 0; break;
                        case 2: case 3: id = u < .28 ? TallGrass : u < .305 ? FLW[(int)Math.Floor(v * 4)] : u < .315 ? (v < .5 ? RedMushroom : BrownMushroom) : 0; break;
                        case 4: id = u < .3 ? Fern : u < .4 ? TallGrass : u < .41 ? (v < .5 ? RedMushroom : BrownMushroom) : u < .425 ? SweetBerryBush : 0; break;
                        case 14: id = u < .3 ? TallGrass : u < .45 ? Fern : u < .52 ? Bluebell : u < .56 ? FLW[(int)Math.Floor(v * 4)] : u < .565 ? (v < .5 ? RedMushroom : BrownMushroom) : 0; break;
                        case 15: id = u < .28 ? PinkPetals : u < .46 ? TallGrass : u < .49 ? PinkTulip : u < .51 ? FLW[(int)Math.Floor(v * 4)] : 0; break;
                        case 16: id = u < .3 ? TallGrass : u < .33 ? FLW[(int)Math.Floor(v * 4)] : 0; break;
                        case 19: id = u < .35 ? Fern : u < .55 ? TallGrass : u < .58 ? (v < .5 ? RedMushroom : BrownMushroom) : u < .6 ? SweetBerryBush : 0; break;
                        case 26: id = u < .45 ? (v < .6 ? TallGrass : Fern) : u < .47 ? Melon : 0; break;
                        case 28: id = u < .4 ? (v < .6 ? TallGrass : Fern) : 0; break;
                        case 29: id = u < .3 ? TallGrass : u < .62 ? FL2[(int)Math.Floor(v * 7)] : 0; break;
                        case 6: id = u < .5 ? (v < .6 ? TallGrass : Fern) : u < .505 ? Melon : 0; break;
                        case 7: id = u < .4 ? DryGrass : 0; break;
                        case 10: id = u < .3 ? TallGrass : u < .33 ? Cornflower : u < .35 ? (v < .5 ? RedMushroom : BrownMushroom) : 0; break;
                    }
                }
                if (id == SugarCane) { for (int k = 0; k < hg && y + k < CS; k++) if (d[j + k * 1024] == 0) d[j + k * 1024] = (ushort)SugarCane; }
                else if (id != 0) d[j] = (ushort)id;
            }
    }

    // underwater ground cover: seagrass, short kelp, sea pickles and coral fans standing in the water above the sea floor
    internal static void CoverSea(Chunk ch, ushort[] d, Col c, Band B, int f)
    {
        int cx0 = ch.cx * CS, cz0 = ch.cz * CS, cy0 = ch.cy * CS;
        for (int z = 0; z < CS; z++)
            for (int x = 0; x < CS; x++)
            {
                int i = x + CS * z; double h = c.h[i];
                if (h > -2.3 || h < -30 || c.ice[i] != 0) continue;
                int bi = c.b[i]; if (bi != 0 && bi != 21 && bi != 22 && bi != 31) continue;
                int gy = GyOf(B, h), y = gy + 1 - cy0;
                if (y < 0 || y >= CS) continue;
                int j = x + CS * (z + CS * y);
                if (d[j] != Water) continue;
                int bl = y > 0 ? d[j - CS * CS] : Gen(B, f, cx0 + x, gy, cz0 + z);
                if (bl == 0 || bl == Water || bl == Lava || SPC[bl] != 0 || LEAFS[bl]) continue;
                double u = HH(f, cx0 + x, cz0 + z, 62), v = HH(f, cx0 + x, cz0 + z, 63); int id = 0, hg = 1;
                switch (bi)
                {
                    case 21: id = u < .22 ? Seagrass : u < .40 ? FANS[(int)Math.Floor(v * 5)] : u < .43 ? SeaPickle : 0; if (id == Seagrass) hg = 1 + (v < .4 ? 1 : 0); break;
                    case 31: id = u < .25 ? Seagrass : u < .28 ? SeaPickle : u < .31 ? FANS[(int)Math.Floor(v * 5)] : 0; if (id == Seagrass) hg = 1 + (v < .4 ? 1 : 0); break;
                    case 22: id = u < .1 ? Seagrass : u < .3 ? Kelp : 0; if (id == Kelp) hg = 1 + (int)Math.Floor(v * 3); break;
                    case 0: id = u < .12 ? Seagrass : 0; break;
                }
                if (id == 0) continue;
                if (-h < hg + 1.3) hg = 1;
                for (int k = 0; k < hg && y + k < CS; k++) if (d[j + k * CS * CS] == Water) d[j + k * CS * CS] = (ushort)id; else break;
            }
    }
}
