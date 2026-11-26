namespace CubePlanet;

public static partial class DemoWorld
{
    // ---------- --demosprinkler NAME [--demosecs S]: how the sprinkler works, three side by side ----------
    // Each station: a round field of dry farmland with wheat (radius 4) and a sprinkler in the middle, powered by a coal generator.
    // A (x 0..12, stone bricks, WORKS): a water tower (glass basin 6 blocks up, a spring keeps it full) -> a liquid pipe down the tower, under
    //   the floor and up into the sprinkler from below. The water stands higher than the nozzle, so it has pressure: when the soil dries it sprays.
    // B (x 20..32, cobblestone, NO PRESSURE): the same, but the basin is sunk into the floor, lower than the nozzle: the pipe has water but no
    //   pressure, nothing sprays.
    // C (x 40..48, sandstone, NO PIPE): a sprinkler standing on a water block: it only takes water from a pipe, nothing sprays.
    // The basins stand more than 4 blocks from the fields, so the farmland cannot drink from them by itself.
    public static int RunSprinkler(string name, double secs)
    {
        string folder = WorldSave.FolderFor(name);
        if (Directory.Exists(folder)) { Console.WriteLine($"DEMOWORLD a world folder '{folder}' already exists: not touching it"); return 1; }
        World.InitWorldData(1200, 1337); ChunkGenerator.mainTid = Environment.CurrentManagedThreadId;
        if (!FindSite()) { Console.WriteLine("DEMOWORLD no dry building site near spawn"); return 1; }
        int bricks = Id("stone_bricks"), cobble = Id("cobblestone"), sand = Id("sandstone"), glass = Id("glass"), stone = Id("stone");
        for (int y = -4; y <= 14; y++) for (int z = -10; z <= 12; z++) for (int x = -6; x <= 50; x++) Cell(x, y, z, y <= -1 ? stone : 0);
        for (int z = -10; z <= 12; z++) for (int x = -6; x <= 50; x++) Cell(x, -1, z, x < 16 ? bricks : x < 36 ? cobble : sand);
        int coal = GameAssets.ItemReg.Id("coal");

        // a round field of farmland with wheat around (cx, cz), the sprinkler's spot left free
        void Field(int cx, int cz)
        {
            for (int dz = -4; dz <= 4; dz++) for (int dx = -4; dx <= 4; dx++)
                {
                    if (dx * dx + dz * dz > 20 || (dx == 0 && dz == 0)) continue;
                    Cell(cx + dx, -1, cz + dz, Id("dirt")); Cell(cx + dx, 0, cz + dz, Id("dirt")); Farming.Till(At(cx + dx, 0, cz + dz));
                    Farming.SetMoisture(At(cx + dx, 0, cz + dz), 2); Put(cx + dx, 1, cz + dz, "wheat");
                }
        }
        // power: a coal generator south of the field, a cable to the sprinkler
        void Power(int cx, int cz)
        {
            Put(cx, 0, cz - 7, "coal_generator", "north"); E(cx, 0, cz - 7).Inv("fuel")[0] = new ItemStack(coal, 64);
            for (int z = cz - 6; z <= cz - 1; z++) Put(cx, z < cz - 4 ? 0 : 1, z, "gold_cable");
            Put(cx, 0, cz - 5, "gold_cable"); Put(cx, 1, cz - 5, "gold_cable");
        }
        // basin: water cells (x, y0b..y1b, z) 3 x 3 with glass walls and a stone floor under it
        void Basin(int bx, int by0, int by1, int bz)
        {
            for (int y = by0 - 1; y <= by1; y++) for (int z = bz - 2; z <= bz + 2; z++) for (int x = bx - 2; x <= bx + 2; x++)
                    {
                        bool inside = Math.Abs(x - bx) <= 1 && Math.Abs(z - bz) <= 1 && y >= by0;
                        Cell(x, y, z, inside ? Water : y == by0 - 1 ? stone : glass, inside ? 8 : 0);
                    }
        }

        // ---- A: water tower 6 up, pipe down and under the floor into the sprinkler from below
        int ax = 6, az = 2; Field(ax, az); Power(ax, az);
        int tx = ax, tz = az + 9;                                                    // the tower, north of the field
        for (int y = 0; y <= 4; y++) for (int z = tz - 2; z <= tz + 2; z++) for (int x = tx - 2; x <= tx + 2; x++) if (Math.Abs(x - tx) == 2 && Math.Abs(z - tz) == 2) Cell(x, y, z, bricks); // legs
        Basin(tx, 6, 7, tz); Put(tx + 2, 7, tz, "spring", "west"); E(tx + 2, 7, tz).SetNum("rate", 4); // the spring keeps the basin full
        for (int y = -1; y <= 5; y++) Put(tx, y, tz, "liquid_pipe");                // down the tower (its top end opens into the basin)
        for (int z = az + 1; z < tz; z++) Put(ax, -1, z, "liquid_pipe");            // under the floor to the field
        Put(ax, -1, az, "liquid_pipe"); Put(ax, 0, az, "liquid_pipe"); Put(ax, 1, az, "sprinkler", "up");

        // ---- B: the basin sunk into the floor (lower than the nozzle): water in the pipe, no pressure
        int bxc = 26, bzc = 2; Field(bxc, bzc); Power(bxc, bzc);
        int sbx = bxc, sbz = bzc + 11;                                               // far enough that the farmland cannot drink from it
        for (int y = -3; y <= -1; y++) for (int z = sbz - 2; z <= sbz + 2; z++) for (int x = sbx - 2; x <= sbx + 2; x++)
                    { bool inside = Math.Abs(x - sbx) <= 1 && Math.Abs(z - sbz) <= 1 && y >= -2; Cell(x, y, z, inside ? Water : y == -3 ? stone : glass, inside ? 8 : 0); }
        Put(sbx + 2, -1, sbz, "spring", "west"); E(sbx + 2, -1, sbz).SetNum("rate", 2);
        for (int z = bzc + 1; z <= sbz - 2; z++) Put(bxc, -2, z, "liquid_pipe");    // under the floor; its end (in the basin's wall) opens into the water
        Put(bxc, -2, bzc, "liquid_pipe"); Put(bxc, -1, bzc, "liquid_pipe"); Put(bxc, 0, bzc, "liquid_pipe"); Put(bxc, 1, bzc, "sprinkler", "up");

        // ---- C: a sprinkler on a water block, no pipe
        int ccx = 44, ccz = 2; Field(ccx, ccz); Power(ccx, ccz);
        Cell(ccx, -1, ccz, stone); Cell(ccx, 0, ccz, Water, 8); Put(ccx, 1, ccz, "sprinkler", "up");

        // hotbar
        string[] give = { "sprinkler", "liquid_pipe", "liquid_tank_wall", "liquid_tank_valve", "spring", "coal_generator", "gold_cable", "wheat_seeds", "hoe" };
        for (int i = 0; i < 9; i++) PlayerInventory.Hotbar[i] = null;
        int slot = 0; foreach (var g in give) if (GameAssets.ItemReg.Has(g) && slot < 9) PlayerInventory.Hotbar[slot++] = new ItemStack(GameAssets.ItemReg.Id(g), GameAssets.ItemReg.All.First(it => it?.IdName == g).StackSize > 1 ? 64 : 1);
        PlayerInventory.Selected = 0; WaterSim.WAct.Clear(); WaterSim.wactQ.Clear();

        int Moist(int cx, int cz) { int n = 0; for (int dz = -4; dz <= 4; dz++) for (int dx = -4; dx <= 4; dx++) if (dx * dx + dz * dz <= 20 && (dx | dz) != 0 && Farming.IsFarmland(At(cx + dx, 0, cz + dz).Id)) n += Farming.MoistureAt(At(cx + dx, 0, cz + dz)); return n; }
        int[] before = { Moist(ax, az), Moist(bxc, bzc), Moist(ccx, ccz) };
        int ticks = (int)(secs * 10);
        for (int i = 0; i < ticks; i++) { BlockActions.Tick(.1); if ((i & 1) == 1) WaterSim.WaterStep(); }
        string S(int x, int z) => BEH[Id("sprinkler")].Describe(E(x, 1, z));
        string report = $"SPRINKLERDEMO after {secs:0} s: A moisture {before[0]} -> {Moist(ax, az)} ({S(ax, az)}); B {before[1]} -> {Moist(bxc, bzc)} ({S(bxc, bzc)}); C {before[2]} -> {Moist(ccx, ccz)} ({S(ccx, ccz)})";
        return Finish(folder, name, report, 26, -9, 26, 1, 3, false);
    }
}
