namespace CubePlanet;

/// <summary>
/// The one place that names every asset file. Assets/ holds exactly the files the Python AssetEditor exports and is copied next to the exe
/// (csproj CopyToOutputDirectory; for the single-file publish ship the Assets folder next to CubePlanet.exe).
/// </summary>
public static class AssetPaths
{
    public const string BlockAtlas = "block_texture_atlas.png";
    public const string ItemAtlas = "item_texture_atlas.png";
    public const string BlockTextureDefinitions = "block_texture_definitions.txt";
    public const string ItemTextureDefinitions = "item_texture_definitions.txt";
    public const string BlockDefinitions = "block_definitions.txt";
    public const string ItemDefinitions = "item_definitions.txt";
    public const string UiDefinitions = "ui_definitions.txt";
    // files beyond the editor's 7 (the editor does not write them yet; edit them by hand)
    public const string RecipeDefinitions = "recipe_definitions.txt";
    public const string SoundDefinitions = "sound_definitions.txt";
    public const string EntityDefinitions = "entity_definitions.txt";

    /// <summary>Assets folder: next to the exe; when running from the source tree (dotnet run) the project's Assets folder is found too.</summary>
    public static string Directory = Find();

    public static string Get(string file) => Path.Combine(Directory, file);

    static string Find()
    {
        string exe = Path.Combine(AppContext.BaseDirectory, "Assets");
        if (System.IO.Directory.Exists(exe)) return exe;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "CubePlanet.csproj")) && System.IO.Directory.Exists(Path.Combine(d.FullName, "Assets"))) return Path.Combine(d.FullName, "Assets");
        return exe;
    }
}
