namespace Modeler;

/// <summary>Кватернион для поворотов моделей (порядок Эйлера — как в Unity).</summary>
public readonly record struct Quat(double X, double Y, double Z, double W)
{
    public static readonly Quat Identity = new(0, 0, 0, 1);

    /// <summary>Угол в градусах вокруг оси.</summary>
    public static Quat AngleAxis(double degrees, Vec3 axis)
    {
        double len = Math.Sqrt(axis.X * axis.X + axis.Y * axis.Y + axis.Z * axis.Z);
        if (len < 1e-12) return Identity;
        double half = degrees * Math.PI / 360.0;
        double s = Math.Sin(half) / len;
        return new Quat(axis.X * s, axis.Y * s, axis.Z * s, Math.Cos(half));
    }

    /// <summary>Эйлеровы углы в градусах в кватернион, порядок применения Z→X→Y (Unity).</summary>
    public static Quat Euler(Vec3 e) =>
        AngleAxis(e.Y, new Vec3(0, 1, 0))
        * AngleAxis(e.X, new Vec3(1, 0, 0))
        * AngleAxis(e.Z, new Vec3(0, 0, 1));

    /// <summary>Произведение кватернионов: применяет правый поворот после левого.</summary>
    public static Quat operator *(Quat a, Quat b) => new(
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

    /// <summary>Повернуть вектор.</summary>
    public Vec3 Rotate(Vec3 v)
    {
        // t = 2 * (q_vec × v); v' = v + w*t + q_vec × t
        double tx = 2 * (Y * v.Z - Z * v.Y);
        double ty = 2 * (Z * v.X - X * v.Z);
        double tz = 2 * (X * v.Y - Y * v.X);
        return new Vec3(
            v.X + W * tx + (Y * tz - Z * ty),
            v.Y + W * ty + (Z * tx - X * tz),
            v.Z + W * tz + (X * ty - Y * tx));
    }
}
