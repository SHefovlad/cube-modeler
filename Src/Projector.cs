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
/// Оси камеры берутся один в один из Unity (см. <see cref="CameraAxes"/>), поэтому
/// ракурс совпадает с игрой: камера смотрит на фасад (-Z) и повёрнута к -X.
/// Экран: X вправо, Y вниз (как в растровом изображении).
/// </summary>
public sealed class Projector
{
    public const double IsoPitch = 52.0;
    public const double IsoYaw = 45.0;

    /// <summary>Поворот камеры вида (мировой вектор → координаты камеры).</summary>
    public static Quat RotationFor(ViewKind kind) => kind switch
    {
        ViewKind.Front  => Quat.Euler(new Vec3(0, 0, 0)),
        ViewKind.Back   => Quat.Euler(new Vec3(0, 180, 0)),
        ViewKind.Left   => Quat.Euler(new Vec3(0, -90, 0)),
        ViewKind.Right  => Quat.Euler(new Vec3(0, 90, 0)),
        ViewKind.Top    => Quat.Euler(new Vec3(90, 0, 0)),
        ViewKind.Bottom => Quat.Euler(new Vec3(-90, 0, 0)),

        // Изометрия как в игре: камера смотрит на фасад (-Z), повёрнута к -X.
        // Единственная версия, совпавшая с рендером игры (после ручной правки камеры автором):
        // компас мировой X→экран (−0.707, −0.364), Y→(0, 0.788), Z→(0.707, −0.364).
        // Реализация — прямая таблица осей камеры, см. <see cref="CameraAxes"/>.
        ViewKind.Iso    => Quat.Euler(new Vec3(IsoPitch, IsoYaw, 0)),
        _ => Quat.Identity,
    };

    /// <summary>Инверсия кватерниона (обратный поворот).</summary>
    public static Quat Inverse(Quat q) => new(-q.X, -q.Y, -q.Z, q.W);

    /// <summary>Камера вида в мировых осях: куда смотрит (forward), вверх (up), вправо (right).</summary>
    public static (Vec3 Forward, Vec3 Up, Vec3 Right) CameraAxes(ViewKind kind)
    {
        if (kind == ViewKind.Iso)
        {
            // Камера наклонена вниз на 52°, азимут берётся у первого угла (45°) по умолчанию.
            return IsoAxes(0);
        }

        var camera = Inverse(RotationFor(kind));
        return (
            Normalize(camera.Rotate(new Vec3(0, 0, 1))),
            Normalize(camera.Rotate(new Vec3(0, 1, 0))),
            Normalize(camera.Rotate(new Vec3(1, 0, 0))));
    }

    /// <summary>Текущие азимуты изометрии (4 угла по 45°, 135°, 225°, 315°).</summary>
    /// <summary>Список азимутов (yaw) для режима углов: 45, 135, 225, 315 — смотрим на 4 боковых угла.</summary>
    public static readonly double[] IsoCornerYaws = { 45, 135, 225, 315 };

    /// <summary>Число вариантов режима углов.</summary>
    public const int IsoVariantCount = 4;

    /// <summary>Все азимуты, кратные 45° (0..315) — для большой сетки «все ракурсы».</summary>
    public static readonly double[] AllYaws45 = { 0, 45, 90, 135, 180, 225, 270, 315 };

    /// <summary>Все наклоны (pitch) для большой сетки, от «почти сверху» вниз (в игре знак — отрицательный).</summary>
    public static readonly double[] AllPitches = { -75, -60, -52, -45, -30, -15, 0, 15, 30, 60 };

    /// <summary>Оси изометрии для yaw-угла (наклон взят с отрицательным знаком: камера СВЕРХУ, смотрит вниз).</summary>
    public static (Vec3 Forward, Vec3 Up, Vec3 Right) IsoAxesByYaw(double yaw)
        => IsoAxesByYawPitch(yaw, IsoPitch);

    /// <summary>Оси для произвольного азимута (yaw) и наклона (pitch).</summary>
    public static (Vec3 Forward, Vec3 Up, Vec3 Right) IsoAxesByYawPitch(double yaw, double pitch)
    {
        // Наклон (pitch) — вокруг ЭКРАННОЙ горизонтали, уже после поворота на азимут (yaw).
        // В нашем Quat «правый множитель применяется первым», поэтому порядок:
        //   pitch * yaw  =  сначала yaw (вокруг вертикали), затем pitch (от камеры вниз).
        var orbit = Quat.Euler(new Vec3(pitch, 0, 0)) * Quat.Euler(new Vec3(0, yaw, 0));

        // Камера смотрит ИЗ orbit*back В фокус: её оси = оси поворота orbit^−1.
        var cam = Inverse(orbit);
        return (
            Normalize(cam.Rotate(new Vec3(0, 0, 1))),
            Normalize(cam.Rotate(new Vec3(0, 1, 0))),
            Normalize(cam.Rotate(new Vec3(1, 0, 0))));
    }

    /// <summary>Оси режима углов для варианта 0..3.</summary>
    public static (Vec3 Forward, Vec3 Up, Vec3 Right) IsoAxes(int variant)
    {
        int i = ((variant % IsoVariantCount) + IsoVariantCount) % IsoVariantCount;
        return IsoAxesByYaw(IsoCornerYaws[i]);
    }

    /// <summary>Смотрит ли мировая нормаль на камеру заданного вида.</summary>
    public static bool CameraAxesFacing(ViewKind kind, Vec3 worldNormal)
    {
        var (forward, _, _) = CameraAxes(kind);
        return forward.X * worldNormal.X + forward.Y * worldNormal.Y + forward.Z * worldNormal.Z < -1e-9;
    }

    private static Vec3 Normalize(Vec3 v)
    {
        double len = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return len < 1e-12 ? v : new Vec3(v.X / len, v.Y / len, v.Z / len);
    }

    private readonly Vec3 _focus;
    private readonly Vec3 _forward, _up, _right;

    /// <summary>Масштаб: пикселей на юнит.</summary>
    public double Scale { get; }

    public Projector(ViewKind kind, Vec3 focus, double scale)
    {
        _focus = focus;
        Scale = scale;
        (_forward, _up, _right) = CameraAxes(kind);
    }

    /// <summary>Спроецировать мировую точку. Возвращает экранные координаты (без сдвига в центр).</summary>
    public Vec2 Project(Vec3 world)
    {
        var local = world - _focus;

        double sx = Dot(local, _right);   // вправо
        double sy = Dot(local, _up);      // вверх по миру
        double sz = Dot(local, _forward); // больше = глубже

        return new Vec2(sx * Scale, -sy * Scale);
    }

    /// <summary>Глубина мировой точки: больше = дальше от камеры.</summary>
    public double Depth(Vec3 world)
    {
        var local = world - _focus;
        return Dot(local, _forward);
    }

    /// <summary>Смотрит ли нормаль на камеру (отсечение задних граней).</summary>
    public bool FacesCamera(Vec3 worldNormal) => Dot(Normalize(worldNormal), _forward) < -1e-9;

    private static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}
