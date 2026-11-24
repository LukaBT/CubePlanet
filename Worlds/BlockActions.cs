namespace CubePlanet;

/// <summary>
/// Placing, breaking and using blocks the way a player does: facing from the look direction, multiblocks (every part must fit; breaking one
/// part removes all), behaviour hooks, block entities and neighbour notifications. Creative rules for now: placing costs nothing, breaking drops
/// nothing (a container's contents are lost).
/// </summary>
public static class BlockActions
{
    /// <summary>Cells a placed block may replace: air, water, plants.</summary>
    public static bool Replaceable(int id) => id == 0 || id == Water || (SPC[id] != 0);

    /// <summary>Facing index for a block placed while looking along look (see placement_facing).</summary>
    public static int FacingFor(BlockDefinition d, BlockPos p, V3 look)
    {
        var f = d.State.Find("facing"); if (f == null) return 0;
        var (ax, ay, az) = p.Axes(); double x = M.Dot(look, ax), y = M.Dot(look, ay), z = M.Dot(look, az);
        string dir = Math.Abs(x) > Math.Abs(z) ? (x > 0 ? "east" : "west") : (z > 0 ? "south" : "north");
        if (Array.IndexOf(f.Values, "up") >= 0 && Math.Abs(y) > Math.Max(Math.Abs(x), Math.Abs(z))) dir = y > 0 ? "up" : "down";
        if (!d.FacingAway) dir = dir switch { "east" => "west", "west" => "east", "north" => "south", "south" => "north", "up" => "down", _ => "up" };
        return Math.Max(0, Array.IndexOf(f.Values, dir));
    }
    static string FacingName(BlockDefinition d, int state) { var f = d.State.Find("facing"); return f == null ? "north" : f.Values[f.Index((uint)state)]; }

    /// <summary>Cells of all parts of the multiblock that p belongs to (just p for normal blocks); false if p's state matches no part.</summary>
    public static bool Parts(BlockPos p, int id, int state, List<(BlockPos pos, int part)> into)
    {
        into.Clear(); var d = DEFS[id];
        if (d == null || d.Parts.Length == 0) { into.Add((p, -1)); return true; }
        int me = d.PartOf((uint)state); if (me < 0) { into.Add((p, -1)); return false; }
        string face = FacingName(d, state);
        var (ox, oy, oz) = BlockVariant.TurnOffset(face, d.Parts[me].X, d.Parts[me].Y, d.Parts[me].Z);
        if (!p.TryOffset(-ox, -oy, -oz, out var origin)) return false;
        for (int i = 0; i < d.Parts.Length; i++)
        {
            var (x, y, z) = BlockVariant.TurnOffset(face, d.Parts[i].X, d.Parts[i].Y, d.Parts[i].Z);
            if (origin.TryOffset(x, y, z, out var q)) into.Add((q, i));
        }
        return true;
    }

    /// <summary>Places block id at cell 'at' as a player looking along 'look' would. Returns false (with a reason) if it does not fit.</summary>
    /// <param name="upperHalf">the click was on the upper half of a side face or on the underside of a block: blocks with a "half" field
    /// (values bottom / top) are placed upside down, like Minecraft stairs</param>
    public static bool Place(BlockPos at, int id, V3 look, out string why, bool upperHalf = false)
    {
        why = null; var d = DEFS[id];
        if (d == null) { at.Set(id); return true; }
        int facing = FacingFor(d, at, look); var ff = d.State.Find("facing");
        uint baseWord = ff != null ? ff.With(0, facing) : 0;
        var hf = d.State.Find("half");
        if (hf != null && hf.Packed && hf.Values != null && Array.IndexOf(hf.Values, "top") >= 0 && Array.IndexOf(hf.Values, "bottom") >= 0)
            baseWord = hf.With(baseWord, Array.IndexOf(hf.Values, upperHalf ? "top" : "bottom"));
        var cells = new List<(BlockPos pos, uint word)>();
        if (d.Parts.Length == 0) cells.Add((at, baseWord));
        else
        {
            string face = ff != null ? ff.Values[facing] : "north";
            foreach (var part in d.Parts)
            {
                var (x, y, z) = BlockVariant.TurnOffset(face, part.X, part.Y, part.Z);
                if (!at.TryOffset(x, y, z, out var q)) { why = "Does not fit here"; return false; }
                cells.Add((q, d.State.Apply(baseWord, part.State)));
            }
        }
        foreach (var (q, _) in cells) if (!Replaceable(q.Id)) { why = $"{d.Name} needs {cells.Count} free cells"; return false; }
        if (BEH[id] != null && !BEH[id].CanPlaceAt(at, id, out why)) return false;
        foreach (var (q, w) in cells) q.Set(id, (int)w);
        BEH[id]?.OnPlace(at, id);
        foreach (var (q, _) in cells) NotifyNeighbors(q);
        return true;
    }

