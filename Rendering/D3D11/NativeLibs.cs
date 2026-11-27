using System.Runtime.InteropServices;

namespace CubePlanet;

/// <summary>
/// The single-file .exe unpacks its native libraries (shaderc, SPIRV-Cross) into a temp folder, but Silk.NET only looks next to the .exe:
/// load them from the unpack folder first, so later loads by name find them (a fresh PC has no shader cache and needs both).
/// </summary>
public static class NativeLibs
{
    public static void Preload(params string[] names)
    {
        var dirs = ((AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string) ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Append(AppContext.BaseDirectory);
        foreach (var n in names)
            foreach (var d in dirs)
            {
                string p = Path.Combine(d, n);
                if (File.Exists(p) && NativeLibrary.TryLoad(p, out _)) break;
            }
    }
}
