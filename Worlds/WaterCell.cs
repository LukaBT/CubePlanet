namespace CubePlanet;

/// <summary>A resolved block cell used by the water simulation (k = edit key, b/F/X/Y/Z = normalised band coords).</summary>
public struct WC { public bool ok; public long k; public int b, F, X, Y, Z, id; }
