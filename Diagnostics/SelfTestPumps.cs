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
        void Clear() { Tanks.Reset(); for (int y = -2; y <= 20; y++) for (int z = -3; z <= 20; z++) for (int x = -4; x <= 9; x++) { var p = At(x, y, z); BlockStates.entities.Remove(p.Key); SetCell(bi, f, x0 + x, y0 + y, z0 + z, 0); } WaterSim.WAct.Clear(); WaterSim.wactQ.Clear(); }
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
            Put(1, height, 1, "liquid_pump", "up"); E(1, height, 1).SetNum("mode", 1);
            for (int y = 0; y < height; y++) Put(1, y, 1, "liquid_pipe"); // the pipe's open end sits in the pool's surface cell... (y -1 is water)
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
                        SetCell(bi, f, x0 + x, y0 + y, z0 + z, inside ? 0 : Id("liquid_tank_wall"));
                    }
            Put(2, 3, 2, "liquid_tank_valve"); var t = Tanks.Form(At(2, 3, 2)); t.Vent = false; t.Water = 0; t.Air = 27;
            for (int z = -1; z <= 5; z++) for (int x = -1; x <= 5; x++) { SetCell(bi, f, x0 + x, y0 - 2, z0 + z, Stone); bool rim = x == -1 || x == 5 || z == -1 || z == 5; for (int y = -1; y <= 0; y++) SetCell(bi, f, x0 + x, y0 + y, z0 + z, rim ? Stone : Water, rim ? 0 : 8); } // a walled pool
            Put(2, 1, 2, pump, "up"); E(2, 1, 2).SetNum("mode", 1); Put(2, 2, 2, "liquid_pipe"); Power(2, 1, 2);
            for (int i = 0; i < 40 && t.Water < 215; i++) { Ticks(40); for (int z = 0; z <= 4; z++) for (int x = 0; x <= 4; x++) { SetCell(bi, f, x0 + x, y0 - 1, z0 + z, Water, 8); SetCell(bi, f, x0 + x, y0, z0 + z, Water, 8); } } // keep the pool topped up
            return t.Pressure;
        }
        double pCentri = Fill("liquid_pump"); string whyC = PumpBehavior.Why(E(2, 1, 2));
        double pPiston = Fill("high_pressure_pump");
        double centriMax = 1 + PumpBehavior.SpecOf(Id("liquid_pump")).MaxSpecific * WaterMachines.Rho / Tank.P0; // ~6.9 bar if there were no height
        Check(pCentri > 4 && pCentri < centriMax && whyC.Contains("TOO HIGH") && pPiston > 18,
              $"filling a sealed tank: the centrifugal pump stalls at {pCentri:0.0} bar (its 60-block head; {whyC.Trim()}), the piston pump reaches {pPiston:0.0} bar");
        // ----- gravity: at half gravity a centrifugal pump lifts twice as high, suction reaches twice as far, a turbine gets half from the same drop
        var cs = PumpBehavior.SpecOf(Id("liquid_pump"));
        double lift100Full = PumpBehavior.Curve(cs, WaterMachines.G0 * 100).flow, lift100Half = PumpBehavior.Curve(cs, WaterMachines.G0 / 2 * 100).flow;
        Check(lift100Full == 0 && lift100Half > 0 && Math.Abs(WaterMachines.MaxSuction(WaterMachines.G0 / 2) - 2 * WaterMachines.MaxSuction(WaterMachines.G0)) < 1e-6
              && Math.Abs(WaterMachines.EPerUnit(WaterMachines.G0 / 2 * 10) - .5) < 1e-9 && Math.Abs(WaterMachines.EPerUnit(WaterMachines.G0 * 10) - 1) < 1e-9
              && Math.Abs(GravK(R / 2) - .5) < 1e-9 && GravK(R * 2) == .25,
              "gravity: a 100-block lift is impossible at surface gravity and fine at half; suction doubles; a 10-block drop gives 1 E per unit at the surface, 0.5 at half gravity (inside the planet at half radius / in space at twice the radius: 1/2, 1/4 g)");
        // the player's gravity setting: game ~3 g (snappy) or real 9.81 m/s2; both jumps still clear a full block
        double keep = Settings.SET.realgrav;
        Settings.SET.realgrav = 0; double gGame = PlayerPhysics.Gravity, hGame = PlayerPhysics.JumpSpeed * PlayerPhysics.JumpSpeed / (2 * gGame), tGame = 2 * PlayerPhysics.JumpSpeed / gGame;
        Settings.SET.realgrav = 1; double gReal = PlayerPhysics.Gravity, hReal = PlayerPhysics.JumpSpeed * PlayerPhysics.JumpSpeed / (2 * gReal), tReal = 2 * PlayerPhysics.JumpSpeed / gReal;
        Settings.SET.realgrav = keep;
        Check(gGame == 30 && gReal == 9.81 && hGame > 1.2 && hReal > 1.2 && tReal > tGame * 1.5,
              $"player gravity: game {gGame} m/s2 (jump {hGame:0.00} blocks, {tGame:0.00} s in the air), realistic {gReal} m/s2 (jump {hReal:0.00} blocks, {tReal:0.00} s)");
        // ----- pipe friction: a turbine fed from a sealed tank at 2 bar through 20 small pipes vs 20 large ones
        Tank PipeTank(double bar)
        {
            Clear();
            for (int y = 3; y <= 7; y++) for (int z = 0; z <= 4; z++) for (int x = 0; x <= 4; x++)
                    {
                        bool inside = x >= 1 && x <= 3 && y >= 4 && y <= 6 && z >= 1 && z <= 3;
                        SetCell(bi, f, x0 + x, y0 + y, z0 + z, inside ? 0 : Id("liquid_tank_wall"));
                    }
            Put(4, 4, 2, "liquid_tank_valve"); var tk = Tanks.Form(At(4, 4, 2)); tk.Vent = false; tk.Water = 108; tk.Air = bar * (27 - 13.5); return tk;
        }
        double TurbineFlow(string pipe)
        {
            var tk = PipeTank(2); for (int x = 5; x <= 24; x++) { int px = x <= 9 ? x : x; if (x > 9) continue; Put(x, 4, 2, pipe); } // 5 straight...
            for (int z = 3; z <= 17; z++) Put(9, 4, z, pipe);                                                                         // ...then 15 along z (20 pipes)
            Put(9, 4, 18, "liquid_turbine", "south"); var tu = E(9, 4, 18); tu.SetNum("mode", 1); Put(10, 4, 18, "copper_cable"); Put(11, 4, 18, "battery");
            double w0 = tk.Water; Ticks(20); return (w0 - tk.Water) / 2.0;
        }
        double qSmall = TurbineFlow("liquid_pipe"), lossSmall = E(9, 4, 18).Num("loss"), qLarge = TurbineFlow("large_liquid_pipe"), lossLarge = E(9, 4, 18).Num("loss");
        Check(qSmall < 8 && qLarge > 14 && lossSmall > .3 && lossLarge < .15, $"pipe friction: 20 small pipes let a turbine take {qSmall:0.0} u/s (friction {lossSmall * 100:0}% of the head), 20 large pipes {qLarge:0.0} u/s ({lossLarge * 100:0}%)");
        // ----- pipe valve: a sealed tank at 3 bar pushes water 13 blocks up a pipe into a basin, with no pump, until its pressure balances the height
        {
            var tk = PipeTank(3); SetCell(bi, f, x0 + 4, y0 + 4, z0 + 2, Id("liquid_tank_wall")); BlockStates.entities.Remove(At(4, 4, 2).Key);
            Put(2, 7, 2, "liquid_tank_valve"); Tanks.Reset(); tk = Tanks.Form(At(2, 7, 2)); tk.Vent = false; tk.Water = 108; tk.Air = 3 * (27 - 13.5);
            Put(2, 8, 2, "liquid_pipe"); Put(2, 9, 2, "liquid_pipe_valve", "up"); for (int y = 10; y <= 17; y++) Put(2, y, 2, "liquid_pipe");
            for (int z = 0; z <= 4; z++) for (int x = 0; x <= 4; x++) { if (!(x == 2 && z == 2)) SetCell(bi, f, x0 + x, y0 + 17, z0 + z, Stone); if (x == 0 || x == 4 || z == 0 || z == 4) for (int y = 18; y <= 19; y++) SetCell(bi, f, x0 + x, y0 + y, z0 + z, Stone); }
            double p0 = tk.Pressure, w0 = tk.Water; Ticks(300);
            int basin = 0; for (int y = 18; y <= 19; y++) for (int z = 1; z <= 3; z++) for (int x = 1; x <= 3; x++) basin += Wl(Wcell(bi, f, x0 + x, y0 + y, z0 + z));
            double height = y0 + 18 - (tk.Y0 + tk.Depth), pEq = 1 + height * g * 1000 / 1e5; var vv = E(2, 9, 2);
            Check(basin > 5 && Math.Abs(basin - (w0 - tk.Water)) < 1.01 && Math.Abs(tk.Pressure - pEq) < .15 && vv.Num("q") < .5,
                  $"a pipe valve lets a 3 bar tank push {basin} units {height:0} blocks up into a basin with no pump; it stops at {tk.Pressure:0.00} bar (balance {pEq:0.00})");
            vv.SetNum("closed", 1); double wc = tk.Water; Ticks(10); Check(tk.Water == wc, "a closed valve stops it");
        }
        Console.WriteLine($"PUMPS suction limit {maxS:0.0} blocks; centrifugal stalls at {pCentri:0.0} bar, piston {pPiston:0.0} bar; g here {g:0.00}");
        Clear(); WaterFlow.Clear();
        foreach (var c in made) FreeChunk(c);
    }
}
