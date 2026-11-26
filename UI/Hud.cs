namespace CubePlanet;

/// <summary>Text of the HUD (position, mode, FPS, location info).</summary>
public static class Hud
{
    internal static double fps;

    internal static readonly string[] FN = { "+X", "-X", "+Y", "-Y", "+Z", "-Z" };
    internal static string LocInfo()
    {
        V3 p = P.on && !C.orbit ? P.p : new V3(C.x, C.y, C.z); int bi = P2b(p.X, p.Y, p.Z);
        if (bi < 0)
        {
            int i = (int)Math.Floor(p.X), j = (int)Math.Floor(p.Y), k = (int)Math.Floor(p.Z);
            return p.Len2 < CORE * CORE ? $"CARTESIAN CORE (r<{CORE})\nBLOCK x{i} y{j} z{k}\nCHUNK {i >> 5},{j >> 5},{k >> 5}  LOCAL {i & 31},{j & 31},{k & 31}" : "OUTSIDE WORLD";
        }
        Band B = bands[bi]; int pf = PF, px = PX, py = PY, pz = PZ;
        return $"FACE {FN[pf]} (#{pf})  BAND {bi} (r {B.inner}..{B.inner + B.rows})\nBLOCK x{px} y{py} z{pz}\nCHUNK {px >> 5},{py >> 5},{pz >> 5}  LOCAL {px & 31},{py & 31},{pz & 31}\nBIOME {BiomeName(p)}";
    }
    public static string HudText()
    {
        double r = Math.Sqrt(C.x * C.x + C.y * C.y + C.z * C.z); double h = LocalHour();
        string mode = C.orbit ? "ORBIT" : P.on ? (P.fly ? "PLAYER FLY" : P.wet ? "SWIM" : P.gnd ? "WALK" : "AIR") : (C.lvl ? "FREECAM LEVEL" : "FREECAM");
        return $"{mode}  ALT {(r - R):0.0}  R {R}  {(P.on && !C.orbit ? "" : "SPEED x" + spd.ToString("0.00"))}\n" +
               $"LAT {Math.Asin(C.y / r) * 180 / PI:0.0}  LON {Math.Atan2(C.x, C.z) * 180 / PI:0.0}  {(int)h:00}:{(int)((h % 1) * 60):00}\n" +
               $"RD {RD}  G {GravK(r) * GRAV:0.0}  CHUNKS {chunks.Count}  LOD {lodDrawn}  {fps:0} FPS {(SET.vsync > .5 ? "VSYNC" : "UNCAPPED")}\n{LocInfo()}{Target()}";
    }
    /// <summary>The targeted block and its state (doors: half / open / facing, water: level ...).</summary>
    static string Target()
    {
        var t = TG; if (t == null || t.id == 0) return "";
        var d = DEFS[t.id]; string st = d == null || d.State.Packed.Count == 0 ? "" : " " + d.State.Describe((uint)t.state).ToUpperInvariant();
        string more = "";
        if (BEH[t.id] != null && (BlockStates.entities.TryGetValue(BlockPos.Of(t).Key, out var be) || BlockStates.entities.TryGetValue(Electric.MainPart(BlockPos.Of(t)).Key, out be))) { string s = BEH[t.id].Describe(be); if (s.Length > 0) more = "\n" + s; }
        var tp = BlockPos.Of(t);
        if (Wires.IsAnchor(tp)) more += "\n" + Wires.Describe(tp);
        else if (EnergyNet.IsCable(t.id)) more += "\n" + EnergyNet.DescribeCable(tp);
        if (EnergyNet.WrongVoltage.TryGetValue(tp.Key, out var wv)) more += $"\nWRONG VOLTAGE ({Voltage.Name(wv)}): MACHINES RUN ON LV, PUT A TRANSFORMER BETWEEN";
        return $"\nTARGET {(d?.Name ?? BN[t.id]).ToUpperInvariant()}{st}{more}";
    }
    public static void UpdateFps(double dt) { fps += (1 / Math.Max(dt, 1e-3) - fps) * .05; }
}
