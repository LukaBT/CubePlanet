using System.Diagnostics;
using Silk.NET.Vulkan;

namespace CubePlanet;

/// <summary>Per-frame uniforms: one std140 block shared by every pipeline (layout in header.glsl).</summary>
public static class FrameUniforms
{
    // ---------- per-frame uniforms: one std140 block shared by every pipeline (layout in Gpu.HEADER) ----------
    internal static void V3U(int i, double x, double y, double z) { UB[i] = (float)x; UB[i + 1] = (float)y; UB[i + 2] = (float)z; }
    internal static readonly float[] prevVP = new float[16]; internal static V3 prevC; internal static bool havePrev;
    internal static void FillUniforms(double fogK, double tn, double hz)
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
}
