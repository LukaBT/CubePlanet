using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.SPIRV.Cross;
using Vk = Silk.NET.Vulkan;
using DxFmt = Silk.NET.DXGI.Format;

namespace CubePlanet;

/// <summary>
/// Direct3D 11 backend for PCs without Vulkan (Intel HD 4000 / 4400 / 5500 and other pre-2015 GPUs): the same passes as the Vulkan backend
/// (cascaded shadow maps, HDR scene with a view-distance target, clouds, bloom chain, composite, UI), reverse-Z depth, behind the same
/// <see cref="Gpu"/> functions. The shaders stay GLSL: compiled to SPIR-V (shaderc, as for Vulkan), turned into HLSL by SPIRV-Cross and
/// compiled by the system's d3dcompiler_47 into DXBC (cached on disk). Register layout: b0 = frame uniforms, b9 = per-draw values (the
/// Vulkan push constants), t/s1..8 = the global textures, t/s10..14 = the post-processing inputs (Vulkan set 1).
/// </summary>
public static unsafe class Dx
{
    public static bool On;
    static D3D11 d3d; static D3DCompiler fxc; static Cross cross;
    static ID3D11Device* dev; static ID3D11DeviceContext* ctx; static IDXGISwapChain* swap; static IDXGIFactory* factory; static nint hwnd;
    static ID3D11Texture2D* backBuf; static ID3D11RenderTargetView* backRtv;
    static uint W, H, SW, SH; static float renderScale = 1; static bool needRecreate, vsync = true, flipModel;
    static uint samples = 1;
    const DxFmt HDR = DxFmt.FormatR16G16B16A16Float, DIST = DxFmt.FormatR16Float, BACK = DxFmt.FormatR8G8B8A8Unorm;
    sealed class Tgt { public ID3D11Texture2D* tex; public ID3D11RenderTargetView* rtv; public ID3D11ShaderResourceView* srv; public uint w, h; }
    static readonly Tgt[] sceneCol = new Tgt[2], sceneDist = new Tgt[2]; static Tgt msCol, msDist, cloudCol, cloudDist;
    static ID3D11Texture2D* depthTex; static ID3D11DepthStencilView* depthDsv;
    static ID3D11Texture2D* bloomTex; static ID3D11RenderTargetView*[] bloomRtv = new ID3D11RenderTargetView*[Gpu.BLM]; static ID3D11ShaderResourceView*[] bloomSrv = new ID3D11ShaderResourceView*[Gpu.BLM]; static (uint w, uint h)[] bloomExt = new (uint, uint)[Gpu.BLM];
    static ID3D11Texture2D* shadowTex; static readonly ID3D11DepthStencilView*[] shadowDsv = new ID3D11DepthStencilView*[Gpu.CASC]; static ID3D11ShaderResourceView* shadowSrv;
    static ID3D11SamplerState* linClamp, blockPoint, shadowCmp;
    static ID3D11Buffer* cbFrame, cbPush, dynVB; static uint dynOff; const uint DynSize = 32u << 20;
    static readonly float[] ubo = new float[Gpu.UBO_FLOATS], push = new float[8]; static bool uboDirty = true, pushDirty = true;
    static int fi; static DxPipe bound;
    public static string DeviceName = ""; public static bool Integrated;

    sealed class DxPipe
    {
        public ID3D11VertexShader* vs; public ID3D11PixelShader* ps; public ID3D11InputLayout* il; public ID3D11RasterizerState* rs; public ID3D11DepthStencilState* ds; public ID3D11BlendState* bs;
        public D3DPrimitiveTopology topo; public uint stride; public RP pass; public string name;
    }
    static void Chk(int hr, string what) { if (hr < 0) throw new Exception($"{what} failed: 0x{hr:X8}"); }

