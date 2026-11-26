namespace CubePlanet;

public static partial class SelfTest
{
    // liquids other than water: oil floats on water, lava meeting water freezes, liquid oxygen boils off in the open, a pump moves oil into a
    // tank (which then takes no water)
    static void LiquidTest()
    {
        int Id(string n) => GameAssets.BlockReg.Id(n); int Oil = Id("oil"), Lox = Id("liquid_oxygen");
        var (_, _, g) = SpotInFront(16); int bi = BlockAccess.P2b(g.X, g.Y, g.Z); int f = PF, x0 = PX + 40, y0 = PY + 24, z0 = PZ;
        var made = new List<Chunk>();
        for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                {
                    long k = ck(bi, f, (x0 >> 5) + dx, (y0 >> 5) + dy, (z0 >> 5) + dz);
                    if (!chunks.TryGetValue(k, out var c)) { c = new Chunk { key = k, band = bi, face = f, cx = (x0 >> 5) + dx, cy = (y0 >> 5) + dy, cz = (z0 >> 5) + dz }; AddChunk(c); made.Add(c); }
                    ChunkGenerator.Ensure(c); c.built = true;
                }
        BlockPos At(int x, int y, int z) => new(false, bi, f, x0 + x, y0 + y, z0 + z);
        void Cell(int x, int y, int z, int id, int lvl = 0) => SetCell(bi, f, x0 + x, y0 + y, z0 + z, id, lvl);
        void Put(int x, int y, int z, string block, string facing = null)
        {
            int id = Id(block); var p = At(x, y, z); uint w = 0; var ff = DEFS[id]?.State.Find("facing");
            if (facing != null && ff != null) w = ff.With(0, Array.IndexOf(ff.Values, facing));
            p.Set(id, (int)w); BEH[id]?.OnPlace(p, id); BlockActions.NotifyNeighbors(p);
        }
        void Clear() { Tanks.Reset(); for (int y = 2; y <= 14; y++) for (int z = -3; z <= 7; z++) for (int x = -3; x <= 9; x++) { var p = At(x, y, z); BlockStates.entities.Remove(p.Key); Cell(x, y, z, 0); } WaterSim.WAct.Clear(); WaterSim.wactQ.Clear(); }
        BlockEntity E(int x, int y, int z) => BlockStates.entities[At(x, y, z).Key];
        void Pit(int x, int z, int y0b, int y1b) { Cell(x, y0b - 1, z, Stone); for (int y = y0b; y <= y1b; y++) { Cell(x + 1, y, z, Stone); Cell(x - 1, y, z, Stone); Cell(x, y, z + 1, Stone); Cell(x, y, z - 1, Stone); } }
        void Wake(int x, int y, int z) => WaterSim.Wake(bi, f, x0 + x, y0 + y, z0 + z);
        void Steps(int n) { for (int i = 0; i < n; i++) WaterSim.WaterStep(); }

        // oil under water in a 1 x 1 shaft: the water sinks, the oil rises
        Clear(); Pit(0, 0, 4, 6); Cell(0, 4, 0, Oil, 8); Cell(0, 5, 0, Water, 8); Wake(0, 4, 0); Wake(0, 5, 0); Steps(6);
        Check(At(0, 4, 0).Id == Water && At(0, 5, 0).Id == Oil, $"oil floats on water (bottom {DEFS[At(0, 4, 0).Id].IdName}, top {DEFS[At(0, 5, 0).Id].IdName})");
        // lava next to water: the lava freezes to obsidian, a unit of the water boils off
        Clear(); Pit(3, 0, 4, 4); Pit(4, 0, 4, 4); Cell(3, 4, 0, Lava, 8); Cell(4, 4, 0, Water, 8); Wake(3, 4, 0); Steps(3);
        Check(At(3, 4, 0).Id == Obsidian && Wl(Wcell(bi, f, x0 + 4, y0 + 4, z0)) == 7, $"lava meeting water turns to obsidian ({DEFS[At(3, 4, 0).Id].IdName}), the water loses a unit to steam");
        // liquid oxygen in an open pit boils away
        Clear(); Pit(6, 0, 4, 4); Cell(6, 4, 0, Lox, 8); Wake(6, 4, 0); Steps(150);
        int lox = At(6, 4, 0).Id == Lox ? Wl(Wcell(bi, f, x0 + 6, y0 + 4, z0)) : 0;
        Check(lox < 8, $"liquid oxygen open to the air boils off ({8 - lox} of 8 units in 30 s)");
        // a pump lifts oil from a basin into a tank; the tank then holds oil and takes no water
        Clear();
        for (int y = 8; y <= 12; y++) for (int z = 0; z <= 4; z++) for (int x = 0; x <= 4; x++)
                { bool inside = x >= 1 && x <= 3 && y >= 9 && y <= 11 && z >= 1 && z <= 3; Cell(x, y, z, inside ? 0 : Id("liquid_tank_wall")); }
        Put(2, 8, 2, "liquid_tank_valve"); var t = Tanks.Form(At(2, 8, 2));
        for (int z = 0; z <= 4; z++) for (int x = 0; x <= 4; x++) { Cell(x, 4, z, Stone); Cell(x, 5, z, x == 0 || x == 4 || z == 0 || z == 4 ? Stone : Oil, 8); }
        Put(2, 6, 2, "liquid_pump", "up"); Put(2, 7, 2, "liquid_pipe"); E(2, 6, 2).SetNum("mode", 1);
        for (int x = -2; x <= 0; x++) { Put(x, 6, 2, x == 0 ? "copper_cable" : "coal_generator", "north"); if (x < 0) E(x, 6, 2).Inv("fuel")[0] = new ItemStack(GameAssets.ItemReg.Id("coal"), 16); }
        Put(1, 6, 2, "copper_cable");
        for (int i = 0; i < 60; i++) { BlockActions.Tick(.1); if ((i & 1) == 1) WaterSim.WaterStep(); }
        int water = t?.Put(5, Water) ?? -1;
        Check(t != null && t.Holds == Oil && t.Water > 10 && water == 0, $"a pump fills a tank with oil ({t?.Water:0} units of {(t?.Holds > 0 ? DEFS[t.Holds].IdName : "nothing")}); the oil tank takes no water ({water})");
        Clear();
        foreach (var c in made) FreeChunk(c);
    }
}
