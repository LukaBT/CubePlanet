using System.Diagnostics;

namespace CubePlanet;

/// <summary>--benchgen (no window): cost of looking up single blocks in chunks that are not generated (what physics and raycasts do when
/// flying ahead of chunk loading) and of computing new terrain columns.</summary>
public static class GenBench
{
    public static void Run()
    {
        World.InitWorldData(1200, 1337);
        V3 c = new(C.x, C.y, C.z); int bi = BlockAccess.P2b(c.X, c.Y, c.Z); int f = PF, x0 = PX, y0 = PY, z0 = PZ; Band B = bands[bi];
        var rnd = new Random(3); int n = 20000;
        for (int i = 0; i < 2000; i++) Gen(B, f, x0 + rnd.Next(-200, 200), y0 + rnd.Next(-20, 10), z0 + rnd.Next(-200, 200)); // warm-up
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < n; i++) Gen(B, f, x0 + 300 + rnd.Next(-200, 200), y0 + rnd.Next(-20, 10), z0 + rnd.Next(-200, 200));
        double us = sw.Elapsed.TotalMilliseconds * 1000 / n;
        var sw2 = Stopwatch.StartNew(); int cols = 0;
        for (int cz = 20; cz < 30; cz++) for (int cx = 0; cx < 10; cx++) { ColsOf(B, f, (x0 >> 5) + 20 + cx, (z0 >> 5) + cz); cols++; }
        Console.WriteLine($"BENCHGEN single-block lookup in an ungenerated chunk: {us:0.0} us each; new terrain column chunk (32x32 columns): {sw2.Elapsed.TotalMilliseconds / cols:0.00} ms");
    }
}
