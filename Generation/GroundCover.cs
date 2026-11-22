namespace CubePlanet;

/// <summary>Ground cover on land and under water, applied after features.</summary>
public static class GroundCover
{
    // ground cover: grass, ferns, flowers, mushrooms, dead bushes, sugar cane, pumpkins and melons (after trees/structures, only into free air)
    internal static void Cover(Chunk ch, byte[] d, Col c, Band B, int f)
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
                if (bi == 9 || bi == 17) { if (hi && u < .012) id = 50; }
                else if ((bl == 7 || bl == 1 || bl == 3 || bl == 82 || bl == 76) && h < 3 && x > 0 && x < CS - 1 && z > 0 && z < CS - 1 && Math.Min(Math.Min(c.h[i - 1], c.h[i + 1]), Math.Min(c.h[i - CS], c.h[i + CS])) < 0)
                { if (u < .3) { id = 55; hg = 1 + (int)Math.Floor(v * 3); } }
                else if (bl == 7 || bl == 12) { if (hi && (bi == 8 || bi == 17) && u < .012) id = 50; }
                else if (Array.IndexOf(GR, bl) >= 0 && bl != 8 && hi)
                {
                    switch (bi)
                    {
                        case 1: id = u < .42 ? 43 : u < .46 ? FLW[(int)Math.Floor(v * 4)] : u < .4625 ? 56 : 0; break;
                        case 2: case 3: id = u < .28 ? 43 : u < .305 ? FLW[(int)Math.Floor(v * 4)] : u < .315 ? (v < .5 ? 53 : 54) : 0; break;
                        case 4: id = u < .3 ? 45 : u < .4 ? 43 : u < .41 ? (v < .5 ? 53 : 54) : u < .425 ? 109 : 0; break;
                        case 14: id = u < .3 ? 43 : u < .45 ? 45 : u < .52 ? 106 : u < .56 ? FLW[(int)Math.Floor(v * 4)] : u < .565 ? (v < .5 ? 53 : 54) : 0; break;
                        case 15: id = u < .28 ? 104 : u < .46 ? 43 : u < .49 ? 105 : u < .51 ? FLW[(int)Math.Floor(v * 4)] : 0; break;
                        case 16: id = u < .3 ? 43 : u < .33 ? FLW[(int)Math.Floor(v * 4)] : 0; break;
                        case 19: id = u < .35 ? 45 : u < .55 ? 43 : u < .58 ? (v < .5 ? 53 : 54) : u < .6 ? 109 : 0; break;
                        case 26: id = u < .45 ? (v < .6 ? 43 : 45) : u < .47 ? 57 : 0; break;
                        case 28: id = u < .4 ? (v < .6 ? 43 : 45) : 0; break;
                        case 29: id = u < .3 ? 43 : u < .62 ? FL2[(int)Math.Floor(v * 7)] : 0; break;
                        case 6: id = u < .5 ? (v < .6 ? 43 : 45) : u < .505 ? 57 : 0; break;
                        case 7: id = u < .4 ? 44 : 0; break;
                        case 10: id = u < .3 ? 43 : u < .33 ? 48 : u < .35 ? (v < .5 ? 53 : 54) : 0; break;
                    }
                }
                if (id == 55) { for (int k = 0; k < hg && y + k < CS; k++) if (d[j + k * 1024] == 0) d[j + k * 1024] = 55; }
                else if (id != 0) d[j] = (byte)id;
            }
    }

    // underwater ground cover: seagrass, short kelp, sea pickles and coral fans standing in the water above the sea floor
    internal static void CoverSea(Chunk ch, byte[] d, Col c, Band B, int f)
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
                if (d[j] != 9) continue;
                int bl = y > 0 ? d[j - CS * CS] : Gen(B, f, cx0 + x, gy, cz0 + z);
                if (bl == 0 || bl == 9 || bl == 75 || SPC[bl] != 0 || LEAFS[bl]) continue;
                double u = HH(f, cx0 + x, cz0 + z, 62), v = HH(f, cx0 + x, cz0 + z, 63); int id = 0, hg = 1;
                switch (bi)
                {
                    case 21: id = u < .22 ? 110 : u < .40 ? 113 + (int)Math.Floor(v * 5) : u < .43 ? 112 : 0; if (id == 110) hg = 1 + (v < .4 ? 1 : 0); break;
                    case 31: id = u < .25 ? 110 : u < .28 ? 112 : u < .31 ? 113 + (int)Math.Floor(v * 5) : 0; if (id == 110) hg = 1 + (v < .4 ? 1 : 0); break;
                    case 22: id = u < .1 ? 110 : u < .3 ? 111 : 0; if (id == 111) hg = 1 + (int)Math.Floor(v * 3); break;
                    case 0: id = u < .12 ? 110 : 0; break;
                }
                if (id == 0) continue;
                if (-h < hg + 1.3) hg = 1;
                for (int k = 0; k < hg && y + k < CS; k++) if (d[j + k * CS * CS] == 9) d[j + k * CS * CS] = (byte)id; else break;
            }
    }
}
