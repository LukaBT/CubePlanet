using System.Diagnostics;
using Silk.NET.Vulkan;

namespace CubePlanet;

/// <summary>Outline of the targeted block.</summary>
public static class Highlight
{
    public static RayHit TG;
    internal static readonly float[] hlV = new float[24]; internal static readonly V3[] hlP = new V3[8];
    internal static void DrawHighlight()
    {
        if (TG == null || C.orbit) return;
        double cx = 0, cy = 0, cz = 0; var sb = BlockBox.Bounds(SelectionOf(TG.id, TG.state)); // outline the selection boxes (their bounding box)
        for (int i = 0; i < 8; i++)
        {
            V3 o;
            if (TG.core) o = new V3(TG.ci + ((i & 1) != 0 ? sb.X1 : sb.X0), TG.cj + ((i & 2) != 0 ? sb.Y1 : sb.Y0), TG.ck + ((i & 4) != 0 ? sb.Z1 : sb.Z0));
            else { Band B = bands[TG.bi]; o = GpA(TG.f, -PI / 4 + (TG.bz + ((i & 1) != 0 ? sb.Z1 : sb.Z0)) * B.d, -PI / 4 + (TG.bx + ((i & 2) != 0 ? sb.X1 : sb.X0)) * B.d, B.inner + TG.by + ((i & 4) != 0 ? sb.Y1 : sb.Y0)); }
            hlP[i] = new V3(o.X - C.x, o.Y - C.y, o.Z - C.z); cx += hlP[i].X; cy += hlP[i].Y; cz += hlP[i].Z;
        }
        cx /= 8; cy /= 8; cz /= 8;
        for (int i = 0; i < 8; i++) { hlV[i * 3] = (float)(cx + (hlP[i].X - cx) * 1.012); hlV[i * 3 + 1] = (float)(cy + (hlP[i].Y - cy) * 1.012); hlV[i * 3 + 2] = (float)(cz + (hlP[i].Z - cz) * 1.012); }
        Gpu.Bind(pHlTri); Gpu.PushK(1f, 1f, 1f, .16f); Gpu.DrawDynamicIndexed(hlV, 24, hlIdx, 36, 0);
        Gpu.Bind(pHlLine); Gpu.PushK(0f, 0f, 0f, .9f); Gpu.DrawDynamicIndexed(hlV, 24, hlIdx, 24, 36);
    }
}
