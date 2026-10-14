namespace Modeler;

/// <summary>Вектор из трёх чисел (позиция, размер или углы Эйлера).</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public override string ToString() =>
        "(" + Fmt(X) + ", " + Fmt(Y) + ", " + Fmt(Z) + ")";

    private static string Fmt(double v) =>
        v == Math.Floor(v) && Math.Abs(v) < 1e15
            ? ((long)v).ToString()
            : v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
}
