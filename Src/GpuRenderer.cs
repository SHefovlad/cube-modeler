using System.Runtime.InteropServices;
using SkiaSharp;

namespace Modeler;

/// <summary>
/// Рендер через Skia: кубы отдаются как многоугольники, сглаживание делает Skia
/// (суперсэмплингом). Skia здесь без буфера глубины, поэтому порядок отрисовки задаёт
/// BSP-дерево: оно строится один раз на модель и обходится от дальнего к ближнему.
/// Это корректно для любого угла камеры и для пересекающихся граней — в отличие от
/// сортировки треугольников, которая верна только для «удачных» ракурсов.
/// </summary>
public static class GpuRenderer
{
    /// <summary>
    /// Построить BSP-дерево модели в мировых координатах. Вызывать при загрузке/перезагрузке
    /// файла, а не на каждый кадр: сцены меняются редко, а камера — постоянно.
    /// </summary>
    public static BspTree BuildTree(IReadOnlyList<WorldCube> cubes, SKColor? forceColor)
    {
        var polys = new List<BspPoly>();

        foreach (var cube in cubes)
        {
            var corners = CubeGeometry.Corners(cube, out _);
            var baseColor = forceColor ?? Shading.ParseColor(cube.Color);

            for (int f = 0; f < CubeGeometry.Faces.Length; f++)
            {
                var normal = Shading.Normalize(cube.Rotation.Rotate(CubeGeometry.FaceNormals[f]));
                var fill = Shading.Apply(baseColor, Shading.FactorFor(normal));
                var face = CubeGeometry.Faces[f];

                polys.Add(new BspPoly(new[]
                {
                    corners[face[0]], corners[face[1]], corners[face[2]], corners[face[3]],
                }, fill, normal));
            }
        }

        return BspTree.Build(polys);
    }

    public static SKBitmap Render(int width, int height, IReadOnlyList<WorldCube> cubes,
        Vec3 forward, Vec3 up, Vec3 right, SKColor background, SKColor? forceColor = null,
        double margin = 16, double msaa = 4, double zoom = 1.0, double panX = 0, double panY = 0,
        BspTree? tree = null)
    {
        int ss = Math.Clamp((int)Math.Round(msaa), 1, 16);
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

        // MSAA на растровой поверхности Skia не поддерживает: SKSurface.Create(info, ss)
        // возвращает null без GPU-бэкенда. Поэтому сэмплируем сами — рисуем в буфер
        // в ss раз больше и усредняем пиксели.
        int bw = width * ss, bh = height * ss;
        using var surface = SKSurface.Create(new SKImageInfo(bw, bh, SKColorType.Bgra8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Не удалось создать поверхность Skia.");

        var canvas = surface.Canvas;
        canvas.Clear(background);
        canvas.Scale(ss);

        var (scale, offX, offY, focus) = Layout(cubes, width, height, forward, up, right, margin, zoom, panX, panY);
        if (scale <= 0) return ToBitmap(surface, width, height, ss);

        Draw(canvas, cubes, focus, forward, up, right, scale, offX, offY, forceColor, tree);

        return ToBitmap(surface, width, height, ss);
    }

    /// <summary>
    /// Нарисовать сцену в уже подготовленный canvas (координаты — целевого кадра).
    /// Вынесено, чтобы обе ветки — своя поверхность и предварительный проход —
    /// рисовали ровно одним и тем же путём.
    /// </summary>
    private static void Draw(SKCanvas canvas, IReadOnlyList<WorldCube> cubes, Vec3 focus,
        Vec3 forward, Vec3 up, Vec3 right, double scale, double offX, double offY,
        SKColor? forceColor, BspTree? tree)
    {
        using var paint = new SKPaint { IsAntialias = false };
        using var path = new SKPath();

        if (tree is not null)
        {
            // Камера ортографическая: для обхода дерева достаточно любой точки на луче
            // взгляда. Берём её далеко позади сцены, чтобы дерево считало её «снаружи».
            var eye = new Vec3(focus.X - forward.X * 1e4, focus.Y - forward.Y * 1e4, focus.Z - forward.Z * 1e4);
            tree.Draw(canvas, eye, forward, focus, right, up, scale, offX, offY, paint, path);
            return;
        }

        // Skia без буфера глубины рисует в порядке перечисления. Порядок задаём сами и
        // сортируем по НАИБОЛЕЕ УДАЛЁННОЙ вершине треугольника, а не по средней глубине:
        // у больших наклонных граней средняя врёт, и доска под крышей «проступает».
        var tris = new List<Tri>();

        foreach (var cube in cubes)
        {
            var corners = CubeGeometry.Corners(cube, out _);
            var baseColor = forceColor ?? Shading.ParseColor(cube.Color);

            for (int f = 0; f < CubeGeometry.Faces.Length; f++)
            {
                var normal = Shading.Normalize(cube.Rotation.Rotate(CubeGeometry.FaceNormals[f]));

                if (Shading.Dot(normal, forward) >= -1e-9) continue;

                var fill = Shading.Apply(baseColor, Shading.FactorFor(normal));
                var face = CubeGeometry.Faces[f];

                var a = Project(corners[face[0]], focus, right, up, forward, scale, offX, offY);
                var b = Project(corners[face[1]], focus, right, up, forward, scale, offX, offY);
                var c = Project(corners[face[2]], focus, right, up, forward, scale, offX, offY);
                var d = Project(corners[face[3]], focus, right, up, forward, scale, offX, offY);

                tris.Add(new Tri(a.Point, b.Point, c.Point, fill,
                    Math.Max(a.Z, Math.Max(b.Z, c.Z)), Math.Min(a.Z, Math.Min(b.Z, c.Z))));
                tris.Add(new Tri(a.Point, c.Point, d.Point, fill,
                    Math.Max(a.Z, Math.Max(c.Z, d.Z)), Math.Min(a.Z, Math.Min(c.Z, d.Z))));
            }
        }

        if (tris.Count == 0) return;

        tris.Sort((x, y) =>
        {
            int byFar = y.Far.CompareTo(x.Far);
            return byFar != 0 ? byFar : y.Near.CompareTo(x.Near);
        });

        // Резервный путь без BSP: сортировка по дальнему углу. Годится, но может врать
        // на произвольных углах — поэтому в окне всегда передаётся дерево.
        foreach (var t in tris)
        {
            path.Reset();
            path.MoveTo(t.A);
            path.LineTo(t.B);
            path.LineTo(t.C);
            path.Close();

            paint.Color = t.Fill;
            canvas.DrawPath(path, paint);
        }
    }

    private readonly record struct Tri(SKPoint A, SKPoint B, SKPoint C, SKColor Fill,
        double Far, double Near);

    /// <summary>
    /// Снять поверхность в битmap целевого размера. При ss > 1 усредняет блок ss×ss:
    /// это и есть сглаживание краёв (как SSAA в CPU-режиме).
    /// </summary>
    private static SKBitmap ToBitmap(SKSurface surface, int width, int height, int ss)
    {
        int bw = width * ss, bh = height * ss;
        var bigInfo = new SKImageInfo(bw, bh, SKColorType.Bgra8888, SKAlphaType.Premul);
        var result = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));

        using var image = surface.Snapshot();
        using var big = new SKBitmap(bigInfo);
        if (!image.ReadPixels(bigInfo, big.GetPixels(), bigInfo.RowBytes, 0, 0))
            return result;

        var rowScratch = new byte[big.RowBytes];

        if (ss == 1)
        {
            // Читаем пиксели напрямую из образа в целевой битмап: строка за строкой.
            int rowBytes = Math.Min(big.RowBytes, result.RowBytes);
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(IntPtr.Add(big.GetPixels(), y * big.RowBytes), rowScratch, 0, rowBytes);
                Marshal.Copy(rowScratch, 0, IntPtr.Add(result.GetPixels(), y * result.RowBytes), rowBytes);
            }
            return result;
        }

