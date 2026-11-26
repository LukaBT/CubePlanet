namespace CubePlanet;

public static partial class SelfTest
{
    // rotation machines (millstone, sawmill through a gearbox, mechanical pump) and the multimeter's network report
    static void Mechanical()
    {
        int Id(string n) => GameAssets.BlockReg.Id(n); int Item(string n) => GameAssets.ItemReg.Id(n);
        var (_, _, g) = SpotInFront(16); int bi = BlockAccess.P2b(g.X, g.Y, g.Z); int f = PF, x0 = PX, y0 = PY + 24, z0 = PZ;
        var made = new List<Chunk>();
        for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                {
                    long k = ck(bi, f, (x0 >> 5) + dx, (y0 >> 5) + dy, (z0 >> 5) + dz);
                    if (!chunks.TryGetValue(k, out var c)) { c = new Chunk { key = k, band = bi, face = f, cx = (x0 >> 5) + dx, cy = (y0 >> 5) + dy, cz = (z0 >> 5) + dz }; AddChunk(c); made.Add(c); }
                    ChunkGenerator.Ensure(c); c.built = true;
                }
        BlockPos At(int x, int y, int z) => new(false, bi, f, x0 + x, y0 + y, z0 + z);
        void Put(int x, int y, int z, string block, string facing = null)
        {
            int id = Id(block); var p = At(x, y, z); uint w = 0; var ff = DEFS[id]?.State.Find("facing");
            if (facing != null && ff != null) w = ff.With(0, Array.IndexOf(ff.Values, facing));
            p.Set(id, (int)w); BEH[id]?.OnPlace(p, id); BlockActions.NotifyNeighbors(p);
        }
        void Clear() { for (int y = 3; y <= 14; y++) for (int z = -3; z <= 10; z++) for (int x = -4; x <= 12; x++) { var p = At(x, y, z); BlockStates.entities.Remove(p.Key); SetCell(bi, f, x0 + x, y0 + y, z0 + z, 0); } }
        BlockEntity E(int x, int y, int z) => BlockStates.entities[At(x, y, z).Key];
        void Ticks(int n) { for (int i = 0; i < n; i++) BlockActions.Tick(.1); }
        void Coal(int x, int y, int z) { Put(x, y, z, "coal_generator", "north"); E(x, y, z).Inv("fuel")[0] = new ItemStack(Item("coal"), 16); }
        Clear();

        // ----- 2 coal generators -> motor (27 E/s of rotation) -> shaft -> gearbox -> millstone (east) and sawmill (west), 15 E/s each wanted
        Coal(0, 8, -2); Coal(1, 8, -2); Put(0, 8, -1, "copper_cable"); Put(0, 8, 0, "electric_motor", "south"); Put(0, 8, 1, "shaft", "south"); Put(0, 8, 2, "gearbox");
        Put(1, 8, 2, "millstone"); Put(-1, 8, 2, "sawmill");
        var mill = E(1, 8, 2); var saw = E(-1, 8, 2);
        mill.Inv("input")[0] = new ItemStack(Item("iron_ore"), 2); saw.Inv("input")[0] = new ItemStack(Item("oak_log"), 2);
        Ticks(220);
        var dust = mill.Inv("output")[0]; var planks = saw.Inv("output")[0];
        Check(dust?.Item == Item("iron_dust") && dust.Count == 4 && planks?.Item == Item("oak_planks") && planks.Count == 12 && GameAssets.RecipeReg.SmeltingFor(Item("iron_dust"))?.Output == Item("iron_ingot"),
              $"a motor through a gearbox drives a millstone (2 iron ore -> {dust?.Count ?? 0} iron dust, which smelts to ingots) and a sawmill (2 oak logs -> {planks?.Count ?? 0} planks)");
        mill.Inv("input")[0] = new ItemStack(Item("iron_ore"), 64); saw.Inv("input")[0] = new ItemStack(Item("oak_log"), 64); Ticks(5);
        Check(Math.Abs(mill.Num("input_power") + saw.Num("input_power") - E(0, 8, 0).Num("drive")) < .3 && Math.Abs(mill.Num("input_power") - saw.Num("input_power")) < .3,
              $"two machines on one line share its power ({mill.Num("input_power"):0.0} + {saw.Num("input_power"):0.0} of {E(0, 8, 0).Num("drive"):0.0} E/s)");
        string line = Multimeter.Report(At(0, 8, 1));
        Check(line.Contains("ROTATION LINE") && line.Contains("WANT MORE"), $"multimeter on a shaft reports its line ({line.Replace("\n", " | ")})");
        Clear();

        // ----- mechanical pump: a pool, the pump above it facing up, driven from the side by a motor
        int Units(int ya, int yb) { int n = 0; for (int y = ya; y <= yb; y++) for (int z = 3; z <= 9; z++) for (int x = 3; x <= 9; x++) n += Wl(Wcell(bi, f, x0 + x, y0 + y, z0 + z)); return n; }
        for (int z = 4; z <= 8; z++) for (int x = 4; x <= 8; x++) { SetCell(bi, f, x0 + x, y0 + 4, z0 + z, Stone); if (x == 4 || x == 8 || z == 4 || z == 8) SetCell(bi, f, x0 + x, y0 + 5, z0 + z, Stone); else SetCell(bi, f, x0 + x, y0 + 5, z0 + z, Water, 8); }
        WaterSim.WAct.Clear(); WaterSim.wactQ.Clear();
        Put(6, 6, 6, "mechanical_pump", "up"); Coal(3, 6, 6); Put(4, 6, 6, "electric_motor", "east"); Put(5, 6, 6, "shaft", "east");
        int pool0 = Units(5, 5);
        Ticks(30);
        var mp = E(6, 6, 6);
        Check(Units(5, 5) < pool0 && Units(7, 12) > 0 && mp.Num("power") > 0, $"a mechanical pump driven by a motor lifts water ({pool0 - Units(5, 5)} units out of the pool, {Units(7, 12)} above; {mp.Num("power"):0.0} E/s of rotation)");
        Clear(); WaterSim.WAct.Clear(); WaterSim.wactQ.Clear(); WaterFlow.Clear();

        // ----- multimeter on a network: a copper cable bottleneck (4 coal generators = 80 E/s into copper's 64), then a clean one
        for (int x = 0; x <= 3; x++) Coal(x, 8, 0);
        Put(4, 8, 0, "copper_cable"); Put(5, 8, 0, "copper_cable"); Put(6, 8, 0, "battery"); Put(7, 8, 0, "battery");
        Ticks(10); string rep = Multimeter.Report(At(5, 8, 0));
        Check(rep.Contains("CABLE BLOCKS AT THEIR LIMIT") && rep.Contains("COPPER") && rep.Contains("6 MACHINES"), $"multimeter finds the copper bottleneck ({rep.Replace("\n", " | ")})");
        Put(4, 8, 0, "gold_cable"); Put(5, 8, 0, "gold_cable"); Ticks(10); string rep2 = Multimeter.Report(At(6, 8, 0));
        Check(rep2.Contains("NOTHING LIMITING") && Multimeter.Report(At(10, 12, 9)).Contains("NOT PART"), $"with gold cable nothing limits it ({rep2.Replace("\n", " | ")})");
        Console.WriteLine($"MECHANICAL millstone {dust?.Count ?? 0} dust, sawmill {planks?.Count ?? 0} planks; pump lifted {pool0 - Units(5, 5)}; multimeter:{rep.Replace("\n", " |")}");
        Clear();
        foreach (var c in made) FreeChunk(c);
    }
}
