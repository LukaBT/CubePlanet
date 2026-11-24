namespace CubePlanet;

/// <summary>
/// Fences, cables, pipes (anything that joins its neighbours): boolean state fields "north", "east", "south", "west" (and "up", "down" if the
/// block declares them) are true when that neighbour is a block with one of the definition's "connects_to" group tags, or a full solid cube
/// (unless connect_solid is false). The arms are state_overrides of the definition (add_boxes: one per side).
/// </summary>
public sealed class ConnectingBehavior : BlockBehavior
{
    static readonly string[] Dirs = { "north", "east", "south", "west", "up", "down" };
    static readonly (int x, int y, int z)[] Off = { (0, 0, -1), (1, 0, 0), (0, 0, 1), (-1, 0, 0), (0, 1, 0), (0, -1, 0) };

    public override void OnPlace(BlockPos p, int id) => Update(p, id);
    public override void OnNeighborChanged(BlockPos p, int id, BlockPos from) => Update(p, id);

    public static bool Connects(BlockDefinition me, int nb)
    {
        if (nb == 0) return false;
        if (me.ConnectSolid && OPQ[nb] != 0 && MODEL[nb] == ModelCube) return true;
        var d = DEFS[nb]; if (d == null) return false;
        foreach (var t in me.ConnectsTo) if (d.HasTag(t)) return true;
        return false;
    }

    static void Update(BlockPos p, int id)
    {
        var d = DEFS[id]; uint st = (uint)p.State, want = st;
        for (int i = 0; i < Dirs.Length; i++)
        {
            var f = d.State.Find(Dirs[i]); if (f == null) continue;
            bool c = p.TryOffset(Off[i].x, Off[i].y, Off[i].z, out var q) && Connects(d, q.Id);
            want = f.With(want, c ? 1 : 0);
        }
        if (want != st) BlockStates.Set(p, (int)want);
    }
}
