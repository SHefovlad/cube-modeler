using System.Runtime.InteropServices;
using SkiaSharp;

namespace Modeler;

/// <summary>
/// Ортографический растеризатор с per-pixel z-буфером и под-пиксельным сглаживанием (SSAA).
/// Порядок треугольников НЕ важен (побеждает ближайший фрагмент), поэтому корректно
/// рисуются вложенные, стоящие вплотную и ПЕРЕСЕКАЮЩИЕСЯ кубы — в отличие от painter's algorithm.
///
/// Кадр отдаётся сырыми пикселями BGRA (<see cref="RenderPixels"/>): интерактивное окно кладёт
/// его в WriteableBitmap одной копией, без PNG-encode/decode на каждый кадр.
/// </summary>
public static class ZRenderer
{
    /// <summary>Сэмплов на сторону по умолчанию (×9). Меньше — быстрее, но с зубцами.</summary>
    public const int DefaultSamples = 3;

    /// <summary>Отрисовать сцену в canvas (для PNG и плиток).</summary>
    public static void Draw(
        SKCanvas canvas, int width, int height, IReadOnlyList<WorldCube> cubes,
        Vec3 forward, Vec3 up, Vec3 right, SKColor background, SKColor? forceColor = null,
        double margin = 24, int samples = DefaultSamples)
    {
        var pixels = RenderPixels(width, height, cubes, forward, up, right, background, forceColor, margin, samples);
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        canvas.DrawBitmap(bitmap, 0, 0);
    }

    /// <summary>
    /// Отрисовать сцену в сырые пиксели BGRA (premultiplied), длина = width*height*4.
    /// Без промежуточного PNG — интерактивный кадр не кодируется/декодируется зря.
    /// </summary>
    public static byte[] RenderPixels(
        int width, int height, IReadOnlyList<WorldCube> cubes,
        Vec3 forward, Vec3 up, Vec3 right, SKColor background, SKColor? forceColor = null,
        double margin = 24, int samples = DefaultSamples)
    {
        int ss = Math.Max(1, samples);
        int sw = width * ss;
        int sh = height * ss;
        var depth = new float[sw * sh];
        var color = new uint[sw * sh];
        Array.Fill(depth, float.MaxValue);
        Array.Fill(color, Pack(background));

        var outPixels = new byte[width * height * 4];

        // Габариты всех углов кубов (от того же focus, что и отрисовка) → масштаб и смещение.
        var focus = BoundsCenter(cubes);
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var cube in cubes)
        {
            foreach (var p in CubeGeometry.Corners(cube, out _))
            {
                var local = p - focus;
                double sx = Dot(local, right);
                double sy = -Dot(local, up);
                minX = Math.Min(minX, sx); maxX = Math.Max(maxX, sx);
                minY = Math.Min(minY, sy); maxY = Math.Max(maxY, sy);
            }
        }

        if (minX <= maxX)
        {
            double spanX = Math.Max(maxX - minX, 1e-9);
            double spanY = Math.Max(maxY - minY, 1e-9);
            double m = margin * ss;
            double scale = Math.Min((sw - 2 * m) / spanX, (sh - 2 * m) / spanY);
            double offX = sw / 2.0 - (minX + maxX) / 2.0 * scale;
            double offY = sh / 2.0 - (minY + maxY) / 2.0 * scale;

            foreach (var cube in cubes)
            {
                var corners = CubeGeometry.Corners(cube, out _);
                var baseColor = forceColor ?? Shading.ParseColor(cube.Color);

                for (int f = 0; f < CubeGeometry.Faces.Length; f++)
                {
                    var normal = Shading.Normalize(cube.Rotation.Rotate(CubeGeometry.FaceNormals[f]));

                    // Отсечение задних граней: у выпуклого куба они всегда закрыты его же
                    // передними гранями, поэтому на картинку не влияют, а работы вдвое меньше.
                    if (Dot(normal, forward) >= -1e-9) continue;

                    var idx = CubeGeometry.Faces[f];
                    uint faceFill = Pack(Shading.Apply(baseColor, Shading.FactorFor(normal)));

                    var a = Project(corners[idx[0]], focus, right, up, forward, scale, offX, offY);
                    var b = Project(corners[idx[1]], focus, right, up, forward, scale, offX, offY);
                    var c = Project(corners[idx[2]], focus, right, up, forward, scale, offX, offY);
                    var d = Project(corners[idx[3]], focus, right, up, forward, scale, offX, offY);

                    Raster(depth, color, sw, sh, a, b, c, faceFill);
                    Raster(depth, color, sw, sh, a, c, d, faceFill);
                }
            }
        }

