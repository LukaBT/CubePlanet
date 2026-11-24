namespace CubePlanet;

/// <summary>
/// How a block looks and collides in one state: boxes, collision / selection boxes and face textures after the matching state_overrides
/// and the turn by its "facing" field. Built once per (block, state word) and cached (see Blocks.Variant).
/// </summary>
public sealed class BlockVariant
{
    public BlockDefinition.ModelBox[] Boxes;
    public BlockBox[] Collision, Selection; // null = full block
    public int[] Faces;

    public static BlockVariant Compute(BlockDefinition d, uint word)
    {
        var tex = d.Textures; var boxes = d.Boxes; var coll = d.Collision; var sel = d.Selection;
        foreach (var ov in d.Overrides)
        {
            if (!d.State.Matches(word, ov.When)) continue;
            if (ov.Boxes != null) boxes = ov.Boxes;
            if (ov.AddBoxes != null) boxes = boxes.Concat(ov.AddBoxes).ToArray();
            if (ov.HasCollision) coll = ov.Collision;
            if (ov.HasSelection) sel = ov.Selection;
            if (ov.Textures != null) tex = BlockDefinition.Merge(tex, ov.Textures);
        }
        if (d.CollideBoxes) coll = boxes.Select(m => m.Box).ToArray();
        if (d.SelectBoxes) sel = boxes.Select(m => m.Box).ToArray();
        var v = new BlockVariant { Collision = coll, Selection = sel, Faces = BlockDefinition.FacesOf(tex) };
        v.Boxes = boxes.Select(mb => new BlockDefinition.ModelBox { Box = mb.Box, Tex = mb.Tex, Faces = BlockDefinition.FacesOf(mb.Tex == null ? tex : BlockDefinition.Merge(tex, mb.Tex)) }).ToArray();
        var facing = d.Rotation != "none" ? d.State.Find("facing") : null;
        if (facing != null)
        {
            string f = facing.Values[facing.Index(word)];
            int turns = f switch { "east" => 1, "south" => 2, "west" => 3, _ => 0 }, ups = f switch { "up" => 1, "down" => 3, _ => 0 };
            for (int i = 0; i < ups; i++) v.Turn(BoxUp, UpFace);
            for (int i = 0; i < turns; i++) v.Turn(BoxCw, CwFace);
        }
        return v;
    }

    // a quarter turn clockwise seen from above (north -> east): x' = 1 - z, z' = x; faces: east <- north, south <- east, west <- south, north <- west
    static readonly int[] CwFace = { 5, 4, 2, 3, 0, 1 };
    static BlockBox BoxCw(BlockBox b) => new(1 - b.Z1, b.Y0, b.X0, 1 - b.Z0, b.Y1, b.X1);
    // north turned to face up: y' = 1 - z, z' = y; faces: top <- north, south <- top, bottom <- south, north <- bottom
    static readonly int[] UpFace = { 0, 1, 5, 4, 2, 3 };
    static BlockBox BoxUp(BlockBox b) => new(b.X0, 1 - b.Z1, b.Y0, b.X1, 1 - b.Z0, b.Y1);

    void Turn(Func<BlockBox, BlockBox> box, int[] from)
    {
        int[] Map(int[] f) { var n = new int[6]; for (int i = 0; i < 6; i++) n[i] = f[from[i]]; return n; }
        Faces = Map(Faces);
        foreach (var mb in Boxes) { mb.Box = box(mb.Box); mb.Faces = Map(mb.Faces); }
        if (Collision != null) Collision = Collision.Select(box).ToArray();
        if (Selection != null) Selection = Selection.Select(box).ToArray();
    }

    /// <summary>Turns an offset (multiblock part) the same way: clockwise steps for east / south / west, up / down tilts.</summary>
    public static (int x, int y, int z) TurnOffset(string facing, int x, int y, int z)
    {
        int ups = facing switch { "up" => 1, "down" => 3, _ => 0 }, turns = facing switch { "east" => 1, "south" => 2, "west" => 3, _ => 0 };
        for (int i = 0; i < ups; i++) (y, z) = (-z, y);   // around x: north (-z) -> up (+y)
        for (int i = 0; i < turns; i++) (x, z) = (-z, x); // around y: north (-z) -> east (+x)
        return (x, y, z);
    }
}
