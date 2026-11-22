namespace CubePlanet;

/// <summary>All player-adjustable settings (saved to %APPDATA%/CubePlanet/settings.json).</summary>
public class Settings
{
    public static Settings SET = new(); // the active settings

    public double rd = 6, lod = 1, lodOn = 1, fov = 60, sens = 1, fly = 20, shadows = 3, clouds = 2, fog = 1, shInt = .5, day = 600, pause = 0, vsync = 1;
    public double wvA = 1, wvSpd = 1, wvLen = 1, wvK = 1.8, wvDir = 0, wvRip = 1, wvFoam = 1, wvOp = 1, tilt = 23.5;
    // graphics features (each can be switched off for slower PCs); msaa applies on the next start
    public double ao = 1, aniso = 1, bloom = 1, godrays = 1, fxaa = 1, ssr = 1, msaa = 4;
    public double ver; // settings file version (old files: 0)
    public double rscale = 1; // render resolution (fraction of the window); the UI always stays at full resolution

    public static readonly string[] PresetNames = { "LOW", "MEDIUM", "HIGH", "ULTRA" };
    //                                   shadows  ao  aniso bloom rays  ssr  fxaa msaa clouds
    static readonly double[][] PRESETS = { new double[] { 0, 0, 0, 0, 0, 0, 0, 0, 1 },
                                           new double[] { 1, 1, 1, 0, 0, 0, 1, 0, 1 },
                                           new double[] { 2, 1, 1, 1, 1, 1, 1, 0, 2 },
                                           new double[] { 3, 1, 1, 1, 1, 1, 1, 4, 3 } };
    double[] Vals() => new[] { shadows, ao, aniso, bloom, godrays, ssr, fxaa, msaa, clouds };
    public void ApplyPreset(int p) { var v = PRESETS[p]; shadows = v[0]; ao = v[1]; aniso = v[2]; bloom = v[3]; godrays = v[4]; ssr = v[5]; fxaa = v[6]; msaa = v[7]; clouds = v[8]; }
    /// <summary>Index of the preset the current values match, or -1 (custom).</summary>
    public int Preset() { var c = Vals(); for (int p = 0; p < PRESETS.Length; p++) if (PRESETS[p].SequenceEqual(c)) return p; return -1; }
}
