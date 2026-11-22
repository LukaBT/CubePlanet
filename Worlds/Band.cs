namespace CubePlanet;

/// <summary>One radial shell of the cube-sphere: every band doubles the face resolution of the one below it.</summary>
public class Band { public int b, fc, cols, rows, inner; public double d; }
