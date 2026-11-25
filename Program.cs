namespace CubePlanet;

public static class Program
{
    public static int Msaa = -1; // -1 = use the settings value
    public static readonly List<string> Overrides = new(); public static bool SyncMesh; public static double Hour = -1, Yaw, Pitch, Up, TurnAt = -1; // bench camera: --hour 18 --yaw 90 --pitch -20 // --syncmesh: mesh on the main thread (debug)
    public static bool Showcase; // --showcase: bench places example blocks in front of the camera
    public static bool BenchSave; // --benchsave: a bench run with --world saves the world when it ends
    public static string WorldArg; // --world NAME: play that saved world right away (created with --radius / --seed if missing)
    public static string StartShot; // --startshot file.png: screenshot of the start screen, then quit
    public static string ShowUi; // --showui inventory|chest|furnace|oxygen_generator (with --showcase)
    public static bool NoVsync; public static double Bench, FlySpeed; public static string Shot; // --bench N: start right away, quit after N seconds and print FPS (--shot file.png saves the last frame)
    public static void Main(string[] args)
    {
        // everything (generation, meshing, UI) uses the block tables, and static tables (hotbar, palettes, ore lists) are built from the named block IDs, so the asset files are loaded before anything else
        int ai = Array.IndexOf(args, "--assets"); if (ai >= 0 && ai + 1 < args.Length) AssetPaths.Directory = Path.GetFullPath(args[ai + 1]); // --assets DIR: load another asset folder (e.g. an editor export)
        try { GameAssets.Load(); }
        catch (Exception e) when (e is IOException || e is InvalidDataException || e is KeyNotFoundException)
        {
            Console.Error.WriteLine("Could not load the game assets from " + AssetPaths.Directory + ":\n" + e.Message);
            Environment.ExitCode = 1; return;
        }
        // optional: CubePlanet --radius 1200 --seed 1337 --msaa 0|2|4|8 --novsync
        foreach (var a in args) { if (a == "--novsync") NoVsync = true; if (a == "--syncmesh") SyncMesh = true; if (a == "--showcase") Showcase = true; if (a == "--benchsave") BenchSave = true; if (a == "--showsettings") UI.settingsOpen = true; }
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--radius") UI.radiusStr = args[i + 1];
            if (args[i] == "--seed") UI.seedStr = args[i + 1];
            if (args[i] == "--msaa" && int.TryParse(args[i + 1], out int ms)) Msaa = Math.Max(0, ms);
            if (args[i] == "--shot") Shot = args[i + 1];
            if (args[i] == "--showui") ShowUi = args[i + 1];
            if (args[i] == "--world") WorldArg = args[i + 1];
            if (args[i] == "--startshot") StartShot = args[i + 1];
            if (args[i] == "--hour") double.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out Hour);
            if (args[i] == "--yaw") double.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out Yaw);
            if (args[i] == "--up") double.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out Up);
            if (args[i] == "--turnat") double.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out TurnAt); // bench: turn 180 degrees after N seconds, report the worst frame after it
            if (args[i] == "--pitch") double.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out Pitch);
            if (args[i] == "--set") Overrides.Add(args[i + 1]); // e.g. --set ao=0 (for this run only, not saved)
            if (args[i] == "--bench" && double.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out double bs)) Bench = bs;
            if (args[i] == "--flyspeed" && double.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out double fsp)) FlySpeed = fsp;
        }
        { int ia = Array.IndexOf(args, "--iconsheet"); if (ia >= 0 && ia + 1 < args.Length) { ItemIcons.WriteSheet(args[ia + 1]); return; } } // all item icons in one PNG (no window)
        { int ia = Array.IndexOf(args, "--demofarm"); if (ia >= 0 && ia + 1 < args.Length) { int js = Array.IndexOf(args, "--demosecs"), jr = Array.IndexOf(args, "--demoroot"); if (jr >= 0 && jr + 1 < args.Length) WorldSave.Root = args[jr + 1]; Environment.ExitCode = DemoWorld.RunFarm(args[ia + 1], js >= 0 && js + 1 < args.Length ? double.Parse(args[js + 1], System.Globalization.CultureInfo.InvariantCulture) : 120); return; } } // builds a saved world with three farms (no window)
        { int ia = Array.IndexOf(args, "--demogrid"); if (ia >= 0 && ia + 1 < args.Length) { int js = Array.IndexOf(args, "--demosecs"), jr = Array.IndexOf(args, "--demoroot"); if (jr >= 0 && jr + 1 < args.Length) WorldSave.Root = args[jr + 1]; Environment.ExitCode = DemoWorld.RunGrid(args[ia + 1], js >= 0 && js + 1 < args.Length ? double.Parse(args[js + 1], System.Globalization.CultureInfo.InvariantCulture) : 60); return; } } // builds a saved world with a power line from a wind farm to a town (no window)
        { int ia = Array.IndexOf(args, "--demowind"); if (ia >= 0 && ia + 1 < args.Length) { int js = Array.IndexOf(args, "--demosecs"), jr = Array.IndexOf(args, "--demoroot"); if (jr >= 0 && jr + 1 < args.Length) WorldSave.Root = args[jr + 1]; Environment.ExitCode = DemoWorld.RunWind(args[ia + 1], js >= 0 && js + 1 < args.Length ? double.Parse(args[js + 1], System.Globalization.CultureInfo.InvariantCulture) : 60); return; } } // builds a saved world with a wind farm and a coal plant (no window)
        { int ia = Array.IndexOf(args, "--demoworld"); if (ia >= 0 && ia + 1 < args.Length) { int js = Array.IndexOf(args, "--demosecs"), jr = Array.IndexOf(args, "--demoroot"); if (jr >= 0 && jr + 1 < args.Length) WorldSave.Root = args[jr + 1]; Environment.ExitCode = DemoWorld.Run(args[ia + 1], js >= 0 && js + 1 < args.Length ? double.Parse(args[js + 1], System.Globalization.CultureInfo.InvariantCulture) : 200); return; } } // builds a saved world with hydro plants (no window)
        if (Array.IndexOf(args, "--hydrostats") >= 0) { var sw = System.Diagnostics.Stopwatch.StartNew(); World.InitWorldData(double.TryParse(UI.radiusStr, out var hr) ? hr : 1200, int.TryParse(UI.seedStr, out var hs) && hs != 0 ? hs : 1337); Console.WriteLine($"HYDRO {Hydrology.Stats()}, world init {sw.ElapsedMilliseconds} ms"); int hp = Array.IndexOf(args, "--hydropng"); if (hp >= 0 && hp + 1 < args.Length) Hydrology.WritePng(args[hp + 1]); return; }
        if (Array.IndexOf(args, "--benchfly") >= 0) { FlyBench.Run(); return; } // headless main-thread cost of flying fast
        if (Array.IndexOf(args, "--benchgen") >= 0) { GenBench.Run(); return; } // headless cost of block lookups in ungenerated chunks
        if (Array.IndexOf(args, "--benchwater") >= 0) { WaterBench.Run(); return; } // headless cave water + water simulation cost
        if (Array.IndexOf(args, "--benchedit") >= 0) { EditBench.Run(); return; } // headless block-edit cost
        if (Array.IndexOf(args, "--selftest") >= 0) { Environment.ExitCode = SelfTest.Run(); return; } // headless rule checks
        if (Array.IndexOf(args, "--fingerprint") >= 0)
        {   // headless world fingerprint (no window): --fingerprint [--radius N] [--seed N] [--fprd CHUNKS]
            int fprd = 12; int fi = Array.IndexOf(args, "--fprd"); if (fi >= 0 && fi + 1 < args.Length) int.TryParse(args[fi + 1], out fprd);
            Fingerprint.Run(double.TryParse(UI.radiusStr, out var fr) ? fr : 1200, int.TryParse(UI.seedStr, out var fs) && fs != 0 ? fs : 1337, fprd);
            return;
        }
        using var game = new Game();
        game.Run();
    }
}
