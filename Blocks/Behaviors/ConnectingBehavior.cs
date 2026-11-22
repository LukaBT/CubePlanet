namespace CubePlanet;

/// <summary>
/// Fences (and anything else that joins its neighbours): boolean state fields "north", "east", "south", "west" are true when that neighbour
/// is a block with one of the definition's "connects_to" group tags, or a full solid cube. The arms are state_overrides of the definition.
/// </summary>
public sealed class ConnectingBehavior : BlockBehavior
{
    static readonly string[] Dirs = { "north", "east", "south", "west" };
    static readonly (int x, int z)[] Off = { (0, -1), (1, 0), (0, 1), (-1, 0) };

    public override void OnPlace(BlockPos p, int id) => Update(p, id);
    public override void OnNeighborChanged(BlockPos p, int id, BlockPos from) => Update(p, id);

    public static bool Connects(BlockDefinition me, int nb)
    {
        if (nb == 0) return false;
        if (OPQ[nb] != 0 && MODEL[nb] == ModelCube) return true;
        var d = DEFS[nb]; if (d == null) return false;
        foreach (var t in me.ConnectsTo) if (d.HasTag(t)) return true;
        return false;
    }

    static void Update(BlockPos p, int id)
    {
        var d = DEFS[id]; uint st = (uint)p.State, want = st;
        for (int i = 0; i < 4; i++)
        {
            var f = d.State.Find(Dirs[i]); if (f == null) continue;
            bool c = p.TryOffset(Off[i].x, 0, Off[i].z, out var q) && Connects(d, q.Id);
            want = f.With(want, c ? 1 : 0);
        }
        if (want != st) BlockStates.Set(p, (int)want);
    }
}
