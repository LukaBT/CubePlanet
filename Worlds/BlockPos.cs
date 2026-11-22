namespace CubePlanet;

/// <summary>
/// One block cell, either in a band (B, F = band / cube face, X / Z along the block grid, Y radial) or in the Cartesian core (X, Y, Z world grid).
/// Used by behaviours, multiblocks and block entities, so they work the same everywhere on the planet.
/// </summary>
public readonly struct BlockPos : IEquatable<BlockPos>
{
    public readonly bool Core; public readonly int B, F, X, Y, Z;
    public BlockPos(bool core, int b, int f, int x, int y, int z) { Core = core; B = b; F = f; X = x; Y = y; Z = z; }

    /// <summary>The cell containing a world point (false outside the world).</summary>
    public static bool TryAt(double x, double y, double z, out BlockPos p)
    {
        if (x * x + y * y + z * z < CORE * CORE) { p = new BlockPos(true, 0, 0, (int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Floor(z)); return true; }
        int bi = P2b(x, y, z); p = bi < 0 ? default : new BlockPos(false, bi, PF, PX, PY, PZ); return bi >= 0;
    }
    public static BlockPos Of(RayHit h) => h.core ? new BlockPos(true, 0, 0, h.ci, h.cj, h.ck) : new BlockPos(false, h.bi, h.f, h.bx, h.by, h.bz);

    /// <summary>Neighbour cell in block-local directions (x = east, y = up, z = south); wraps across cube-face seams and band borders.</summary>
    public bool TryOffset(int dx, int dy, int dz, out BlockPos p)
    {
        if (Core) { p = new BlockPos(true, 0, 0, X + dx, Y + dy, Z + dz); return true; }
        if (!Norm(B, F, X + dx, Y + dy, Z + dz)) { p = default; return false; }
        p = new BlockPos(false, NB, NF, NX, NY, NZ); return true;
    }
    public int Id => Core ? CoreCell(X, Y, Z) : Blk(B, F, X, Y, Z);
    public int State => BlockStates.Get(this);
    /// <summary>Same key as the player-edit / state / entity tables (band cells: Mix hash; core cells: a separate range).</summary>
    public long Key => Core ? long.MinValue + 1 + cEk(X, Y, Z) : Mix(B * 6 + F, X, Y, Z);
    public V3 Center => Core ? new V3(X + .5, Y + .5, Z + .5) : Gp(bands[B], F, Z + .5, X + .5, bands[B].inner + Y + .5);
    /// <summary>World directions of the block-local axes x (east), y (up) and z (south).</summary>
    public (V3 x, V3 y, V3 z) Axes()
    {
        if (Core) return (new V3(1, 0, 0), new V3(0, 1, 0), new V3(0, 0, 1));
        V3 c = Center, up = M.Nrm(c); var (az, ax) = GridAx(c, up); return (ax, up, az);
    }
    /// <summary>Sets the block (and its state word); clears the block entity of the old block.</summary>
    public void Set(int id, int state = 0)
    {
        if (Core) CoreGrid.CoreSetCell(X, Y, Z, id, state);
        else SetCell(B, F, X, Y, Z, id, 0, state);
    }
    public bool Equals(BlockPos o) => Core == o.Core && B == o.B && F == o.F && X == o.X && Y == o.Y && Z == o.Z;
    public override bool Equals(object o) => o is BlockPos p && Equals(p);
    public override int GetHashCode() => Key.GetHashCode();
    public override string ToString() => Core ? $"core {X},{Y},{Z}" : $"band {B} face {F} {X},{Y},{Z}";
}
