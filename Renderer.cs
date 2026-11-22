using System.Diagnostics;
using Silk.NET.Vulkan;
using static CubePlanet.Cube;
using static CubePlanet.Blocks;

namespace CubePlanet;

public class Settings
{
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

public class LodMesh { public GAlloc a; public int t; public V3 o; }

public static partial class W
{
    public static Settings SET = new();
    public static int VW = 1280, VH = 720;
    static Pipe pOpaque, pWater, pGlass, pShadow, pLod, pSea, pSky, pGlow, pHlTri, pHlLine;
    static GAlloc hlIdx, lodIdx; static uint lodN;
    const int SMS = Gpu.SMS; const double SHE = 112;
    public static double AT = 300, RC0, RC1, HA, lodDelta;
    public static bool LODON = true; public static double LODK = 1;
    static bool shOn; static Pipe pLodShadow; static double lastAO = -1, lastSh = -1;
    static readonly float[] UB = new float[Gpu.UBO_FLOATS];
    static readonly Stopwatch clock = Stopwatch.StartNew();
    public static double NowMs => clock.Elapsed.TotalMilliseconds;

    // sun orbits in a plane tilted from the planet's equator
    static readonly V3 SA = new(1, 0, 0); static V3 SB = M.Nrm(new V3(0, Math.Sin(.41), -Math.Cos(.41))); public static V3 SUN = new(1, 0, 0); public static double SUNA;
    public static void SetTilt(double v) { double t = v * PI / 180; SB = M.Nrm(new V3(0, Math.Sin(t), -Math.Cos(t))); }
    public static void SunVec() { double c = Math.Cos(SUNA), s = Math.Sin(SUNA); SUN = new V3(SA.X * c + SB.X * s, SA.Y * c + SB.Y * s, SA.Z * c + SB.Z * s); }
    static double NoonAng() { double r = Math.Sqrt(C.x * C.x + C.y * C.y + C.z * C.z); if (r == 0) r = 1; V3 g = new(C.x / r, C.y / r, C.z / r); return Math.Atan2(M.Dot(g, SB), M.Dot(g, SA)); }
    public static double LocalHour() { double h = 12 + ((SUNA - NoonAng()) / (2 * PI)) * 24; return ((h % 24) + 24) % 24; }
    public static void SetHour(double h) { SUNA = NoonAng() + (h - 12) / 24 * 2 * PI; SunVec(); }

    // ambient (non-interactive) atmosphere constants: a fixed clear day with a few clouds and a light breeze (weather system is not part of this port)
    const double CLOUD_COV = .28, OVERCAST = .1, WIND = .15, SEA = .12, WIND_DIR_DEG = 60;
    static double[] cloudDrift = new double[3], windW = new double[3], windD = new double[3]; static V3 windDir = new(1, 0, 0);
    static double wvT, wvLast;

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
        // block texture array with a CPU-built mip chain (16 -> 1), sampled nearest / mip-linear like before
        var img = Tex.BuildAtlas(); var mips = new List<byte[]> { img }; int sz = 16;
        while (sz > 1)
        {
            int ns = sz / 2; var prev = mips[^1]; var nx = new byte[ns * ns * NT * 4];
            for (int l = 0; l < NT; l++)
                for (int y = 0; y < ns; y++)
                    for (int x = 0; x < ns; x++)
                        for (int c = 0; c < 4; c++)
                        {
                            int b = l * sz * sz * 4, s0 = b + ((2 * y) * sz + 2 * x) * 4 + c, s1 = s0 + sz * 4;
                            nx[l * ns * ns * 4 + (y * ns + x) * 4 + c] = (byte)((prev[s0] + prev[s0 + 4] + prev[s1] + prev[s1 + 4] + 2) >> 2);
                        }
            mips.Add(nx); sz = ns;
        }
        Gpu.texBlocks = Gpu.MakeTexture(ImageType.Type2D, ImageViewType.Type2DArray, Format.R8G8B8A8Unorm, 4, 16, 16, 1, NT, mips.ToArray(), Filter.Nearest, Filter.Nearest, SamplerMipmapMode.Linear, SamplerAddressMode.Repeat);
        // 128^3 cloud noise (cached on disk after the first start)
        Gpu.texCloud = Gpu.MakeTexture(ImageType.Type3D, ImageViewType.Type3D, Format.R8G8B8A8Unorm, 4, CloudNoise.S, CloudNoise.S, CloudNoise.S, 1, new[] { CloudNoise.Get(Gpu.Cache) }, Filter.Linear, Filter.Linear, SamplerMipmapMode.Nearest, SamplerAddressMode.Repeat);
        // 64^3 tiling noise (does not depend on the seed, so it is built once at startup)
        Gpu.texNoise = Gpu.MakeTexture(ImageType.Type3D, ImageViewType.Type3D, Format.R8Unorm, 1, 64, 64, 64, 1, new[] { Tex.BuildNoise3D() }, Filter.Linear, Filter.Linear, SamplerMipmapMode.Nearest, SamplerAddressMode.Repeat);
        // highlight box indices (36 triangle + 24 line indices)
        ushort[] hi = { 0, 2, 6, 0, 6, 4, 1, 3, 7, 1, 7, 5, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 0, 1, 3, 0, 3, 2, 4, 5, 7, 4, 7, 6, 0, 1, 2, 3, 4, 5, 6, 7, 0, 2, 1, 3, 4, 6, 5, 7, 0, 4, 1, 5, 2, 6, 3, 7 };
        hlIdx = Gpu.Upload(hi, hi.Length, Array.Empty<byte>(), 0);
        // every LOD patch is the same 11x11 grid, so they all share one index buffer
        const int S = 11; var ix = new ushort[(S - 1) * (S - 1) * 6]; int p = 0;
        for (int gi = 0; gi < S - 1; gi++) for (int gj = 0; gj < S - 1; gj++) { int q = gi * S + gj; ix[p++] = (ushort)q; ix[p++] = (ushort)(q + 1); ix[p++] = (ushort)(q + S); ix[p++] = (ushort)(q + 1); ix[p++] = (ushort)(q + S + 1); ix[p++] = (ushort)(q + S); }
        lodIdx = Gpu.Upload(ix, ix.Length, Array.Empty<byte>(), 0); lodN = (uint)ix.Length;
    }

