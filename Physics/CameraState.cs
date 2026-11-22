namespace CubePlanet;

/// <summary>Camera position, orientation, orbit parameters and projection.</summary>
public class Cam
{
    public double x, y, z; public V3 f = new(0, 0, -1), u = new(0, 1, 0), rt = new(1, 0, 0), up = new(0, 1, 0);
    public bool orbit, lvl; public double lon, lat = .4, d, fov = PI / 3, near = .1, far = 1000, asp = 1;
    public float[] vp = new float[16];
    const double PI = Math.PI;
}
