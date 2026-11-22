namespace CubePlanet;

/// <summary>Door: a 2-block multiblock (state "half": lower / upper). Using either half opens / closes both ("open" state).</summary>
public sealed class DoorBehavior : BlockBehavior
{
    public override bool Usable => true;
    public override bool OnUse(BlockPos p, int id, int state)
    {
        var f = DEFS[id].State.Find("open"); if (f == null) return false;
        int open = f.Index((uint)state) == 0 ? 1 : 0;
        var parts = new List<(BlockPos pos, int part)>(); BlockActions.Parts(p, id, state, parts);
        foreach (var (q, _) in parts) if (q.Id == id) BlockStates.Set(q, (int)f.With((uint)q.State, open));
        return true;
    }
    public override void OnNeighborChanged(BlockPos p, int id, BlockPos from) => BlockActions.RemoveIfBroken(p, id);
}
