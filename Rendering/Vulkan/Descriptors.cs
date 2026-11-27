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

/// <summary>Descriptor set layouts, pools and writes.</summary>
public static unsafe partial class Gpu
{
    // ======================================================================= descriptors
    static void CreateDescriptorLayout()
    {
        var all = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit;
        var b = stackalloc DescriptorSetLayoutBinding[9];
        b[0] = new DescriptorSetLayoutBinding { Binding = 0, DescriptorType = DescriptorType.UniformBuffer, DescriptorCount = 1, StageFlags = all };
        for (uint i = 1; i < 9; i++) b[i] = new DescriptorSetLayoutBinding { Binding = i, DescriptorType = DescriptorType.CombinedImageSampler, DescriptorCount = 1, StageFlags = all };
        var li = new DescriptorSetLayoutCreateInfo { SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = 9, PBindings = b };
        Check(vk.CreateDescriptorSetLayout(dev, &li, null, out dsl), "dsl");
        var b1 = stackalloc DescriptorSetLayoutBinding[5];
        for (uint i = 0; i < 5; i++) b1[i] = new DescriptorSetLayoutBinding { Binding = i, DescriptorType = DescriptorType.CombinedImageSampler, DescriptorCount = 1, StageFlags = all };
        var li1 = new DescriptorSetLayoutCreateInfo { SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = 5, PBindings = b1 };
        Check(vk.CreateDescriptorSetLayout(dev, &li1, null, out dsl1), "dsl1");
        var pcr = new PushConstantRange { StageFlags = all, Offset = 0, Size = 32 };
        var ls = stackalloc DescriptorSetLayout[2]; ls[0] = dsl; ls[1] = dsl1;
        var pli = new PipelineLayoutCreateInfo { SType = StructureType.PipelineLayoutCreateInfo, SetLayoutCount = 2, PSetLayouts = ls, PushConstantRangeCount = 1, PPushConstantRanges = &pcr };
        Check(vk.CreatePipelineLayout(dev, &pli, null, out playout), "pipeline layout");
        var ps = stackalloc DescriptorPoolSize[2];
        ps[0] = new DescriptorPoolSize(DescriptorType.UniformBuffer, FIF); ps[1] = new DescriptorPoolSize(DescriptorType.CombinedImageSampler, FIF * 8 + FIF * PSETS * 5);
        var dpi = new DescriptorPoolCreateInfo { SType = StructureType.DescriptorPoolCreateInfo, MaxSets = FIF + FIF * PSETS, PoolSizeCount = 2, PPoolSizes = ps };
        Check(vk.CreateDescriptorPool(dev, &dpi, null, out dpool), "descriptor pool");
        var lays = stackalloc DescriptorSetLayout[FIF]; for (int i = 0; i < FIF; i++) lays[i] = dsl;
        var dai = new DescriptorSetAllocateInfo { SType = StructureType.DescriptorSetAllocateInfo, DescriptorPool = dpool, DescriptorSetCount = FIF, PSetLayouts = lays };
        fixed (DescriptorSet* p = dsets) Check(vk.AllocateDescriptorSets(dev, &dai, p), "descriptor sets");
        var l1 = stackalloc DescriptorSetLayout[FIF * PSETS]; for (int i = 0; i < FIF * PSETS; i++) l1[i] = dsl1;
        var tmp = new DescriptorSet[FIF * PSETS];
        var dai1 = new DescriptorSetAllocateInfo { SType = StructureType.DescriptorSetAllocateInfo, DescriptorPool = dpool, DescriptorSetCount = FIF * PSETS, PSetLayouts = l1 };
        fixed (DescriptorSet* p = tmp) Check(vk.AllocateDescriptorSets(dev, &dai1, p), "post descriptor sets");
        for (int f = 0; f < FIF; f++) for (int i = 0; i < PSETS; i++) psets[f, i] = tmp[f * PSETS + i];
    }
    static void WriteImg(DescriptorSet s, uint binding, Sampler smp, ImageView v)
    {
        var ii = new DescriptorImageInfo(smp, v, ImageLayout.ShaderReadOnlyOptimal);
        var w = new WriteDescriptorSet { SType = StructureType.WriteDescriptorSet, DstSet = s, DstBinding = binding, DescriptorCount = 1, DescriptorType = DescriptorType.CombinedImageSampler, PImageInfo = &ii };
        vk.UpdateDescriptorSets(dev, 1, &w, 0, null);
    }
    /// <summary>(Re)writes every descriptor: call once every texture exists, and after resize / aniso changes (with the GPU idle).</summary>
    public static void WriteDescriptors()
    {
        if (Dx.On) return;
        if (sceneCol[0] == null) return;
        for (int f = 0; f < FIF; f++)
        {
            var bi = new DescriptorBufferInfo(ubo[f], 0, UBO_FLOATS * 4);
            var w = new WriteDescriptorSet { SType = StructureType.WriteDescriptorSet, DstSet = dsets[f], DstBinding = 0, DescriptorCount = 1, DescriptorType = DescriptorType.UniformBuffer, PBufferInfo = &bi };
            vk.UpdateDescriptorSets(dev, 1, &w, 0, null);
            WriteImg(dsets[f], 1, anisoOn ? blockAniso : blockPlain, texBlocks.view);
            WriteImg(dsets[f], 2, shadow.smp, shadow.view);
            WriteImg(dsets[f], 3, texNoise.smp, texNoise.view);
            WriteImg(dsets[f], 4, texFont.smp, texFont.view);
            int pf = (f + 1) % FIF; // previous frame's targets
            WriteImg(dsets[f], 5, linClamp, sceneCol[pf].view);
            WriteImg(dsets[f], 6, linClamp, sceneDist[pf].view);
            WriteImg(dsets[f], 7, texCloud.smp, texCloud.view);
            WriteImg(dsets[f], 8, texItems.smp, texItems.view); // item icons (layer = item ID)
            // post sets (bindings: 0/1 = per-pass inputs, 2 = cloud colour, 3 = cloud distance, 4 = scene distance)
            // 0 = bright-pass from the scene, 1..5 = downsample from mip i-1, 6..10 = upsample into mip 4..0 from the mip below, 11 = composite, 12 = clouds
            for (int i = 0; i < PSETS; i++) { WriteImg(psets[f, i], 2, linClamp, cloudCol.view); WriteImg(psets[f, i], 3, linClamp, cloudDist.view); WriteImg(psets[f, i], 4, linClamp, sceneDist[f].view); }
            WriteImg(psets[f, 0], 0, linClamp, sceneCol[f].view); WriteImg(psets[f, 0], 1, linClamp, sceneCol[f].view);
            for (int m = 1; m < BLM; m++) { WriteImg(psets[f, m], 0, linClamp, bloomViews[m - 1]); WriteImg(psets[f, m], 1, linClamp, bloomViews[m - 1]); }
            for (int k = BLM - 2, i = 0; k >= 0; k--, i++) { WriteImg(psets[f, BLM + i], 0, linClamp, bloomViews[k + 1]); WriteImg(psets[f, BLM + i], 1, linClamp, bloomViews[k + 1]); }
            WriteImg(psets[f, 11], 0, linClamp, sceneCol[f].view); WriteImg(psets[f, 11], 1, linClamp, bloomViews[0]);
            WriteImg(psets[f, 12], 0, linClamp, sceneDist[f].view); WriteImg(psets[f, 12], 1, linClamp, sceneDist[f].view);
        }
    }
    public static bool AnisoSupported => !Dx.On && anisoSupported;
    public static void SetAniso(bool on)
    {
        if (Dx.On) return;
        if (on == anisoOn) return; anisoOn = on;
        if (texBlocks == null) return;
        vk.DeviceWaitIdle(dev); WriteDescriptors();
    }
}
