using System.Diagnostics;
using Silk.NET.Vulkan;

namespace CubePlanet;

/// <summary>Sun position, time of day, atmosphere / cloud layer heights and the ambient wind drift.</summary>
public static class Sky
{
    // sun orbits in a plane tilted from the planet's equator
    internal static readonly V3 SA = new(1, 0, 0); internal static V3 SB = M.Nrm(new V3(0, Math.Sin(.41), -Math.Cos(.41))); public static V3 SUN = new(1, 0, 0); public static double SUNA;
    public static void SetTilt(double v) { double t = v * PI / 180; SB = M.Nrm(new V3(0, Math.Sin(t), -Math.Cos(t))); }
    public static void SunVec() { double c = Math.Cos(SUNA), s = Math.Sin(SUNA); SUN = new V3(SA.X * c + SB.X * s, SA.Y * c + SB.Y * s, SA.Z * c + SB.Z * s); }
    internal static double NoonAng() { double r = Math.Sqrt(C.x * C.x + C.y * C.y + C.z * C.z); if (r == 0) r = 1; V3 g = new(C.x / r, C.y / r, C.z / r); return Math.Atan2(M.Dot(g, SB), M.Dot(g, SA)); }
    public static double LocalHour() { double h = 12 + ((SUNA - NoonAng()) / (2 * PI)) * 24; return ((h % 24) + 24) % 24; }
    public static void SetHour(double h) { SUNA = NoonAng() + (h - 12) / 24 * 2 * PI; SunVec(); }

    // ambient (non-interactive) atmosphere constants: a fixed clear day with a few clouds and a light breeze (weather system is not part of this port)
    internal const double CLOUD_COV = .28, OVERCAST = .1, WIND = .15, SEA = .12, WIND_DIR_DEG = 60;
    internal static double[] cloudDrift = new double[3], windW = new double[3], windD = new double[3]; internal static V3 windDir = new(1, 0, 0);
    internal static double wvT, wvLast;

    /// <summary>Called once the planet radius is known.</summary>

    public static void MakeAtmosphereResources()
    {
        double CH = HMAX * 1.05 + 12, CT = Math.Max(90, HMAX * .9); // cloud layer thick enough for towering cumulus
        RC0 = R + CH; RC1 = RC0 + CT; AT = Math.Max(R * .22, CH + CT + 90);
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
}
