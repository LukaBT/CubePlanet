namespace CubePlanet;

public static partial class SelfTest
{
    // realistic pumps: suction limit, the centrifugal pump curve against a pressurized tank vs the piston pump, gravity
    static void PumpTest()
    {
        int Id(string n) => GameAssets.BlockReg.Id(n);
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
        void Clear() { Tanks.Reset(); for (int y = -2; y <= 18; y++) for (int z = -3; z <= 7; z++) for (int x = -4; x <= 9; x++) { var p = At(x, y, z); BlockStates.entities.Remove(p.Key); SetCell(bi, f, x0 + x, y0 + y, z0 + z, 0); } WaterSim.WAct.Clear(); WaterSim.wactQ.Clear(); }
        BlockEntity E(int x, int y, int z) => BlockStates.entities[At(x, y, z).Key];
        void Ticks(int n) { for (int i = 0; i < n; i++) { BlockActions.Tick(.1); if ((i & 1) == 1) WaterSim.WaterStep(); } }
        void Power(int x, int y, int z) // coal generators and a cable into (x, y, z)
        {
            for (int k = 1; k <= 3; k++) { Put(x - k - 1, y, z, "coal_generator", "north"); E(x - k - 1, y, z).Inv("fuel")[0] = new ItemStack(GameAssets.ItemReg.Id("coal"), 32); }
            Put(x - 1, y, z, "gold_cable");
        }
        double g = WaterMachines.G(At(0, 5, 0));
        // ----- suction: a pump above a pool with a pipe down to it pulls water up only so far (~7.5 blocks at surface gravity)
        double Suck(int height)
        {
            Clear();
            for (int z = -1; z <= 3; z++) for (int x = -1; x <= 3; x++) { SetCell(bi, f, x0 + x, y0 - 2, z0 + z, Stone); bool rim = x == -1 || x == 3 || z == -1 || z == 3; SetCell(bi, f, x0 + x, y0 - 1, z0 + z, rim ? Stone : Water, rim ? 0 : 8); } // a walled pool
            Put(1, height, 1, "water_pump", "up"); E(1, height, 1).SetNum("mode", 1);
            for (int y = 0; y < height; y++) Put(1, y, 1, "water_pipe"); // the pipe's open end sits in the pool's surface cell... (y -1 is water)
            Power(1, height, 1);
            Ticks(20); return E(1, height, 1).Num("flow");
        }
        double low = Suck(5), high = Suck(10); string why = PumpBehavior.Why(E(1, 10, 1)); double maxS = WaterMachines.MaxSuction(g);
        Check(low > 1 && high == 0 && why.Contains("PULL WATER UP") && maxS > 7 && maxS < 8.5,
              $"suction: a pump 5 blocks above the water pumps ({low:0.0} u/s), 10 blocks above it cannot ({why.Trim()}); limit here {maxS:0.0} blocks (g {g:0.00})");
        // ----- a sealed tank (3x3x3) filled from a pool: the centrifugal pump stalls near its maximum head, the piston pump goes on to ~20 bar
        double Fill(string pump)
        {
            Clear();
            for (int y = 3; y <= 7; y++) for (int z = 0; z <= 4; z++) for (int x = 0; x <= 4; x++)
                    {
                        bool inside = x >= 1 && x <= 3 && y >= 4 && y <= 6 && z >= 1 && z <= 3;
                        SetCell(bi, f, x0 + x, y0 + y, z0 + z, inside ? 0 : Id("tank_wall"));
                    }
            Put(2, 3, 2, "tank_valve"); var t = Tanks.Form(At(2, 3, 2)); t.Vent = false; t.Water = 0; t.Air = 27;
            for (int z = -1; z <= 5; z++) for (int x = -1; x <= 5; x++) { SetCell(bi, f, x0 + x, y0 - 2, z0 + z, Stone); bool rim = x == -1 || x == 5 || z == -1 || z == 5; for (int y = -1; y <= 0; y++) SetCell(bi, f, x0 + x, y0 + y, z0 + z, rim ? Stone : Water, rim ? 0 : 8); } // a walled pool
            Put(2, 1, 2, pump, "up"); E(2, 1, 2).SetNum("mode", 1); Put(2, 2, 2, "water_pipe"); Power(2, 1, 2);
            for (int i = 0; i < 40 && t.Water < 215; i++) { Ticks(40); for (int z = 0; z <= 4; z++) for (int x = 0; x <= 4; x++) { SetCell(bi, f, x0 + x, y0 - 1, z0 + z, Water, 8); SetCell(bi, f, x0 + x, y0, z0 + z, Water, 8); } } // keep the pool topped up
            return t.Pressure;
        }
        double pCentri = Fill("water_pump"); string whyC = PumpBehavior.Why(E(2, 1, 2));
        double pPiston = Fill("high_pressure_pump");
        double centriMax = 1 + PumpBehavior.SpecOf(Id("water_pump")).MaxSpecific * WaterMachines.Rho / Tank.P0; // ~6.9 bar if there were no height
        Check(pCentri > 4 && pCentri < centriMax && whyC.Contains("TOO HIGH") && pPiston > 18,
              $"filling a sealed tank: the centrifugal pump stalls at {pCentri:0.0} bar (its 60-block head; {whyC.Trim()}), the piston pump reaches {pPiston:0.0} bar");
        // ----- gravity: at half gravity a centrifugal pump lifts twice as high, suction reaches twice as far, a turbine gets half from the same drop
        var cs = PumpBehavior.SpecOf(Id("water_pump"));
        double lift100Full = PumpBehavior.Curve(cs, WaterMachines.G0 * 100).flow, lift100Half = PumpBehavior.Curve(cs, WaterMachines.G0 / 2 * 100).flow;
        Check(lift100Full == 0 && lift100Half > 0 && Math.Abs(WaterMachines.MaxSuction(WaterMachines.G0 / 2) - 2 * WaterMachines.MaxSuction(WaterMachines.G0)) < 1e-6
              && Math.Abs(WaterMachines.EPerUnit(WaterMachines.G0 / 2 * 10) - .5) < 1e-9 && Math.Abs(WaterMachines.EPerUnit(WaterMachines.G0 * 10) - 1) < 1e-9
              && Math.Abs(GravK(R / 2) - .5) < 1e-9 && GravK(R * 2) == .25,
              "gravity: a 100-block lift is impossible at surface gravity and fine at half; suction doubles; a 10-block drop gives 1 E per unit at the surface, 0.5 at half gravity (inside the planet at half radius / in space at twice the radius: 1/2, 1/4 g)");
        Console.WriteLine($"PUMPS suction limit {maxS:0.0} blocks; centrifugal stalls at {pCentri:0.0} bar, piston {pPiston:0.0} bar; g here {g:0.00}");
        Clear(); WaterFlow.Clear();
        foreach (var c in made) FreeChunk(c);
    }
}
