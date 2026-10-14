using SkiaSharp;

namespace Modeler;

/// <summary>
/// Рендер кубической модели: painter's algorithm по граням с плоской закраской и
/// направленным светом. Падающих теней нет.
/// </summary>
public static class Renderer
{
    private sealed class Face
    {
        public Vec3[] WorldPoints = Array.Empty<Vec3>();
        public Vec2[] ScreenPoints = Array.Empty<Vec2>();
        public Vec3 WorldNormal;
        public double Depth;
    }

    /// <summary>
    /// Нарисовать модель на канвасе. <paramref name="color"/> задаёт цвет куба (по умолчанию серый).
    /// </summary>
    public static void Draw(SKCanvas canvas, int width, int height, List<WorldCube> cubes,
        ViewKind view, SKColor color, double margin = 24)
    {
        var camRot = Projector.RotationFor(view);
        var faces = new List<Face>();

        foreach (var cube in cubes)
        {
            var corners = CubeGeometry.Corners(cube, out _);
            for (int f = 0; f < CubeGeometry.Faces.Length; f++)
            {
                var idx = CubeGeometry.Faces[f];
                var normal = Shading.Normalize(cube.Rotation.Rotate(CubeGeometry.FaceNormals[f]));

                // Отсечение задних граней: нормаль в координатах камеры должна смотреть на неё.
                if (camRot.Rotate(normal).Z <= 1e-9) continue;

                var world = new Vec3[4];
                for (int i = 0; i < 4; i++) world[i] = corners[idx[i]];

                faces.Add(new Face { WorldPoints = world, WorldNormal = normal });
            }
        }

        if (faces.Count == 0) return;

        // Вписать все мировые точки в кадр, посчитать масштаб.
        var all = new List<Vec3>(faces.Count * 4);
        foreach (var face in faces) all.AddRange(face.WorldPoints);
        var (focus, scale, offset) = Fit(all, view, width, height, margin);

        var projector = new Projector(view, focus, scale);

        foreach (var face in faces)
        {
            face.ScreenPoints = new Vec2[4];
            double depth = 0;
            for (int i = 0; i < 4; i++)
            {
                face.ScreenPoints[i] = projector.Project(face.WorldPoints[i]) + offset;
                depth += projector.Depth(face.WorldPoints[i]);
            }
            face.Depth = depth / 4.0;
        }

        faces.Sort((a, b) => a.Depth.CompareTo(b.Depth)); // дальние первыми

        foreach (var face in faces)
        {
            double factor = Shading.FactorFor(face.WorldNormal);
            var fill = Shading.Apply(color, factor);

            using var path = new SKPath();
            path.MoveTo((float)face.ScreenPoints[0].X, (float)face.ScreenPoints[0].Y);
            for (int i = 1; i < 4; i++)
                path.LineTo((float)face.ScreenPoints[i].X, (float)face.ScreenPoints[i].Y);
            path.Close();

            using var paint = new SKPaint
            {
                Color = fill,
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
            };
            canvas.DrawPath(path, paint);
        }
    }

    private static (Vec3 focus, double scale, Vec2 offset) Fit(List<Vec3> points, ViewKind view,
        int width, int height, double margin)
    {
        var center = Bounds(points, out _, out _);

        // Проецируем без масштаба, чтобы измерить габарит.
        var probe = new Projector(view, center, 1.0);
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in points)
        {
            var s = probe.Project(p);
            minX = Math.Min(minX, s.X); maxX = Math.Max(maxX, s.X);
            minY = Math.Min(minY, s.Y); maxY = Math.Max(maxY, s.Y);
        }

        double spanX = Math.Max(maxX - minX, 1e-6);
        double spanY = Math.Max(maxY - minY, 1e-6);
        double scale = Math.Min((width - 2 * margin) / spanX, (height - 2 * margin) / spanY);

        double offX = width / 2.0 - (minX + maxX) / 2.0 * scale;
        double offY = height / 2.0 - (minY + maxY) / 2.0 * scale;

        return (center, scale, new Vec2(offX, offY));
    }

    private static Vec3 Bounds(List<Vec3> points, out Vec3 min, out Vec3 max)
    {
        min = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
        max = new Vec3(double.MinValue, double.MinValue, double.MinValue);
        foreach (var p in points)
        {
            min = new Vec3(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
            max = new Vec3(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
        }
        return new Vec3((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2);
    }
}
