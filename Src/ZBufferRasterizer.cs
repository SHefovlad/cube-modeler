using SkiaSharp;

namespace Modeler;

/// <summary>
/// Программный растеризатор с z-буфером (ортографическая камера). Заменяет painter's
/// algorithm: порядок граней не важен — для каждого пикселя берётся ближайший фрагмент.
/// Поэтому корректно рисуются вложенные, стоящие вплотную и ПЕРЕСЕКАЮЩИЕСЯ кубы.
/// </summary>
public sealed class ZBufferRasterizer
{
    private readonly int _width;
    private readonly int _height;
    private readonly float[] _depth;      // глубина ближайшего фрагмента (меньше = ближе)
    private readonly SKColor[] _color;    // цвет ближайшего фрагмента
    private readonly float _scale;
    private readonly float _offX;
    private readonly float _offY;
    private readonly Vec3 _focus;
    private readonly Vec3 _right;
    private readonly Vec3 _up;
    private readonly Vec3 _forward;

    public ZBufferRasterizer(int width, int height, Vec3 focus, double scale, Vec2 offset,
        Vec3 forward, Vec3 up, Vec3 right, SKColor background)
    {
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);
        _depth = new float[_width * _height];
        _color = new SKColor[_width * _height];
        Array.Fill(_depth, float.MaxValue);
        Array.Fill(_color, background);

        _scale = (float)scale;
        _offX = (float)offset.X;
        _offY = (float)offset.Y;
        _focus = focus;
        _forward = forward;
        _up = up;
        _right = right;
    }

    /// <summary>Спроецировать мировую точку в экранные координаты и глубину.</summary>
    private (float x, float y, float z) Project(Vec3 p)
    {
        var local = p - _focus;
        float sx = (float)Vec3Dot(local, _right);
        float sy = (float)Vec3Dot(local, _up);
        float sz = (float)Vec3Dot(local, _forward);
        return (sx * _scale + _offX, -sy * _scale + _offY, sz);
    }

    /// <summary>Добавить треугольник (мировые точки, цвет). Нормаль влияет на затенение.</summary>
    public void AddTriangle(Vec3 a, Vec3 b, Vec3 c, SKColor baseColor, Vec3 normal)
    {
        var (ax, ay, az) = Project(a);
        var (bx, by, bz) = Project(b);
        var (cx, cy, cz) = Project(c);

        // Отсечение задних граней (для пересечений всё равно: z-буфер выберет ближний).
        var fill = Shading.Apply(baseColor, Shading.FactorFor(normal));

        float area = (bx - ax) * (cy - ay) - (cx - ax) * (by - ay);
        if (Math.Abs(area) < 1e-9f) return;
        if (area < 0)
        {
            (bx, by, bz, cx, cy, cz) = (cx, cy, cz, bx, by, bz);
            area = -area;
        }

        int minX = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx))));
        int maxX = Math.Min(_width - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy))));
        int maxY = Math.Min(_height - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));

        for (int y = minY; y <= maxY; y++)
        {
            float py = y + 0.5f;
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f;

                // Барицентрические координаты.
                float w0 = ((bx - ax) * (py - ay) - (px - ax) * (by - ay)) / area;
                float w1 = ((px - ax) * (cy - ay) - (cx - ax) * (py - ay)) / area;
                float w2 = 1f - w0 - w1;
                if (w0 < 0f || w1 < 0f || w2 < 0f) continue;

                // Глубина интерполируется как 1/z для корректности, но при ортографике
                // линейная интерполяция z точна — оставляем линейную.
                float z = w1 * az + w0 * bz + w2 * cz;

                int index = y * _width + x;
                if (z < _depth[index])
                {
                    _depth[index] = z;
                    _color[index] = fill;
                }
            }
        }
    }

    /// <summary>Записать буферы кадра в canvas (пиксель за пикселем).</summary>
    public void Blit(SKCanvas canvas)
    {
        using var bitmap = new SKBitmap(_width, _height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var pixels = bitmap.GetPixelSpan();
        var buffer = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(pixels);

        for (int i = 0; i < _color.Length; i++)
        {
            var c = _color[i];
            buffer[i] = (uint)((c.Alpha << 24) | (c.Red << 16) | (c.Green << 8) | c.Blue);
        }

        canvas.DrawBitmap(bitmap, 0, 0);
    }

    private static double Vec3Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}
