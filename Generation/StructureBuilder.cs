namespace CubePlanet;

/// <summary>Collects blocks as (x, z, y absolute, id) relative to an anchor column; id 255 = carve to air.</summary>
public class Sb
{
    public List<int> L = new();
    public int[] m = { 1000000000, -1000000000, 1000000000, -1000000000, 1000000000, -1000000000 };
    public void Put(int x, int y, int z, int id)
    {
        L.Add(x); L.Add(z); L.Add(y); L.Add(id);
        if (x < m[0]) m[0] = x; if (x > m[1]) m[1] = x;
        if (z < m[2]) m[2] = z; if (z > m[3]) m[3] = z;
        if (y < m[4]) m[4] = y; if (y > m[5]) m[5] = y;
    }
    public void Box(int x0, int y0, int z0, int x1, int y1, int z1, int id)
    {
        for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) for (int z = z0; z <= z1; z++) Put(x, y, z, id);
    }
}
