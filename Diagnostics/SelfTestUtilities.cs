namespace CubePlanet;

public static partial class SelfTest
{
    // miner, hydrogen (electrolyzer, tank, fuel cell), charger + drill + chainsaw, grow light, sprinkler, elevator, item pipes, status lamp
    static void Utilities()
    {
        int Id(string n) => GameAssets.BlockReg.Id(n); int Item(string n) => GameAssets.ItemReg.Id(n);
        var (_, _, g0) = SpotInFront(16); int bi = BlockAccess.P2b(g0.X, g0.Y, g0.Z); int f = PF, x0 = PX, y0 = PY + 24, z0 = PZ;
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
        void Clear() { for (int y = 3; y <= 16; y++) for (int z = -6; z <= 8; z++) for (int x = -6; x <= 10; x++) { var p = At(x, y, z); BlockStates.entities.Remove(p.Key); SetCell(bi, f, x0 + x, y0 + y, z0 + z, 0); } BlockLights.Reset(); GrowLights.Lit.Clear(); WaterSim.WAct.Clear(); WaterSim.wactQ.Clear(); }
        BlockEntity E(int x, int y, int z) => BlockStates.entities[At(x, y, z).Key];
        void Ticks(int n) { for (int i = 0; i < n; i++) BlockActions.Tick(.1); }
        void Coal(int x, int y, int z, int count = 1) { for (int k = 0; k < count; k++) { Put(x - k, y, z, "coal_generator", "north"); E(x - k, y, z).Inv("fuel")[0] = new ItemStack(Item("coal"), 64); } }
        int CountIn(ItemStack[] inv, int item) => inv == null ? 0 : inv.Where(s => s != null && !s.IsEmpty && s.Item == item).Sum(s => s.Count);
        double g = WaterMachines.G(At(0, 8, 0));

        // ----- miner: a 9 x 9 layer of dirt under it goes into the chest beside it
        Clear(); for (int z = -4; z <= 4; z++) for (int x = -4; x <= 4; x++) SetCell(bi, f, x0 + x, y0 + 7, z0 + z, Dirt);
        Put(0, 8, 0, "miner"); Put(1, 8, 0, "chest", "north"); Coal(-1, 8, 0, 2); var mn = E(0, 8, 0); // dirt: 2 + 6 x hardness 1 = 8 E a block, 81 blocks at 20 E/s ~ 33 s
        int dirtItem = GameAssets.ItemReg.Has("dirt") ? Item("dirt") : -1;
        Ticks(450);
        int left = 0; for (int z = -4; z <= 4; z++) for (int x = -4; x <= 4; x++) if (At(x, 7, z).Id == Dirt) left++;
        Check(left == 0 && CountIn(E(1, 8, 0).Inv("slots"), dirtItem) >= 81 && mn.Num("mined") >= 81, $"the miner digs a 9x9 layer of dirt into its chest ({CountIn(E(1, 8, 0).Inv("slots"), dirtItem)} dirt, {mn.Num("mined"):0} blocks, {left} left)");
        mn.SetNum("off", 1);
        // ----- hydrogen: coal -> electrolyzer (water below it) -> tank -> fuel cell -> oxygen generator
        Clear(); SetCell(bi, f, x0, y0 + 6, z0, Stone); SetCell(bi, f, x0, y0 + 7, z0, Water, 8); for (int k = 0; k < 4; k++) { var (ax, az) = k switch { 0 => (1, 0), 1 => (-1, 0), 2 => (0, 1), _ => (0, -1) }; SetCell(bi, f, x0 + ax, y0 + 7, z0 + az, Stone); }
        Put(0, 8, 0, "electrolyzer", "up"); Put(1, 8, 0, "hydrogen_tank"); Coal(-1, 8, 0, 5); Put(1, 9, 0, "fuel_cell"); Put(1, 10, 0, "oxygen_generator", "north");
        var el = E(0, 8, 0); var ht = E(1, 8, 0); var og = E(1, 10, 0); og.SetNum("energy", 0);
        Ticks(100);
        double kgExpect = ElectrolyzerBehavior.Power * 10 * Tank.JoulesPerE * ElectrolyzerBehavior.Efficiency / Hydrogen.Hhv;
        Check(Math.Abs(el.Num("made_kg") - kgExpect) < kgExpect * .05 && og.Num("energy") > 50 && Wl(Wcell(bi, f, x0, y0 + 7, z0)) <= 7,
              $"electrolyzer: {el.Num("made_kg") * 1000:0.0} g of hydrogen in 10 s at 100 E/s (expected {kgExpect * 1000:0.0}), {8 - Wl(Wcell(bi, f, x0, y0 + 7, z0))} unit(s) of water taken; the fuel cell gave the oxygen generator {og.Num("energy"):0} E; tank {Hydrogen.Bar(ht):0.000} bar");
        // ----- charger + drill + chainsaw
        Clear(); Coal(-1, 8, 0, 2); Put(0, 8, 0, "charger"); var drill = new ItemStack(Item("electric_drill"), 1); E(0, 8, 0).Inv("slot")[0] = drill;
        Ticks(50); int charged = Tools.Charge(drill);
        Check(charged > 150 && charged < 200, $"the charger put {charged} E into the drill in 5 s (40 E/s x 95 %)");
        for (int z = 2; z <= 4; z++) for (int y = 9; y <= 11; y++) SetCell(bi, f, x0 + 5, y0 + y, z0 + z, Stone);
        var (ax2, _, _) = At(5, 10, 3).Axes(); int dug = Tools.Drill(drill, At(5, 10, 3), ax2); int left2 = 0;
        for (int z = 2; z <= 4; z++) for (int y = 9; y <= 11; y++) if (At(5, y, z).Id != 0) left2++;
        Check(dug == 9 && left2 == 0 && Math.Abs(charged - Tools.Charge(drill) - 9 * MinerBehavior.Cost(Stone)) < 9.5, $"the drill digs 3x3 ({dug} blocks) for {charged - Tools.Charge(drill)} E");
        int log = BEH.Length > 0 ? Enumerable.Range(1, DEFS.Length - 1).First(i => LOGID[i]) : 0, leaf = Enumerable.Range(1, DEFS.Length - 1).First(i => LEAFS[i]);
        for (int y = 8; y <= 11; y++) SetCell(bi, f, x0 + 7, y0 + y, z0, log); for (int z = -1; z <= 1; z++) for (int x = 6; x <= 8; x++) SetCell(bi, f, x0 + x, y0 + 12, z0 + z, leaf);
        var saw = new ItemStack(Item("chainsaw"), 1); Tools.SetCharge(saw, 1000); int cut = Tools.Chainsaw(saw, At(7, 8, 0)); int treeLeft = 0;
        for (int y = 8; y <= 12; y++) for (int z = -1; z <= 1; z++) for (int x = 6; x <= 8; x++) if (At(x, y, z).Id != 0) treeLeft++;
        Check(cut == 13 && treeLeft == 0 && Tools.Charge(saw) == 1000 - 26, $"the chainsaw fells a whole tree ({cut} blocks, {1000 - Tools.Charge(saw)} E)");
        // ----- grow light: lit and powered, crops within 4 blocks grow twice as fast
        Clear(); Coal(-1, 8, 0); Put(0, 8, 0, "grow_light"); E(0, 8, 0).SetNum("mode", 1); Ticks(5);
        Check(GrowLights.Boost(At(3, 8, 2)) == 2 && GrowLights.Boost(At(6, 8, 0)) == 1, "a lit grow light makes crops within 4 blocks grow twice as fast");
        // ----- sprinkler: water from below, 3x3... 8 farmland around it on its row
        Clear(); SetCell(bi, f, x0, y0 + 6, z0, Stone); SetCell(bi, f, x0, y0 + 7, z0, Water, 8); SetCell(bi, f, x0, y0 + 5, z0, Stone);
        for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++) { if (x == 0 && z == 0) continue; SetCell(bi, f, x0 + x, y0 + 7, z0 + z, Dirt); Farming.Till(At(x, 7, z)); }
        SetCell(bi, f, x0, y0 + 6, z0, Water, 8);
        Put(0, 8, 0, "sprinkler", "up"); Coal(-1, 8, 0);
        int Moist() { int n = 0; for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++) if (!(x == 0 && z == 0)) n += Farming.MoistureAt(At(x, 7, z)); return n; }
        int w0 = Wl(Wcell(bi, f, x0, y0 + 7, z0)) + Wl(Wcell(bi, f, x0, y0 + 6, z0)), m0 = Moist(); Ticks(50);
        int w1 = Wl(Wcell(bi, f, x0, y0 + 7, z0)) + Wl(Wcell(bi, f, x0, y0 + 6, z0)), m1 = Moist();
        Check(m1 > m0 && (w0 - w1) == (m1 - m0), $"a sprinkler waters the farmland around it ({m1 - m0} moisture from {w0 - w1} units of water)");
        // ----- elevator: two elevators 10 blocks apart; the ride costs 80 kg x g x 10 / 80 %
        Clear(); Put(0, 4, 0, "elevator"); Put(0, 14, 0, "elevator"); var nextUp = Elevator.Next(At(0, 4, 0), 1); var nextDn = Elevator.Next(At(0, 14, 0), -1);
        double ge = WaterMachines.G(At(0, 4, 0)), cost = Elevator.Cost(10, ge), back = Elevator.Cost(-10, ge); // gravity where the ride starts
        var P = PlayerPhysics.P; var keepP = P.p; bool keepOn = P.on, keepFly = P.fly;
        E(0, 4, 0).SetNum("energy", 50); var top = At(0, 4, 0).Center; P.p = top + M.Nrm(top) * .5; P.on = true; P.fly = false;
        bool started = Elevator.Ride(.05, true, false); for (int i = 0; i < 100 && Elevator.Riding; i++) Elevator.Ride(.05, false, false);
        double arrived = (P.p - (At(0, 14, 0).Center + M.Nrm(At(0, 14, 0).Center) * .5)).Len, used = 50 - E(0, 4, 0).Num("energy");
        P.p = keepP; P.on = keepOn; P.fly = keepFly; Elevator.Ride(.05, false, false);
        Check(nextUp?.Key == At(0, 14, 0).Key && nextDn?.Key == At(0, 4, 0).Key && started && arrived < .01 && Math.Abs(used - cost) < 1e-6 && back < 0 && cost > .7 && cost < .8,
              $"elevator: rides 10 blocks up for {used:0.00} E (80 kg x {ge:0.00} x 10 / 0.8), riding down gives {-back:0.00} back");
        // ----- item pipes: chest A (10 iron ore) -> extractor -> pipes and a belt -> chest B
        Clear(); Put(0, 8, 0, "chest", "north"); E(0, 8, 0).Inv("slots")[0] = new ItemStack(Item("iron_ore"), 10);
        Put(1, 8, 0, "extractor", "east"); Put(2, 8, 0, "item_pipe"); Put(3, 8, 0, "conveyor_belt", "north"); Put(4, 8, 0, "item_pipe"); Put(5, 8, 0, "chest", "north");
        Coal(1, 9, 0); Ticks(40);
        Check(CountIn(E(5, 8, 0).Inv("slots"), Item("iron_ore")) == 10 && CountIn(E(0, 8, 0).Inv("slots"), Item("iron_ore")) == 0, $"an extractor sends 10 iron ore along item pipes and a belt to another chest ({CountIn(E(5, 8, 0).Inv("slots"), Item("iron_ore"))} arrived)");
        // ----- status lamp: a battery at 80 % on its grid -> green
        Clear(); Put(0, 8, 0, "battery"); E(0, 8, 0).SetNum("energy", BatteryBehavior.Capacity * .8); Put(1, 8, 0, "status_lamp"); Ticks(5);
        Check(E(1, 8, 0).Num("level") == 2 && DEFS[Id("status_lamp")].State.Find("level").Value((uint)At(1, 8, 0).State) == 2, "the grid status lamp turns green at 80 % storage");
        Console.WriteLine($"UTILITIES miner {mn.Num("mined"):0} blocks; hydrogen {el.Num("made_kg") * 1000:0.0} g; drill {dug}; chainsaw {cut}; elevator 10 blocks {cost:0.00} E");
        Clear();
        foreach (var c in made) FreeChunk(c);
    }
}
