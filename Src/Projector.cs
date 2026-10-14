namespace Modeler;

/// <summary>Простой 2D-вектор экрана (после проекции).</summary>
public readonly record struct Vec2(double X, double Y)
{
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
}

/// <summary>Плоскость вида и углы камеры.</summary>
public enum ViewKind { Front, Back, Left, Right, Top, Bottom, Iso }

/// <summary>
/// Проекция 3D → 2D для ортогональных видов и изометрии.
/// Изометрия: pitch 52°, yaw 45° — те же углы, что у камеры в игре.
/// Экран: X вправо, Y вниз (как в растровом изображении).
/// </summary>
public sealed class Projector
{
    public const double IsoPitch = 52.0;
    public const double IsoYaw = 45.0;

    private readonly Quat _rot;
    private readonly Vec3 _focus;

    /// <summary>Масштаб: пикселей на юнит.</summary>
    public double Scale { get; }

    public Projector(ViewKind kind, Vec3 focus, double scale)
    {
        _focus = focus;
        Scale = scale;
        _rot = RotationFor(kind);
    }

    /// <summary>Спроецировать мировую точку. Возвращает экранные координаты (без сдвига в центр).</summary>
    public Vec2 Project(Vec3 world)
    {
        var local = world - _focus;
        var cam = _rot.Rotate(local);

        // Экранный Y — инвертированный cam.Y (вверх по миру = вверх по экрану).
        // Depth (cam.Z) хранится отдельно в вызывающем коде для сортировки.
        return new Vec2(cam.X * Scale, -cam.Y * Scale);
    }

    /// <summary>Глубина (cam.Z) мировой точки: больше = ближе к камере.</summary>
    public double Depth(Vec3 world)
    {
        var local = world - _focus;
        return _rot.Rotate(local).Z;
    }

    /// <summary>Поворот камеры вида (мировой вектор → координаты камеры).</summary>
    public static Quat RotationFor(ViewKind kind) => kind switch
    {
        ViewKind.Front  => Quat.Euler(new Vec3(0, 0, 0)),
        ViewKind.Back   => Quat.Euler(new Vec3(0, 180, 0)),
        ViewKind.Left   => Quat.Euler(new Vec3(0, -90, 0)),
        ViewKind.Right  => Quat.Euler(new Vec3(0, 90, 0)),
        ViewKind.Top    => Quat.Euler(new Vec3(90, 0, 0)),
        ViewKind.Bottom => Quat.Euler(new Vec3(-90, 0, 0)),
        ViewKind.Iso    => Quat.Euler(new Vec3(IsoPitch, IsoYaw, 0)),
        _ => Quat.Identity,
    };

    /// <summary>Единичное направление «к камере» в мировых координатах.</summary>
    public static Quat InverseRotationFor(ViewKind kind) => Invert(RotationFor(kind));

    private static Quat Invert(Quat q) => new(-q.X, -q.Y, -q.Z, q.W);
}
