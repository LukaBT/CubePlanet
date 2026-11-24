using System.Diagnostics;

namespace CubePlanet;

/// <summary>
/// --benchfly (no window): the main thread's per-frame work while flying fast (player fly + sprint with collision, chunk loading and
/// generation requests, the block-target raycast, the HUD text, water, machines), 60 frames a second for 20 s: frame times and GC pauses.
/// Meshing and drawing are not included (they need the GPU).
/// </summary>
public static class FlyBench
{
    public static void Run()
    {
        World.InitWorldData(1200, 1337); ChunkGenerator.mainTid = Environment.CurrentManagedThreadId;
        V3 c = new(C.x, C.y, C.z), up = M.Nrm(c);
        P.on = false; C.lvl = true;
        // look along the ground (towards some tangent direction)
        V3 fwd = M.Nrm(M.Cross(up, new V3(.31, .83, .47))); C.f = fwd; C.u = up;
        var times = new List<double>(); int g0 = GC.CollectionCount(0), g2 = GC.CollectionCount(2); double dt = 1 / 60.0;
        var total = Stopwatch.StartNew(); V3 start = new(C.x, C.y, C.z);
        for (int fr = 0; fr < 60 * 20; fr++)
        {
            long t0 = Stopwatch.GetTimestamp();
            // free camera with sprint, as Game does it
            double alt = Math.Sqrt(C.x * C.x + C.y * C.y + C.z * C.z) - World.R, sp = 12 * Camera.spd * Math.Max(1, alt / 25) * dt * 5;
            Camera.CamMove(sp, 0, 0, C.lvl);
            Camera.CamUpdate(dt); ChunkLoader.LoadStep(); WaterSim.WaterTick(dt); BlockActions.Tick(dt);
            ChunkMeshes.PublishGen(); ChunkLoader.frameNo++;
            if (fr % 6 == 0) Blocks.Wet(BlockAccess.GetAt(C.x, C.y, C.z));
            if (fr % 2 == 0) Highlight.TG = BlockEditing.Raycast(10);
            if (fr % 10 == 0) Hud.HudText();
            times.Add((Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
            // keep a steady real-time pace so the worker threads generate as they would in the game
            double wait = (fr + 1) * dt * 1000 - total.Elapsed.TotalMilliseconds; if (wait > 1) Thread.Sleep((int)wait);
        }
        times.Sort(); double sum = times.Sum();
        // mesh every generated chunk (as the worker threads would): failures fall back to the main thread in the game
        int built = 0, failed = 0; double mt = 0, mworst = 0; string err = "";
        foreach (var ch in chunks.Values.ToArray())
        {
            if (ch.d == null || ch.core) continue;
            long m0 = Stopwatch.GetTimestamp();
            try { Mesher.Build(ch, out _); built++; } catch (Exception e) { failed++; if (err.Length == 0) err = e.GetType().Name + ": " + e.Message + " @ " + e.StackTrace?.Split((char)10).FirstOrDefault()?.Trim(); }
            double ms = (Stopwatch.GetTimestamp() - m0) * 1000.0 / Stopwatch.Frequency; mt += ms; mworst = Math.Max(mworst, ms);
        }
        Console.WriteLine($"BENCHFLY meshing: {built} chunks, {failed} failed, {mt / Math.Max(1, built):0.00} ms avg, worst {mworst:0.0} ms {err}");
        Console.WriteLine($"BENCHFLY {(new V3(C.x, C.y, C.z) - start).Len:0} blocks in 20 s: frame {sum / times.Count:0.00} ms avg, 99% {times[(int)(times.Count * .99)]:0.0} ms, worst {times[^1]:0.0} ms, " +
                          $"{times.Count(t => t > 16)} frames over 16 ms, {times.Count(t => t > 33)} over 33 ms; GC gen0 +{GC.CollectionCount(0) - g0}, gen2 +{GC.CollectionCount(2) - g2}; chunks {chunks.Count}");
    }
}
