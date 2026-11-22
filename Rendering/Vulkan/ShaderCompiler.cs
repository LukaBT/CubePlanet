using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Silk.NET.Core.Native;
using Silk.NET.Shaderc;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Buffer = Silk.NET.Vulkan.Buffer;
using Image = Silk.NET.Vulkan.Image;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace CubePlanet;

/// <summary>GLSL -> SPIR-V compilation with shaderc, cached on disk by source hash.</summary>
public static unsafe partial class Gpu
{
    // ======================================================================= shaders / pipelines
    // All loose GL-style uniforms live in one std140 block (written once per frame); per-draw values are push constants.

    static string HEADER => Glsl.HEADER;
    static readonly Regex LooseUniform = new(@"uniform\s+(?!\w+\s*\{)[^;{]*;", RegexOptions.Compiled);
    static Shaderc sc; static Compiler* scComp; static CompileOptions* scOpt;

    static byte[] Spirv(string src, ShaderKind kind, string name)
    {
        string body = LooseUniform.Replace(src, "").Replace("gl_VertexID", "gl_VertexIndex");
        if (kind == ShaderKind.FragmentShader) body = body.Replace("out vec4 o;", "layout(location=0) out vec4 o;");
        string full = HEADER + body;
        string hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(full + (int)kind)));
        string path = Path.Combine(CacheDir, hash + ".spv");
        try { if (File.Exists(path)) return File.ReadAllBytes(path); } catch { }
        if (sc == null)
        {
            sc = Shaderc.GetApi(); scComp = sc.CompilerInitialize(); scOpt = sc.CompileOptionsInitialize();
            sc.CompileOptionsSetOptimizationLevel(scOpt, OptimizationLevel.Performance);
            sc.CompileOptionsSetAutoMapLocations(scOpt, true);
            sc.CompileOptionsSetTargetEnv(scOpt, TargetEnv.Vulkan, (uint)EnvVersion.Vulkan10);
        }
        var bytes = Encoding.UTF8.GetBytes(full);
        CompilationResult* res;
        fixed (byte* p = bytes) res = sc.CompileIntoSpv(scComp, p, (nuint)bytes.Length, kind, name, "main", scOpt);
        if (sc.ResultGetCompilationStatus(res) != CompilationStatus.Success)
        {
            string err = SilkMarshal.PtrToString((nint)sc.ResultGetErrorMessage(res)); sc.ResultRelease(res);
            throw new Exception($"shader {name} compile error:\n{err}");
        }
        var outb = new byte[(int)sc.ResultGetLength(res)]; Marshal.Copy((nint)sc.ResultGetBytes(res), outb, 0, outb.Length); sc.ResultRelease(res);
        try { File.WriteAllBytes(path, outb); } catch { }
        return outb;
    }
    static ShaderModule Module(byte[] code)
    {
        fixed (byte* p = code)
        {
            var ci = new ShaderModuleCreateInfo { SType = StructureType.ShaderModuleCreateInfo, CodeSize = (nuint)code.Length, PCode = (uint*)p };
            Check(vk.CreateShaderModule(dev, &ci, null, out var m), "shader module"); return m;
        }
    }
}
