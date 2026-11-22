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

/// <summary>Graphics pipeline creation.</summary>
public static unsafe partial class Gpu
{
    public static Pipe MakePipe(string name, string vs, string fs, PipeCfg c)
    {
        var vm = Module(Spirv(vs, ShaderKind.VertexShader, name + ".vert")); var fm = Module(Spirv(fs, ShaderKind.FragmentShader, name + ".frag"));
        var main = (byte*)SilkMarshal.StringToPtr("main");
        var st = stackalloc PipelineShaderStageCreateInfo[2];
        st[0] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.VertexBit, Module = vm, PName = main };
        st[1] = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo, Stage = ShaderStageFlags.FragmentBit, Module = fm, PName = main };
        (uint loc, Format f, uint off)[] attrs; uint stride;
        switch (c.layout)
        {
            case VL.Chunk: stride = 16; attrs = new[] { (0u, Format.R16G16B16A16Sint, 0u), (1u, Format.R16G16Uint, 8u), (2u, Format.R8G8B8A8SNorm, 12u) }; break;
            case VL.ChunkShadow: stride = 16; attrs = new[] { (0u, Format.R16G16B16A16Sint, 0u), (1u, Format.R16G16Uint, 8u) }; break;
            case VL.Lod: stride = 48; attrs = new[] { (0u, Format.R32G32B32Sfloat, 0u), (1u, Format.R32G32Sfloat, 12u), (2u, Format.R32G32B32Sfloat, 20u), (3u, Format.R32Sfloat, 32u), (4u, Format.R32G32B32Sfloat, 36u) }; break;
            case VL.LodSea: stride = 48; attrs = new[] { (0u, Format.R32G32B32Sfloat, 0u), (3u, Format.R32Sfloat, 32u) }; break;
            case VL.LodShadow: stride = 48; attrs = new[] { (0u, Format.R32G32B32Sfloat, 0u) }; break;
            case VL.Pos3: stride = 12; attrs = new[] { (0u, Format.R32G32B32Sfloat, 0u) }; break;
            case VL.UI: stride = 40; attrs = new[] { (0u, Format.R32G32Sfloat, 0u), (1u, Format.R32G32Sfloat, 8u), (2u, Format.R32G32B32A32Sfloat, 16u), (3u, Format.R32G32Sfloat, 32u) }; break;
            default: stride = 0; attrs = Array.Empty<(uint, Format, uint)>(); break;
        }
        var bind = new VertexInputBindingDescription(0, stride, VertexInputRate.Vertex);
        var ad = stackalloc VertexInputAttributeDescription[Math.Max(1, attrs.Length)];
        for (int i = 0; i < attrs.Length; i++) ad[i] = new VertexInputAttributeDescription(attrs[i].loc, 0, attrs[i].f, attrs[i].off);
        var vin = new PipelineVertexInputStateCreateInfo { SType = StructureType.PipelineVertexInputStateCreateInfo, VertexBindingDescriptionCount = attrs.Length > 0 ? 1u : 0u, PVertexBindingDescriptions = &bind, VertexAttributeDescriptionCount = (uint)attrs.Length, PVertexAttributeDescriptions = ad };
        var ia = new PipelineInputAssemblyStateCreateInfo { SType = StructureType.PipelineInputAssemblyStateCreateInfo, Topology = c.topo };
        var vps = new PipelineViewportStateCreateInfo { SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, ScissorCount = 1 };
        bool shadowPass = c.pass == RP.Shadow, scene = c.pass == RP.Scene;
        // Vulkan measures winding in Y-down framebuffer space: with the Y-flipped main projection GL CCW faces stay CCW, the unflipped shadow pass sees them CW
        var rs = new PipelineRasterizationStateCreateInfo
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo, PolygonMode = PolygonMode.Fill, CullMode = c.cull, FrontFace = shadowPass ? FrontFace.Clockwise : FrontFace.CounterClockwise, LineWidth = 1,
            DepthBiasEnable = c.depthBias, DepthBiasConstantFactor = shadowPass ? 1.5f : -3, DepthBiasSlopeFactor = shadowPass ? 1.5f : -3,
        };
        var msi = new PipelineMultisampleStateCreateInfo { SType = StructureType.PipelineMultisampleStateCreateInfo, RasterizationSamples = scene ? samples : SampleCountFlags.Count1Bit };
        bool hasDepth = scene || shadowPass;
        var ds = new PipelineDepthStencilStateCreateInfo { SType = StructureType.PipelineDepthStencilStateCreateInfo, DepthTestEnable = hasDepth && c.depthTest, DepthWriteEnable = hasDepth && c.depthWrite, DepthCompareOp = c.cmp };
        var cba = stackalloc PipelineColorBlendAttachmentState[2];
        cba[0] = new PipelineColorBlendAttachmentState { ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit | ColorComponentFlags.BBit | ColorComponentFlags.ABit, BlendEnable = c.blend != BlendMode.None };
        // alpha channel of the scene target = how much of the sky is covered (the god-ray pass only marches through open sky)
        if (c.blend == BlendMode.Alpha) { cba[0].SrcColorBlendFactor = BlendFactor.SrcAlpha; cba[0].DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha; cba[0].SrcAlphaBlendFactor = BlendFactor.One; cba[0].DstAlphaBlendFactor = BlendFactor.OneMinusSrcAlpha; cba[0].ColorBlendOp = cba[0].AlphaBlendOp = BlendOp.Add; }
        if (c.blend == BlendMode.Add) { cba[0].SrcColorBlendFactor = BlendFactor.One; cba[0].DstColorBlendFactor = BlendFactor.One; cba[0].SrcAlphaBlendFactor = BlendFactor.Zero; cba[0].DstAlphaBlendFactor = BlendFactor.One; cba[0].ColorBlendOp = cba[0].AlphaBlendOp = BlendOp.Add; }
        cba[1] = new PipelineColorBlendAttachmentState { ColorWriteMask = c.writeDist ? ColorComponentFlags.RBit : 0, BlendEnable = false };
        if (c.pass == RP.Cloud) cba[1].ColorWriteMask = ColorComponentFlags.RBit;
        var cbs2 = new PipelineColorBlendStateCreateInfo { SType = StructureType.PipelineColorBlendStateCreateInfo, AttachmentCount = shadowPass ? 0u : (scene || c.pass == RP.Cloud) ? 2u : 1u, PAttachments = cba };
        var dyn = stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor };
        var dsi = new PipelineDynamicStateCreateInfo { SType = StructureType.PipelineDynamicStateCreateInfo, DynamicStateCount = 2, PDynamicStates = dyn };
        var rp = c.pass switch { RP.Shadow => shadowRP, RP.Post => postRP, RP.PostAdd => postAddRP, RP.Final => finalRP, RP.Cloud => cloudRP, _ => sceneRP };
        var gpi = new GraphicsPipelineCreateInfo
        {
            SType = StructureType.GraphicsPipelineCreateInfo, StageCount = 2, PStages = st, PVertexInputState = &vin, PInputAssemblyState = &ia, PViewportState = &vps,
            PRasterizationState = &rs, PMultisampleState = &msi, PDepthStencilState = &ds, PColorBlendState = &cbs2, PDynamicState = &dsi, Layout = playout,
            RenderPass = rp, Subpass = 0,
        };
        Check(vk.CreateGraphicsPipelines(dev, pcache, 1, &gpi, null, out var pl), "pipeline " + name);
        vk.DestroyShaderModule(dev, vm, null); vk.DestroyShaderModule(dev, fm, null); SilkMarshal.Free((nint)main);
        return new Pipe { p = pl, name = name };
    }
}