    // ======================================================================= init
    public static void Init(Window* win, Func<bool, int> pickMsaa, bool vs)
    {
        d3d = D3D11.GetApi(null); fxc = D3DCompiler.GetApi(); vsync = vs;
        var levels = stackalloc D3DFeatureLevel[] { D3DFeatureLevel.Level111, D3DFeatureLevel.Level110 };
        uint flags = (uint)CreateDeviceFlag.BgraSupport; if (Environment.GetEnvironmentVariable("CUBEPLANET_DXDEBUG") == "1") flags |= (uint)CreateDeviceFlag.Debug;
        ID3D11Device* d; ID3D11DeviceContext* c; D3DFeatureLevel got;
        int hr = d3d.CreateDevice(null, D3DDriverType.Hardware, 0, flags, levels, 2, 7, &d, &got, &c);
        if (hr < 0) { levels[0] = D3DFeatureLevel.Level110; hr = d3d.CreateDevice(null, D3DDriverType.Hardware, 0, flags, levels, 1, 7, &d, &got, &c); }
        Chk(hr, "D3D11CreateDevice (needs a DirectX 11 GPU)"); dev = d; ctx = c;
        // the adapter: its name, integrated or not (little dedicated memory), and the factory that makes the swap chain
        IDXGIDevice* dd; Guid gd = IDXGIDevice.Guid; Chk(dev->QueryInterface(&gd, (void**)&dd), "IDXGIDevice");
        IDXGIAdapter* ad; Chk(dd->GetAdapter(&ad), "GetAdapter"); AdapterDesc desc; ad->GetDesc(&desc);
        DeviceName = new string((char*)desc.Description).TrimEnd('\0'); Integrated = (ulong)desc.DedicatedVideoMemory < 600UL << 20;
        IDXGIFactory* fac; Guid gf = IDXGIFactory.Guid; Chk(ad->GetParent(&gf, (void**)&fac), "IDXGIFactory"); factory = fac;
        ad->Release(); dd->Release();
        int msaa = pickMsaa(Integrated); samples = 1;
        foreach (uint s in new uint[] { 8, 4, 2 }) { uint q = 0; if (msaa >= s && dev->CheckMultisampleQualityLevels(HDR, s, &q) >= 0 && q > 0 && dev->CheckMultisampleQualityLevels(DxFmt.FormatD32Float, s, &q) >= 0 && q > 0) { samples = s; break; } }
        hwnd = GLFW.GetWin32Window(win);
        W = (uint)Math.Max(1, Renderer.VW); H = (uint)Math.Max(1, Renderer.VH);
        CreateSwapChain();
        factory->MakeWindowAssociation(hwnd, 2); // no Alt+Enter (the game has its own fullscreen toggle)
        // constant buffers, the dynamic vertex ring, samplers, shadow cascades
        cbFrame = MkBuf((uint)Gpu.UBO_FLOATS * 4, BindFlag.ConstantBuffer, Usage.Dynamic, null); cbPush = MkBuf(32, BindFlag.ConstantBuffer, Usage.Dynamic, null);
        dynVB = MkBuf(DynSize, BindFlag.VertexBuffer, Usage.Dynamic, null);
        linClamp = MkSampler(Filter.MinMagLinearMipPoint, TextureAddressMode.Clamp, 0, false);
        blockPoint = MkSampler(Filter.MinMagPointMipLinear, TextureAddressMode.Wrap, 4, false);
        shadowCmp = MkSampler(Filter.ComparisonMinMagLinearMipPoint, TextureAddressMode.Clamp, 0, true);
        var sd = new Texture2DDesc { Width = Gpu.SMS, Height = Gpu.SMS, MipLevels = 1, ArraySize = Gpu.CASC, Format = DxFmt.FormatR32Typeless, SampleDesc = new SampleDesc(1, 0), Usage = Usage.Default, BindFlags = (uint)(BindFlag.DepthStencil | BindFlag.ShaderResource) };
        ID3D11Texture2D* st; Chk(dev->CreateTexture2D(&sd, null, &st), "shadow map"); shadowTex = st;
        for (uint l = 0; l < Gpu.CASC; l++)
        {
            var dv = new DepthStencilViewDesc { Format = DxFmt.FormatD32Float, ViewDimension = DsvDimension.Texture2Darray }; dv.Texture2DArray = new Tex2DArrayDsv(0, l, 1);
            ID3D11DepthStencilView* v; Chk(dev->CreateDepthStencilView((ID3D11Resource*)st, &dv, &v), "shadow layer"); shadowDsv[l] = v;
            ctx->ClearDepthStencilView(v, (uint)ClearFlag.Depth, 1, 0);
        }
        var sv = new ShaderResourceViewDesc { Format = DxFmt.FormatR32Float, ViewDimension = D3DSrvDimension.D3DSrvDimensionTexture2Darray }; sv.Texture2DArray = new Tex2DArraySrv(0, 1, 0, Gpu.CASC);
        ID3D11ShaderResourceView* ssrv; Chk(dev->CreateShaderResourceView((ID3D11Resource*)st, &sv, &ssrv), "shadow srv"); shadowSrv = ssrv;
        CreateTargets();
        On = true;
    }
    static void CreateSwapChain()
    {
        var sd = new SwapChainDesc
        {
            BufferDesc = new ModeDesc { Width = W, Height = H, Format = BACK }, SampleDesc = new SampleDesc(1, 0), BufferUsage = 0x20 /* render target output */,
            BufferCount = 2, OutputWindow = hwnd, Windowed = true, SwapEffect = SwapEffect.FlipDiscard,
        };
        IDXGISwapChain* s; int hr = factory->CreateSwapChain((IUnknown*)dev, &sd, &s); flipModel = hr >= 0;
        if (hr < 0) { sd.SwapEffect = SwapEffect.Discard; sd.BufferCount = 1; hr = factory->CreateSwapChain((IUnknown*)dev, &sd, &s); } // Windows 7 / 8: the old blit model
        Chk(hr, "swap chain"); swap = s; GetBackBuffer();
    }
    static void GetBackBuffer()
    {
        ID3D11Texture2D* bb; Guid g = ID3D11Texture2D.Guid; Chk(swap->GetBuffer(0, &g, (void**)&bb), "back buffer"); backBuf = bb;
        ID3D11RenderTargetView* r; Chk(dev->CreateRenderTargetView((ID3D11Resource*)bb, null, &r), "back buffer rtv"); backRtv = r;
    }
    static ID3D11Buffer* MkBuf(uint size, BindFlag bind, Usage use, void* data)
    {
        var bd = new BufferDesc { ByteWidth = size, BindFlags = (uint)bind, Usage = use, CPUAccessFlags = use == Usage.Dynamic ? (uint)CpuAccessFlag.Write : 0 };
        var init = new SubresourceData { PSysMem = data };
        ID3D11Buffer* b; Chk(dev->CreateBuffer(&bd, data == null ? null : &init, &b), "buffer"); return b;
    }
    static ID3D11SamplerState* MkSampler(Filter f, TextureAddressMode am, float maxLod, bool cmp)
    {
        var s = new SamplerDesc { Filter = f, AddressU = am, AddressV = am, AddressW = am, MaxAnisotropy = 1, ComparisonFunc = cmp ? ComparisonFunc.LessEqual : ComparisonFunc.Never, MinLOD = 0, MaxLOD = maxLod <= 0 ? 0 : maxLod };
        s.BorderColor[0] = s.BorderColor[1] = s.BorderColor[2] = s.BorderColor[3] = 1;
        ID3D11SamplerState* r; Chk(dev->CreateSamplerState(&s, &r), "sampler"); return r;
    }
    static Tgt MkTarget(uint w, uint h, DxFmt f, uint s, float clear)
    {
        var td = new Texture2DDesc { Width = w, Height = h, MipLevels = 1, ArraySize = 1, Format = f, SampleDesc = new SampleDesc(s, 0), Usage = Usage.Default, BindFlags = (uint)(BindFlag.RenderTarget | (s == 1 ? BindFlag.ShaderResource : 0)) };
        var t = new Tgt { w = w, h = h }; ID3D11Texture2D* tex; Chk(dev->CreateTexture2D(&td, null, &tex), "target"); t.tex = tex;
        ID3D11RenderTargetView* rtv; Chk(dev->CreateRenderTargetView((ID3D11Resource*)tex, null, &rtv), "target rtv"); t.rtv = rtv;
        if (s == 1) { ID3D11ShaderResourceView* srv; Chk(dev->CreateShaderResourceView((ID3D11Resource*)tex, null, &srv), "target srv"); t.srv = srv; }
        var cc = stackalloc float[4] { clear, clear, clear, 0 }; ctx->ClearRenderTargetView(rtv, cc);
        return t;
    }
    static void Rel(ref Tgt t) { if (t == null) return; if (t.srv != null) t.srv->Release(); t.rtv->Release(); t.tex->Release(); t = null; }
    /// <summary>Every size-dependent target: scene (colour + distance, two of each: the other is the previous frame), MSAA, depth, bloom mips, clouds.</summary>
    static void CreateTargets()
    {
        SW = Math.Max(16, (uint)Math.Round(W * renderScale)); SH = Math.Max(16, (uint)Math.Round(H * renderScale));
        for (int f = 0; f < 2; f++) { sceneCol[f] = MkTarget(SW, SH, HDR, 1, 0); sceneDist[f] = MkTarget(SW, SH, DIST, 1, 60000); }
        if (samples > 1) { msCol = MkTarget(SW, SH, HDR, samples, 0); msDist = MkTarget(SW, SH, DIST, samples, 60000); }
        var dd = new Texture2DDesc { Width = SW, Height = SH, MipLevels = 1, ArraySize = 1, Format = DxFmt.FormatD32Float, SampleDesc = new SampleDesc(samples, 0), Usage = Usage.Default, BindFlags = (uint)BindFlag.DepthStencil };
        ID3D11Texture2D* dt; Chk(dev->CreateTexture2D(&dd, null, &dt), "depth"); depthTex = dt;
        ID3D11DepthStencilView* dsv; Chk(dev->CreateDepthStencilView((ID3D11Resource*)dt, null, &dsv), "depth dsv"); depthDsv = dsv;
        uint bw = Math.Max(1, SW / 2), bh = Math.Max(1, SH / 2);
        var bd = new Texture2DDesc { Width = bw, Height = bh, MipLevels = Gpu.BLM, ArraySize = 1, Format = HDR, SampleDesc = new SampleDesc(1, 0), Usage = Usage.Default, BindFlags = (uint)(BindFlag.RenderTarget | BindFlag.ShaderResource) };
        ID3D11Texture2D* bt; Chk(dev->CreateTexture2D(&bd, null, &bt), "bloom"); bloomTex = bt;
        var zero = stackalloc float[4];
        for (uint m = 0; m < Gpu.BLM; m++)
        {
            bloomExt[m] = (Math.Max(1, bw >> (int)m), Math.Max(1, bh >> (int)m));
            var rv = new RenderTargetViewDesc { Format = HDR, ViewDimension = RtvDimension.Texture2D }; rv.Texture2D = new Tex2DRtv(m);
            ID3D11RenderTargetView* r; Chk(dev->CreateRenderTargetView((ID3D11Resource*)bt, &rv, &r), "bloom rtv"); bloomRtv[m] = r; ctx->ClearRenderTargetView(r, zero);
            var sv = new ShaderResourceViewDesc { Format = HDR, ViewDimension = D3DSrvDimension.D3DSrvDimensionTexture2D }; sv.Texture2D = new Tex2DSrv(m, 1);
            ID3D11ShaderResourceView* s; Chk(dev->CreateShaderResourceView((ID3D11Resource*)bt, &sv, &s), "bloom srv"); bloomSrv[m] = s;
        }
        cloudCol = MkTarget(bw, bh, HDR, 1, 0); cloudDist = MkTarget(bw, bh, DIST, 1, 60000);
    }
    static void DestroyTargets()
    {
        ctx->ClearState(); bound = null;
        for (int f = 0; f < 2; f++) { Rel(ref sceneCol[f]); Rel(ref sceneDist[f]); }
        Rel(ref msCol); Rel(ref msDist); Rel(ref cloudCol); Rel(ref cloudDist);
        if (depthDsv != null) { depthDsv->Release(); depthTex->Release(); depthDsv = null; depthTex = null; }
        for (int m = 0; m < Gpu.BLM; m++) { if (bloomRtv[m] != null) bloomRtv[m]->Release(); if (bloomSrv[m] != null) bloomSrv[m]->Release(); bloomRtv[m] = null; bloomSrv[m] = null; }
        if (bloomTex != null) { bloomTex->Release(); bloomTex = null; }
    }
    static void Recreate()
    {
        DestroyTargets();
        W = (uint)Math.Max(1, Renderer.VW); H = (uint)Math.Max(1, Renderer.VH);
        backRtv->Release(); backBuf->Release();
        Chk(swap->ResizeBuffers(0, W, H, DxFmt.FormatUnknown, 0), "ResizeBuffers"); GetBackBuffer();
        CreateTargets(); needRecreate = false;
    }
    public static void Resize() => needRecreate = true;
    public static void SetRenderScale(double s) { float v = (float)Math.Clamp(s, .25, 1); if (Math.Abs(v - renderScale) > .001) { renderScale = v; needRecreate = true; } }
    public static void Shutdown() { if (dev == null) return; ctx->ClearState(); ctx->Flush(); }

