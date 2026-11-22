namespace CubePlanet;

/// <summary>Bed: a 2-block multiblock (state "part": foot / head). Using it at night skips to the morning.</summary>
public sealed class BedBehavior : BlockBehavior
{
    public override bool Usable => true;
    public override bool OnUse(BlockPos p, int id, int state)
    {
        double h = LocalHour();
        if (h >= 19 || h < 6) { SetHour(7); UI.Note("You slept until the morning", 3); }
        else UI.Note("You can only sleep at night", 2);
        return true;
    }
    public override void OnNeighborChanged(BlockPos p, int id, BlockPos from) => BlockActions.RemoveIfBroken(p, id);
}