        // Даунсэмпл ×ss и упаковка BGRA (в памяти: B, G, R, A).
        int sampleCount = ss * ss;
        int o = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                uint v;
                if (ss == 1)
                {
                    v = color[y * sw + x];
                }
                else
                {
                    uint r = 0, g = 0, bb = 0, al = 0;
                    for (int sy = 0; sy < ss; sy++)
                    {
                        int row = (y * ss + sy) * sw + x * ss;
                        for (int sx = 0; sx < ss; sx++)
                        {
                            uint s = color[row + sx];
                            al += (s >> 24) & 0xff;
                            r += (s >> 16) & 0xff;
                            g += (s >> 8) & 0xff;
                            bb += s & 0xff;
                        }
                    }
                    v = (uint)((al / sampleCount) << 24 | (r / sampleCount) << 16
                        | (g / sampleCount) << 8 | bb / sampleCount);
                }

                outPixels[o++] = (byte)(v & 0xff);
                outPixels[o++] = (byte)((v >> 8) & 0xff);
                outPixels[o++] = (byte)((v >> 16) & 0xff);
                outPixels[o++] = (byte)((v >> 24) & 0xff);
            }
        }

        return outPixels;
    }

    private readonly record struct P(float X, float Y, float Z);

    private static P Project(Vec3 world, Vec3 focus, Vec3 right, Vec3 up, Vec3 forward,
        double scale, double offX, double offY)
    {
        var local = world - focus;
        float sx = (float)(Dot(local, right) * scale + offX);
        float sy = (float)(-Dot(local, up) * scale + offY);
        float sz = (float)Dot(local, forward);
        return new P(sx, sy, sz);
    }

    /// <summary>Растеризовать треугольник a,b,c с интерполяцией глубины; ближе — побеждает.</summary>
    private static void Raster(float[] depth, uint[] color, int sw, int sh, P a, P b, P c, uint fill)
    {
        float area = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
        if (Math.Abs(area) < 1e-9f) return;
        if (area < 0) (b, c) = (c, b);

        int minX = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))));
        int maxX = Math.Min(sw - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))));
        int maxY = Math.Min(sh - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));

        for (int y = minY; y <= maxY; y++)
        {
            float py = y + 0.5f;
            int row = y * sw;
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f;
                float z = InterpolateDepth(
                    a.X, a.Y, a.Z, b.X, b.Y, b.Z, c.X, c.Y, c.Z, px, py, out bool inside);
                if (!inside) continue;

                int index = row + x;
                if (z < depth[index])
                {
                    depth[index] = z;
                    color[index] = fill;
                }
            }
        }
    }

    private static Vec3 BoundsCenter(IReadOnlyList<WorldCube> cubes)
    {
        var min = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
        var max = new Vec3(double.MinValue, double.MinValue, double.MinValue);
        foreach (var cube in cubes)
        {
            // Для повёрнутых кубов центр габарита считаем по 8 углам — точно.
            foreach (var p in CubeGeometry.Corners(cube, out _))
            {
                min = new Vec3(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                max = new Vec3(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
            }
        }
        return new Vec3((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2);
    }

    private static uint Pack(SKColor c) => (uint)((c.Alpha << 24) | (c.Red << 16) | (c.Green << 8) | c.Blue);

    private static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    /// <summary>
    /// Барицентрические веса и глубина точки (px,py) в треугольнике. Веса — (λa, λb, λc);
    /// <paramref name="inside"/> — лежит ли точка в треугольнике (с допуском на общем ребре).
    /// Глубина интерполируется ПО ВЕРШИНАМ: λa·az + λb·bz + λc·cz.
    /// Вынесено отдельно, чтобы это можно было покрыть тестом.
    /// </summary>
    public static float InterpolateDepth(
        float ax, float ay, float az, float bx, float by, float bz, float cx, float cy, float cz,
        float px, float py, out bool inside)
    {
        float area = (bx - ax) * (cy - ay) - (cx - ax) * (by - ay);
        if (Math.Abs(area) < 1e-9f) { inside = false; return float.NaN; }

        // w0 = λc, w1 = λb, w2 = λa.
        float w0 = ((bx - ax) * (py - ay) - (px - ax) * (by - ay)) / area;
        float w1 = ((px - ax) * (cy - ay) - (cx - ax) * (py - ay)) / area;
        float w2 = 1f - w0 - w1;

        // Допуск на общем ребре: на центрах пикселей вес выходит за [0,1] на 1e-6..1e-4
        // из-за float-округления — без допуска часть пикселей на стыках пропускалась.
        const float eps = 1e-4f;
        inside = w0 >= -eps && w1 >= -eps && w2 >= -eps;
        return w2 * az + w1 * bz + w0 * cz;
    }
}