    // ======================================================================= textures and meshes
    static DxFmt Fmt(Vk.Format f) => f switch
    {
        Vk.Format.R8G8B8A8Unorm => DxFmt.FormatR8G8B8A8Unorm, Vk.Format.R8Unorm => DxFmt.FormatR8Unorm, Vk.Format.R16G16B16A16Sint => DxFmt.FormatR16G16B16A16Sint,
        Vk.Format.R16G16Uint => DxFmt.FormatR16G16Uint, Vk.Format.R8G8B8A8SNorm => DxFmt.FormatR8G8B8A8SNorm, Vk.Format.R8G8B8A8Uint => DxFmt.FormatR8G8B8A8Uint,
        Vk.Format.R32G32B32Sfloat => DxFmt.FormatR32G32B32Float, Vk.Format.R32G32Sfloat => DxFmt.FormatR32G32Float, Vk.Format.R32Sfloat => DxFmt.FormatR32Float,
        Vk.Format.R32G32B32A32Sfloat => DxFmt.FormatR32G32B32A32Float, _ => throw new NotSupportedException("format " + f),
    };
    public static GTex MakeTexture(Vk.ImageType t, Vk.ImageViewType vt, Vk.Format f, int bpp, uint w, uint h, uint d, uint layers, byte[][] mips, Vk.Filter mag, Vk.Filter min, Vk.SamplerMipmapMode mm, Vk.SamplerAddressMode am)
    {
        var fmt = Fmt(f); int nm = mips.Length; var pins = mips.Select(m => GCHandle.Alloc(m, GCHandleType.Pinned)).ToArray();
        try
        {
            var g = new GTex(); ID3D11Resource* res; ID3D11ShaderResourceView* srv; var sv = new ShaderResourceViewDesc { Format = fmt };
            if (t == Vk.ImageType.Type3D)
            {
                var sub = new SubresourceData[nm];
                for (int l = 0; l < nm; l++) { uint mw = Math.Max(1, w >> l), mh = Math.Max(1, h >> l); sub[l] = new SubresourceData((void*)pins[l].AddrOfPinnedObject(), (uint)(mw * bpp), (uint)(mw * mh * bpp)); }
                var td = new Texture3DDesc { Width = w, Height = h, Depth = d, MipLevels = (uint)nm, Format = fmt, Usage = Usage.Immutable, BindFlags = (uint)BindFlag.ShaderResource };
                ID3D11Texture3D* tex; fixed (SubresourceData* ps = sub) Chk(dev->CreateTexture3D(&td, ps, &tex), "texture 3D"); res = (ID3D11Resource*)tex;
                sv.ViewDimension = D3DSrvDimension.D3DSrvDimensionTexture3D; sv.Texture3D = new Tex3DSrv(0, (uint)nm);
            }
            else
            {
                var sub = new SubresourceData[nm * layers];
                for (uint layer = 0; layer < layers; layer++)
                    for (int l = 0; l < nm; l++)
                    {
                        uint mw = Math.Max(1, w >> l), mh = Math.Max(1, h >> l); byte* basep = (byte*)pins[l].AddrOfPinnedObject();
                        sub[layer * nm + l] = new SubresourceData(basep + layer * mw * mh * bpp, (uint)(mw * bpp), (uint)(mw * mh * bpp));
                    }
                var td = new Texture2DDesc { Width = w, Height = h, MipLevels = (uint)nm, ArraySize = layers, Format = fmt, SampleDesc = new SampleDesc(1, 0), Usage = Usage.Immutable, BindFlags = (uint)BindFlag.ShaderResource };
                ID3D11Texture2D* tex; fixed (SubresourceData* ps = sub) Chk(dev->CreateTexture2D(&td, ps, &tex), "texture 2D"); res = (ID3D11Resource*)tex;
                if (vt == Vk.ImageViewType.Type2DArray) { sv.ViewDimension = D3DSrvDimension.D3DSrvDimensionTexture2Darray; sv.Texture2DArray = new Tex2DArraySrv(0, (uint)nm, 0, layers); }
                else { sv.ViewDimension = D3DSrvDimension.D3DSrvDimensionTexture2D; sv.Texture2D = new Tex2DSrv(0, (uint)nm); }
            }
            Chk(dev->CreateShaderResourceView(res, &sv, &srv), "texture srv");
            int fi2 = (min == Vk.Filter.Linear ? 4 : 0) + (mag == Vk.Filter.Linear ? 2 : 0) + (mm == Vk.SamplerMipmapMode.Linear ? 1 : 0);
            var flt = new[] { Filter.MinMagMipPoint, Filter.MinMagPointMipLinear, Filter.MinPointMagLinearMipPoint, Filter.MinPointMagMipLinear, Filter.MinLinearMagMipPoint, Filter.MinLinearMagPointMipLinear, Filter.MinMagLinearMipPoint, Filter.MinMagMipLinear }[fi2];
            var adr = am switch { Vk.SamplerAddressMode.Repeat => TextureAddressMode.Wrap, Vk.SamplerAddressMode.MirroredRepeat => TextureAddressMode.Mirror, _ => TextureAddressMode.Clamp };
            g.dxtex = (nint)res; g.dxsrv = (nint)srv; g.dxsmp = (nint)MkSampler(flt, adr, nm - 1, false);
            return g;
        }
        finally { foreach (var p in pins) p.Free(); }
    }
    public static GAlloc Upload<T1, T2>(T1[] a, int aCount, T2[] b, int bCount) where T1 : unmanaged where T2 : unmanaged
    {
        uint sa = (uint)(aCount * sizeof(T1)), sb = (uint)(bCount * sizeof(T2)), size = Math.Max(16, (sa + sb + 15) & ~15u);
        var data = new byte[size];
        fixed (T1* pa = a) fixed (byte* pd = data) System.Buffer.MemoryCopy(pa, pd, size, sa);
        if (sb > 0) fixed (T2* pb = b) fixed (byte* pd = data) System.Buffer.MemoryCopy(pb, pd + sa, size - sa, sb);
        ID3D11Buffer* buf; fixed (byte* pd = data) buf = MkBuf(size, BindFlag.VertexBuffer | BindFlag.IndexBuffer, Usage.Immutable, pd);
        Gpu.HeapUsed += size; return new GAlloc { dxbuf = (nint)buf, size = size };
    }
    public static void Free(GAlloc a) { if (a == null || a.freed) return; a.freed = true; if (a.dxbuf != 0) ((ID3D11Buffer*)a.dxbuf)->Release(); a.dxbuf = 0; Gpu.HeapUsed -= (long)a.size; }

