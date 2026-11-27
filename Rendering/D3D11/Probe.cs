namespace CubePlanet;

/// <summary>--dxshaders (no window): every pipeline's shaders through GLSL -> SPIR-V -> HLSL -> DXBC, as the Direct3D 11 backend does.</summary>
public static class DxProbe
{
    public static (string name, string vs, string fs)[] Pipelines()
    {
        string WV = Glsl.WAVE, CB = Glsl.WAVE + Glsl.COMMON_BODY;
        return new[]
        {
            ("terrain", WV + Glsl.PV_VS, CB + Glsl.PV_FS), ("shadow", Glsl.PS_VS, Glsl.PS_FS), ("lodshadow", Glsl.LS_VS, Glsl.LS_FS), ("lod", Glsl.PL_VS, CB + Glsl.PL_FS),
            ("lodsea", WV + Glsl.PW_VS, CB + Glsl.PW_FS), ("sky", Glsl.FSV, CB + Glsl.RAY + Glsl.PK_FS), ("clouds", Glsl.FSV, CB + Glsl.RAY + Glsl.CLOUD_FS),
            ("glow", Glsl.FSV, CB + Glsl.RAY + Glsl.PG_FS), ("highlight", Glsl.PHL_VS, Glsl.PHL_FS), ("bloom_pre", Glsl.FSV, Glsl.POST_COMMON + Glsl.BLOOM_PRE),
            ("bloom_down", Glsl.FSV, Glsl.POST_COMMON + Glsl.BLOOM_DOWN), ("bloom_up", Glsl.FSV, Glsl.POST_COMMON + Glsl.BLOOM_UP), ("composite", Glsl.FSV, Glsl.POST_COMMON + Glsl.COMPOSITE),
            ("ui", Glsl.UI_VS, Glsl.UI_FS),
        };
    }
    public static int Run()
    {
        int bad = 0; string dir = Path.Combine(Path.GetTempPath(), "cubeplanet_dxcheck"); Directory.CreateDirectory(dir);
        foreach (var (n, v, f) in Pipelines())
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { var (a, b) = Dx.Compile(v, f, n, dir); Console.WriteLine($"DX ok   {n}: vs {a.Length} B, ps {b.Length} B ({sw.ElapsedMilliseconds} ms)"); }
            catch (Exception e) { bad++; Console.WriteLine($"DX FAIL {n}: {e.Message}"); }
        }
        foreach (var x in Directory.GetFiles(dir)) try { File.Delete(x); } catch { }
        Console.WriteLine(bad == 0 ? "DXSHADERS OK" : $"DXSHADERS {bad} FAILED"); return bad == 0 ? 0 : 1;
    }
}
