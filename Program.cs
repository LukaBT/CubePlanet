namespace CubePlanet;

public static class Program
{
    public static int Msaa = -1; // -1 = use the settings value
    public static readonly List<string> Overrides = new(); public static bool SyncMesh; public static double Hour = -1, Yaw, Pitch, Up, TurnAt = -1; // bench camera: --hour 18 --yaw 90 --pitch -20 // --syncmesh: mesh on the main thread (debug)
    public static bool Showcase; // --showcase: bench places example blocks in front of the camera
    public static bool NoVsync; public static double Bench; public static string Shot; // --bench N: start right away, quit after N seconds and print FPS (--shot file.png saves the last frame)
    public static void Main(string[] args)
    {
        // everything (generation, meshing, UI) uses the block tables, and static tables (hotbar, palettes, ore lists) are built from the named block IDs, so the asset files are loaded before anything else
        try { GameAssets.Load(); }
        catch (Exception e) when (e is IOException || e is InvalidDataException || e is KeyNotFoundException)
        {
            Console.Error.WriteLine("Could not load the game assets from " + AssetPaths.Directory + ":\n" + e.Message);
            Environment.ExitCode = 1; return;
        }
        // optional: CubePlanet --radius 1200 --seed 1337 --msaa 0|2|4|8 --novsync
        foreach (var a in args) { if (a == "--novsync") NoVsync = true; if (a == "--syncmesh") SyncMesh = true; if (a == "--showcase") Showcase = true; if (a == "--showsettings") UI.settingsOpen = true; }
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--radius") UI.radiusStr = args[i + 1];
            if (args[i] == "--seed") UI.seedStr = args[i + 1];
            if (args[i] == "--msaa" && int.TryParse(args[i + 1], out int ms)) Msaa = Math.Max(0, ms);
            if (args[i] == "--shot") Shot = args[i + 1];
            if (args[i] == "--hour") double.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out Hour);
            if (args[i] == "--yaw") double.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out Yaw);
            if (args[i] == "--up") double.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out Up);
            if (args[i] == "--turnat") double.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out TurnAt); // bench: turn 180 degrees after N seconds, report the worst frame after it
            if (args[i] == "--pitch") double.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out Pitch);
            if (args[i] == "--set") Overrides.Add(args[i + 1]); // e.g. --set ao=0 (for this run only, not saved)
            if (args[i] == "--bench" && double.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out double bs)) Bench = bs;
        }
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
