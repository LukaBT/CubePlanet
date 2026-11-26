namespace CubePlanet;

public static partial class DemoWorld
{
    // ---------- --demomill NAME [--demosecs S]: a windmill driving a millstone, a sawmill and a wind pump through gearboxes ----------
    // A large wind turbine on a tower (hub 14 up, facing north), its shaft into a gearbox, a vertical shaft down to a gearbox on the ground:
    // a millstone (east, 64 iron ore) and a sawmill (west, 64 oak logs), and a shaft south to a mechanical pump lifting water from a pool
    // into a glass tank above it. No electricity at all. The hotbar has a multimeter (hold it and look at a shaft).
    public static int RunMill(string name, double secs)
    {
        string folder = WorldSave.FolderFor(name);
        if (Directory.Exists(folder)) { Console.WriteLine($"DEMOWORLD a world folder '{folder}' already exists: not touching it"); return 1; }
        World.InitWorldData(1200, 1337); ChunkGenerator.mainTid = Environment.CurrentManagedThreadId;
        if (!FindSite()) { Console.WriteLine("DEMOWORLD no dry building site near spawn"); return 1; }
        int stone = Id("stone_bricks"), glass = Id("glass");
        for (int y = -5; y <= 30; y++) for (int z = -16; z <= 16; z++) for (int x = -12; x <= 20; x++) Cell(x, y, z, y <= -1 ? stone : 0);
        for (int y = 0; y <= 13; y++) Cell(0, y, 0, stone);
        Put(0, 14, 0, "wind_turbine_large", "north"); Put(0, 14, 1, "shaft", "north"); Put(0, 14, 2, "shaft", "north"); Put(0, 14, 3, "gearbox");
        for (int y = 1; y <= 13; y++) Put(0, y, 3, "shaft", "up");
        Put(0, 0, 3, "gearbox"); Put(1, 0, 3, "millstone"); Put(-1, 0, 3, "sawmill");
        E(1, 0, 3).Inv("input")[0] = new ItemStack(GameAssets.ItemReg.Id("iron_ore"), 64); E(-1, 0, 3).Inv("input")[0] = new ItemStack(GameAssets.ItemReg.Id("oak_log"), 64);
        // wind pump: a pool dug into the ground, the pump on it facing up into a glass tank
        for (int z = 5; z <= 10; z++) for (int x = -3; x <= 3; x++) for (int y = -3; y <= -1; y++) Cell(x, y, z, Water, 8);
        Put(0, -1, 6, "liquid_pipe"); Put(0, -2, 6, "liquid_pipe"); // the intake pipe down to the bottom of the pool
                Put(0, 0, 4, "shaft", "south"); Put(0, 0, 5, "shaft", "south"); Put(0, 0, 6, "mechanical_pump", "up");
        // a pipe up from the pump into a raised glass tank (floor y 3, inside y 4..7)
        for (int y = 1; y <= 3; y++) Put(0, y, 6, "liquid_pipe");
        for (int z = 4; z <= 8; z++) for (int x = -2; x <= 2; x++) for (int y = 3; y <= 8; y++) if ((x == -2 || x == 2 || z == 4 || z == 8 || y == 3) && !(x == 0 && z == 6 && y == 3)) Cell(x, y, z, glass);
        string[] give = { "multimeter", "wind_turbine_large", "shaft", "gearbox", "millstone", "sawmill", "mechanical_pump", "iron_ore", "oak_log" };
        for (int i = 0; i < give.Length; i++) PlayerInventory.Hotbar[i] = new ItemStack(GameAssets.ItemReg.Id(give[i]), give[i] == "multimeter" ? 1 : 64);
        PlayerInventory.Selected = 0;
        Kinetics.Clock = 300; WaterSim.WAct.Clear(); WaterSim.wactQ.Clear();
        int ticks = (int)(secs * 10);
        for (int i = 0; i < ticks; i++) { BlockActions.Tick(.1); if ((i & 1) == 1) WaterSim.WaterStep(); }
        int tank = 0; for (int y = 4; y <= 8; y++) for (int z = 5; z <= 7; z++) for (int x = -1; x <= 1; x++) tank += Wl(Wcell(bi, f, x0 + x, y0 + y, z0 + z));
        var t = E(0, 14, 0);
        string report = $"MILLDEMO after {secs:0} s (wind {Kinetics.WindSpeed:0.0} m/s from {Kinetics.WindFrom}): turbine {t.Num("power"):0.0} E/s {Kinetics.Rpm(t):0} rpm; " +
                        $"millstone {E(1, 0, 3).Num("input_power"):0.0} E/s -> {E(1, 0, 3).Inv("output")[0]?.Count ?? 0} iron dust; sawmill {E(-1, 0, 3).Num("input_power"):0.0} E/s -> {E(-1, 0, 3).Inv("output")[0]?.Count ?? 0} planks; " +
                        $"pump {E(0, 0, 6).Num("power"):0.0} E/s, {tank} units in the tank";
        return Finish(folder, name, report, 8, -10, 0, 6, 2, false);
    }
}
