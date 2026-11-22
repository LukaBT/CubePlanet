namespace CubePlanet;

/// <summary>Double precision 3-vector (the planet is large, so the CPU side works in doubles).</summary>
public struct V3
{
    public double X, Y, Z;
    public V3(double x, double y, double z) { X = x; Y = y; Z = z; }
    public static V3 operator +(V3 a, V3 b) => new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static V3 operator -(V3 a, V3 b) => new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static V3 operator -(V3 a) => new V3(-a.X, -a.Y, -a.Z);
    public static V3 operator *(V3 a, double s) => new V3(a.X * s, a.Y * s, a.Z * s);
    public static V3 operator *(double s, V3 a) => new V3(a.X * s, a.Y * s, a.Z * s);
    public static V3 operator /(V3 a, double s) => new V3(a.X / s, a.Y / s, a.Z / s);
    public double Len => Math.Sqrt(X * X + Y * Y + Z * Z);
    public double Len2 => X * X + Y * Y + Z * Z;
    public double this[int i] => i == 0 ? X : i == 1 ? Y : Z;
    public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
}