    // ======================================================================= shaders and pipelines
    static readonly Regex Push = new(@"layout\(push_constant\)\s*uniform", RegexOptions.Compiled), Set1 = new(@"set\s*=\s*1\s*,\s*binding\s*=\s*(\d)", RegexOptions.Compiled);
    /// <summary>The Vulkan shader header with Direct3D's register layout: push constants become uniform block 9 (b9), set 1 bindings 10..14.</summary>
    static string Header => Set1.Replace(Push.Replace(Glsl.HEADER, "layout(std140,set=0,binding=9) uniform"), m => "set=0,binding=1" + m.Groups[1].Value);
    static string Hlsl(byte[] spirv, bool frag, string name)
    {
        cross ??= Cross.GetApi();
        Context* c; cross.ContextCreate(&c);
        try
        {
            ParsedIr* ir; fixed (byte* p = spirv) if (cross.ContextParseSpirv(c, (uint*)p, (nuint)(spirv.Length / 4), &ir) != Result.Success) throw new Exception($"{name}: SPIRV-Cross parse: {SilkMarshal.PtrToString((nint)cross.ContextGetLastErrorString(c))}");
            Silk.NET.SPIRV.Cross.Compiler* comp; cross.ContextCreateCompiler(c, Backend.Hlsl, ir, CaptureMode.TakeOwnership, &comp);
            CompilerOptions* o; cross.CompilerCreateCompilerOptions(comp, &o);
            cross.CompilerOptionsSetUint(o, CompilerOption.HlslShaderModel, 50);
            if (!frag) cross.CompilerOptionsSetBool(o, CompilerOption.FlipVertexY, 1); // Vulkan clip space is y-down, Direct3D's y-up: the picture comes out the same
            cross.CompilerInstallCompilerOptions(comp, o);
            byte* src; if (cross.CompilerCompile(comp, &src) != Result.Success) throw new Exception($"{name}: SPIRV-Cross HLSL: {SilkMarshal.PtrToString((nint)cross.ContextGetLastErrorString(c))}");
            return ExplicitLod(SilkMarshal.PtrToString((nint)src));
        }
        finally { cross.ContextDestroy(c); }
    }
    // textures with a single mip level: sampled at level 0 explicitly (HLSL refuses implicit-gradient samples inside loops with a varying count,
    // e.g. ray marches through the noise volume and the previous frame); shadow compares likewise (the cascades have no mips)
    // by register: 3 noise, 4 font, 5 / 6 previous frame, 7 cloud noise, 10..14 post inputs (the block and item arrays, 1 and 8, keep their mips)
    static readonly int[] OneMip = { 3, 4, 5, 6, 7, 10, 11, 12, 13, 14 };
    static readonly Regex TexDecl = new(@"(\w+)\s*:\s*register\(t(\d+)\)", RegexOptions.Compiled);
    static string ExplicitLod(string h)
    {
        h = h.Replace(".SampleCmp(", ".SampleCmpLevelZero(");
        var names = TexDecl.Matches(h).Where(m => OneMip.Contains(int.Parse(m.Groups[2].Value))).Select(m => m.Groups[1].Value).Distinct().ToList();
        foreach (var n in names)
        {
            string key = n + ".Sample("; int i = 0;
            while ((i = h.IndexOf(key, i, StringComparison.Ordinal)) >= 0)
            {
                if (i > 0 && (char.IsLetterOrDigit(h[i - 1]) || h[i - 1] == '_')) { i += key.Length; continue; }
                int open = i + key.Length - 1, depth = 0, j = open;
                for (; j < h.Length; j++) { if (h[j] == '(') depth++; else if (h[j] == ')' && --depth == 0) break; }
                h = h.Substring(0, j) + ", 0.0f" + h.Substring(j); h = h.Substring(0, i) + n + ".SampleLevel(" + h.Substring(open + 1);
                i += key.Length;
            }
        }
        return h;
    }
    // the varyings struct of a shader stage: members "type name : SEMANTIC;"
    static readonly Regex Member = new(@"^\s*(?:nointerpolation\s+|noperspective\s+|centroid\s+)*\w+\s+\w+\s*:\s*(\w+)\s*;", RegexOptions.Multiline | RegexOptions.Compiled);
    static (int start, int end, List<(string line, string sem)> members) Struct(string hlsl, string name)
    {
        int s = hlsl.IndexOf("struct " + name); if (s < 0) return (-1, -1, new());
        int a = hlsl.IndexOf('{', s), b = hlsl.IndexOf("};", a);
        var list = new List<(string, string)>();
        foreach (Match m in Member.Matches(hlsl.Substring(a + 1, b - a - 1))) list.Add((m.Value.Trim(), m.Groups[1].Value));
        return (a + 1, b, list);
    }
    /// <summary>Direct3D links a vertex shader to a pixel shader by register order, so the pixel shader's input struct is made the same as the
    /// vertex shader's output struct (inputs the pixel shader does not use are added unused, system values stay last).</summary>
    static string MatchInputs(string vs, string ps)
    {
        var (_, _, outs) = Struct(vs, "SPIRV_Cross_Output"); var (a, b, ins) = Struct(ps, "SPIRV_Cross_Input");
        if (a < 0) return ps;
        var sb = new StringBuilder("\n"); int n = 0;
        foreach (var (line, sem) in outs)
        {
            if (sem.StartsWith("SV_")) continue;
            var mine = ins.FirstOrDefault(i => i.sem == sem);
            if (mine.line != null) sb.Append("    ").Append(mine.line).Append('\n');
            else sb.Append("    ").Append(Regex.Replace(line, @"^(\s*(?:nointerpolation\s+|noperspective\s+|centroid\s+)*\w+\s+)\w+", "${1}_unused" + n++)).Append('\n');
        }
        foreach (var (line, sem) in ins) if (sem.StartsWith("SV_")) sb.Append("    ").Append(line).Append('\n');
        return ps.Substring(0, a) + sb + ps.Substring(b);
    }
    static byte[] Fxc(string hlsl, string target, string name)
    {
        var src = Encoding.UTF8.GetBytes(hlsl); var nm = (byte*)SilkMarshal.StringToPtr(name); var ep = (byte*)SilkMarshal.StringToPtr("main"); var tg = (byte*)SilkMarshal.StringToPtr(target);
        ID3D10Blob* code = null, err = null;
        try
        {
            int hr; fixed (byte* p = src) hr = fxc.Compile(p, (nuint)src.Length, nm, null, null, ep, tg, 1u << 15 /* optimization level 3 */, 0, &code, &err);
            if (hr < 0)
            {
                string msg = err != null ? Encoding.UTF8.GetString((byte*)err->GetBufferPointer(), (int)err->GetBufferSize()) : $"0x{hr:X8}";
                throw new Exception($"shader {name} ({target}) HLSL compile error:\n{msg}\n--- HLSL ---\n{hlsl}");
            }
            var outb = new byte[(int)code->GetBufferSize()]; Marshal.Copy((nint)code->GetBufferPointer(), outb, 0, outb.Length); return outb;
        }
        finally { if (code != null) code->Release(); if (err != null) err->Release(); SilkMarshal.Free((nint)nm); SilkMarshal.Free((nint)ep); SilkMarshal.Free((nint)tg); }
    }
    /// <summary>GLSL vertex + fragment shader -> DXBC pair (cached on disk by source hash).</summary>
    public static (byte[] vs, byte[] ps) Compile(string vsSrc, string fsSrc, string name, string cacheDir = null)
    {
        fxc ??= D3DCompiler.GetApi();
        string hdr = Header, key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes("dx1|" + hdr + vsSrc + "|" + fsSrc)));
        string dir = cacheDir ?? Gpu.Cache ?? Path.GetTempPath(), pv = Path.Combine(dir, key + ".dxvs"), pp = Path.Combine(dir, key + ".dxps");
        try { if (File.Exists(pv) && File.Exists(pp)) return (File.ReadAllBytes(pv), File.ReadAllBytes(pp)); } catch { }
        string vh = Hlsl(Gpu.SpirvWith(hdr, vsSrc, false, name + ".vert"), false, name + ".vert");
        string ph = MatchInputs(vh, Hlsl(Gpu.SpirvWith(hdr, fsSrc, true, name + ".frag"), true, name + ".frag"));
        var r = (Fxc(vh, "vs_5_0", name + ".vert"), Fxc(ph, "ps_5_0", name + ".frag"));
        try { File.WriteAllBytes(pv, r.Item1); File.WriteAllBytes(pp, r.Item2); } catch { }
        return r;
    }
    static readonly byte[] TexcoordName = Encoding.ASCII.GetBytes("TEXCOORD\0");
    public static Pipe MakePipe(string name, string vsSrc, string fsSrc, PipeCfg c)
    {
        var (vb, pb) = Compile(vsSrc, fsSrc, name); var p = new DxPipe { name = name, pass = c.pass };
        fixed (byte* v = vb) { ID3D11VertexShader* s; Chk(dev->CreateVertexShader(v, (nuint)vb.Length, null, &s), "vertex shader " + name); p.vs = s; }
        fixed (byte* v = pb) { ID3D11PixelShader* s; Chk(dev->CreatePixelShader(v, (nuint)pb.Length, null, &s), "pixel shader " + name); p.ps = s; }
        (uint loc, Vk.Format f, uint off)[] attrs; uint stride;
        switch (c.layout)
        {
            case VL.Chunk: stride = 16; attrs = new[] { (0u, Vk.Format.R16G16B16A16Sint, 0u), (1u, Vk.Format.R16G16Uint, 8u), (2u, Vk.Format.R8G8B8A8SNorm, 12u), (3u, Vk.Format.R8G8B8A8Uint, 12u) }; break;
            case VL.ChunkShadow: stride = 16; attrs = new[] { (0u, Vk.Format.R16G16B16A16Sint, 0u), (1u, Vk.Format.R16G16Uint, 8u), (3u, Vk.Format.R8G8B8A8Uint, 12u) }; break;
            case VL.Lod: stride = 48; attrs = new[] { (0u, Vk.Format.R32G32B32Sfloat, 0u), (1u, Vk.Format.R32G32Sfloat, 12u), (2u, Vk.Format.R32G32B32Sfloat, 20u), (3u, Vk.Format.R32Sfloat, 32u), (4u, Vk.Format.R32G32B32Sfloat, 36u) }; break;
            case VL.LodSea: stride = 48; attrs = new[] { (0u, Vk.Format.R32G32B32Sfloat, 0u), (3u, Vk.Format.R32Sfloat, 32u) }; break;
            case VL.LodShadow: stride = 48; attrs = new[] { (0u, Vk.Format.R32G32B32Sfloat, 0u) }; break;
            case VL.Pos3: stride = 12; attrs = new[] { (0u, Vk.Format.R32G32B32Sfloat, 0u) }; break;
            case VL.UI: stride = 40; attrs = new[] { (0u, Vk.Format.R32G32Sfloat, 0u), (1u, Vk.Format.R32G32Sfloat, 8u), (2u, Vk.Format.R32G32B32A32Sfloat, 16u), (3u, Vk.Format.R32G32Sfloat, 32u) }; break;
            default: stride = 0; attrs = Array.Empty<(uint, Vk.Format, uint)>(); break;
        }
        p.stride = stride;
        if (attrs.Length > 0)
            fixed (byte* sem = TexcoordName) fixed (byte* v = vb)
            {
                var el = stackalloc InputElementDesc[attrs.Length];
                for (int i = 0; i < attrs.Length; i++) el[i] = new InputElementDesc { SemanticName = sem, SemanticIndex = attrs[i].loc, Format = Fmt(attrs[i].f), InputSlot = 0, AlignedByteOffset = attrs[i].off, InputSlotClass = InputClassification.PerVertexData };
                ID3D11InputLayout* il; int hr = dev->CreateInputLayout(el, (uint)attrs.Length, v, (nuint)vb.Length, &il);
                if (hr < 0) // a shader that does not read every attribute: keep only those it declares
                {
                    var used = new List<InputElementDesc>(); string vh = Encoding.ASCII.GetString(vb);
                    for (int i = 0; i < attrs.Length; i++) { var one = el[i]; ID3D11InputLayout* t1; if (dev->CreateInputLayout(&one, 1, v, (nuint)vb.Length, &t1) >= 0) { t1->Release(); used.Add(one); } }
                    var arr = used.ToArray(); fixed (InputElementDesc* pa = arr) hr = dev->CreateInputLayout(pa, (uint)arr.Length, v, (nuint)vb.Length, &il);
                }
                Chk(hr, "input layout " + name); p.il = il;
            }
        p.topo = c.topo == Vk.PrimitiveTopology.LineList ? D3DPrimitiveTopology.D3DPrimitiveTopologyLinelist : D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist;
        bool shadowPass = c.pass == RP.Shadow, scene = c.pass == RP.Scene;
        // same picture as Vulkan (the vertex shaders flip y), so the same winding: counter-clockwise front faces except in the shadow pass
        var rd = new RasterizerDesc
        {
            FillMode = FillMode.Solid, CullMode = (c.cull & Vk.CullModeFlags.BackBit) != 0 ? CullMode.Back : (c.cull & Vk.CullModeFlags.FrontBit) != 0 ? CullMode.Front : CullMode.None,
            FrontCounterClockwise = !shadowPass, DepthBias = c.depthBias ? (shadowPass ? 2 : -3) : 0, SlopeScaledDepthBias = c.depthBias ? (shadowPass ? 1.5f : -3) : 0,
            DepthClipEnable = true, MultisampleEnable = scene && samples > 1,
        };
        ID3D11RasterizerState* rs; Chk(dev->CreateRasterizerState(&rd, &rs), "rasterizer " + name); p.rs = rs;
        bool hasDepth = scene || shadowPass;
        var dsd = new DepthStencilDesc { DepthEnable = hasDepth && c.depthTest, DepthWriteMask = hasDepth && c.depthWrite ? DepthWriteMask.All : DepthWriteMask.Zero, DepthFunc = Cmp(c.cmp) };
        ID3D11DepthStencilState* ds; Chk(dev->CreateDepthStencilState(&dsd, &ds), "depth state " + name); p.ds = ds;
        var bd = new BlendDesc { IndependentBlendEnable = true };
        var r0 = new RenderTargetBlendDesc { RenderTargetWriteMask = (byte)ColorWriteEnable.All, BlendEnable = c.blend != BlendMode.None, SrcBlend = Blend.One, DestBlend = Blend.Zero, BlendOp = BlendOp.Add, SrcBlendAlpha = Blend.One, DestBlendAlpha = Blend.Zero, BlendOpAlpha = BlendOp.Add };
        if (c.blend == BlendMode.Alpha) { r0.SrcBlend = Blend.SrcAlpha; r0.DestBlend = Blend.InvSrcAlpha; r0.SrcBlendAlpha = Blend.One; r0.DestBlendAlpha = Blend.InvSrcAlpha; }
        if (c.blend == BlendMode.Add) { r0.SrcBlend = Blend.One; r0.DestBlend = Blend.One; r0.SrcBlendAlpha = Blend.Zero; r0.DestBlendAlpha = Blend.One; }
        var r1 = new RenderTargetBlendDesc { RenderTargetWriteMask = (byte)(c.writeDist || c.pass == RP.Cloud ? ColorWriteEnable.Red : 0), SrcBlend = Blend.One, DestBlend = Blend.Zero, BlendOp = BlendOp.Add, SrcBlendAlpha = Blend.One, DestBlendAlpha = Blend.Zero, BlendOpAlpha = BlendOp.Add };
        bd.RenderTarget[0] = r0; bd.RenderTarget[1] = r1;
        ID3D11BlendState* bs; Chk(dev->CreateBlendState(&bd, &bs), "blend " + name); p.bs = bs;
        return new Pipe { name = name, dx = p };
    }
    static ComparisonFunc Cmp(Vk.CompareOp o) => o switch
    {
        Vk.CompareOp.Never => ComparisonFunc.Never, Vk.CompareOp.Less => ComparisonFunc.Less, Vk.CompareOp.Equal => ComparisonFunc.Equal, Vk.CompareOp.LessOrEqual => ComparisonFunc.LessEqual,
        Vk.CompareOp.Greater => ComparisonFunc.Greater, Vk.CompareOp.NotEqual => ComparisonFunc.NotEqual, Vk.CompareOp.GreaterOrEqual => ComparisonFunc.GreaterEqual, _ => ComparisonFunc.Always,
    };

    // ======================================================================= frame and passes
    public static bool BeginFrame(bool vs)
    {
        if (vs != vsync) vsync = vs;
        if (needRecreate || Renderer.VW != W || Renderer.VH != H) { if (Renderer.VW <= 0 || Renderer.VH <= 0) return false; Recreate(); }
        bound = null; dynOff = DynSize; // the first dynamic draw of the frame starts the ring over (discard)
        BindConstants();
        return true;
    }
    static void BindConstants()
    {
        var bufs = stackalloc ID3D11Buffer*[10]; bufs[0] = cbFrame; bufs[9] = cbPush;
        ctx->VSSetConstantBuffers(0, 1, &bufs[0]); ctx->PSSetConstantBuffers(0, 1, &bufs[0]);
        ctx->VSSetConstantBuffers(9, 1, &bufs[9]); ctx->PSSetConstantBuffers(9, 1, &bufs[9]);
    }
    static readonly nint[] nulls = new nint[16];
    static void UnbindSrvs() { fixed (nint* n = nulls) { ctx->PSSetShaderResources(0, 16, (ID3D11ShaderResourceView**)n); ctx->VSSetShaderResources(0, 16, (ID3D11ShaderResourceView**)n); } }
    static void Viewport(uint w, uint h) { var vp = new Silk.NET.Direct3D11.Viewport(0, 0, w, h, 0, 1); ctx->RSSetViewports(1, &vp); }
    /// <summary>Global textures t1..t8 (shadow cascades left out while drawing into them).</summary>
    static void BindGlobals(bool shadowPass)
    {
        var srv = stackalloc ID3D11ShaderResourceView*[9]; var smp = stackalloc ID3D11SamplerState*[9];
        int pf = 1 - fi;
        srv[1] = (ID3D11ShaderResourceView*)(Gpu.texBlocks?.dxsrv ?? 0); smp[1] = blockPoint;
        srv[2] = shadowPass ? null : shadowSrv; smp[2] = shadowCmp;
        srv[3] = (ID3D11ShaderResourceView*)(Gpu.texNoise?.dxsrv ?? 0); smp[3] = (ID3D11SamplerState*)(Gpu.texNoise?.dxsmp ?? 0);
        srv[4] = (ID3D11ShaderResourceView*)(Gpu.texFont?.dxsrv ?? 0); smp[4] = (ID3D11SamplerState*)(Gpu.texFont?.dxsmp ?? 0);
        srv[5] = sceneCol[pf].srv; smp[5] = linClamp; srv[6] = sceneDist[pf].srv; smp[6] = linClamp;
        srv[7] = (ID3D11ShaderResourceView*)(Gpu.texCloud?.dxsrv ?? 0); smp[7] = (ID3D11SamplerState*)(Gpu.texCloud?.dxsmp ?? 0);
        srv[8] = (ID3D11ShaderResourceView*)(Gpu.texItems?.dxsrv ?? 0); smp[8] = (ID3D11SamplerState*)(Gpu.texItems?.dxsmp ?? 0);
        ctx->PSSetShaderResources(1, 8, &srv[1]); ctx->VSSetShaderResources(1, 8, &srv[1]);
        ctx->PSSetSamplers(1, 8, &smp[1]); ctx->VSSetSamplers(1, 8, &smp[1]);
    }
    /// <summary>Post-processing inputs t10..t14 (Vulkan set 1): 0 bright pass, 1..5 downsample, 6..10 upsample, 11 composite, 12 clouds.</summary>
    static void BindPost(int i)
    {
        var srv = stackalloc ID3D11ShaderResourceView*[5]; var smp = stackalloc ID3D11SamplerState*[5];
        for (int k = 0; k < 5; k++) smp[k] = linClamp;
        srv[2] = cloudCol.srv; srv[3] = cloudDist.srv; srv[4] = sceneDist[fi].srv;
        if (i == 0) srv[0] = srv[1] = sceneCol[fi].srv;
        else if (i < Gpu.BLM) srv[0] = srv[1] = bloomSrv[i - 1];
        else if (i < 11) { int k = Gpu.BLM - 2 - (i - Gpu.BLM); srv[0] = srv[1] = bloomSrv[k + 1]; }
        else if (i == 11) { srv[0] = sceneCol[fi].srv; srv[1] = bloomSrv[0]; }
        else srv[0] = srv[1] = sceneDist[fi].srv;
        ctx->PSSetShaderResources(10, 5, srv); ctx->PSSetSamplers(10, 5, smp);
    }
    static void Targets(int n, ID3D11RenderTargetView** rtv, ID3D11DepthStencilView* dsv)
    {
        UnbindSrvs(); ctx->OMSetRenderTargets((uint)n, rtv, dsv); bound = null;
    }
    public static void BeginShadow(int layer)
    {
        Targets(0, null, shadowDsv[layer]); ctx->ClearDepthStencilView(shadowDsv[layer], (uint)ClearFlag.Depth, 1, 0);
        Viewport(Gpu.SMS, Gpu.SMS); BindGlobals(true);
    }
    public static void EndPass() { }
    public static void BeginScene()
    {
        var rt = stackalloc ID3D11RenderTargetView*[2];
        rt[0] = samples > 1 ? msCol.rtv : sceneCol[fi].rtv; rt[1] = samples > 1 ? msDist.rtv : sceneDist[fi].rtv;
        Targets(2, rt, depthDsv);
        var c0 = stackalloc float[4] { 0, 0, 0, 0 }; var c1 = stackalloc float[4] { 60000, 0, 0, 0 };
        ctx->ClearRenderTargetView(rt[0], c0); ctx->ClearRenderTargetView(rt[1], c1); ctx->ClearDepthStencilView(depthDsv, (uint)ClearFlag.Depth, 0, 0);
        Viewport(SW, SH); BindGlobals(false);
    }
    public static void PostProcess(bool doBloom, bool doClouds)
    {
        if (samples > 1)
        {
            Targets(0, null, null);
            ctx->ResolveSubresource((ID3D11Resource*)sceneCol[fi].tex, 0, (ID3D11Resource*)msCol.tex, 0, HDR);
            ctx->ResolveSubresource((ID3D11Resource*)sceneDist[fi].tex, 0, (ID3D11Resource*)msDist.tex, 0, DIST);
        }
        if (doClouds)
        {
            var rt = stackalloc ID3D11RenderTargetView*[2]; rt[0] = cloudCol.rtv; rt[1] = cloudDist.rtv;
            Targets(2, rt, null); Viewport(cloudCol.w, cloudCol.h); BindGlobals(false); BindPost(12);
            Bind(Gpu.pCloud); SetPush(1f / W, 1f / H, 0, 0); DrawFull();
        }
        if (doBloom)
        {
            for (int m = 0; m < Gpu.BLM; m++)
            {
                var r = bloomRtv[m]; Targets(1, &r, null); Viewport(bloomExt[m].w, bloomExt[m].h); BindGlobals(false); BindPost(m);
                Bind(m == 0 ? Gpu.pBloomPre : Gpu.pBloomDown);
                var src = m == 0 ? (SW, SH) : bloomExt[m - 1]; SetPush(1f / src.Item1, 1f / src.Item2, 0, m); DrawFull();
            }
            for (int k = Gpu.BLM - 2, i = 0; k >= 0; k--, i++)
            {
                var r = bloomRtv[k]; Targets(1, &r, null); Viewport(bloomExt[k].w, bloomExt[k].h); BindGlobals(false); BindPost(Gpu.BLM + i);
                Bind(Gpu.pBloomUp); SetPush(1f / bloomExt[k + 1].w, 1f / bloomExt[k + 1].h, 0, k); DrawFull();
            }
        }
        BeginFinal(0, 0, 0); BindPost(11);
        Bind(Gpu.pComposite); SetPush(1f / SW, 1f / SH, 0, 0); DrawFull();
    }
    public static void BeginFinal(float r, float g, float b)
    {
        var rt = backRtv; Targets(1, &rt, null);
        var cc = stackalloc float[4] { r, g, b, 1 }; ctx->ClearRenderTargetView(backRtv, cc);
        Viewport(W, H); BindGlobals(false);
    }
    public static void EndFrame(ref string shotPath)
    {
        string shot = shotPath; shotPath = null;
        if (shot != null) Screenshot(shot);
        int hr = swap->Present(vsync ? 1u : 0u, 0);
        if (hr < 0) needRecreate = true;
        fi = 1 - fi;
    }
    static void Screenshot(string path)
    {
        var td = new Texture2DDesc { Width = W, Height = H, MipLevels = 1, ArraySize = 1, Format = BACK, SampleDesc = new SampleDesc(1, 0), Usage = Usage.Staging, CPUAccessFlags = (uint)CpuAccessFlag.Read };
        ID3D11Texture2D* st; if (dev->CreateTexture2D(&td, null, &st) < 0) return;
        ctx->CopyResource((ID3D11Resource*)st, (ID3D11Resource*)backBuf);
        MappedSubresource ms; if (ctx->Map((ID3D11Resource*)st, 0, Map.Read, 0, &ms) >= 0)
        {
            var px = new byte[W * H * 4];
            for (int y = 0; y < H; y++) Marshal.Copy((nint)((byte*)ms.PData + y * ms.RowPitch), px, (int)(y * W * 4), (int)(W * 4));
            ctx->Unmap((ID3D11Resource*)st, 0); Png.Write(path, px, (int)W, (int)H, false, false);
        }
        st->Release();
    }

    // ======================================================================= uniforms and draws
    public static void WriteUbo(float[] u) { Array.Copy(u, ubo, Math.Min(u.Length, ubo.Length)); uboDirty = true; }
    public static void UboSet(int i, float v) { if (ubo[i] != v) { ubo[i] = v; uboDirty = true; } }
    public static void SetPush(float x, float y, float z, float w) { push[0] = x; push[1] = y; push[2] = z; push[3] = w; pushDirty = true; }
    public static void SetPushK(float r, float g, float b, float a) { push[4] = r; push[5] = g; push[6] = b; push[7] = a; pushDirty = true; }
    static void Upd(ID3D11Buffer* b, float[] data)
    {
        MappedSubresource ms; Chk(ctx->Map((ID3D11Resource*)b, 0, Map.WriteDiscard, 0, &ms), "map constants");
        fixed (float* p = data) System.Buffer.MemoryCopy(p, ms.PData, data.Length * 4, data.Length * 4); ctx->Unmap((ID3D11Resource*)b, 0);
    }
    static void PreDraw()
    {
        if (uboDirty) { Upd(cbFrame, ubo); uboDirty = false; }
        if (pushDirty) { Upd(cbPush, push); pushDirty = false; }
    }
    public static void Bind(Pipe pp)
    {
        var p = (DxPipe)pp.dx; if (bound == p) return; bound = p;
        ctx->VSSetShader(p.vs, null, 0); ctx->PSSetShader(p.ps, null, 0); ctx->IASetInputLayout(p.il); ctx->IASetPrimitiveTopology(p.topo);
        ctx->RSSetState(p.rs); ctx->OMSetDepthStencilState(p.ds, 0); var bf = stackalloc float[4]; ctx->OMSetBlendState(p.bs, bf, 0xffffffff);
    }
    static void SetVB(ID3D11Buffer* b, uint off) { uint st = bound?.stride ?? 0; ctx->IASetVertexBuffers(0, 1, &b, &st, &off); }
    public static void DrawMesh(GAlloc a, uint vbytes, uint count, uint first, Vk.IndexType it)
    {
        var b = (ID3D11Buffer*)a.dxbuf; SetVB(b, 0); ctx->IASetIndexBuffer(b, it == Vk.IndexType.Uint16 ? DxFmt.FormatR16Uint : DxFmt.FormatR32Uint, vbytes);
        PreDraw(); ctx->DrawIndexed(count, first, 0);
    }
    public static void DrawShared(GAlloc v, GAlloc ix, Vk.IndexType t, uint count, uint first)
    {
        SetVB((ID3D11Buffer*)v.dxbuf, 0); ctx->IASetIndexBuffer((ID3D11Buffer*)ix.dxbuf, t == Vk.IndexType.Uint16 ? DxFmt.FormatR16Uint : DxFmt.FormatR32Uint, 0);
        PreDraw(); ctx->DrawIndexed(count, first, 0);
    }
    public static void DrawFull() { PreDraw(); ctx->Draw(3, 0); }
    static uint Ring(void* src, uint bytes)
    {
        bytes = (bytes + 15) & ~15u; if (bytes > DynSize) throw new Exception("dynamic draw too big");
        var mode = Map.WriteNoOverwrite; if (dynOff + bytes > DynSize) { mode = Map.WriteDiscard; dynOff = 0; }
        MappedSubresource ms; Chk(ctx->Map((ID3D11Resource*)dynVB, 0, mode, 0, &ms), "map dynamic");
        System.Buffer.MemoryCopy(src, (byte*)ms.PData + dynOff, bytes, bytes); ctx->Unmap((ID3D11Resource*)dynVB, 0);
        uint o = dynOff; dynOff += bytes; return o;
    }
    public static void DrawDynamic(ReadOnlySpan<float> data, uint vcount)
    {
        if (vcount == 0) return; uint o; fixed (float* p = data) o = Ring(p, (uint)data.Length * 4);
        SetVB(dynVB, o); PreDraw(); ctx->Draw(vcount, 0);
    }
    public static void DrawDynamicIndexedBytes(byte[] data, int bytes, GAlloc ix, uint count)
    {
        if (count == 0) return; uint o; fixed (byte* p = data) o = Ring(p, (uint)bytes);
        SetVB(dynVB, o); ctx->IASetIndexBuffer((ID3D11Buffer*)ix.dxbuf, DxFmt.FormatR16Uint, 0); PreDraw(); ctx->DrawIndexed(count, 0, 0);
    }
    public static void DrawDynamicIndexed(float[] data, int floats, GAlloc ix, uint count, uint first)
    {
        uint o; fixed (float* p = data) o = Ring(p, (uint)floats * 4);
        SetVB(dynVB, o); ctx->IASetIndexBuffer((ID3D11Buffer*)ix.dxbuf, DxFmt.FormatR16Uint, 0); PreDraw(); ctx->DrawIndexed(count, first, 0);
    }
}
