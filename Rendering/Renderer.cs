using System.Diagnostics;
using Silk.NET.Vulkan;

namespace CubePlanet;

/// <summary>Creates the pipelines and records the 3D part of each frame.</summary>
public static class Renderer
{
    public static int VW = 1280, VH = 720;
    internal static Pipe pOpaque, pWater, pGlass, pShadow, pLod, pSea, pSky, pGlow, pHlTri, pHlLine;
    internal static GAlloc hlIdx, lodIdx; internal static uint lodN;
    internal const int SMS = Gpu.SMS; internal const double SHE = 112;
    public static double AT = 300, RC0, RC1, HA, lodDelta;
    public static bool LODON = true; public static double LODK = 1;
    internal static bool shOn; internal static Pipe pLodShadow; internal static double lastAO = -1, lastSh = -1;
    internal static readonly float[] UB = new float[Gpu.UBO_FLOATS];
    internal static readonly Stopwatch clock = Stopwatch.StartNew();
    public static double NowMs => clock.Elapsed.TotalMilliseconds;

    /// <summary>Creates every pipeline, the block texture array, the cloud/water noise volume and the shared index buffers.</summary>
    public static void InitGpu()
    {
        string WV = Glsl.WAVE, CB = Glsl.WAVE + Glsl.COMMON_BODY;
        var back = CullModeFlags.BackBit;
        pOpaque = Gpu.MakePipe("terrain", WV + Glsl.PV_VS, CB + Glsl.PV_FS, new PipeCfg { layout = VL.Chunk, cull = back, writeDist = true });
        pWater = Gpu.MakePipe("water", WV + Glsl.PV_VS, CB + Glsl.PV_FS, new PipeCfg { layout = VL.Chunk, blend = BlendMode.Alpha });
        pGlass = Gpu.MakePipe("glass", WV + Glsl.PV_VS, CB + Glsl.PV_FS, new PipeCfg { layout = VL.Chunk, cull = back, blend = BlendMode.Alpha, depthWrite = false });
        pShadow = Gpu.MakePipe("shadow", Glsl.PS_VS, Glsl.PS_FS, new PipeCfg { layout = VL.ChunkShadow, cull = CullModeFlags.FrontBit, pass = RP.Shadow, cmp = CompareOp.Less });
        pLodShadow = Gpu.MakePipe("lodshadow", Glsl.LS_VS, Glsl.LS_FS, new PipeCfg { layout = VL.LodShadow, pass = RP.Shadow, cmp = CompareOp.Less, depthBias = true });
        pLod = Gpu.MakePipe("lod", Glsl.PL_VS, CB + Glsl.PL_FS, new PipeCfg { layout = VL.Lod, writeDist = true });
        pSea = Gpu.MakePipe("lodsea", WV + Glsl.PW_VS, CB + Glsl.PW_FS, new PipeCfg { layout = VL.LodSea, blend = BlendMode.Alpha, depthWrite = false });
        pSky = Gpu.MakePipe("sky", Glsl.FSV, CB + Glsl.RAY + Glsl.PK_FS, new PipeCfg { layout = VL.None, cmp = CompareOp.GreaterOrEqual, depthWrite = false, writeDist = true });
        Gpu.pCloud = Gpu.MakePipe("clouds", Glsl.FSV, CB + Glsl.RAY + Glsl.CLOUD_FS, new PipeCfg { layout = VL.None, pass = RP.Cloud, depthTest = false, depthWrite = false });
        pGlow = Gpu.MakePipe("glow", Glsl.FSV, CB + Glsl.RAY + Glsl.PG_FS, new PipeCfg { layout = VL.None, depthTest = false, depthWrite = false, blend = BlendMode.Add });
        pHlTri = Gpu.MakePipe("highlight", Glsl.PHL_VS, Glsl.PHL_FS, new PipeCfg { layout = VL.Pos3, cmp = CompareOp.GreaterOrEqual, depthWrite = false, blend = BlendMode.Alpha });
        var post = new PipeCfg { layout = VL.None, pass = RP.Post, depthTest = false, depthWrite = false };
        Gpu.pBloomPre = Gpu.MakePipe("bloom_pre", Glsl.FSV, Glsl.POST_COMMON + Glsl.BLOOM_PRE, post);
        Gpu.pBloomDown = Gpu.MakePipe("bloom_down", Glsl.FSV, Glsl.POST_COMMON + Glsl.BLOOM_DOWN, post);
        Gpu.pBloomUp = Gpu.MakePipe("bloom_up", Glsl.FSV, Glsl.POST_COMMON + Glsl.BLOOM_UP, new PipeCfg { layout = VL.None, pass = RP.PostAdd, depthTest = false, depthWrite = false, blend = BlendMode.Add });
        Gpu.pComposite = Gpu.MakePipe("composite", Glsl.FSV, Glsl.POST_COMMON + Glsl.COMPOSITE, new PipeCfg { layout = VL.None, pass = RP.Final, depthTest = false, depthWrite = false });
        pHlLine = Gpu.MakePipe("highlight_lines", Glsl.PHL_VS, Glsl.PHL_FS, new PipeCfg { layout = VL.Pos3, topo = PrimitiveTopology.LineList, cmp = CompareOp.GreaterOrEqual, depthWrite = false, blend = BlendMode.Alpha });
        // block texture array from Assets/ (layer = block texture ID) with a CPU-built mip chain (16 -> 1), sampled nearest / mip-linear
        int NL = GameAssets.BlockLayerCount, TW = GameAssets.BlockTexW;
        if (TW != GameAssets.BlockTexH || (TW & (TW - 1)) != 0) throw new InvalidDataException("block textures must be square with a power-of-two size, got " + TW + "x" + GameAssets.BlockTexH);
        var img = GameAssets.BlockLayers; var mips = new List<byte[]> { img }; int sz = TW;
        while (sz > 1)
        {
            int ns = sz / 2; var prev = mips[^1]; var nx = new byte[ns * ns * NL * 4];
            for (int l = 0; l < NL; l++)
                for (int y = 0; y < ns; y++)
                    for (int x = 0; x < ns; x++)
                        for (int c = 0; c < 4; c++)
                        {
                            int b = l * sz * sz * 4, s0 = b + ((2 * y) * sz + 2 * x) * 4 + c, s1 = s0 + sz * 4;
                            nx[l * ns * ns * 4 + (y * ns + x) * 4 + c] = (byte)((prev[s0] + prev[s0 + 4] + prev[s1] + prev[s1 + 4] + 2) >> 2);
                        }
            mips.Add(nx); sz = ns;
        }
        Gpu.texBlocks = Gpu.MakeTexture(ImageType.Type2D, ImageViewType.Type2DArray, Format.R8G8B8A8Unorm, 4, (uint)TW, (uint)TW, 1, (uint)NL, mips.ToArray(), Filter.Nearest, Filter.Nearest, SamplerMipmapMode.Linear, SamplerAddressMode.Repeat);
        // item atlas pictures for the UI (layer = item ID; "texture" icons), no mips: drawn at about their own size. Block items are drawn
        // live from their 3D model with the block textures (ItemIcons, UI.ItemIcon)
        Gpu.texItems = Gpu.MakeTexture(ImageType.Type2D, ImageViewType.Type2DArray, Format.R8G8B8A8Unorm, 4, (uint)GameAssets.ItemTexW, (uint)GameAssets.ItemTexH, 1, (uint)GameAssets.ItemLayerCount, new[] { GameAssets.ItemLayers }, Filter.Nearest, Filter.Nearest, SamplerMipmapMode.Nearest, SamplerAddressMode.ClampToEdge);
        // 128^3 cloud noise (cached on disk after the first start)
        Gpu.texCloud = Gpu.MakeTexture(ImageType.Type3D, ImageViewType.Type3D, Format.R8G8B8A8Unorm, 4, CloudNoise.S, CloudNoise.S, CloudNoise.S, 1, new[] { CloudNoise.Get(Gpu.Cache) }, Filter.Linear, Filter.Linear, SamplerMipmapMode.Nearest, SamplerAddressMode.Repeat);
        // 64^3 tiling noise (does not depend on the seed, so it is built once at startup)
        Gpu.texNoise = Gpu.MakeTexture(ImageType.Type3D, ImageViewType.Type3D, Format.R8Unorm, 1, 64, 64, 64, 1, new[] { NoiseVolume.Build() }, Filter.Linear, Filter.Linear, SamplerMipmapMode.Nearest, SamplerAddressMode.Repeat);
        // highlight box indices (36 triangle + 24 line indices)
        ushort[] hi = { 0, 2, 6, 0, 6, 4, 1, 3, 7, 1, 7, 5, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 0, 1, 3, 0, 3, 2, 4, 5, 7, 4, 7, 6, 0, 1, 2, 3, 4, 5, 6, 7, 0, 2, 1, 3, 4, 6, 5, 7, 0, 4, 1, 5, 2, 6, 3, 7 };
        hlIdx = Gpu.Upload(hi, hi.Length, Array.Empty<byte>(), 0);
        // every LOD patch is the same 11x11 grid, so they all share one index buffer
        const int S = 11; var ix = new ushort[(S - 1) * (S - 1) * 6]; int p = 0;
        for (int gi = 0; gi < S - 1; gi++) for (int gj = 0; gj < S - 1; gj++) { int q = gi * S + gj; ix[p++] = (ushort)q; ix[p++] = (ushort)(q + 1); ix[p++] = (ushort)(q + S); ix[p++] = (ushort)(q + 1); ix[p++] = (ushort)(q + S + 1); ix[p++] = (ushort)(q + S); }
        lodIdx = Gpu.Upload(ix, ix.Length, Array.Empty<byte>(), 0); lodN = (uint)ix.Length;
    }

