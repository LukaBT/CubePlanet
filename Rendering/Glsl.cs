using System.Text;

namespace CubePlanet;

// GLSL sources extracted verbatim from the original WebGL2 page (weather-only programs removed).
// Compiled for Vulkan by Gpu.Spirv: it prepends #version 450, one std140 uniform block holding every loose uniform below
// (the 'uniform ...;' lines are stripped), push constants (uO, uWt, uK) and the sampler bindings.
// Vulkan clip space: Y points down and depth is reverse-Z (1 = near plane, 0 = infinitely far).
// The sources live in Rendering/Shaders and are embedded into the exe (resource name "shaders/<file>"); they are read once at startup.
public static class Glsl
{
    public static readonly string HEADER = Load("header.glsl"); // prepended to every shader by Gpu.Spirv: #version, the std140 Frame block, push constants, sampler bindings
    public static readonly string WAVE = Load("wave.glsl"); // ocean wave functions
    public static readonly string COMMON_BODY = Load("common.glsl"); // sky, fog, lighting, shadows, clouds, water and glass shading shared by the scene shaders
    public static readonly string RAY = Load("view_ray.glsl"); // view ray for fullscreen passes
    public static readonly string FSV = Load("fullscreen.vert"); // fullscreen triangle vertex shader
    public static readonly string PV_VS = Load("terrain.vert"); // voxel terrain (opaque / water / glass)
    public static readonly string PV_FS = Load("terrain.frag");
    public static readonly string PS_VS = Load("terrain_shadow.vert"); // voxel terrain shadow pass
    public static readonly string PS_FS = Load("terrain_shadow.frag");
    public static readonly string PL_VS = Load("lod.vert"); // LOD terrain
    public static readonly string PL_FS = Load("lod.frag");
    public static readonly string PW_VS = Load("lod_sea.vert"); // LOD sea
    public static readonly string PW_FS = Load("lod_sea.frag");
    public static readonly string PK_FS = Load("sky.frag"); // sky, stars, aurora
    public static readonly string CLOUD_FS = Load("clouds.frag"); // volumetric clouds (half resolution)
    public static readonly string PG_FS = Load("glow.frag"); // atmosphere limb glow
    public static readonly string LS_VS = Load("lod_shadow.vert"); // LOD terrain shadow pass
    public static readonly string LS_FS = Load("lod_shadow.frag");
    public static readonly string POST_COMMON = Load("post_common.glsl"); // post-processing helpers (fullscreen triangle from FSV; uO.xy = source texel size)
    public static readonly string BLOOM_PRE = Load("bloom_pre.frag"); // bloom chain
    public static readonly string BLOOM_DOWN = Load("bloom_down.frag");
    public static readonly string BLOOM_UP = Load("bloom_up.frag");
    public static readonly string COMPOSITE = Load("composite.frag"); // tone map, god rays, SSR, FXAA, upscale
    public static readonly string PHL_VS = Load("highlight.vert"); // block highlight
    public static readonly string PHL_FS = Load("highlight.frag");
    public static readonly string UI_VS = Load("ui.vert"); // 2D UI layer
    public static readonly string UI_FS = Load("ui.frag");

    static string Load(string file)
    {
        using var s = typeof(Glsl).Assembly.GetManifestResourceStream("shaders/" + file) ?? throw new FileNotFoundException("embedded shader missing", file);
        using var r = new StreamReader(s, new UTF8Encoding(false));
        return r.ReadToEnd();
    }
}
