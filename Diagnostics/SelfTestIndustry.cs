namespace CubePlanet;

public static partial class SelfTest
{
    // mass on every block and item, no recipe makes mass; the metal chain: crusher, concentrator (water from a pipe), coke oven (hydrogen into
    // a tank), blast furnace, air separation unit, oxygen furnace (waits for its oxygen), plate mill, copper smelter, electrolytic refinery;
    // plates placed against the surface you look at
    static void Industry()
    {
        int Id(string n) => GameAssets.BlockReg.Id(n); int Item(string n) => GameAssets.ItemReg.Id(n);
        // ----- the mass rule
        var noMass = new List<string>();
        for (int i = 1; i < DEFS.Length; i++) if (DEFS[i] != null && DEFS[i].Mass <= 0) noMass.Add(DEFS[i].IdName);
        foreach (var it in GameAssets.ItemReg.All) if (Mass.OfItem(it.Id) <= 0) noMass.Add("item " + it.IdName);
        Check(noMass.Count == 0, $"every block and item has a mass ({(noMass.Count == 0 ? "all" : "missing: " + string.Join(", ", noMass.Take(12)))})");
        var made = new List<string>(); int nr = 0;
        bool Bad(double inKg, double outKg) => outKg > inKg * (1 + 1e-9) + 1e-6;
        foreach (var r in GameAssets.RecipeReg.SmeltingRecipes) { nr++; if (Bad(Mass.OfItem(r.Input), Mass.OfItem(r.Output) * r.Count)) made.Add(r.IdName); }
        for (int i = 1; i < DEFS.Length; i++)
            foreach (var r in ProcessorBehavior.RecipesOf(i).Concat(IndustryBehavior.RecipesOf(i))) { nr++; if (Bad(r.MassIn, r.MassOut)) made.Add($"{DEFS[i].IdName}: {r} ({r.MassIn:0.###} kg -> {r.MassOut:0.###} kg)"); }
        Check(made.Count == 0 && nr > 40, $"no recipe makes mass ({nr} recipes{(made.Count == 0 ? "" : "; makes mass: " + string.Join("; ", made.Take(6)))})");
        double plate = Mass.OfItem(Item("steel_plate")), ingot = Mass.OfItem(Item("steel_ingot"));
        Check(Math.Abs(plate - 4 * ingot) < 1e-6 && Math.Abs(16 * plate - 7850) < 1e-6 && Mass.OfItem(Item("stone")) == 2700 && Math.Abs(Mass.OfItem(Item("steel_rod")) * 4 - ingot) < 1e-6,
              $"a steel plate (1/16 block) is 4 ingots, 16 plates are a block of steel ({Mass.Text(plate)}, {Mass.Text(16 * plate)}); a rod is 1/4 ingot");
        var sawR = ProcessorBehavior.RecipesOf(Id("sawmill")).First(r => r.Uses(Item("oak_log")));
        Check(sawR.Out.Any(o => o.item == Item("oak_planks") && o.count < 1), $"the sawmill makes less than one block of planks from a log ({sawR.Out.First().count} planks + wood chips)");

        // ----- a test site in the air
        var (_, _, g0) = SpotInFront(16); int bi = BlockAccess.P2b(g0.X, g0.Y, g0.Z); int f = PF, x0 = PX, y0 = PY + 24, z0 = PZ;
        var chunksMade = new List<Chunk>();
        for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                {
                    long k = ck(bi, f, (x0 >> 5) + dx, (y0 >> 5) + dy, (z0 >> 5) + dz);
                    if (!chunks.TryGetValue(k, out var c)) { c = new Chunk { key = k, band = bi, face = f, cx = (x0 >> 5) + dx, cy = (y0 >> 5) + dy, cz = (z0 >> 5) + dz }; AddChunk(c); chunksMade.Add(c); }
                    ChunkGenerator.Ensure(c); c.built = true;
                }
        BlockPos At(int x, int y, int z) => new(false, bi, f, x0 + x, y0 + y, z0 + z);
        void Put(int x, int y, int z, string block, string facing = null)
        {
            int id = Id(block); var p = At(x, y, z); uint w = 0; var ff = DEFS[id]?.State.Find("facing");
            if (facing != null && ff != null) w = ff.With(0, Array.IndexOf(ff.Values, facing));
            p.Set(id, (int)w); BEH[id]?.OnPlace(p, id); BlockActions.NotifyNeighbors(p);
        }
        void Clear() { for (int y = 3; y <= 14; y++) for (int z = -4; z <= 6; z++) for (int x = -8; x <= 6; x++) { var p = At(x, y, z); BlockStates.entities.Remove(p.Key); SetCell(bi, f, x0 + x, y0 + y, z0 + z, 0); } WaterSim.WAct.Clear(); WaterSim.wactQ.Clear(); PipeContents.Reset(); }
        BlockEntity E(int x, int y, int z) => BlockStates.entities[At(x, y, z).Key];
        void Ticks(int n) { for (int i = 0; i < n; i++) BlockActions.Tick(.1); }
        void Coal(int x, int y, int z, int count) { for (int k = 0; k < count; k++) { Put(x - k, y, z, "coal_generator", "north"); E(x - k, y, z).Inv("fuel")[0] = new ItemStack(Item("coal"), 64); } }
        int CountIn(ItemStack[] inv, string item) => inv == null ? 0 : inv.Where(s => s != null && !s.IsEmpty && s.Item == Item(item)).Sum(s => s.Count);
        BlockEntity Machine(string block, int gens, params (string item, int n)[] input)
        {
            Clear(); Put(0, 8, 0, block, "north"); if (gens > 0) Coal(-1, 8, 0, gens); var m = E(0, 8, 0);
            for (int k = 0; k < input.Length; k++) m.Inv("input")[k] = new ItemStack(Item(input[k].item), input[k].n);
            return m;
        }
        BlockEntity O2Tank(int x, int y, int z, double mol) { Put(x, y, z, "gas_tank"); var t = E(x, y, z); t.SetNum("gas", Gas.Oxygen); t.SetNum("mol", mol); return t; }

        // crusher: an iron ore block (4 t) -> 16 crushed iron ore (250 kg each)
        var cr = Machine("jaw_crusher", 2, ("iron_ore", 1)); Ticks(140);
        Check(CountIn(cr.Inv("output"), "crushed_iron_ore") == 16 && cr.Inv("input")[0] == null, $"jaw crusher: an iron ore block -> {CountIn(cr.Inv("output"), "crushed_iron_ore")} crushed iron ore ({cr.Num("power"):0.0} E/s)");
        // concentrator: water from a pipe at its back (south), 1 unit a batch
        Clear(); SetCell(bi, f, x0, y0 + 5, z0 + 1, Stone); SetCell(bi, f, x0, y0 + 6, z0 + 1, Water, 8);
        foreach (var (wx, wz) in new[] { (1, 1), (-1, 1), (0, 2), (0, 0) }) SetCell(bi, f, x0 + wx, y0 + 6, z0 + wz, Stone);
        Put(0, 7, 1, "liquid_pipe"); Put(0, 8, 1, "liquid_pipe"); Put(0, 8, 0, "ore_concentrator", "north"); Coal(-1, 8, 0, 3);
        var oc = E(0, 8, 0); oc.Inv("input")[0] = new ItemStack(Item("crushed_iron_ore"), 2); Ticks(200);
        int water = Wl(Wcell(bi, f, x0, y0 + 6, z0 + 1));
        Check(CountIn(oc.Inv("output"), "iron_concentrate") == 1 && oc.Num("carry:iron_concentrate") > .39 && water == 6 && oc.Inv("input")[0] == null,
              $"ore concentrator: 2 crushed iron ore + 2 units of water from a pipe -> {CountIn(oc.Inv("output"), "iron_concentrate")} concentrate (+{oc.Num("carry:iron_concentrate"):0.00} kept), water left {water}/8");
        // coke oven: no power, hydrogen into the tank on top
        var co = Machine("coke_oven", 0, ("coal", 4)); Put(0, 9, 0, "gas_tank"); var ht = E(0, 9, 0); Ticks(110);
        Check(CountIn(co.Inv("output"), "coke") == 3 && (int)ht.Num("gas") == Gas.Hydrogen && Math.Abs(ht.Num("mol") - 794) < 1,
              $"coke oven (self-heated): 4 coal -> {CountIn(co.Inv("output"), "coke")} coke, {ht.Num("mol"):0} mol of hydrogen into the tank");
        // blast furnace
        var bf = Machine("blast_furnace", 3, ("iron_concentrate", 3), ("coke", 8), ("crushed_limestone", 1)); Ticks(420);
        Check(CountIn(bf.Inv("output"), "pig_iron") == 4 && CountIn(bf.Inv("output"), "slag") == 1 && bf.Inv("input").All(s => s == null),
              $"blast furnace: 3 concentrate + 8 coke + limestone -> {CountIn(bf.Inv("output"), "pig_iron")} pig iron + {CountIn(bf.Inv("output"), "slag")} slag ({bf.Num("power"):0.0} E/s)");
        // air separation unit: 100 E/s -> ~38 mol of oxygen a second into a tank
        Clear(); Put(0, 8, 0, "air_separator"); Coal(-1, 8, 0, 5); Put(0, 9, 0, "gas_tank"); var at = E(0, 9, 0); Ticks(100);
        double expect = AirSeparatorBehavior.Power * 10 / AirSeparatorBehavior.EPerMol;
        Check((int)at.Num("gas") == Gas.Oxygen && Math.Abs(at.Num("mol") - expect) < expect * .05, $"air separation unit: {at.Num("mol"):0} mol of oxygen in 10 s at 100 E/s (expected {expect:0})");
        // oxygen furnace: one tank (1,200 mol) is not enough for a batch (2,200): it waits; a second tank lets it finish
        var bo = Machine("oxygen_furnace", 2, ("pig_iron", 8), ("crushed_limestone", 1)); var t1 = O2Tank(1, 8, 0, 1200); Ticks(230);
        bool waited = (int)bo.Num("status") == 2 && CountIn(bo.Inv("output"), "steel_ingot") == 0 && t1.Num("mol") == 1200;
        var t2 = O2Tank(0, 9, 0, 1200); Ticks(3);
        Check(waited && CountIn(bo.Inv("output"), "steel_ingot") == 6 && Math.Abs(t1.Num("mol") + t2.Num("mol") - 200) < 1e-6,
              $"oxygen furnace waits for 2,200 mol of oxygen ({(waited ? "waited" : "did not wait")}), then 8 pig iron -> {CountIn(bo.Inv("output"), "steel_ingot")} steel ingots (+{bo.Num("carry:steel_ingot"):0.0}); {t1.Num("mol") + t2.Num("mol"):0} mol left");
        // plate mill
        var pm = Machine("plate_mill", 4, ("steel_ingot", 8)); Ticks(320);
        Check(CountIn(pm.Inv("output"), "steel_plate") == 2 && pm.Inv("input")[0] == null, $"plate mill: 8 steel ingots -> {CountIn(pm.Inv("output"), "steel_plate")} steel plates");
        // copper: smelter (oxygen) -> blister copper -> electrolytic refinery -> copper + anode slime
        var cs = Machine("copper_smelter", 3, ("copper_concentrate", 4)); O2Tank(1, 8, 0, 1200); O2Tank(0, 9, 0, 1200); Ticks(310);
        Check(CountIn(cs.Inv("output"), "blister_copper") == 1 && cs.Num("carry:blister_copper") > .9, $"copper smelter: 4 copper concentrate + oxygen -> {CountIn(cs.Inv("output"), "blister_copper")} blister copper (+{cs.Num("carry:blister_copper"):0.00})");
        var er = Machine("electrolytic_refinery", 4, ("blister_copper", 2)); Ticks(610);
        Check(CountIn(er.Inv("output"), "copper_ingot") == 1 && CountIn(er.Inv("output"), "anode_slime") == 1, $"electrolytic refinery: 2 blister copper -> {CountIn(er.Inv("output"), "copper_ingot")} copper ingot (+{er.Num("carry:copper_ingot"):0.00}) + {CountIn(er.Inv("output"), "anode_slime")} anode slime");
        // a plate placed while looking down lies on the floor of its cell; looking north it stands against the north side
        Clear(); int pid = Id("steel_plate"); var (_, ay, az) = At(0, 8, 0).Axes();
        BlockActions.Place(At(0, 8, 0), pid, -ay, out _); var down = Variant(pid, At(0, 8, 0).State).Collision[0];
        BlockActions.Place(At(1, 8, 0), pid, -az, out _); var north = Variant(pid, At(1, 8, 0).State).Collision[0];
        Check(down.Y1 <= .0626 && down.X1 - down.X0 > .99 && north.Z1 <= .0626 && north.Y1 - north.Y0 > .99, $"a steel plate lies against the surface you look at (looking down: y {down.Y0}..{down.Y1}; looking north: z {north.Z0}..{north.Z1})");
        Clear();
        foreach (var c in chunksMade) FreeChunk(c);
    }
}