    /// <summary>Breaks the block at p (every part of a multiblock).</summary>
    public static void Break(BlockPos p)
    {
        int id = p.Id; if (id == 0) return;
        int state = p.State; var parts = new List<(BlockPos pos, int part)>();
        Parts(p, id, state, parts);
        var drops = BEH[id]?.Harvest(p, id, state);
        BEH[id]?.OnBreak(p, id, state);
        foreach (var (q, _) in parts) if (q.Id == id) { BlockStates.entities.Remove(q.Key); q.Set(0); }
        foreach (var (q, _) in parts) NotifyNeighbors(q);
        if (drops != null) foreach (var s in drops) if (s != null && !s.IsEmpty) PlayerInventory.Add(s); // straight into the inventory (no item drops yet)
    }

    /// <summary>Removes p if it is a multiblock part whose other parts are gone (e.g. the other door half was replaced).</summary>
    public static void RemoveIfBroken(BlockPos p, int id)
    {
        var d = DEFS[id]; if (d == null || d.Parts.Length == 0) return;
        var parts = new List<(BlockPos pos, int part)>();
        if (!Parts(p, id, p.State, parts) || parts.Count < d.Parts.Length || parts.Any(q => q.pos.Id != id)) { BlockStates.entities.Remove(p.Key); p.Set(0); }
    }
    /// <summary>Does right-clicking this block do something?</summary>
    public static bool CanUse(int id) => (BEH[id]?.Usable ?? false) || (DEFS[id]?.UiId.Length ?? 0) > 0;
    /// <summary>Right click on a block: its behaviour, else its UI (ui_id). False if the block has no use.</summary>
    public static bool Use(BlockPos p)
    {
        int id = p.Id, state = p.State;
        if (BEH[id] != null && BEH[id].OnUse(p, id, state)) return true;
        var d = DEFS[id];
        if (d != null && d.UiId.Length > 0) { UI.OpenBlockUi(d.UiId, p, id); return true; }
        return false;
    }

    static readonly int[][] N6 = { new[] { 1, 0, 0 }, new[] { -1, 0, 0 }, new[] { 0, 1, 0 }, new[] { 0, -1, 0 }, new[] { 0, 0, 1 }, new[] { 0, 0, -1 } };
    public static void NotifyNeighbors(BlockPos p)
    {
        foreach (var o in N6)
            if (p.TryOffset(o[0], o[1], o[2], out var q)) { int nb = q.Id; BEH[nb]?.OnNeighborChanged(q, nb, p); }
    }

    // ---------- block entity ticking (furnaces, machines): 10 times per second ----------
    static double tickAcc;
    public static void Tick(double dt)
    {
        tickAcc += dt; if (tickAcc < .1) return; double step = tickAcc; tickAcc = 0;
        if (BlockStates.entities.Count == 0) return;
        Kinetics.Tick(step); // rotation first: generators know their input before the energy networks balance
        EnergyNet.Tick(step);
        foreach (var e in BlockStates.entities.Values.ToArray()) BEH[e.BlockId]?.OnTick(e, step);
    }
}
