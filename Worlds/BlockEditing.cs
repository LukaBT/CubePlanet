namespace CubePlanet;

/// <summary>Breaking, placing, using and picking blocks with the item in the selected hotbar slot.</summary>
public static class BlockEditing
{

    // ---------- editing ----------
    /// <summary>March along the view ray; returns the first solid block hit plus the empty cell just before it.</summary>
    /// <remarks>A block is hit where the ray enters one of its selection boxes (geometry.selection). px/py/pz is the last point in the cell before
    /// the hit cell, so placing next to a slab or a flower never lands inside that block's own cell.</remarks>
    public static RayHit Raycast(double reach)
    {
        double px = C.x, py = C.y, pz = C.z, lx0 = C.x, ly0 = C.y, lz0 = C.z; long cell = long.MinValue;
        for (double t = 0; t <= reach; t += .07)
        {
            double x = C.x + C.f.X * t, y = C.y + C.f.Y * t, z = C.z + C.f.Z * t;
            if (x * x + y * y + z * z < CORE * CORE)
            {
                int ci = (int)Math.Floor(x), cj = (int)Math.Floor(y), ck = (int)Math.Floor(z); long key = long.MinValue + 1 + cEk(ci, cj, ck); // core cells, kept apart from the band cell hashes
                if (key != cell) { px = lx0; py = ly0; pz = lz0; cell = key; }
                int id = CoreAt(x, y, z), st = HASVAR[id] ? BlockStates.Get(new BlockPos(true, 0, 0, ci, cj, ck)) : 0;
                if (Selects(id, st, x - ci, y - cj, z - ck)) return new RayHit { x = x, y = y, z = z, px = px, py = py, pz = pz, core = true, id = id, state = st, ci = ci, cj = cj, ck = ck };
                lx0 = x; ly0 = y; lz0 = z; continue;
            }
            int bi = P2b(x, y, z);
            if (bi >= 0)
            {
                int f = PF, bx = PX, by = PY, bz = PZ; double fx = PFX, fy = PFY, fz = PFZ;
                long key = Mix(bi * 6 + f, bx, by, bz);
                if (key != cell) { px = lx0; py = ly0; pz = lz0; cell = key; }
                int hb = Blk(bi, f, bx, by, bz), st = HASVAR[hb] ? BlockStates.GetCell(bi, f, bx, by, bz) : 0;
                if (Selects(hb, st, fx, fy, fz)) return new RayHit { x = x, y = y, z = z, px = px, py = py, pz = pz, id = hb, state = st, bi = bi, f = f, bx = bx, by = by, bz = bz };
            }
            lx0 = x; ly0 = y; lz0 = z;
        }
        return null;
    }
    /// <param name="useClick">the right button went down this frame: using a block (door, chest...) happens once per click, not while held</param>
    /// <param name="sneaking">sneak + right click places against a usable block instead of using it</param>
    public static void Edit(bool brk, bool plc, bool sneaking = false, bool useClick = false)
    {
        var held = PlayerInventory.Held;
        var T = Raycast(P.on ? 6 : 10);
        if (plc && !sneaking && T != null && BlockActions.CanUse(T.id)) { if (useClick) BlockActions.Use(BlockPos.Of(T)); return; }
        if (held is { IsEmpty: false } && held.Def?.Behavior == "bucket") { BucketUse(held, brk, plc); return; }
        if (brk && T != null && held is { IsEmpty: false } && held.Def?.Behavior == "drill") { Tools.Drill(held, BlockPos.Of(T), C.f); return; }
        if (brk && T != null && held is { IsEmpty: false } && held.Def?.Behavior == "chainsaw") { Tools.Chainsaw(held, BlockPos.Of(T)); return; }
        if (plc && held is { IsEmpty: false } && held.Def?.Behavior == "wire_spool") { if (T != null && useClick) Wires.Use(BlockPos.Of(T)); return; }
        if (plc && held is { IsEmpty: false } && held.Def?.Behavior == "hoe") { if (T != null && useClick) { if (!Farming.Till(BlockPos.Of(T))) UI.Note("A hoe tills dirt or grass with air above it", 1.5); } return; }
        if (T == null) return;
        if (brk)
        {
            int old0 = T.id;
            BlockActions.Break(BlockPos.Of(T));
            if (UWP[old0]) PlaceWater(T.x, T.y, T.z); // underwater plants leave water behind, not an air pocket
            V3 g = M.Nrm(new V3(T.x, T.y, T.z));
            for (int k = 1; k < 6; k++)
            {
                double X = T.x + g.X * k, Y = T.y + g.Y * k, Z = T.z + g.Z * k; int b = GetAt(X, Y, Z);
                if (SPC[b] != 0 && ATT[b] == 0) { SetAt(X, Y, Z, 0); if (UWP[b]) PlaceWater(X, Y, Z); } else break;
            }
        }
        else if (plc)
        {
            bool rp = T.core ? SPC[T.id] != 0 : SPC[Blk(T.bi, T.f, T.bx, T.by, T.bz)] != 0;
            double X = rp ? T.x : T.px, Y = rp ? T.y : T.py, Z = rp ? T.z : T.pz;
            int sel = PlayerInventory.HeldBlock; if (sel == 0) return;
            if ((X - C.x) * (X - C.x) + (Y - C.y) * (Y - C.y) + (Z - C.z) * (Z - C.z) > 1 && BlockPos.TryAt(X, Y, Z, out var at))
            {
                if (LIQ[sel]) { PlaceLiquid(X, Y, Z, 8, sel); return; }
                // upper half like Minecraft: clicked the underside of a block, or the upper half of a side face
                bool upper; var hitPos = BlockPos.Of(T);
                if (at.TryOffset(0, 1, 0, out var above) && above.Equals(hitPos)) upper = true;
                else if (at.TryOffset(0, -1, 0, out var below) && below.Equals(hitPos)) upper = false;
                else { LocalAt(T.x, T.y, T.z, out _, out double hy, out _); upper = hy > .5; }
                if (!BlockActions.Place(at, sel, C.f, out string why, upper)) { UI.Note(why, 1.5); return; }
                if (P.on && Pcol(P.p, HW)) BlockActions.Break(at); // would trap the player: undo
            }
        }
    }
    public static void PickBlock()
    {
        var T = Raycast(P.on ? 6 : 10); if (T == null) return;
        int id = T.core ? T.id : Blk(T.bi, T.f, T.bx, T.by, T.bz);
        if (id == 0 || LIQ[id]) return;
        PlayerInventory.PickBlock(id);
    }
}
