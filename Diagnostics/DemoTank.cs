namespace CubePlanet;

public static partial class DemoWorld
{
    // ---------- --demotank NAME [--demosecs S]: pumped storage in a pressurized tank ----------
    // A sealed 5 x 5 x 5 tank (windows on its south face) half full at 8 bar. West of it a pool. Coal generators power a high-pressure (piston) pump lifting water from
    // the pool up a pipe into the tank (west valve, high) and a compressor on the top valve (target 10 bar). A water turbine on the low west valve
    // lets water back down a pipe into the pool (on), charging its own network: 3 batteries and an oxygen generator.
    public static int RunTank(string name, double secs)
    {
        string folder = WorldSave.FolderFor(name);
        if (Directory.Exists(folder)) { Console.WriteLine($"DEMOWORLD a world folder '{folder}' already exists: not touching it"); return 1; }
        World.InitWorldData(1200, 1337); ChunkGenerator.mainTid = Environment.CurrentManagedThreadId;
        if (!FindSite()) { Console.WriteLine("DEMOWORLD no dry building site near spawn"); return 1; }
        int stone = Id("stone_bricks"), wall = Id("liquid_tank_wall"), window = Id("liquid_tank_window");
        for (int y = -8; y <= 30; y++) for (int z = -16; z <= 16; z++) for (int x = -12; x <= 20; x++) Cell(x, y, z, y <= -1 ? stone : 0);
        // tank: inside x 2..6, y 1..5, z 2..6; shell around it, the south face (z 1) windows
        for (int y = 0; y <= 6; y++) for (int z = 1; z <= 7; z++) for (int x = 1; x <= 7; x++)
                {
                    bool inside = x >= 2 && x <= 6 && y >= 1 && y <= 5 && z >= 2 && z <= 6; if (inside) continue;
                    Cell(x, y, z, z == 1 && x >= 2 && x <= 6 && y >= 1 && y <= 5 ? window : wall);
                }
        Put(1, 1, 4, "liquid_tank_valve"); Put(1, 4, 3, "liquid_tank_valve"); Put(4, 6, 4, "liquid_tank_valve");
        var t = Tanks.Form(At(1, 1, 4));
        if (t != null) { t.Vent = false; t.Water = 500; t.Air = 8 * (t.Volume - 500 * Tank.Unit); t.Settle(); }
        // pool west of it
        for (int y = -6; y <= -2; y++) for (int z = 1; z <= 7; z++) for (int x = -8; x <= -1; x++) Cell(x, y, z, Water, 8); // the top layer (y -1) left free for returning water
        for (int z = 1; z <= 7; z++) for (int x = -8; x <= -1; x++) Cell(x, -1, z, 0);
        // turbine on the low valve, its outlet pipe down into the pool; its network: batteries + oxygen generator
        Put(0, 1, 4, "liquid_turbine", "west"); E(0, 1, 4).SetNum("mode", 2); // off: right click it (on / auto) to let the tank drive it Put(-1, 1, 4, "liquid_pipe"); Put(-1, 0, 4, "liquid_pipe");
        Put(0, 2, 4, "copper_cable"); Put(0, 3, 4, "copper_cable"); Put(-1, 3, 4, "copper_cable"); Put(-2, 3, 4, "battery"); Put(-2, 3, 5, "battery"); Put(-2, 3, 6, "battery"); Put(-2, 3, 3, "oxygen_generator", "north");
        // pump from the pool up into the high valve, powered by coal; compressor on the top valve
        Put(-4, 0, 3, "high_pressure_pump", "up"); E(-4, 0, 3).SetNum("mode", 1); // a piston pump (a centrifugal one stalls near 6 bar)
        for (int y = -1; y >= -4; y--) Put(-4, y, 3, "liquid_pipe"); // intake pipe down into the water
        for (int y = 1; y <= 4; y++) Put(-4, y, 3, "liquid_pipe");
        for (int x = -3; x <= 0; x++) Put(x, 4, 3, "liquid_pipe");
        Put(4, 7, 4, "compressor"); E(4, 7, 4).SetNum("mode", 0); // 10 bar
        int coal = GameAssets.ItemReg.Id("coal");
        for (int x = -8; x <= -5; x++) { Put(x, 0, -2, "coal_generator", "north"); E(x, 0, -2).Inv("fuel")[0] = new ItemStack(coal, 64); }
        for (int x = -8; x <= 8; x++) Put(x, 0, -1, "gold_cable");
        Put(-4, 0, 0, "gold_cable"); Put(-4, 0, 1, "gold_cable"); Put(-4, 0, 2, "gold_cable"); // to the pump
        Put(8, 0, 0, "gold_cable"); for (int y = 1; y <= 7; y++) Put(8, y, 0, "gold_cable"); for (int z = 1; z <= 4; z++) Put(8, 7, z, "gold_cable"); for (int x = 5; x <= 7; x++) Put(x, 7, 4, "gold_cable"); // to the compressor
        string[] give = { "liquid_tank_wall", "liquid_tank_window", "liquid_tank_valve", "compressor", "high_pressure_pump", "liquid_turbine", "liquid_pipe", "multimeter", "bucket" };
        for (int i = 0; i < give.Length; i++) PlayerInventory.Hotbar[i] = new ItemStack(GameAssets.ItemReg.Id(give[i]), give[i] == "multimeter" || give[i] == "bucket" ? 1 : 64);
        PlayerInventory.Selected = 7; WaterSim.WAct.Clear(); WaterSim.wactQ.Clear();
        double w0 = t?.Water ?? 0, p0 = t?.Pressure ?? 0;
        int ticks = (int)(secs * 10);
        for (int i = 0; i < ticks; i++) { BlockActions.Tick(.1); if ((i & 1) == 1) WaterSim.WaterStep(); }
        var tb = E(0, 1, 4); var pu = E(-4, 0, 3);
        string report = $"TANKDEMO after {secs:0} s: tank {(t == null ? "NOT FORMED" : t.Describe())} (started {w0:0} units at {p0:0.0} bar); " +
                        $"turbine head {tb.Num("head"):0} blocks, {tb.Num("power"):0.0} E/s; pump lift {pu.Num("lift"):0}, {pu.Num("flow"):0.0} u/s, {pu.Num("power"):0.0} E/s{PumpBehavior.Why(pu)}; compressor {E(4, 7, 4).Num("power"):0.0} E/s; " +
                        $"oxygen generator {E(-2, 3, 3).Num("energy"):0} E, batteries {E(-2, 3, 4).Num("energy") + E(-2, 3, 5).Num("energy") + E(-2, 3, 6).Num("energy"):0} E";
        return Finish(folder, name, report, 4, -9, 4, 3, 1, false);
    }
}
