namespace CubePlanet;

/// <summary>
/// Block instance state. Small fields: one 16-bit state word per cell, 0 = all defaults. Loaded chunks keep them in Chunk.st (allocated only
/// when a chunk has a non-zero word); the states table keeps every non-zero word so it survives chunks being dropped and regenerated (like
/// player edits; phase 5 saves touched chunks instead). Big fields (inventories, numbers) live in a <see cref="BlockEntity"/> per cell.
/// </summary>
public static class BlockStates
{
    /// <summary>Non-zero state words by cell key (guarded by EditLock: chunk workers read it while generating).</summary>
    public static readonly Dictionary<long, ushort> states = new();
    /// <summary>Block entities by cell key (main thread only).</summary>
    public static readonly Dictionary<long, BlockEntity> entities = new();
    public static int Count => states.Count;

    public static int Get(BlockPos p)
    {
        if (p.Core)
        {
            if (chunks.TryGetValue(cKey(p.X >> 5, p.Y >> 5, p.Z >> 5), out var cc) && cc.d != null) return cc.st?[(p.X & 31) + 32 * ((p.Z & 31) + 32 * (p.Y & 31))] ?? 0;
        }
        else if (chunkMap.TryGetValue(ck(p.B, p.F, p.X >> 5, p.Y >> 5, p.Z >> 5), out var ch) && ch.d != null) return ch.st?[(p.X & 31) + 32 * ((p.Z & 31) + 32 * (p.Y & 31))] ?? 0;
        lock (EditLock) return states.TryGetValue(p.Key, out ushort s) ? s : 0;
    }
    /// <summary>State word of a normalised band cell (fast path for physics / raycast; works for unloaded chunks too).</summary>
    public static int GetCell(int bi, int f, int x, int y, int z) => Get(new BlockPos(false, bi, f, x, y, z));
    /// <summary>State word by key (water levels in the mesher).</summary>
    public static bool TryGet(long key, out ushort s) { lock (EditLock) return states.TryGetValue(key, out s); }

    /// <summary>Changes only the state word of a cell (the block stays) and re-meshes it.</summary>
    /// <param name="background">re-mesh on the worker threads (automatic changes such as crops growing), not on the main thread this frame</param>
    public static void Set(BlockPos p, int state, bool background = false)
    {
        lock (EditLock) { if (state != 0) states[p.Key] = (ushort)state; else states.Remove(p.Key); }
        Chunk ch = null;
        if (p.Core) chunks.TryGetValue(cKey(p.X >> 5, p.Y >> 5, p.Z >> 5), out ch);
        else chunks.TryGetValue(ck(p.B, p.F, p.X >> 5, p.Y >> 5, p.Z >> 5), out ch);
        if (ch != null && ch.d != null) { Store(ch, (p.X & 31) + 32 * ((p.Z & 31) + 32 * (p.Y & 31)), state); ch.ver++; if (background) ch.relight = true; else ch.dirty = true; }
    }
    /// <summary>Writes a state word into a loaded chunk (allocating its state array on the first non-zero word).</summary>
    public static void Store(Chunk ch, int idx, int state)
    {
        if (state == 0 && ch.st == null) return;
        ch.st ??= new ushort[32768]; ch.st[idx] = (ushort)state;
    }

    public static BlockEntity Entity(BlockPos p) => entities.TryGetValue(p.Key, out var e) ? e : null;
    /// <summary>The block entity of a cell, created from the block's entity fields if missing (null for blocks without any).</summary>
    public static BlockEntity EntityOrNew(BlockPos p, int id)
    {
        if (entities.TryGetValue(p.Key, out var e) && e.BlockId == id) return e;
        var d = DEFS[id]; if (d == null || d.State.Entity.Count == 0) return null;
        e = new BlockEntity(p, id, d.State); entities[p.Key] = e; return e;
    }
}
