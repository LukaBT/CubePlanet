namespace CubePlanet;

public struct Link { public int g; public bool acU, af, al; }

/// <summary>Cube-face axes (same as the original) and the face-to-face seam table.</summary>
public static class Cube
{
    public static readonly V3[] N = { new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1) };
    public static readonly V3[] U = { new(0, 0, -1), new(0, 0, 1), new(1, 0, 0), new(1, 0, 0), new(1, 0, 0), new(-1, 0, 0) };
    public static readonly V3[] V = { new(0, 1, 0), new(0, 1, 0), new(0, 0, -1), new(0, 0, 1), new(0, 1, 0), new(0, 1, 0) };
    public static readonly int[] DX = { 1, -1, 0, 0, 0, 0 }, DY = { 0, 0, 1, -1, 0, 0 }, DZ = { 0, 0, 0, 0, 1, -1 };
    // per face: 4 corners x (x,y,z) offsets of the quad for face direction fc
    public static readonly int[][] CN =
    {
        new[]{1,0,1, 1,0,0, 1,1,0, 1,1,1}, new[]{0,0,0, 0,0,1, 0,1,1, 0,1,0}, new[]{0,1,1, 1,1,1, 1,1,0, 0,1,0},
        new[]{0,0,0, 1,0,0, 1,0,1, 0,0,1}, new[]{0,0,1, 1,0,1, 1,1,1, 0,1,1}, new[]{1,0,0, 0,0,0, 0,1,0, 1,1,0}
    };
    public static readonly int[] CU = { 0, 1, 1, 0 }, CV = { 0, 0, 1, 1 };

    public static readonly Link[] LK = BuildLinks();

    static Link[] BuildLinks()
    {
        var lk = new Link[24];
        for (int f = 0; f < 6; f++)
            for (int s = 0; s < 4; s++)
            {
                V3 ax = s < 2 ? U[f] : V[f];
                double sg = s % 2 != 0 ? -1 : 1;
                V3 dir = ax * sg;
                int g = 0;
                for (int i = 0; i < 6; i++) if (M.Dot(N[i], dir) > .5) { g = i; break; }
                V3 al = s < 2 ? V[f] : U[f];
                double dU = M.Dot(U[g], N[f]), dV = M.Dot(V[g], N[f]);
                bool acU = Math.Abs(dU) > .5;
                double across = acU ? dU : dV;
                V3 other = acU ? V[g] : U[g];
                lk[f * 4 + s] = new Link { g = g, acU = acU, af = across > 0, al = M.Dot(other, al) < 0 };
            }
        return lk;
    }

    public static int FaceOf(double x, double y, double z)
    {
        double ax = Math.Abs(x), ay = Math.Abs(y), az = Math.Abs(z);
        if (ax >= ay && ax >= az) return x > 0 ? 0 : 1;
        if (ay >= az) return y > 0 ? 2 : 3;
        return z > 0 ? 4 : 5;
    }
}
