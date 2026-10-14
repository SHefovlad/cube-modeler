namespace Modeler;

/// <summary>Вектор из трёх чисел (позиция, размер или углы Эйлера).</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);
    public static readonly Vec3 One = new(1, 1, 1);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, Vec3 b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);

    public override string ToString() =>
        "(" + Fmt(X) + ", " + Fmt(Y) + ", " + Fmt(Z) + ")";

    private static string Fmt(double v) =>
        v == Math.Floor(v) && Math.Abs(v) < 1e15
            ? ((long)v).ToString()
            : v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
}
