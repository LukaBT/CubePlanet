namespace CubePlanet;

public static partial class SelfTest
{
    // water tanks: forming, Boyle's law, water compressibility, a pump filling a sealed tank, a turbine on a pressurized tank, the compressor,
    // bursting, saving
    static void TankTest()
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
        void Clear() { Tanks.Reset(); for (int y = 3; y <= 14; y++) for (int z = -3; z <= 7; z++) for (int x = -3; x <= 9; x++) { var p = At(x, y, z); BlockStates.entities.Remove(p.Key); SetCell(bi, f, x0 + x, y0 + y, z0 + z, 0); } WaterSim.WAct.Clear(); WaterSim.wactQ.Clear(); }
        BlockEntity E(int x, int y, int z) => BlockStates.entities[At(x, y, z).Key];
        void Ticks(int n, bool water = false) { for (int i = 0; i < n; i++) { BlockActions.Tick(.1); if (water && (i & 1) == 1) WaterSim.WaterStep(); } }
        // a 3 x 3 x 3 room (x 1..3, y 9..11, z 1..3) walled with tank walls (a window on top), valves on the bottom (2,8,2) and east (4,9,2) faces
        void Shell()
        {
            for (int y = 8; y <= 12; y++) for (int z = 0; z <= 4; z++) for (int x = 0; x <= 4; x++)
                    {
                        bool inside = x >= 1 && x <= 3 && y >= 9 && y <= 11 && z >= 1 && z <= 3;
                        SetCell(bi, f, x0 + x, y0 + y, z0 + z, inside ? 0 : y == 12 ? Id("tank_window") : Id("tank_wall"));
                    }
            Put(2, 8, 2, "tank_valve"); Put(4, 9, 2, "tank_valve");
        }
        Clear(); Shell();
        var t = Tanks.Form(At(2, 8, 2));
        Check(t != null && t.Volume == 27 && t.Room() == 216 && Tanks.Of(At(4, 9, 2)) == t && t.Vent, $"a 3x3x3 room walled with tank blocks forms a tank ({t?.Volume} m3, room {t?.Room()} units), both valves see it");
        // Boyle: sealed with 1 bar of air, half full -> 2 bar; the 20 bar safety valve limits how full it gets
        t.Vent = false; t.Air = 27; t.Water = 0; t.Put(108); double pHalf = t.Pressure; int roomSealed = t.Room();
        Check(Math.Abs(pHalf - 2) < .01 && Math.Abs(roomSealed - ((27 - 27 / 20.0) * (1 + 19 * 1e5 / 2.2e9) / .125 - 108)) < 1.01, $"sealed half full: {pHalf:0.000} bar (Boyle: 2); room before 20 bar {roomSealed} units");
        // water barely compresses: full, no air, 1 unit more -> ~52 bar (bulk modulus 2.2 GPa)
        t.Vent = true; t.Water = 216; t.Settle(); t.Vent = false; t.Air = 0; t.Water = 216.5; double pOver = t.Pressure; t.Water = 216;
        Check(pOver > 45 && pOver < 60 && t.Room() == 0, $"water barely compresses: half a unit (62 l) more in a full 27 m3 tank = {pOver:0} bar");
        // a pump fills the sealed tank through a pipe from a pool below; the pressure rises (and so does the lift); no water is created or lost
        t.Water = 0; t.Air = 27; t.Vent = false;
        for (int z = 0; z <= 4; z++) for (int x = 0; x <= 4; x++) SetCell(bi, f, x0 + x, y0 + 4, z0 + z, Stone);
        for (int z = 1; z <= 3; z++) for (int x = 1; x <= 3; x++) SetCell(bi, f, x0 + x, y0 + 5, z0 + z, Water, 8);
        for (int z = 0; z <= 4; z++) for (int x = 0; x <= 4; x++) if (x == 0 || x == 4 || z == 0 || z == 4) SetCell(bi, f, x0 + x, y0 + 5, z0 + z, Stone);
        Put(2, 6, 2, "water_pump", "up"); Put(2, 7, 2, "water_pipe"); E(2, 6, 2).SetNum("mode", 1);
        for (int x = -2; x <= 0; x++) { Put(x, 6, 2, x == 0 ? "copper_cable" : "coal_generator", "north"); if (x < 0) E(x, 6, 2).Inv("fuel")[0] = new ItemStack(GameAssets.ItemReg.Id("coal"), 16); }
        Put(1, 6, 2, "copper_cable");
        int Pool() { int n = 0; for (int z = 1; z <= 3; z++) for (int x = 1; x <= 3; x++) n += Wl(Wcell(bi, f, x0 + x, y0 + 5, z0 + z)); return n; }
        int pool0 = Pool(); Ticks(60, true);
        Check(t.Water > 20 && t.Pressure > 1.05 && Math.Abs(Pool() + t.Water - pool0) < 1e-6 && E(2, 6, 2).Num("lift") > 2,
              $"a pump fills the sealed tank through a pipe: {t.Water:0} units in, {t.Pressure:0.00} bar, lift {E(2, 6, 2).Num("lift"):0.0}; pool + tank = {Pool() + t.Water:0} (was {pool0})");
        E(2, 6, 2).SetNum("mode", 2); double pPump = t.Pressure;
        // a turbine on the east valve, outlet into the air: at 10 bar the tank gives ~92 blocks of head
        t.Water = 108; t.Air = 10 * (27 - 13.5); Put(5, 9, 2, "water_turbine", "east"); Put(5, 10, 2, "copper_cable"); Put(5, 11, 2, "battery");
        var tb = E(5, 9, 2); tb.SetNum("mode", 1); double p10 = t.Pressure, w0 = t.Water; Ticks(10);
        double head = tb.Num("head"), used = w0 - t.Water, e1 = E(5, 11, 2).Num("energy");
        Check(Math.Abs(p10 - 10) < .05 && head > 80 && used > 5 && t.Pressure < p10 && e1 > used * 80 * WaterMachines.K * TurbineBehavior.Efficiency * BatteryBehavior.Efficiency && e1 < (used + 1) * 95 * WaterMachines.K * TurbineBehavior.Efficiency * BatteryBehavior.Efficiency, // the head falls from ~93 as it drains
              $"a turbine on a 10 bar tank: head {head:0} blocks, {used:0} units -> battery {e1:0.0} E; the pressure drops to {t.Pressure:0.00} bar as the air expands");
        tb.SetNum("mode", 2);
        // compressor: powered, on the bottom valve's side... it touches the valve at (2,8,2)
        SetCell(bi, f, x0 + 2, y0 + 7, z0 + 2, 0); Put(2, 7, 2, "compressor"); var co = E(2, 7, 2); co.SetNum("mode", 1); // 20 bar target
        Put(1, 7, 2, "copper_cable"); Put(1, 6, 2, "copper_cable");
        double pA = t.Pressure, eA = t.AirEnergy, va = t.Air / pA; Ticks(100);
        double pExpect = pA + CompressorBehavior.FreeAir * 10 / va; // 0.5 m3 of free air a second for 10 s into the cushion
        Check(Math.Abs(t.Pressure - pExpect) < .05 * pExpect && t.AirEnergy > eA && co.Num("power") > 1, $"a compressor raises the pressure {pA:0.00} -> {t.Pressure:0.00} bar (expected {pExpect:0.00}) using ~{co.Num("power"):0.0} E/s (air stores {eA:0} -> {t.AirEnergy:0} E)");
        // save / load
        var js = Tanks.Save(); double wS = t.Water; Tanks.Load(System.Text.Json.Nodes.JsonNode.Parse(js.ToJsonString())); var t2 = Tanks.Of(At(2, 8, 2));
        Check(t2 != null && Math.Abs(t2.Water - wS) < 1e-9 && !t2.Vent, "tanks save and load");
        // burst: breaking a wall spills the water into the room
        double inTank = t2.Water; SetCell(bi, f, x0 + 2, y0 + 10, z0 + 0, 0); Ticks(12);
        int spilled = 0; for (int y = 8; y <= 12; y++) for (int z = -1; z <= 5; z++) for (int x = -1; x <= 5; x++) spilled += Wl(Wcell(bi, f, x0 + x, y0 + y, z0 + z));
        Check(Tanks.All.Count == 0 && spilled >= (int)inTank - 8, $"breaking a wall bursts the tank: {spilled} of {inTank:0} units spill out");
        Console.WriteLine($"TANKS half full sealed {pHalf:0.00} bar; overfull {pOver:0} bar; pump filled to {pPump:0.00} bar; turbine head {head:0}; compressor -> {t.Pressure:0.0} bar");
        Clear(); WaterFlow.Clear();
        foreach (var c in made) FreeChunk(c);
    }
}
