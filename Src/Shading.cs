using SkiaSharp;

namespace Modeler;

/// <summary>Цвета и множители освещения граней по нормали (без падающих теней).</summary>
public static class Shading
{
    /// <summary>Направление света (из мира наружу, к источнику). Немного сверху и с -X/-Z.</summary>
    public static readonly Vec3 LightDirection = Normalize(new Vec3(-0.4, 0.82, -0.4));

    /// <summary>Базовая засветка, чтобы тёмные грани не были чёрными.</summary>
    public const double Ambient = 0.35;

    /// <summary>Доля направленного света.</summary>
    public const double Diffuse = 0.75;

    /// <summary>Множитель для грани по её мировой нормали.</summary>
    public static double FactorFor(Vec3 worldNormal)
    {
        double d = Dot(Normalize(worldNormal), LightDirection);
        if (d < 0) d = 0;
        double f = Ambient + Diffuse * d;
        return f > 1.25 ? 1.25 : f;
    }

    /// <summary>Применить множитель к цвету.</summary>
    public static SKColor Apply(SKColor color, double factor)
    {
        byte c(double v) => (byte)Math.Clamp(Math.Round(v * factor), 0, 255);
        return new SKColor(c(color.Red), c(color.Green), c(color.Blue), color.Alpha);
    }

    // --- Векторные помощники ---
    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static Vec3 Normalize(Vec3 v)
    {
        double len = Math.Sqrt(Dot(v, v));
        return len < 1e-12 ? v : new Vec3(v.X / len, v.Y / len, v.Z / len);
    }
}