    /// <summary>Called once the planet radius is known.</summary>
    public static bool AnisoOk => Gpu.AnisoSupported;
    public static void MakeAtmosphereResources()
    {
        double CH = HMAX * 1.05 + 12, CT = Math.Max(90, HMAX * .9); // cloud layer thick enough for towering cumulus
        RC0 = R + CH; RC1 = RC0 + CT; AT = Math.Max(R * .22, CH + CT + 90);
    }

    // ---------- mesh upload ----------
    static Mesh MkMesh(MeshData md) => new Mesh
    {
        a = md.i16 != null ? Gpu.Upload(md.vb, md.vb.Length, md.i16, md.i16.Length) : Gpu.Upload(md.vb, md.vb.Length, md.i32, md.i32.Length),
        vb = (uint)md.vb.Length, n = md.n, i16 = md.i16 != null,
    };
    static void DropMesh(Mesh m) { if (m != null) Gpu.Free(m.a); }
    static void DrawM(Mesh m, uint count, uint first = 0) => Gpu.DrawMesh(m.a, m.vb, count, first, m.i16 ? IndexType.Uint16 : IndexType.Uint32);
    static void Upload(Chunk ch, BuildResult m, List<long> wait, bool clearDirty = true)
    {
        ShadowTouches(ch);
        DropMesh(ch.mesh); DropMesh(ch.wmesh); ch.mesh = ch.wmesh = null; if (clearDirty) ch.dirty = false; ch.built = true; ch.wait = wait; ch.waitStamp = -1; meshVer++;
        if (m == null) return;
        if (m.o != null) { ch.mesh = MkMesh(m.o); ch.mesh.org = m.org; }
        if (m.w != null) { ch.wmesh = MkMesh(m.w); ch.wmesh.org = m.org; ch.wmesh.nw = m.nw; }
    }
    /// <summary>Marks every chunk for a background re-mesh (e.g. after toggling ambient occlusion).</summary>
    public static void RemeshAll() { foreach (var ch in chunks.Values) if (ch.built) { if (ch.core) ch.dirty = true; else ch.relight = true; } }
    public static void FreeChunk(Chunk ch)
    {
        if (ch.mesh != null) ShadowTouches(ch);
        DropMesh(ch.mesh); DropMesh(ch.wmesh); ch.mesh = ch.wmesh = null; chunks.Remove(ch.key); chunkMap.TryRemove(ch.key, out _); if (blkCh == ch) blkCh = null; chunksVer++; meshVer++;
    }
    /// <summary>Main-thread mesh (edited chunks, core chunks, failed background jobs).</summary>
    static void BuildSync(Chunk ch)
    {
        if (ch.built) profRebuild++;
        long tp = Stopwatch.GetTimestamp(); var br = Build(ch, out var wl); profBuildTicks += Stopwatch.GetTimestamp() - tp; profBuildN++;
        ch.mfail = false; ch.relight = false; Upload(ch, br, wl);
    }
    /// <summary>True when every existing same-band neighbour (3x3x3) already has block data, so lighting won't need a second pass.</summary>
    static bool NeighborsReady(Chunk ch)
    {
        if (ch.core) return true;
        Band B = bands[ch.band]; int nrow = B.rows / CS;
        for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0 && dz == 0) continue;
                    int x = ch.cx + dx, y = ch.cy + dy, z = ch.cz + dz;
                    if (x < 0 || z < 0 || x >= B.fc || z >= B.fc || y < 0 || y >= nrow) continue;
                    if (chunks.TryGetValue(ck(ch.band, ch.face, x, y, z), out var n) && Volatile.Read(ref n.d) == null && !n.gfail) return false;
                }
        return true;
    }
    static readonly List<(double d, Chunk c)> meshQ = new();
    static int meshPubFrame;
    /// <summary>
    /// Chunk pipeline step: apply finished background meshes, hand new mesh jobs to the workers (nearest first, chunks in view first,
    /// only once their neighbours are generated), and mesh edited chunks right here so block edits show up the same frame.
    /// </summary>
    static void ChunkWork(double t0)
    {
        while (NowMs - t0 < 4 && meshDone.TryDequeue(out var r))
        {
            var ch = r.ch; Volatile.Write(ref ch.mst, 0);
            if (!chunks.TryGetValue(ch.key, out var cur) || cur != ch) continue; // freed meanwhile
            if (r.failed) { ch.mfail = true; continue; }
            ch.relight = false; Upload(ch, r.r, r.wait, false);
        }
        PublishGen();
        buildQ.Clear(); meshQ.Clear();
        foreach (var ch in chunks.Values)
        {
            if (Volatile.Read(ref ch.mst) != 0) continue;
            if (!ch.core && ch.d == null && !ch.gfail) continue; // still being generated by a worker thread
            // re-light when a chunk it was waiting for has appeared (only re-check after the chunk set actually changed)
            if (ch.built && !ch.dirty && !ch.relight && ch.wait != null && ch.wait.Count > 0 && ch.waitStamp != chunksVer)
            {
                ch.waitStamp = chunksVer;
                foreach (var k in ch.wait) if (chunks.ContainsKey(k)) { ch.relight = true; break; }
            }
            bool edited = ch.built && ch.dirty;
            if (!edited && ch.built && !ch.relight && !ch.mfail) continue;
            double dx = ch.mx - C.x, dy = ch.my - C.y, dz = ch.mz - C.z, d2 = dx * dx + dy * dy + dz * dz;
            bool vis = Vis(ch.mx, ch.my, ch.mz, ch.cr);
            if (edited || ch.core || ch.gfail || ch.mfail || Program.SyncMesh)
            {
                if (vis || edited) buildQ.Add((edited ? d2 - 1e12 : d2, ch));
                continue;
            }
            if (!ch.built && !NeighborsReady(ch) && ++ch.readyWait < 90) continue;
            if (!ch.built) ch.dirty = false; // the background build reads the current data; an edit during it sets dirty again
            meshQ.Add((vis ? d2 : d2 * 4, ch));
        }
        if (meshQ.Count > 0 && frameNo - meshPubFrame >= 3)
        {
            meshPubFrame = frameNo; meshQ.Sort((a, b) => a.d.CompareTo(b.d));
            var arr = new Chunk[meshQ.Count]; for (int i = 0; i < arr.Length; i++) arr[i] = meshQ[i].c;
            SetMeshList(arr);
        }
        if (buildQ.Count > 0)
        {
            if (buildQ.Count > 1) buildQ.Sort((a, b) => a.d.CompareTo(b.d));
            foreach (var (_, bc) in buildQ) { if (NowMs - t0 >= 6) break; BuildSync(bc); }
        }
    }

    // ---------- LOD: quadtree of curved terrain patches per cube face ----------
    static readonly Dictionary<long, LodMesh> lodMap = new();
    static readonly List<long> lodDrop = new();
    static readonly List<(double d, Chunk c)> buildQ = new();
    static readonly List<(double d, Chunk c)> genTmp = new();
    static long genVer = -1;
    // hand the chunks that still need block data to the worker threads, nearest first (chunks outside the view count as twice as far)
    static void PublishGen()
    {
        if (genVer == chunksVer && frameNo % 8 != 0) return;
        genVer = chunksVer; genTmp.Clear();
        foreach (var ch in chunks.Values)
        {
            if (ch.core || ch.d != null || ch.gst != 0) continue;
            double dx = ch.mx - C.x, dy = ch.my - C.y, dz = ch.mz - C.z, d2 = dx * dx + dy * dy + dz * dz;
            if (!Vis(ch.mx, ch.my, ch.mz, ch.cr)) d2 *= 4;
            genTmp.Add((d2, ch));
        }
        if (genTmp.Count == 0) return;
        genTmp.Sort((a, b) => a.d.CompareTo(b.d));
        var arr = new Chunk[genTmp.Count];
        for (int i = 0; i < arr.Length; i++) arr[i] = genTmp[i].c;
        SetGenList(arr);
    }
    // LOD patches are built on thread-pool threads (each needs 121 terrain samples, and turning around used to create hundreds of them
    // in one frame); until a patch is ready its parent is drawn instead.
    static readonly HashSet<long> lodPending = new();
    static readonly System.Collections.Concurrent.ConcurrentQueue<(long k, float[] vd, V3 o)> lodDone = new();
    const int LOD_INFLIGHT = 48;
    static long LodKey(int f, int lv, int i, int j) => (((long)f * 16 + lv) * 4096 + i) * 4096 + j;
    static LodMesh TryLod(int f, int lv, int i, int j)
    {
        if (lodMap.TryGetValue(LodKey(f, lv, i, j), out var m)) { m.t = frameNo; return m; }
        return null;
    }
    static void RequestLod(int f, int lv, int i, int j)
    {
        long k = LodKey(f, lv, i, j);
        if (lodPending.Count >= LOD_INFLIGHT || !lodPending.Add(k)) return;
        ThreadPool.UnsafeQueueUserWorkItem(_ => { var (vd, o) = BuildLodData(f, lv, i, j); lodDone.Enqueue((k, vd, o)); }, null);
    }
    /// <summary>Uploads finished LOD patches (before any render pass of the frame).</summary>
    static void LodUploads(double t0)
    {
        while (NowMs - t0 < 12 && lodDone.TryDequeue(out var d))
        {
            lodPending.Remove(d.k);
            if (lodMap.ContainsKey(d.k)) continue;
            lodMap[d.k] = new LodMesh { a = Gpu.Upload(d.vd, d.vd.Length, Array.Empty<byte>(), 0), o = d.o, t = frameNo };
        }
    }
    static (float[] vd, V3 o) BuildLodData(int f, int lv, int i, int j)
    {
        double a = PI / 2 / (1 << lv), u0 = -PI / 4 + i * a, v0 = -PI / 4 + j * a; const int G = 8, S = G + 3;
        double sk = a * R * .12 + 1, lw = a * R / G * 1.2, seg = a * R / G; int seaSk = (int)Math.Ceiling(seg * seg / (8 * R) * 2 + 2);
        V3 o = GpA(f, u0 + a / 2, v0 + a / 2, R);
        var vd = new float[S * S * 12]; var pp = new double[S * S * 3]; int p = 0;
        for (int gi = 0; gi < S; gi++)
            for (int gj = 0; gj < S; gj++)
            {
                int ci = Math.Min(Math.Max(gi - 1, 0), G), cj = Math.Min(Math.Max(gj - 1, 0), G); bool edge = gi == 0 || gj == 0 || gi == S - 1 || gj == S - 1;
                double au = u0 + ci * a / G, av = v0 + cj * a / G; V3 t = GpA(f, au, av, 1);
                Terrain(t.X, t.Y, t.Z, lw);
                double rd = R + gH - 2.2 - (edge ? sk : 0); var c = Tex.TileColor(gT);
                double kk = (gB == 6 || gB == 14 || gB == 26 || gB == 28) ? .8 : (gB == 2 || gB == 3 || gB == 4 || gB == 10 || gB == 19) ? .86 : 1;
                double x = t.X * rd - o.X, y = t.Y * rd - o.Y, z = t.Z * rd - o.Z; int q = (gi * S + gj) * 3;
                pp[q] = x; pp[q + 1] = y; pp[q + 2] = z;
                vd[p++] = (float)x; vd[p++] = (float)y; vd[p++] = (float)z; vd[p++] = (float)au; vd[p++] = (float)av;
                // the ice flag also carries the sea skirt depth for edge vertices (ice + 4*depth): the LOD sea is projected onto the sea sphere,
                // so without this its skirts collapse and patches of different detail leave cracks
                vd[p++] = (float)(c[0] / 255.0 * kk); vd[p++] = (float)(c[1] / 255.0 * kk); vd[p++] = (float)(c[2] / 255.0 * kk); vd[p++] = gI + (edge ? 4 * seaSk : 0); p += 3;
            }
        // smooth per-vertex normals from the height grid (one-sided at the patch border); skirt vertices copy their neighbour
        for (int gi = 1; gi < S - 1; gi++)
            for (int gj = 1; gj < S - 1; gj++)
            {
                int a0 = Math.Max(gi - 1, 1), a1 = Math.Min(gi + 1, S - 2), b0 = Math.Max(gj - 1, 1), b1 = Math.Min(gj + 1, S - 2);
                int pA = (a1 * S + gj) * 3, pB = (a0 * S + gj) * 3, pC = (gi * S + b1) * 3, pD = (gi * S + b0) * 3;
                double ux = pp[pA] - pp[pB], uy = pp[pA + 1] - pp[pB + 1], uz = pp[pA + 2] - pp[pB + 2], vx = pp[pC] - pp[pD], vy = pp[pC + 1] - pp[pD + 1], vz = pp[pC + 2] - pp[pD + 2];
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx; double l = Math.Sqrt(nx * nx + ny * ny + nz * nz); if (l == 0) l = 1; nx /= l; ny /= l; nz /= l;
                int q = (gi * S + gj) * 3;
                if (nx * (pp[q] + o.X) + ny * (pp[q + 1] + o.Y) + nz * (pp[q + 2] + o.Z) < 0) { nx = -nx; ny = -ny; nz = -nz; }
                int w = (gi * S + gj) * 12 + 9; vd[w] = (float)nx; vd[w + 1] = (float)ny; vd[w + 2] = (float)nz;
            }
        for (int gi = 0; gi < S; gi++)
            for (int gj = 0; gj < S; gj++)
            {
                if (gi > 0 && gi < S - 1 && gj > 0 && gj < S - 1) continue;
                int s = (Math.Min(Math.Max(gi, 1), S - 2) * S + Math.Min(Math.Max(gj, 1), S - 2)) * 12 + 9, w = (gi * S + gj) * 12 + 9;
                vd[w] = vd[s]; vd[w + 1] = vd[s + 1]; vd[w + 2] = vd[s + 2];
            }
        return (vd, o);
    }
    public static int lodDrawn;
    static readonly List<(LodMesh m, float x, float y, float z)> lodQ = new();
    // walk the quadtree and collect the visible patches; missing ones are requested from the workers and covered by their parent
    // meanwhile. Returns false when this node could not be covered at all (so the caller falls back to its own patch).
    static bool LodWalk(int f, int lv, int i, int j, double hor, V3 cd)
    {
        double a = PI / 2 / (1 << lv); V3 c = GpA(f, -PI / 4 + (i + .5) * a, -PI / 4 + (j + .5) * a, R);
        double cr = R * a * 1.1 + 3, ang = Math.Acos(M.Clamp((c.X * cd.X + c.Y * cd.Y + c.Z * cd.Z) / R, -1, 1));
        if (ang - cr / R > hor + HA || !Vis(c.X, c.Y, c.Z, cr + HMAX)) return true;
        double dist = Math.Max(Math.Sqrt((c.X - C.x) * (c.X - C.x) + (c.Y - C.y) * (c.Y - C.y) + (c.Z - C.z) * (c.Z - C.z)) - cr, 1), edge = R * a * 1.2;
        if (lv < MAXL && (lv < MINL || edge / dist * LODK > .6))
        {
            int mark = lodQ.Count; bool ok = true;
            for (int di = 0; di < 2; di++) for (int dj = 0; dj < 2; dj++) ok &= LodWalk(f, lv + 1, i * 2 + di, j * 2 + dj, hor, cd);
            if (ok) return true;
            var own = TryLod(f, lv, i, j);
            if (own == null) return false;
            lodDrawn -= lodQ.Count - mark; lodQ.RemoveRange(mark, lodQ.Count - mark);
            lodQ.Add((own, (float)(own.o.X - C.x), (float)(own.o.Y - C.y), (float)(own.o.Z - C.z))); lodDrawn++;
            return true;
        }
        var m = TryLod(f, lv, i, j);
        if (m == null) { RequestLod(f, lv, i, j); return false; }
        lodQ.Add((m, (float)(m.o.X - C.x), (float)(m.o.Y - C.y), (float)(m.o.Z - C.z))); lodDrawn++;
        return true;
    }
    static void CollectLod()
    {
        lodDrawn = 0; lodQ.Clear(); if (!LODON) return;
        double r = Math.Sqrt(C.x * C.x + C.y * C.y + C.z * C.z), hor = r > R ? Math.Acos(R / r) : PI; V3 cd = new(C.x / r, C.y / r, C.z / r);
        for (int f = 0; f < 6; f++) for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) LodWalk(f, 2, i, j, hor, cd);
        // keep the coarse level-2 patches (48 of them) around so there is always something to fall back to
        for (int f = 0; f < 6; f++) for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) if (TryLod(f, 2, i, j) == null) RequestLod(f, 2, i, j);
    }
    static void DrawLod(Pipe p)
    {
        if (lodQ.Count == 0) return;
        Gpu.Bind(p);
        foreach (var (m, x, y, z) in lodQ) { Gpu.Push(x, y, z, 0); Gpu.DrawShared(m.a, lodIdx, IndexType.Uint16, lodN); }
    }

    // ---------- per-frame uniforms: one std140 block shared by every pipeline (layout in Gpu.HEADER) ----------
    static void V3U(int i, double x, double y, double z) { UB[i] = (float)x; UB[i + 1] = (float)y; UB[i + 2] = (float)z; }
    static readonly float[] prevVP = new float[16]; static V3 prevC; static bool havePrev;
    static void FillUniforms(double fogK, double tn, double hz)
    {
        Array.Copy(C.vp, 0, UB, 0, 16); Array.Copy(cas[0].LV, 0, UB, 16, 16);
        V3U(32, SUN.X, SUN.Y, SUN.Z); UB[35] = (float)R;
        V3U(36, C.x, C.y, C.z); UB[39] = (float)AT;
        V3U(40, cloudDrift[0], cloudDrift[1], cloudDrift[2]); UB[43] = (float)(tn % 1000);
        V3U(44, windDir.X, windDir.Y, windDir.Z); UB[47] = (float)fogK;
        V3U(48, C.f.X, C.f.Y, C.f.Z); UB[51] = (float)Math.Tan(C.fov / 2);
        V3U(52, C.rt.X, C.rt.Y, C.rt.Z); UB[55] = (float)C.asp;
        V3U(56, C.up.X, C.up.Y, C.up.Z); UB[59] = 1f / SMS;
        UB[60] = VW; UB[61] = VH; UB[62] = (float)RC0; UB[63] = (float)RC1;
        UB[64] = (float)SET.wvA; UB[65] = (float)SET.wvLen; UB[66] = (float)SET.wvK; UB[67] = (float)(SET.wvDir * PI / 180);
        UB[68] = (float)wvT; UB[69] = (float)R; UB[70] = (float)SEA; UB[71] = (float)SET.wvRip;
        UB[72] = (float)SET.wvFoam; UB[73] = (float)SET.wvOp; UB[74] = (float)OVERCAST; UB[75] = 0;   // uStorm
        UB[76] = 0; UB[77] = 0; UB[78] = 0; UB[79] = (float)CLOUD_COV;                               // uFlash, uSnow, uWet, uCov
        UB[80] = shOn ? 1 : 0; UB[81] = SET.clouds > 0 ? 1 : 0; UB[82] = (float)WIND; UB[83] = (float)lodDelta;
        UB[84] = (float)(C.orbit ? 0 : Math.Max(0, (RD - 1) * CS)); UB[85] = (float)(R - .2); UB[86] = SET.clouds > 1 ? 14 : 8; UB[87] = (float)(SET.fog / (R * .35 + 250) * hz);
        // previous frame camera (screen-space reflections read last frame's image)
        if (!havePrev) { Array.Copy(C.vp, prevVP, 16); prevC = new V3(C.x, C.y, C.z); havePrev = true; }
        Array.Copy(prevVP, 0, UB, 88, 16); Array.Copy(cas[1].LV, 0, UB, 104, 16); Array.Copy(cas[2].LV, 0, UB, 120, 16);
        V3U(136, prevC.X, prevC.Y, prevC.Z); // uPC (world position of last frame's camera)
        UB[139] = SET.ssr > .5 && !C.orbit ? 1 : 0;
        // god rays: sun position on screen; fade when it leaves the screen, at night, in orbit and underwater
        double sx = 0, sy = 0, rk = 0;
        if (SET.godrays > .5 && !C.orbit && !UI.underwater)
        {
            var m = C.vp; double cx = m[0] * SUN.X + m[4] * SUN.Y + m[8] * SUN.Z, cy = m[1] * SUN.X + m[5] * SUN.Y + m[9] * SUN.Z, cw = m[3] * SUN.X + m[7] * SUN.Y + m[11] * SUN.Z;
            if (cw > 1e-3)
            {
                sx = cx / cw * .5 + .5; sy = cy / cw * .5 + .5;
                double r = Math.Sqrt(C.x * C.x + C.y * C.y + C.z * C.z), se = (SUN.X * C.x + SUN.Y * C.y + SUN.Z * C.z) / r;
                double off = Math.Max(Math.Max(-sx, sx - 1), Math.Max(-sy, sy - 1));
                rk = 1.6 * M.Sm(-.05, .15, se) * (1 - M.Sm(0, .6, off)) * (1 - M.Sm(.15 * AT, .5 * AT, r - R));
            }
        }
        UB[140] = (float)sx; UB[141] = (float)sy; UB[142] = (float)rk; UB[143] = SET.bloom > .5 ? .14f : 0;
        UB[144] = SET.fxaa > .5 ? 1 : 0; UB[145] = shN; UB[146] = (float)Math.Clamp(SET.rscale, .25, 1); UB[147] = 0;
        V3U(148, windW[0], windW[1], windW[2]); UB[151] = SET.clouds < 1.5 ? 24 : SET.clouds < 2.5 ? 40 : 64;
        V3U(152, windD[0], windD[1], windD[2]); UB[155] = SET.clouds > 0 ? 1 : 0;
        Gpu.WriteUbo(UB);
        Array.Copy(C.vp, prevVP, 16); prevC = new V3(C.x, C.y, C.z);
    }
    /// <summary>Ambient drift of clouds and the wind direction vector in the camera's local frame (fixed light breeze).</summary>
    public static void AtmosphereStep(double dt)
    {
        double r = Math.Sqrt(C.x * C.x + C.y * C.y + C.z * C.z); if (r == 0) r = 1; V3 g = new(C.x / r, C.y / r, C.z / r); double d = g.Y;
        V3 n = new(-g.X * d, 1 - g.Y * d, -g.Z * d); double nl = n.Len; n = nl < .02 ? new V3(1, 0, 0) : n / nl;
        V3 e = M.Cross(n, g); double a = WIND_DIR_DEG * PI / 180, ca = Math.Cos(a), sa = Math.Sin(a);
        windDir = n * ca + e * sa; double ws = (1.5 + WIND * 18) * 1.4 * dt; // wind speed in blocks per second
        // drift per noise scale in texture space (each wraps at 1, the texture period): shapes, weather, detail (detail drifts faster = evolving edges)
        void Drift(double[] a, double scale) { for (int k = 0; k < 3; k++) a[k] = (((a[k] - windDir[k] * ws / scale) % 1) + 1) % 1; }
        Drift(cloudDrift, 850); Drift(windW, 11000); Drift(windD, 150 / 1.8);
    }

    // ---------- sun shadows: up to 3 cascades following the camera ----------
    // 0: 112-block box (voxel terrain, 5x5 PCF), 1: covers the whole chunk range (voxel terrain + distant LOD terrain),
    // 2: 1800-block box (LOD terrain only) so mountains far away still cast shadows. Each cascade is snapped to a coarse multiple
    // of its texel size and cached: it is re-rendered only when its snap cell / the sun step changes, or (throttled) when a chunk
    // mesh inside it changes. Far cascades are staggered so at most one of them is redrawn per frame.
    sealed class Casc { public readonly float[] LV = new float[16]; public long kx = long.MinValue, ky, kz; public double qa, ox, oy, cX, cY, E, D; public bool valid, dirty; public int last; public V3 sx, sy, fl, cam; }
    static readonly Casc[] cas = { new(), new(), new() };
    static int shN; static readonly bool[] shNeed = new bool[3];
    static bool ShadowTouches(Chunk ch)
    {
        bool any = false;
        for (int i = 0; i < 2; i++)
        {
            var c = cas[i]; if (!c.valid) continue;
            V3 p = new(ch.mx, ch.my, ch.mz);
            if (Math.Abs(M.Dot(p, c.sx) - c.cX) <= c.E + ch.cr && Math.Abs(M.Dot(p, c.sy) - c.cY) <= c.E + ch.cr && Math.Abs(M.Dot(p - c.cam, c.fl)) <= c.D + ch.cr) { c.dirty = true; any = true; }
        }
        return any;
    }
    static void ShadowPrep()
    {
        shOn = false; shN = 0; shNeed[0] = shNeed[1] = shNeed[2] = false;
        int want = (int)Math.Round(SET.shadows);
        if (want <= 0 || C.orbit) { foreach (var c in cas) c.valid = false; return; }
        double r = Math.Sqrt(C.x * C.x + C.y * C.y + C.z * C.z); V3 g = new(C.x / r, C.y / r, C.z / r);
        if (r - R > HMAX * 1.6 + 80 || M.Dot(SUN, g) < -.1) { foreach (var c in cas) c.valid = false; return; }
        double stp = SET.shInt > 0 ? SET.shInt * 2 * PI / SET.day : 0, qa = stp != 0 ? M.JsRound(SUNA / stp) * stp : SUNA, qc = Math.Cos(qa), qs = Math.Sin(qa);
        V3 fl = new(-(SA.X * qc + SB.X * qs), -(SA.Y * qc + SB.Y * qs), -(SA.Z * qc + SB.Z * qs));
        V3 refv = Math.Abs(fl.Y) < .95 ? new V3(0, 1, 0) : new V3(1, 0, 0), sx = M.Nrm(M.Cross(fl, refv)), sy = M.Cross(sx, fl);
        double[] ext = { SHE, Math.Max(SHE * 2, RD * CS + 48), 1800 };
        bool farDone = false;
        for (int i = 0; i < 3; i++)
        {
            var c = cas[i];
            if (i >= want) { c.valid = false; continue; }
            double E = ext[i], D = E * 1.5 + HMAX * .8 + 60, stepw = 2 * E / SMS * 16;
            double lx = C.x * sx.X + C.y * sx.Y + C.z * sx.Z, ly = C.x * sy.X + C.y * sy.Y + C.z * sy.Z, lz = C.x * fl.X + C.y * fl.Y + C.z * fl.Z;
            long kx = (long)M.JsRound(lx / stepw), ky = (long)M.JsRound(ly / stepw), kz = (long)M.JsRound(lz / stepw);
            bool moved = !c.valid || c.kx != kx || c.ky != ky || c.kz != kz || c.qa != qa || c.E != E;
            bool redo = moved || (c.dirty && (i == 0 || frameNo - c.last >= 20));
            if (redo && i > 0 && farDone) redo = false; // stagger the far cascades
            if (redo)
            {
                if (i > 0) farDone = true;
                double ox = lx - kx * stepw, oy = ly - ky * stepw, oz = lz - kz * stepw;
                c.sx = sx; c.sy = sy; c.fl = fl; c.E = E; c.D = D; c.ox = ox; c.oy = oy; c.cX = kx * stepw; c.cY = ky * stepw; c.cam = new V3(C.x, C.y, C.z);
                c.kx = kx; c.ky = ky; c.kz = kz; c.qa = qa;
                shNeed[i] = true;
            }
            if (!c.valid && !shNeed[i]) break;
            // light matrix relative to the CURRENT camera (vertex positions are camera-relative)
            double ccx = c.cX, ccy = c.cY, ccz = M.Dot(c.cam, c.fl);
            double rox = (C.x * c.sx.X + C.y * c.sx.Y + C.z * c.sx.Z) - ccx, roy = (C.x * c.sy.X + C.y * c.sy.Y + C.z * c.sy.Z) - ccy, roz = (C.x * c.fl.X + C.y * c.fl.Y + C.z * c.fl.Z) - ccz;
            // depth is centred on the camera position at render time, so keep that reference
            var L = c.LV;
            L[0] = (float)(c.sx.X / c.E); L[1] = (float)(c.sy.X / c.E); L[2] = (float)(c.fl.X / c.D); L[3] = 0;
            L[4] = (float)(c.sx.Y / c.E); L[5] = (float)(c.sy.Y / c.E); L[6] = (float)(c.fl.Y / c.D); L[7] = 0;
            L[8] = (float)(c.sx.Z / c.E); L[9] = (float)(c.sy.Z / c.E); L[10] = (float)(c.fl.Z / c.D); L[11] = 0;
            L[12] = (float)(rox / c.E); L[13] = (float)(roy / c.E); L[14] = (float)(roz / c.D); L[15] = 1;
            shN = i + 1;
        }
        shOn = shN > 0;
    }
    static bool InCasc(Casc c, double x, double y, double z, double rad)
    {
        V3 p = new(x, y, z);
        return Math.Abs(M.Dot(p, c.sx) - c.cX) <= c.E + rad && Math.Abs(M.Dot(p, c.sy) - c.cY) <= c.E + rad && Math.Abs(M.Dot(p - c.cam, c.fl)) <= c.D + rad;
    }
    // LOD patches for the far cascades: same level selection as the view, but no frustum test (off-screen mountains cast shadows too)
    static readonly List<LodMesh> lodSh = new();
    static void LodShadowWalk(Casc cs, int f, int lv, int i, int j, double minDist, double hor, V3 cd)
    {
        double a = PI / 2 / (1 << lv); V3 c = GpA(f, -PI / 4 + (i + .5) * a, -PI / 4 + (j + .5) * a, R);
        double cr = R * a * 1.1 + 3, ang = Math.Acos(M.Clamp((c.X * cd.X + c.Y * cd.Y + c.Z * cd.Z) / R, -1, 1));
        if (ang - cr / R > hor + HA || !InCasc(cs, c.X, c.Y, c.Z, cr + HMAX)) return;
        double dist = Math.Max(Math.Sqrt((c.X - C.x) * (c.X - C.x) + (c.Y - C.y) * (c.Y - C.y) + (c.Z - C.z) * (c.Z - C.z)) - cr, 1), edge = R * a * 1.2;
        if (lv < MAXL && (lv < MINL || edge / dist * LODK > .6))
        {
            for (int di = 0; di < 2; di++) for (int dj = 0; dj < 2; dj++) LodShadowWalk(cs, f, lv + 1, i * 2 + di, j * 2 + dj, minDist, hor, cd);
            return;
        }
        if (dist + 2 * cr < minDist) return; // fully inside the voxel range: the voxel terrain casts there
        var m = TryLod(f, lv, i, j); if (m == null) RequestLod(f, lv, i, j); else lodSh.Add(m);
    }
    /// <summary>Builds the LOD patch lists for the far cascades (may create + upload patches, so it runs before any render pass).</summary>
    static readonly List<LodMesh>[] lodShQ = { new(), new(), new() };
    static void ShadowCollect()
    {
        for (int k = 1; k < 3; k++)
        {
            lodShQ[k].Clear(); if (!shNeed[k] || !LODON) continue;
            double r = Math.Sqrt(C.x * C.x + C.y * C.y + C.z * C.z), hor = r > R ? Math.Acos(R / r) : PI; V3 cd = new(C.x / r, C.y / r, C.z / r);
            lodSh.Clear(); double minD = Math.Max(0, (RD - 1) * CS);
            for (int f = 0; f < 6; f++) for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) LodShadowWalk(cas[k], f, 2, i, j, minD, hor, cd);
            lodShQ[k].AddRange(lodSh);
        }
    }
    static void ShadowDraw()
    {
        for (int k = 0; k < 3; k++)
        {
            if (!shNeed[k]) continue;
            var cs = cas[k];
            Gpu.BeginShadow(k);
            if (k < 2)
            {
                Gpu.Bind(pShadow);
                foreach (var ch in chunks.Values)
                {
                    if (ch.mesh == null || !InCasc(cs, ch.mx, ch.my, ch.mz, ch.cr)) continue;
                    Gpu.Push((float)(ch.mesh.org.X - C.x), (float)(ch.mesh.org.Y - C.y), (float)(ch.mesh.org.Z - C.z), k);
                    DrawM(ch.mesh, (uint)ch.mesh.n);
                }
            }
            if (k > 0 && lodShQ[k].Count > 0)
            {
                Gpu.Bind(pLodShadow);
                foreach (var m in lodShQ[k]) { Gpu.Push((float)(m.o.X - C.x), (float)(m.o.Y - C.y), (float)(m.o.Z - C.z), k); Gpu.DrawShared(m.a, lodIdx, IndexType.Uint16, lodN); }
            }
            Gpu.EndPass(); cs.valid = true; cs.dirty = false; cs.last = frameNo;
        }
    }

    public static RayHit TG;
    static readonly float[] hlV = new float[24]; static readonly V3[] hlP = new V3[8];
    static void DrawHighlight()
    {
        if (TG == null || C.orbit) return;
        double cx = 0, cy = 0, cz = 0;
        for (int i = 0; i < 8; i++)
        {
            V3 o;
            if (TG.core) o = new V3(TG.ci + (i & 1), TG.cj + ((i >> 1) & 1), TG.ck + ((i >> 2) & 1));
            else { Band B = bands[TG.bi]; o = GpA(TG.f, -PI / 4 + (TG.bz + (i & 1)) * B.d, -PI / 4 + (TG.bx + ((i >> 1) & 1)) * B.d, B.inner + TG.by + ((i >> 2) & 1)); }
            hlP[i] = new V3(o.X - C.x, o.Y - C.y, o.Z - C.z); cx += hlP[i].X; cy += hlP[i].Y; cz += hlP[i].Z;
        }
        cx /= 8; cy /= 8; cz /= 8;
        for (int i = 0; i < 8; i++) { hlV[i * 3] = (float)(cx + (hlP[i].X - cx) * 1.012); hlV[i * 3 + 1] = (float)(cy + (hlP[i].Y - cy) * 1.012); hlV[i * 3 + 2] = (float)(cz + (hlP[i].Z - cz) * 1.012); }
        Gpu.Bind(pHlTri); Gpu.PushK(1f, 1f, 1f, .16f); Gpu.DrawDynamicIndexed(hlV, 24, hlIdx, 36, 0);
        Gpu.Bind(pHlLine); Gpu.PushK(0f, 0f, 0f, .9f); Gpu.DrawDynamicIndexed(hlV, 24, hlIdx, 24, 36);
    }

    static readonly List<Chunk> visList = new();

    /// <summary>Records the 3D part of the frame: uploads + shadow pass, then the main render pass (the UI is drawn into it afterwards).</summary>
    public static void Render()
    {
        double t0 = NowMs, tn = t0 / 1000, r = Math.Sqrt(C.x * C.x + C.y * C.y + C.z * C.z), alt = Math.Max(r - R, 0), hz = 1 - M.Sm(.15 * AT, AT, alt);
        double fogK = (SET.fog / (R * .5 + 250)) * hz;
        if (lastAO >= 0 && (SET.ao > .5) != (lastAO > .5)) RemeshAll();
        lastAO = SET.ao; Gpu.SetAniso(SET.aniso > .5);
        wvT = (wvT + Math.Min(.1, Math.Max(0, tn - wvLast)) * SET.wvSpd * (1 + .5 * SEA)) % 5000; wvLast = tn;
        ShadowPrep();
        // build the chunks that are still missing nearest-first (the dictionary order is arbitrary once chunks have been freed and re-added);
        // edited chunks (rebuilds) go before everything else, and when a lot is queued we spend a bit more of the frame on it
        ChunkWork(t0);
        LodUploads(t0);
        CollectLod();
        ShadowCollect();
        FillUniforms(fogK, tn, hz);
        ShadowDraw();

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
