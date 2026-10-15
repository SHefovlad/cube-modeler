using SkiaSharp;

namespace Modeler;

/// <summary>
/// Рендер кубической модели через <see cref="ZRenderer"/> (z-буфер + SSAA): порядок граней
/// не важен, поэтому корректно рисуются вложенные, стоящие вплотную и пересекающиеся кубы.
/// Плоская закраска по нормалям, падающих теней нет.
/// </summary>
public static class Renderer
{
    /// <summary>Фон превью (общий для всех панелей).</summary>
    public static readonly SKColor Background = new(0xf2, 0xf4, 0xf7);

    /// <summary>
    /// Нарисовать модель вида <paramref name="view"/>; цвет каждого куба берётся из разметки.
    /// </summary>
    public static void Draw(SKCanvas canvas, int width, int height, List<WorldCube> cubes,
        ViewKind view, SKColor? forceColor = null, double margin = 24)
    {
        var (forward, up, right) = Projector.CameraAxes(view);
        DrawWithAxes(canvas, width, height, cubes, forward, up, right, forceColor, margin);
    }

    /// <summary>
    /// Нарисовать модель для произвольных осей камеры (нужно для перебора вариантов изометрии).
    /// forward — направление взгляда; up — верх экрана; right — право экрана (все мировые, единичные).
    /// </summary>
    public static void DrawWithAxes(SKCanvas canvas, int width, int height, List<WorldCube> cubes,
        Vec3 forward, Vec3 up, Vec3 right, SKColor? forceColor = null, double margin = 24)
    {
        ZRenderer.Draw(canvas, width, height, cubes, forward, up, right, Background, forceColor, margin);
    }
}
