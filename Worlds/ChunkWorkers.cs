namespace CubePlanet;

/// <summary>Worker threads that generate chunk block data and mesh chunks in the background, plus the bench profiling counters.</summary>
public static class ChunkWorkers
{
    // ---------- background generation ----------
    // Worker threads generate chunk block data (terrain, caves, ores, features) nearest-first from a list the render thread publishes.
    // Everything Generate() touches is thread-safe: result slots and caches are per thread, player edits are read under EditLock, the chunk itself is claimed via Chunk.gst.
    // Meshing jobs come first (their neighbours are already generated, so a finished mesh shows up right away), then generation.
    internal static readonly object wlock = new(); internal static Chunk[] wlist = Array.Empty<Chunk>(), mlist = Array.Empty<Chunk>(); internal static int wnext, mnext; internal static bool wstarted;
    public sealed class MeshJobResult { public Chunk ch; public BuildResult r; public List<long> wait; public bool failed; public int ver; }
    public static readonly System.Collections.Concurrent.ConcurrentQueue<MeshJobResult> meshDone = new();
    internal static void StartWorkers()
    {
        if (wstarted) return; wstarted = true;
        int n = Math.Clamp(Environment.ProcessorCount - 2, 1, 14);
        for (int i = 0; i < n; i++) new Thread(GenWorker) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "chunkwork" + i }.Start();
    }
    public static void SetGenList(Chunk[] list) { StartWorkers(); lock (wlock) { wlist = list; wnext = 0; Monitor.PulseAll(wlock); } }
    public static void SetMeshList(Chunk[] list) { StartWorkers(); lock (wlock) { mlist = list; mnext = 0; Monitor.PulseAll(wlock); } }
    internal static void GenWorker()
    {
        while (true)
        {
            Chunk c = null; bool mesh = false;
            lock (wlock)
            {
                while (mnext < mlist.Length) { var x = mlist[mnext++]; if (Volatile.Read(ref x.mst) == 0) { c = x; mesh = true; break; } }
                if (c == null)
                    while (wnext < wlist.Length)
                    {
                        var x = wlist[wnext++];
                        if (Volatile.Read(ref x.d) == null && x.gst == 0) { c = x; break; }
                    }
                if (c == null) { Monitor.Wait(wlock, 50); continue; }
            }
            if (mesh)
            {
                if (Interlocked.CompareExchange(ref c.mst, 1, 0) != 0) continue;
                ResetBlkCache();
                long tp = System.Diagnostics.Stopwatch.GetTimestamp();
                int ver = Volatile.Read(ref c.ver);
                try { var r = Build(c, out var wl); meshDone.Enqueue(new MeshJobResult { ch = c, r = r, wait = wl, ver = ver }); }
                catch { meshDone.Enqueue(new MeshJobResult { ch = c, failed = true }); }
                Interlocked.Add(ref profMeshTicks, System.Diagnostics.Stopwatch.GetTimestamp() - tp); Interlocked.Increment(ref profMeshN);
                continue;
            }
            if (Interlocked.CompareExchange(ref c.gst, 1, 0) != 0) continue;
            try { Generate(c); } catch { c.gfail = true; c.gst = 0; }
        }
    }

    public static long profMeshTicks, profMeshN, profGenTicks, profGenN, profSyncGen, profScanTicks, profScanN, profBuildTicks, profBuildN, profRebuild;
}
