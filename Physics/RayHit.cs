namespace CubePlanet;

/// <summary>Result of the view raycast: hit point, the empty cell before it, and the block coordinates (core or band).</summary>
public class RayHit { public double x, y, z, px, py, pz; public bool core; public int id, ci, cj, ck, bi, f, bx, by, bz; }
