namespace CubePlanet;

/// <summary>Container (chest): its inventory fields live in a block entity; right click opens the block's ui_id (handled by BlockActions).</summary>
public sealed class ContainerBehavior : BlockBehavior
{
    public override void OnPlace(BlockPos p, int id) => BlockStates.EntityOrNew(p, id);
}
