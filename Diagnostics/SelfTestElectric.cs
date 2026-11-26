namespace CubePlanet;

public static partial class SelfTest
{
    // lamps (night / on, block light in the mesh), electric furnace, motor driving a generator, solar panels (sun, tracking, a 2 x 2 array)
    static void Electric()
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
        void Put(int x, int y, int z, string block, string facing = null)
        {
            int id = Id(block); var p = At(x, y, z); uint w = 0; var ff = DEFS[id]?.State.Find("facing");
            if (facing != null && ff != null) w = ff.With(0, Array.IndexOf(ff.Values, facing));
            p.Set(id, (int)w); BEH[id]?.OnPlace(p, id); BlockActions.NotifyNeighbors(p);
        }
        void Clear() { BlockLights.Reset(); for (int y = 4; y <= 14; y++) for (int z = -3; z <= 8; z++) for (int x = -3; x <= 12; x++) { var p = At(x, y, z); BlockStates.entities.Remove(p.Key); SetCell(bi, f, x0 + x, y0 + y, z0 + z, 0); } }
        BlockEntity E(int x, int y, int z) => BlockStates.entities[At(x, y, z).Key];
        void Ticks(int n) { for (int i = 0; i < n; i++) BlockActions.Tick(.1); }
        int coal = GameAssets.ItemReg.Id("coal");
        void Coal(int x, int y, int z) { Put(x, y, z, "coal_generator", "north"); E(x, y, z).Inv("fuel")[0] = new ItemStack(coal, 16); }
        // sun positions for this spot: noon, midnight, and a low sun (10..20 degrees up) as far east / west as possible
        double keepA = Sky.SUNA; V3 up = M.Nrm(At(0, 8, 0).Center); var (ax, _, _) = At(0, 8, 0).Axes();
        double noon = 0, night = 0, low = 0, bestN = -2, bestM = 2, bestL = -1;
        for (int i = 0; i < 720; i++)
        {
            Sky.SUNA = i * Math.PI / 360; Sky.SunVec(); double el = M.Dot(Sky.SUN, up), ew = Math.Abs(M.Dot(Sky.SUN, ax));
            if (el > bestN) { bestN = el; noon = Sky.SUNA; } if (el < bestM) { bestM = el; night = Sky.SUNA; }
            if (el > Math.Sin(10 * Math.PI / 180) && el < Math.Sin(20 * Math.PI / 180) && ew > bestL) { bestL = ew; low = Sky.SUNA; }
        }
        void Sun(double a) { Sky.SUNA = a; Sky.SunVec(); }
        Clear();

        // ----- lamp: coal generator -> cable -> lamp
        Coal(0, 8, 0); Put(1, 8, 0, "copper_cable"); Put(2, 8, 0, "electric_lamp"); var lamp = E(2, 8, 0);
        bool Lit() => DEFS[Id("electric_lamp")].State.Find("lit").Index((uint)At(2, 8, 0).State) == 1;
        Sun(noon); Ticks(5); bool dayOff = !Lit() && !BlockLights.All.ContainsKey(At(2, 8, 0).Key);
        Sun(night); Ticks(5); bool nightOn = Lit() && BlockLights.All.ContainsKey(At(2, 8, 0).Key);
        var lch = chunks[ck(bi, f, (x0 + 2) >> 5, (y0 + 8) >> 5, z0 >> 5)]; var lm = Mesher.Build(lch, out _); int maxBl = 0;
        if (lm?.o != null) for (int k = 0; k < lm.o.vb.Length / 16; k++) maxBl = Math.Max(maxBl, BitConverter.ToUInt16(lm.o.vb, k * 16 + 8) >> 12);
        Check(dayOff && nightOn && maxBl >= 7, $"a lamp on AUTO stays dark by day and lights at night (block light up to {maxBl} in the mesh)");
        lamp.SetNum("mode", 1); Sun(noon); Ticks(3); bool onDay = Lit();
        SetCell(bi, f, x0 + 1, y0 + 8, z0, 0); Ticks(3);
        Check(onDay && !Lit() && BlockLights.All.IsEmpty, "a lamp set to ON lights by day too, and goes dark when its cable is cut");
        Clear();

