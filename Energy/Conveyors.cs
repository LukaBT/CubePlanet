namespace CubePlanet;

/// <summary>
/// Conveyor belts: items really ride on them. Each belt holds up to 4 items (a quarter block apart) moving at <see cref="ConveyorBehavior.Speed"/>
/// blocks a second toward its facing side; at the end they move onto the next belt, or into a chest / machine in front (a furnace input if it
/// can smelt them, fuel slots for fuel, chest slots), else they wait there. Belts shape themselves from their neighbours like stairs:
/// <list type="bullet">
/// <item>turns: a belt with no belt feeding it from behind but one feeding it from the left / right becomes a curve;</item>
/// <item>slopes: a belt with a belt (same facing) one block up in front climbs to it ("up"); a belt below and in front of a flat belt's end
/// takes the items down ("down", entered from the cell above behind it).</item>
/// </list>
/// Put items on: right click a belt with an item (one at a time) or an extractor in front of a chest. Right click with an empty hand: take its
/// items. Standing on a flat belt carries you too. The belt surface is drawn moving (MovingParts.AddBelts).
/// </summary>
public sealed class ConveyorBehavior : BlockBehavior
{
    public const double Speed = 1.5, Gap = .25, Top = .1875; public const int Slots = 4;
    public override void OnPlace(BlockPos p, int id) { BlockStates.EntityOrNew(p, id); Conveyor.Reshape(p); Conveyor.ReshapeAround(p); }
    public override void OnNeighborChanged(BlockPos p, int id, BlockPos from) => Conveyor.Reshape(p);
    public override void OnBreak(BlockPos p, int id, int state) { foreach (var q in Conveyor.Around(p)) Conveyor.Later.Add(q); }
    public override bool Usable => true;
    public override bool OnUse(BlockPos p, int id, int state)
    {
        var e = BlockStates.EntityOrNew(p, id); if (e == null) return false;
        var held = PlayerInventory.Hotbar[PlayerInventory.Selected];
        if (held == null || held.IsEmpty)
        {   // empty hand: pick up what rides on it
            int got = 0; var inv = e.Inv("belt");
            for (int i = 0; i < inv.Length; i++) if (inv[i] is { IsEmpty: false } s) { PlayerInventory.Add(new ItemStack(s.Item, s.Count, s.State)); inv[i] = null; got++; }
            UI.Note(got > 0 ? $"Took {got} item(s) off the belt" : "Conveyor: right click with an item to put it on", 1.5); return true;
        }
        if (!Conveyor.Put(e, new ItemStack(held.Item, 1, held.State), 0)) { UI.Note("Conveyor: no room at its start", 1.2); return true; }
        if (--held.Count <= 0) PlayerInventory.Hotbar[PlayerInventory.Selected] = null;
        return true;
    }
    public override void OnTick(BlockEntity e, double dt) => Conveyor.Tick(e, dt);
    public override string Describe(BlockEntity e)
    {
        var inv = e.Inv("belt"); int n = inv?.Count(s => s != null && !s.IsEmpty) ?? 0;
        return $"CONVEYOR {Conveyor.Shape(e.Pos).ToUpperInvariant()}  {n} / {Slots} ITEMS  {Speed:0.0} BLOCKS/S" + (e.Num("stuck") > 0 ? "  (END BLOCKED: NOTHING IN FRONT TAKES IT)" : "");
    }
}

