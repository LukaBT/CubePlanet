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
        void Clear() { for (int y = 1; y <= 15; y++) for (int z = -3; z <= 8; z++) for (int x = -7; x <= 10; x++) { var p = At(x, y, z); BlockStates.entities.Remove(p.Key); SetCell(bi, f, x0 + x, y0 + y, z0 + z, 0); } }
        Clear();
        int wt = Id("wind_turbine"), shaft = Id("shaft"), gen = Id("generator"), cable = Id("copper_cable"), bat = Id("battery"), coal = Id("coal_generator");
        // moving parts turn with the facing: a shaft standing up turns around y; a turbine facing east has its blades east of the hub
        var sv = Variant(shaft, (int)DEFS[shaft].State.Find("facing").With(0, Array.IndexOf(DEFS[shaft].State.Find("facing").Values, "up"))).Moving[0];
        var tv = Variant(wt, (int)DEFS[wt].State.Find("facing").With(0, Array.IndexOf(DEFS[wt].State.Find("facing").Values, "east"))).Moving[0];
        Check(Math.Abs(Math.Abs(sv.Axis.Y) - 1) < 1e-9 && tv.Pivot.X > 1 && Math.Abs(Math.Abs(tv.Axis.X) - 1) < 1e-9 && DEFS[wt].MovingParts[0].Boxes.Length == 5,
              $"moving parts parse and turn with the facing (shaft up: axis {sv.Axis.X:0},{sv.Axis.Y:0},{sv.Axis.Z:0}; turbine east: pivot x {tv.Pivot.X:0.0})");
        // turbine at z 0 facing north (blades in the z -1 plane), shafts z 1..2, generator z 3 with its back to the shaft, cable, battery
        // placed the way a player does (clicking the face they look at): only a shaft line out of the turbine's back drives a generator
        {
            Kinetics.FixedSpeed = 10; Kinetics.FixedHeading = Kinetics.HeadingFrom("north");
            var (ax, ay, az) = At(0, 8, 0).Axes();
            double Probe(int sx, int sy, int sz, V3 look)
            {
                Clear(); Put(0, 8, 0, wt, "north");
                BlockActions.Place(At(sx, sy, sz), shaft, look, out _); BlockActions.Place(At(2 * sx, 8 + 2 * (sy - 8), 2 * sz), gen, look, out _);
                for (int i = 0; i < 20; i++) BlockActions.Tick(.1);
                return BlockStates.entities.TryGetValue(At(2 * sx, 8 + 2 * (sy - 8), 2 * sz).Key, out var ge0) ? ge0.Num("input") : -1;
            }
            double back = Probe(0, 8, 1, -az), east = Probe(1, 8, 0, -ax), west = Probe(-1, 8, 0, ax), top = Probe(0, 9, 0, -ay);
            Check(back > 0 && east == 0 && west == 0 && top == 0, $"only the turbine's back drives a shaft (placed by look): back {back:0.0}, east {east:0.0}, west {west:0.0}, top {top:0.0} E/s");
            Clear();
        }
        // a fixed wind for the checks: 10 m/s from the north (into a turbine facing north)
        Kinetics.FixedSpeed = 10; Kinetics.FixedHeading = Kinetics.HeadingFrom("north");
        Put(0, 8, 0, wt, "north"); Put(0, 8, 1, shaft, "north"); Put(0, 8, 2, shaft, "north"); Put(0, 8, 3, gen, "south"); Put(0, 8, 4, cable); Put(0, 8, 5, bat);
        var te = BlockStates.entities[At(0, 8, 0).Key]; var se = BlockStates.entities[At(0, 8, 2).Key]; var ge = BlockStates.entities[At(0, 8, 3).Key]; var be = BlockStates.entities[At(0, 8, 5).Key];
        double inSum = 0; for (int i = 0; i < 100; i++) { BlockActions.Tick(.1); inSum += ge.Num("input") * .1; }
        double expect = inSum * GeneratorBehavior.Efficiency * BatteryBehavior.Efficiency, shear = WindTurbineBehavior.Shear(te), pSmall = te.Num("power");
        Check(Math.Abs(pSmall - 10 * Math.Min(1.5, Math.Pow(shear, 3))) < 1e-6 && Math.Abs(ge.Num("input") - pSmall) < 1e-6 && se.Num("speed") > 0 && se.Num("angle") == te.Num("angle"),
              $"wind turbine (wind 10 m/s x{shear:0.00} height, straight in) {pSmall:0.0} E/s turns the shafts ({Kinetics.Rpm(se):0} rpm, one shared angle) into the generator");
        Check(be.Num("energy") > 20 && Math.Abs(be.Num("energy") - expect) < expect * .05 + 1, $"generator charges the battery: {be.Num("energy"):0.0} E in 10 s (expected {expect:0.0})");
        double loaded = te.Num("speed");
        // wind direction: only the part blowing into the blades counts (45 degrees: cos^3 = 0.35 of the power; side or behind: nothing)
        double PowerFrom(double heading) { Kinetics.FixedHeading = heading; BlockActions.Tick(.1); return te.Num("power"); }
        double p45 = PowerFrom(Math.Atan2(1, -1)), pSide = PowerFrom(Kinetics.HeadingFrom("east")), pBack = PowerFrom(Kinetics.HeadingFrom("south"));
        string hud = BEH[wt].Describe(te);
        Check(Math.Abs(p45 / pSmall - Math.Pow(Math.Sqrt(.5), 3)) < .01 && pSide == 0 && pBack == 0 && hud.Contains("NOT INTO"),
              $"only wind into the blades counts: from NE {p45 / pSmall * 100:0}% of the power, from the side {pSide}, from behind {pBack} (HUD: {hud})");
        for (int i = 0; i < 80; i++) BlockActions.Tick(.1);
        Check(te.Num("speed") < loaded * .2, $"a turbine with the wind from the side runs down ({Kinetics.Rpm(te):0.0} rpm)");
        Kinetics.FixedHeading = Kinetics.HeadingFrom("north");
        // without the generator the rotor turns faster (load slows it)
        BlockStates.entities.Remove(At(0, 8, 3).Key); SetCell(bi, f, x0, y0 + 8, z0 + 3, 0);
        for (int i = 0; i < 100; i++) BlockActions.Tick(.1);
        Check(te.Num("speed") > loaded * 1.3, $"load slows the rotor: {loaded * 60 / (2 * Math.PI):0} rpm with the generator, {Kinetics.Rpm(te):0} rpm free");
        double freeSmall = te.Num("speed");
        // a block in the blades' 5 x 5 area stops it
        SetCell(bi, f, x0 + 2, y0 + 10, z0 - 1, Stone);
        for (int i = 0; i < 60; i++) BlockActions.Tick(.1);
        Check(te.Num("blocked") == 1 && te.Num("power") == 0 && te.Num("speed") < loaded * .2, $"a block in front of the blades stops the turbine ({Kinetics.Rpm(te):0.0} rpm)");
        SetCell(bi, f, x0 + 2, y0 + 10, z0 - 1, 0);
        for (int i = 0; i < 2; i++) BlockActions.Tick(.1);
        Check(te.Num("blocked") == 0 && te.Num("power") > 0, "clearing the blades starts it again");
        // large turbine (11 blocks): 4.8 x the power at the same wind, turns slower, needs an 11 x 11 area; two generators share it
        Clear(); int big = Id("wind_turbine_large");
        Put(0, 8, 0, big, "north"); Put(0, 8, 1, gen, "north"); Put(0, 8, 2, gen, "south"); Put(1, 8, 1, cable); Put(1, 8, 2, cable); Put(2, 8, 1, bat);
        var lt = BlockStates.entities[At(0, 8, 0).Key]; var g1 = BlockStates.entities[At(0, 8, 1).Key]; var g2 = BlockStates.entities[At(0, 8, 2).Key];
        for (int i = 0; i < 100; i++) BlockActions.Tick(.1);
        Check(Math.Abs(lt.Num("power") / pSmall - 4.8) < .01 && Math.Abs(g1.Num("input") - lt.Num("power") / 2) < 1e-6 && Math.Abs(g2.Num("input") - g1.Num("input")) < 1e-9 && g2.Num("power") > 0,
              $"large turbine {lt.Num("power"):0.0} E/s ({lt.Num("power") / pSmall:0.0} x the small one), shared by two generators on its shaft ({g1.Num("input"):0.0} + {g2.Num("input"):0.0})");
        BlockStates.entities.Remove(At(0, 8, 2).Key); BlockStates.entities.Remove(At(0, 8, 1).Key); SetCell(bi, f, x0, y0 + 8, z0 + 1, 0); SetCell(bi, f, x0, y0 + 8, z0 + 2, 0);
        for (int i = 0; i < 100; i++) BlockActions.Tick(.1);
        Check(Math.Abs(lt.Num("speed") / freeSmall - 5.0 / 11) < .05, $"bigger blades turn slower: large {Kinetics.Rpm(lt):0} rpm free, small {freeSmall * 60 / (2 * Math.PI):0} rpm");
        SetCell(bi, f, x0 + 5, y0 + 13, z0 - 1, Stone); BlockActions.Tick(.1);
        Check(lt.Num("blocked") == 1 && BEH[big].Describe(lt).Contains("11 X 11"), "a block at the corner of the 11 x 11 area blocks the large turbine");
        Clear();


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
        Console.WriteLine($"WIND at 10 m/s: small turbine {pSmall:0.0} E/s, large {lt.Num("power"):0.0} E/s; {loaded * 60 / (2 * Math.PI):0} rpm loaded; battery {be.Num("energy"):0.0} E in 10 s; coal: {coalE:0} E per piece");
        Kinetics.FixedSpeed = null; Kinetics.FixedHeading = null;
        Clear();
        foreach (var c in made) FreeChunk(c);
    }
}