        // ----- electric furnace: 2 iron ore, coal generator power: twice as fast as a furnace
        Coal(0, 8, 0); Put(1, 8, 0, "copper_cable"); Put(2, 8, 0, "electric_furnace", "north"); var fu = E(2, 8, 0);
        int ore = GameAssets.ItemReg.Id("iron_ore"), ingot = GameAssets.ItemReg.Id("iron_ingot"); var rcp = GameAssets.RecipeReg.SmeltingFor(ore);
        fu.Inv("input")[0] = new ItemStack(ore, 2);
        Ticks((int)(rcp.Time / 2 * 2 * 10) + 15);
        Check(fu.Inv("output")[0]?.Item == ingot && fu.Inv("output")[0].Count == 2 && fu.Inv("input")[0] == null, $"the electric furnace smelts 2 iron ore in ~{rcp.Time:0} s (a furnace needs {rcp.Time * 2:0} s)");
        Clear();

        // ----- motor: 2 coal generators (40 E/s) -> motor (30 E/s in) -> shaft -> generator -> its own cable -> battery
        Coal(0, 8, 0); Coal(0, 9, 0); Put(1, 8, 0, "copper_cable"); Put(2, 8, 0, "electric_motor", "east"); Put(3, 8, 0, "shaft", "east"); Put(4, 8, 0, "generator", "east");
        Put(4, 9, 0, "copper_cable"); Put(4, 10, 0, "battery");
        var mo = E(2, 8, 0); var gen = E(4, 8, 0);
        Ticks(80);
        Check(Math.Abs(mo.Num("drive") - 27) < .5 && Math.Abs(gen.Num("input") - 27) < .5 && E(3, 8, 0).Num("speed") != 0 && E(4, 10, 0).Num("energy") > 50,
              $"an electric motor turns 30 E/s into {mo.Num("drive"):0.0} E/s of rotation; the generator on its shaft takes {gen.Num("input"):0.0} E/s and charges a battery ({E(4, 10, 0).Num("energy"):0} E)");
        mo.SetNum("mode", 3); Ticks(40);
        Check(Math.Abs(E(3, 8, 0).Num("speed")) < .1 && gen.Num("input") == 0, "a motor turned OFF lets the line run down");
        Clear();

        // ----- solar: a small panel -> cable -> battery
        Put(0, 8, 0, "solar_panel_small"); Put(1, 8, 0, "copper_cable"); Put(2, 8, 0, "battery"); var sp = E(0, 8, 0);
        double Pw(int n) { double e0 = E(2, 8, 0).Num("energy"); Ticks(n); return (E(2, 8, 0).Num("energy") - e0) / (n * .1) / BatteryBehavior.Efficiency; }
        Sun(noon); Ticks(5); double pNoon = Pw(20), sinNoon = M.Dot(Sky.SUN, up);
        Sun(night); Ticks(5); double pNight = Pw(10);
        Sun(low); Ticks(5); double pFixed = Pw(20);
        sp.SetNum("mode", 1); Ticks(60); double pTrack = Pw(20);
        Check(Math.Abs(pNoon - 3 * sinNoon) < .1 && pNight == 0 && pTrack > pFixed * 2 && Math.Abs(sp.Num("angle")) > .5,
              $"small solar panel: noon {pNoon:0.00} E/s (sun {Math.Asin(sinNoon) * 180 / Math.PI:0} deg up), night {pNight:0.00}, low sun flat {pFixed:0.00} vs tracking {pTrack:0.00} E/s (tilted {sp.Num("angle") * 180 / Math.PI:0} deg)");
        Clear();
        // a 2 x 2 panel: one block entity, the cable on a corner that is not the main part still collects it
        BlockActions.Place(At(0, 8, 0), Id("solar_panel_medium"), new V3(0, 0, 1), out _);
        int ents = 0; for (int z = 0; z <= 1; z++) for (int x = 0; x <= 1; x++) if (BlockStates.entities.ContainsKey(At(x, 8, z).Key)) ents++;
        Put(2, 8, 1, "copper_cable"); Put(3, 8, 1, "battery");
        Sun(noon); Ticks(5); double e1 = E(3, 8, 1).Num("energy"); Ticks(20); double pMed = (E(3, 8, 1).Num("energy") - e1) / 2 / BatteryBehavior.Efficiency;
        Check(At(1, 8, 1).Id == Id("solar_panel_medium") && ents == 1 && Math.Abs(pMed - 14 * sinNoon) < .3,
              $"a 2 x 2 solar panel ({ents} block entity) gives {pMed:0.0} E/s at noon through a cable on its far corner");
        Console.WriteLine($"ELECTRIC lamp block light {maxBl}; solar small noon {pNoon:0.0} / low sun flat {pFixed:0.00}, tracking {pTrack:0.00} E/s; medium {pMed:0.0} E/s; motor {mo.Num("drive"):0.0} E/s rotation");
        Clear(); Sun(keepA);
        foreach (var c in made) FreeChunk(c);
    }
}