        unsafe
        {
            byte* src = (byte*)big.GetPixels();
            byte* dst = (byte*)result.GetPixels();
            int srcRow = big.RowBytes;
            int dstRow = result.RowBytes;
            int sampleCount = ss * ss;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int r = 0, g = 0, b = 0, al = 0;
                    for (int sy = 0; sy < ss; sy++)
                    {
                        byte* row = src + (y * ss + sy) * srcRow + x * ss * 4;
                        for (int sx = 0; sx < ss; sx++)
                        {
                            b += row[sx * 4];
                            g += row[sx * 4 + 1];
                            r += row[sx * 4 + 2];
                            al += row[sx * 4 + 3];
                        }
                    }
                    byte* d = dst + y * dstRow + x * 4;
                    d[0] = (byte)(b / sampleCount);
                    d[1] = (byte)(g / sampleCount);
                    d[2] = (byte)(r / sampleCount);
                    d[3] = (byte)(al / sampleCount);
                }
            }
        }
        return result;
    }

    private static (SKPoint Point, double Z) Project(Vec3 world, Vec3 focus, Vec3 right, Vec3 up,
        Vec3 forward, double scale, double offX, double offY)
    {
        var local = world - focus;
        float sx = (float)(Shading.Dot(local, right) * scale + offX);
        float sy = (float)(-Shading.Dot(local, up) * scale + offY);
        return (new SKPoint(sx, sy), Shading.Dot(local, forward));
    }

    private static (double Scale, double OffX, double OffY, Vec3 Focus) Layout(
        IReadOnlyList<WorldCube> cubes, int width, int height,
        Vec3 forward, Vec3 up, Vec3 right, double margin, double zoom, double panX, double panY)
    {
        if (cubes.Count == 0) return (-1, 0, 0, Vec3.Zero);

        var focus = BoundsCenter(cubes);
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var cube in cubes)
        {
            foreach (var p in CubeGeometry.Corners(cube, out _))
            {
                var local = p - focus;
                double sx = Shading.Dot(local, right);
                double sy = -Shading.Dot(local, up);
                minX = Math.Min(minX, sx); maxX = Math.Max(maxX, sx);
                minY = Math.Min(minY, sy); maxY = Math.Max(maxY, sy);
            }
        }

        if (minX > maxX) return (-1, 0, 0, focus);

        double spanX = Math.Max(maxX - minX, 1e-9);
        double spanY = Math.Max(maxY - minY, 1e-9);
        double scale = Math.Min((width - 2 * margin) / spanX, (height - 2 * margin) / spanY)
            * Math.Max(0.02, zoom);
        double offX = width / 2.0 - (minX + maxX) / 2.0 * scale + panX * zoom;
        double offY = height / 2.0 - (minY + maxY) / 2.0 * scale + panY * zoom;
        return (scale, offX, offY, focus);
    }

    private static Vec3 BoundsCenter(IReadOnlyList<WorldCube> cubes)
    {
        var min = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
        var max = new Vec3(double.MinValue, double.MinValue, double.MinValue);
        foreach (var cube in cubes)
        {
            foreach (var p in CubeGeometry.Corners(cube, out _))
            {
                min = new Vec3(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                max = new Vec3(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
            }
        }
        return new Vec3((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2);
    }
}