public static class Conveyor
{
    /// <summary>Belts to re-shape on the next tick (a belt was broken next to them).</summary>
    public static readonly HashSet<BlockPos> Later = new();
    public static bool IsBelt(int id) => id > 0 && BEH[id] is ConveyorBehavior;
    static StateSchema.Field F(int id, string n) => DEFS[id]?.State.Find(n);
    static string Val(BlockPos p, string n) { var f = F(p.Id, n); return f == null ? "" : f.Values[f.Index((uint)p.State)]; }
    public static string Facing(BlockPos p) => Val(p, "facing");
    public static string Slope(BlockPos p) => Val(p, "slope");
    public static string Shape(BlockPos p) { string s = Slope(p); return s != "flat" && s != "" ? (s == "up" ? "slope up" : "slope down") : Val(p, "shape"); }
    public static (int x, int y, int z) Dir(string facing) => facing switch { "east" => (1, 0, 0), "south" => (0, 0, 1), "west" => (-1, 0, 0), _ => (0, 0, -1) };
    static (int x, int y, int z) Left((int x, int y, int z) d) => (d.z, 0, -d.x);
    static bool Off(BlockPos p, (int x, int y, int z) d, int up, out BlockPos q) => p.TryOffset(d.x, d.y + up, d.z, out q) && q.Loaded;
    static bool BeltAt(BlockPos p, (int x, int y, int z) d, int up, out BlockPos q) => Off(p, d, up, out q) && IsBelt(q.Id);
    public static IEnumerable<BlockPos> Around(BlockPos p)
    {
        for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                    if ((dx | dy | dz) != 0 && p.TryOffset(dx, dy, dz, out var q) && q.Loaded && IsBelt(q.Id)) yield return q;
    }
    public static void ReshapeAround(BlockPos p) { foreach (var q in Around(p)) Reshape(q); }
    /// <summary>The cell a belt hands its items to (its front; one up for a climbing belt).</summary>
    public static bool Exit(BlockPos p, out BlockPos q) => Off(p, Dir(Facing(p)), Slope(p) == "up" ? 1 : 0, out q);
    /// <summary>Does belt q hand its items into cell target?</summary>
    static bool Feeds(BlockPos q, BlockPos target) => IsBelt(q.Id) && Exit(q, out var x) && x.Equals(target);
    /// <summary>Sets a belt's slope and shape from its neighbours (like stairs).</summary>
    public static void Reshape(BlockPos p)
    {
        int id = p.Id; if (!IsBelt(id)) return; var d = Dir(Facing(p)); var back = (-d.x, 0, -d.z); var l = Left(d); var r = (-l.x, 0, -l.z);
        string slope = "flat", shape = "straight";
        if (BeltAt(p, d, 1, out var up) && Facing(up) == Facing(p) && !BeltAt(p, d, 0, out _)) slope = "up";
        else if (BeltAt(p, back, 1, out var hi) && Facing(hi) == Facing(p) && Slope(hi) != "up" && !BeltAt(p, back, 0, out _)) slope = "down";
        if (slope == "flat" && !(Off(p, back, 0, out var b) && Feeds(b, p)))
        {
            bool fl = Off(p, l, 0, out var lq) && Feeds(lq, p), fr = Off(p, r, 0, out var rq) && Feeds(rq, p);
            if (fl && !fr) shape = "left"; else if (fr && !fl) shape = "right";
        }
        uint w = (uint)p.State; var fs = F(id, "slope"); var fh = F(id, "shape");
        if (fs != null) w = fs.With(w, Array.IndexOf(fs.Values, slope)); if (fh != null) w = fh.With(w, Array.IndexOf(fh.Values, shape));
        if ((int)w != p.State) BlockStates.Set(p, (int)w, true);
    }

