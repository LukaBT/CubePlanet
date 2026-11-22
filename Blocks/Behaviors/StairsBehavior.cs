namespace CubePlanet;

/// <summary>
/// Stairs that join like Minecraft's. State fields: "facing" (the high side), "half" (bottom / top = upside down, chosen from where you click)
/// and "shape" (straight, inner_left, inner_right, outer_left, outer_right). The shape follows the neighbouring stairs: a stair behind the high
/// side that faces sideways makes an outer corner, one in front makes an inner corner ("left" = west when the high side is north). Each
/// shape's boxes come from state_overrides of the definition, so they can be changed in the editor.
/// </summary>
public sealed class StairsBehavior : BlockBehavior
{
    static readonly string[] Dirs = { "north", "east", "south", "west" };
    static readonly (int x, int z)[] Off = { (0, -1), (1, 0), (0, 1), (-1, 0) };

    public override void OnPlace(BlockPos p, int id) => Update(p, id);
    public override void OnNeighborChanged(BlockPos p, int id, BlockPos from) => Update(p, id);

    static void Update(BlockPos p, int id)
    {
        var s = DEFS[id].State; var shape = s.Find("shape"); if (shape == null) return;
        int st = p.State, want = (int)shape.With((uint)st, Math.Max(0, Array.IndexOf(shape.Values, ShapeOf(p, id, st))));
        if (want != st) BlockStates.Set(p, want);
    }

    static bool Read(BlockPos p, out int facing, out int half)
    {
        facing = half = 0; int id = p.Id;
        if (BEH[id] is not StairsBehavior) return false;
        var s = DEFS[id].State; int st = p.State;
        var ff = s.Find("facing"); var hf = s.Find("half");
        facing = ff == null ? 0 : Array.IndexOf(Dirs, ff.Values[ff.Index((uint)st)]);
        half = hf == null ? 0 : hf.Index((uint)st);
        return facing >= 0;
    }
    static bool Neighbour(BlockPos p, int dir, out int facing, out int half)
    {
        facing = half = 0;
        return p.TryOffset(Off[dir].x, 0, Off[dir].z, out var q) && Read(q, out facing, out half);
    }

    /// <summary>Minecraft's stair shape rule.</summary>
    static string ShapeOf(BlockPos p, int id, int st)
    {
        if (!Read(p, out int dir, out int half)) return "straight";
        int ccw = (dir + 3) % 4, back = dir, front = (dir + 2) % 4;
        bool SameAxis(int a, int b) => a % 2 == b % 2;
        // the shape may only turn towards a side that is not a stair continuing straight on in the same direction
        bool CanTake(int side) => !Neighbour(p, side, out int f, out int h) || f != dir || h != half;
        if (Neighbour(p, back, out int f1, out int h1) && h1 == half && !SameAxis(f1, dir) && CanTake((f1 + 2) % 4))
            return f1 == ccw ? "outer_left" : "outer_right";
        if (Neighbour(p, front, out int f2, out int h2) && h2 == half && !SameAxis(f2, dir) && CanTake(f2))
            return f2 == ccw ? "inner_left" : "inner_right";
        return "straight";
    }
}