    public static bool AnisoOk => Gpu.AnisoSupported;

    internal static readonly List<Chunk> visList = new();

    /// <summary>Milliseconds the phases of the last frame took (for the bench's slow-frame report): 0 shadow prep, 1 chunk work (mesh uploads,
    /// main-thread meshes), 2 LOD uploads, 3 LOD collect, 4 shadow pass, 5 scene recording, 6 update (Game), 7 UI, 8 end frame (submit / present).</summary>
    public static readonly double[] Phase = new double[11]; // + 9 begin frame (waiting for the GPU), 10 time between frames
    public static readonly string[] PhaseName = { "shadowprep", "chunkwork", "lodupload", "lodcollect", "shadows", "scene", "update", "ui", "endframe", "gpuwait", "between" };
    public static void Prof(int i, ref long t) { long n = System.Diagnostics.Stopwatch.GetTimestamp(); Phase[i] = (n - t) * 1000.0 / System.Diagnostics.Stopwatch.Frequency; t = n; }

    /// <summary>Records the 3D part of the frame: uploads + shadow pass, then the main render pass (the UI is drawn into it afterwards).</summary>
    public static void Render()
    {
        double t0 = NowMs, tn = t0 / 1000, r = Math.Sqrt(C.x * C.x + C.y * C.y + C.z * C.z), alt = Math.Max(r - R, 0), hz = 1 - M.Sm(.15 * AT, AT, alt);
        double fogK = (SET.fog / (R * .5 + 250)) * hz;
        if (lastAO >= 0 && (SET.ao > .5) != (lastAO > .5)) RemeshAll();
        lastAO = SET.ao; Gpu.SetAniso(SET.aniso > .5);
        wvT = (wvT + Math.Min(.1, Math.Max(0, tn - wvLast)) * SET.wvSpd * (1 + .5 * SEA)) % 5000; wvLast = tn;
        long q0 = System.Diagnostics.Stopwatch.GetTimestamp(); ShadowPrep(); Prof(0, ref q0);
        // build the chunks that are still missing nearest-first (the dictionary order is arbitrary once chunks have been freed and re-added);
        // edited chunks (rebuilds) go before everything else, and when a lot is queued we spend a bit more of the frame on it
        ChunkWork(t0); Prof(1, ref q0);
        LodUploads(t0); Prof(2, ref q0);
        CollectLod(); Prof(3, ref q0);
        ShadowCollect();
        FillUniforms(fogK, tn, hz);
        ShadowDraw(); Prof(4, ref q0);

        Gpu.BeginScene();
        // 1) opaque voxel terrain; remember which chunks are visible for the water/glass passes
        Gpu.Bind(pOpaque); visList.Clear();
        foreach (var ch in chunks.Values)
        {
            if (ch.mesh == null && ch.wmesh == null) continue;
            if (!Vis(ch.mx, ch.my, ch.mz, ch.cr)) continue;
            visList.Add(ch);
            if (ch.mesh == null) continue;
            Gpu.Push((float)(ch.mesh.org.X - C.x), (float)(ch.mesh.org.Y - C.y), (float)(ch.mesh.org.Z - C.z), 0);
            DrawM(ch.mesh, (uint)ch.mesh.n);
        }
        // 2) distant terrain
        DrawLod(pLod);
        // 3) sky last, depth-tested at the far plane: pixels already covered by terrain skip the expensive sky shader entirely
        Gpu.Bind(pSky); Gpu.DrawFull();
        // 4) translucent voxel water (writes depth so the LOD sea behind it is rejected), then glass + ice
        Gpu.Bind(pWater);
        foreach (var ch in visList)
        {
            var w = ch.wmesh; if (w == null || w.nw == 0) continue;
            Gpu.Push((float)(w.org.X - C.x), (float)(w.org.Y - C.y), (float)(w.org.Z - C.z), 1); DrawM(w, (uint)w.nw);
        }
        Gpu.Bind(pGlass);
        foreach (var ch in visList)
        {
            var w = ch.wmesh; if (w == null || w.n <= w.nw) continue;
            Gpu.Push((float)(w.org.X - C.x), (float)(w.org.Y - C.y), (float)(w.org.Z - C.z), 2); DrawM(w, (uint)(w.n - w.nw), (uint)w.nw);
        }
        DrawLod(pSea);
        DrawHighlight();
        // atmosphere limb glow from orbit
        if (alt > .3 * AT) { Gpu.Bind(pGlow); Gpu.DrawFull(); }
        // post-processing: bloom chain, then the composite (FXAA, bloom, god rays) into the swapchain; the UI is drawn on top of it
        Gpu.PostProcess(SET.bloom > .5, SET.clouds > 0);
        if (frameNo % 120 == 0)
        {
            lodDrop.Clear();
            foreach (var kv in lodMap) if (kv.Value.t < frameNo - 240) lodDrop.Add(kv.Key);
            foreach (var k in lodDrop) { Gpu.Free(lodMap[k].a); lodMap.Remove(k); }
        }
    }
}