    // ---- items: inventory "belt" (4 slots), progress 0..1 along the belt in numbers "p0".."p3", "m0".."m3" = moved last tick (drawing)
    static IEnumerable<int> ByProgress(BlockEntity e) { var inv = e.Inv("belt"); return Enumerable.Range(0, inv.Length).Where(i => inv[i] is { IsEmpty: false }).OrderByDescending(i => e.Num("p" + i)); }
    /// <summary>The progress of the rearmost item (1 + gap when empty).</summary>
    static double Rear(BlockEntity e) { double r = 1 + ConveyorBehavior.Gap; foreach (var i in ByProgress(e)) r = Math.Min(r, e.Num("p" + i)); return r; }
    /// <summary>Puts one item on a belt at progress t (if there is room).</summary>
    public static bool Put(BlockEntity e, ItemStack s, double t)
    {
        var inv = e.Inv("belt"); if (inv == null || s == null || s.IsEmpty) return false;
        foreach (var i in ByProgress(e)) if (Math.Abs(e.Num("p" + i) - t) < ConveyorBehavior.Gap - 1e-9) return false;
        int k = Array.FindIndex(inv, x => x == null || x.IsEmpty); if (k < 0) return false;
        inv[k] = new ItemStack(s.Item, 1, s.State); e.SetNum("p" + k, t); e.SetNum("m" + k, 0); return true;
    }
    public static bool CanTake(BlockEntity e) => e?.Inv("belt") is { } inv && inv.Any(x => x == null || x.IsEmpty) && Rear(e) >= ConveyorBehavior.Gap - 1e-9;
    /// <summary>Hands an item at a belt's end on: the next belt (the cell in front, or a down slope just below it), or an inventory there.</summary>
    static bool HandOn(BlockEntity e, ItemStack s)
    {
        if (!Exit(e.Pos, out var q)) return false;
        BlockPos target = q;
        if (!IsBelt(q.Id) && !BlockStates.entities.ContainsKey(q.Key) && q.TryOffset(0, -1, 0, out var below) && below.Loaded && IsBelt(below.Id) && Slope(below) == "down" && Facing(below) == Facing(e.Pos)) target = below;
        if (!BlockStates.entities.TryGetValue(target.Key, out var t)) return false;
        if (IsBelt(target.Id)) return CanTake(t) && Put(t, s, 0);
        if (!ItemsUtil.Accepts(t, s)) return false;
        return ItemsUtil.Insert(ItemsUtil.InputFor(t, s), new ItemStack(s.Item, 1, s.State)) == 1;
    }
    public static void Tick(BlockEntity e, double dt)
    {
        if (!e.Pos.Loaded) return;
        if (Later.Count > 0) { foreach (var q in Later.ToArray()) Reshape(q); Later.Clear(); }
        if (((long)(Kinetics.Time * 10) + e.Pos.X) % 10 == 0) Reshape(e.Pos); // now and then: diagonal neighbours (slopes) are not notified
        var inv = e.Inv("belt"); if (inv == null) return;
        double limit = double.MaxValue, step = ConveyorBehavior.Speed * dt; bool stuck = false;
        foreach (var i in ByProgress(e).ToList())
        {
            double p = e.Num("p" + i), np = Math.Min(p + step, limit);
            if (np >= 1)
            {
                if (HandOn(e, inv[i])) { inv[i] = null; e.SetNum("p" + i, 0); e.SetNum("m" + i, 0); continue; }
                np = 1; stuck = true;
            }
            e.SetNum("m" + i, np > p + 1e-9 ? 1 : 0); e.SetNum("p" + i, np); limit = np - ConveyorBehavior.Gap;
        }
        e.SetNum("stuck", stuck ? 1 : 0);
    }

    // ---- paths (block-local, for a belt facing north; turned for the others)
    /// <summary>Where an item at progress t is, block-local (x, y = the belt surface, z).</summary>
    public static V3 Path(string shape, string slope, string facing, double t)
    {
        double x, y = ConveyorBehavior.Top, z;
        if (slope == "up") { x = .5; z = 1 - t; y += t; }
        else if (slope == "down") { x = .5; z = 1 - t; y += 1 - t; }
        else if (shape == "left") { double a = t * Math.PI / 2; x = .5 * Math.Sin(a); z = .5 * Math.Cos(a); }
        else if (shape == "right") { double a = t * Math.PI / 2; x = 1 - .5 * Math.Sin(a); z = .5 * Math.Cos(a); }
        else { x = .5; z = 1 - t; }
        return Turn(facing, x, y, z);
    }
    public static V3 Turn(string facing, double x, double y, double z)
    {
        int n = facing switch { "east" => 1, "south" => 2, "west" => 3, _ => 0 };
        for (int i = 0; i < n; i++) (x, z) = (1 - z, x);
        return new V3(x, y, z);
    }

    /// <summary>Standing on a flat belt carries the player along it.</summary>
    public static void CarryPlayer(double dt)
    {
        var P = PlayerPhysics.P; if (!P.on || P.fly || P.core || !P.gnd) return;
        V3 up = M.Nrm(P.p); if (!BlockPos.TryAt(P.p.X - up.X * .05, P.p.Y - up.Y * .05, P.p.Z - up.Z * .05, out var under) || !under.Loaded) return;
        if (!IsBelt(under.Id))
        {   // feet just above a belt's surface sit in the belt's own cell
            if (!BlockPos.TryAt(P.p.X + up.X * .1, P.p.Y + up.Y * .1, P.p.Z + up.Z * .1, out under) || !IsBelt(under.Id)) return;
        }
        if (Slope(under) != "flat") return;
        var d = Dir(Facing(under)); var (ax, _, az) = under.Axes(); V3 dir = ax * d.x + az * d.z;
        Collision.Mv(M.Nrm(dir), ConveyorBehavior.Speed * dt, false);
    }
}
