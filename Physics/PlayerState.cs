namespace CubePlanet;

/// <summary>Player state: position, velocity (horizontal + along gravity), flags.</summary>
public class Plr
{
    public bool on = true, fly, gnd, wet, head, ready, core; public V3 p, vh, gv = new(0, 1, 0); public double vu; public int rf;
}
