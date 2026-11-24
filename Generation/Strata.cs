using System.Numerics;

namespace CubePlanet;

/// <summary>Which block sits at a given depth of a column: surface, filler, stone turning into deepslate, caves carved out.</summary>
public static class Strata
{
    /// <summary>Stone that turns into deepslate with depth (dithered between 56 and 72 blocks below the surface).</summary>
    public static int StoneAt(double s, int i, double r, int salt)
    {
        if (s < 56) return Stone; if (s >= 72) return Deepslate;
        int h = unchecked(salt * 374761393 ^ i * 668265263 ^ (int)Math.Floor(r) * 2147483647);
        h = unchecked((h ^ (int)((uint)h >> 13)) * 1274126177);
        return (uint)(h ^ (int)((uint)h >> 16)) / 4294967296.0 < (s - 56) / 16 ? Deepslate : Stone;
    }
    public static int BaseBlk(Col c, int i, double r, int salt = 0)
    {
        double s = R + c.h[i] - r; gS = 0;
        if (s <= 0) { double rel = r - R; return rel < 0 ? (c.ice[i] != 0 && rel > -1 ? Ice : Water) : rel < c.rs[i] ? Water : 0; } // sea, river, air
        gS = s;
        if (s <= 1) return c.t[i];
        if (s <= 1 + c.fd[i]) { int q = c.f[i]; return q == FillBands ? BANDS[(((int)Math.Floor(r - R) % 24) + 24) % 24] : q; }
        return StoneAt(s, i, r, salt);
    }
    // caves keep away from water they would let run out: 40 blocks below a river's / lake's surface near it, and the top 14 blocks below
    // sea level under low coasts and shallow sea (ground lower than 16)
    public static bool Cavable(Col c, int i, double r) => gS > 0 && r - R > -CAVB && gS >= (c.h[i] < 3 ? 3 : 1.2) && !(r - R > c.rn[i] - 40) && !(c.h[i] < 16 && r - R > -14);
    public static int Gen(Band B, int f, int bx, int by, int bz)
    {
        Col c = ColsOf(B, f, bx >> 5, bz >> 5); int i = (bx & 31) + 32 * (bz & 31); double r = B.inner + by + .5;
        int id = BaseBlk(c, i, r, (bx >> 5) * 7919 + (bz >> 5) * 104729);
        // same result as the chunk generator (a flooded cave is water here too, so the water simulation sees it at unloaded chunks)
        return id != 0 && Cavable(c, i, r) && CaveAt(B, f, bx, by, bz) > 0 ? Aquifers.CaveFill(r - R, id, Aquifers.Column(f, -PI / 4 + (bz + .5) * B.d, -PI / 4 + (bx + .5) * B.d)) : id;
    }
}
