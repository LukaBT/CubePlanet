namespace CubePlanet;

public static partial class SelfTest
{
    // rotation power: wind turbine -> shafts -> generator -> cable -> battery; blocked blades; load slows the rotor; coal generator
    static void Kinetic()
    {
        int Id(string n) => GameAssets.BlockReg.Id(n);
        var (_, _, g) = SpotInFront(16); int bi = BlockAccess.P2b(g.X, g.Y, g.Z); int f = PF, x0 = PX, y0 = PY + 24, z0 = PZ;
        var made = new List<Chunk>();
        for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                {
                    long k = ck(bi, f, (x0 >> 5) + dx, (y0 >> 5) + dy, (z0 >> 5) + dz);
                    if (!chunks.TryGetValue(k, out var c)) { c = new Chunk { key = k, band = bi, face = f, cx = (x0 >> 5) + dx, cy = (y0 >> 5) + dy, cz = (z0 >> 5) + dz }; AddChunk(c); made.Add(c); }
                    ChunkGenerator.Ensure(c); c.built = true;
                }
        BlockPos At(int x, int y, int z) => new(false, bi, f, x0 + x, y0 + y, z0 + z);
        void Put(int x, int y, int z, int id, string facing = null)
        {
            var p = At(x, y, z); uint w = 0; var ff = DEFS[id]?.State.Find("facing");
            if (facing != null && ff != null) w = ff.With(0, Array.IndexOf(ff.Values, facing));
            p.Set(id, (int)w); BEH[id]?.OnPlace(p, id); BlockActions.NotifyNeighbors(p);
        }
        void Clear() { for (int y = 4; y <= 12; y++) for (int z = -3; z <= 8; z++) for (int x = -4; x <= 10; x++) { var p = At(x, y, z); BlockStates.entities.Remove(p.Key); SetCell(bi, f, x0 + x, y0 + y, z0 + z, 0); } }
        Clear();
        int wt = Id("wind_turbine"), shaft = Id("shaft"), gen = Id("generator"), cable = Id("copper_cable"), bat = Id("battery"), coal = Id("coal_generator");
        // moving parts turn with the facing: a shaft standing up turns around y; a turbine facing east has its blades east of the hub
        var sv = Variant(shaft, (int)DEFS[shaft].State.Find("facing").With(0, Array.IndexOf(DEFS[shaft].State.Find("facing").Values, "up"))).Moving[0];
        var tv = Variant(wt, (int)DEFS[wt].State.Find("facing").With(0, Array.IndexOf(DEFS[wt].State.Find("facing").Values, "east"))).Moving[0];
        Check(Math.Abs(Math.Abs(sv.Axis.Y) - 1) < 1e-9 && tv.Pivot.X > 1 && Math.Abs(Math.Abs(tv.Axis.X) - 1) < 1e-9 && DEFS[wt].MovingParts[0].Boxes.Length == 5,
              $"moving parts parse and turn with the facing (shaft up: axis {sv.Axis.X:0},{sv.Axis.Y:0},{sv.Axis.Z:0}; turbine east: pivot x {tv.Pivot.X:0.0})");
        // turbine at z 0 facing north (blades in the z -1 plane), shafts z 1..2, generator z 3 with its back to the shaft, cable, battery
        Put(0, 8, 0, wt, "north"); Put(0, 8, 1, shaft, "north"); Put(0, 8, 2, shaft, "north"); Put(0, 8, 3, gen, "south"); Put(0, 8, 4, cable); Put(0, 8, 5, bat);
        var te = BlockStates.entities[At(0, 8, 0).Key]; var se = BlockStates.entities[At(0, 8, 2).Key]; var ge = BlockStates.entities[At(0, 8, 3).Key]; var be = BlockStates.entities[At(0, 8, 5).Key];
        double inSum = 0; for (int i = 0; i < 100; i++) { BlockActions.Tick(.1); inSum += ge.Num("input") * .1; }
        double expect = inSum * GeneratorBehavior.Efficiency * BatteryBehavior.Efficiency;
        Check(te.Num("power") > 0 && Math.Abs(ge.Num("input") - te.Num("power")) < 1e-6 && se.Num("speed") > 0 && se.Num("angle") == te.Num("angle"),
              $"wind turbine {te.Num("power"):0.0} E/s turns the shafts ({se.Num("speed") * 60 / (2 * Math.PI):0} rpm, one shared angle) into the generator");
        Check(be.Num("energy") > 20 && Math.Abs(be.Num("energy") - expect) < expect * .05 + 1, $"generator charges the battery: {be.Num("energy"):0.0} E in 10 s (expected {expect:0.0})");
        double loaded = te.Num("speed");
        // without the generator the rotor turns faster (load slows it)
        BlockStates.entities.Remove(At(0, 8, 3).Key); SetCell(bi, f, x0, y0 + 8, z0 + 3, 0);
        for (int i = 0; i < 60; i++) BlockActions.Tick(.1);
        Check(te.Num("speed") > loaded * 1.3, $"load slows the rotor: {loaded * 60 / (2 * Math.PI):0} rpm with the generator, {te.Num("speed") * 60 / (2 * Math.PI):0} rpm free");
        // a block in the blades' 5 x 5 area stops it
        SetCell(bi, f, x0 + 2, y0 + 10, z0 - 1, Stone);
        for (int i = 0; i < 60; i++) BlockActions.Tick(.1);
        Check(te.Num("blocked") == 1 && te.Num("power") == 0 && te.Num("speed") < loaded * .2, $"a block in front of the blades stops the turbine ({te.Num("speed") * 60 / (2 * Math.PI):0.0} rpm)");
        SetCell(bi, f, x0 + 2, y0 + 10, z0 - 1, 0);
        for (int i = 0; i < 2; i++) BlockActions.Tick(.1);
        Check(te.Num("blocked") == 0 && te.Num("power") > 0, "clearing the blades starts it again");

        // coal generator -> cable -> battery: it burns only while the battery has room or something needs energy; one coal = 800 E
        Put(6, 8, 0, coal, "north"); Put(7, 8, 0, cable); Put(8, 8, 0, bat);
        var ce = BlockStates.entities[At(6, 8, 0).Key]; var cb = BlockStates.entities[At(8, 8, 0).Key];
        int coalItem = GameAssets.ItemReg.Id("coal");
        Check(ce.Inv("fuel") is { Length: 1 } && DEFS[coal].UiId.EndsWith("coal_generator"), "coal generator has a fuel slot and a window");
        cb.SetNum("energy", BatteryBehavior.Capacity); ce.Inv("fuel")[0] = new ItemStack(coalItem, 2);
        for (int i = 0; i < 30; i++) BlockActions.Tick(.1);
        Check(ce.Num("burn") == 0 && ce.Inv("fuel")[0]?.Count == 2, "coal generator burns nothing while the battery is full and nothing needs energy");
        cb.SetNum("energy", 0); ce.Inv("fuel")[0] = new ItemStack(coalItem, 1);
        bool lit = false; int n = 0;
        for (; n < 600 && (n < 5 || ce.Num("burn") > 0); n++) { BlockActions.Tick(.1); lit |= DEFS[coal].State.Find("lit").Index((uint)At(6, 8, 0).State) == 1; }
        double coalE = cb.Num("energy") / BatteryBehavior.Efficiency;
        Check(Math.Abs(coalE - CoalGeneratorBehavior.Rate * 40) < 20 && ce.Inv("fuel")[0] == null && lit, $"one coal makes {coalE:0} E (expected {CoalGeneratorBehavior.Rate * 40:0}) in {n / 10.0:0} s, front lit while burning");
        Console.WriteLine($"WIND turbine {te.Num("power"):0.0} E/s (wind {Kinetics.Wind(Kinetics.Clock) * 100:0}%), {loaded * 60 / (2 * Math.PI):0} rpm loaded; battery {be.Num("energy"):0.0} E in 10 s; coal: {coalE:0} E per piece");
        Clear();
        foreach (var c in made) FreeChunk(c);
    }
}
