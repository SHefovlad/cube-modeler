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
        public SKColor Color;
        public double Depth;
    }

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
        var faces = new List<Face>();

        foreach (var cube in cubes)
        {
            var corners = CubeGeometry.Corners(cube, out _);
            var color = forceColor ?? Shading.ParseColor(cube.Color);

            for (int f = 0; f < CubeGeometry.Faces.Length; f++)
            {
                var idx = CubeGeometry.Faces[f];
                var normal = Shading.Normalize(cube.Rotation.Rotate(CubeGeometry.FaceNormals[f]));

                // Отсечение задних граней: нормаль должна смотреть на камеру (против взгляда).
                if (Dot(normal, forward) >= -1e-9) continue;

                var world = new Vec3[4];
                for (int i = 0; i < 4; i++) world[i] = corners[idx[i]];

                faces.Add(new Face { WorldPoints = world, WorldNormal = normal, Color = color });
            }
        }

        if (faces.Count == 0) return;

        var all = new List<Vec3>(faces.Count * 4);
        foreach (var face in faces) all.AddRange(face.WorldPoints);
        var (focus, scale, offset) = Fit(all, forward, up, right, width, height, margin);

        foreach (var face in faces)
        {
            face.ScreenPoints = new Vec2[4];
            double depth = 0;
            for (int i = 0; i < 4; i++)
            {
                var local = face.WorldPoints[i] - focus;
                double sx = Dot(local, right);
                double sy = Dot(local, up);
                double sz = Dot(local, forward);
                face.ScreenPoints[i] = new Vec2(sx * scale + offset.X, -sy * scale + offset.Y);
                depth += sz;
            }
            face.Depth = depth / 4.0;
        }

        faces.Sort((a, b) => b.Depth.CompareTo(a.Depth)); // дальние (большая глубина) первыми

        foreach (var face in faces)
        {
            double factor = Shading.FactorFor(face.WorldNormal);
            var fill = Shading.Apply(face.Color, factor);

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

    private static (Vec3 focus, double scale, Vec2 offset) Fit(List<Vec3> points, Vec3 forward, Vec3 up,
        Vec3 right, int width, int height, double margin)
    {
        var center = Bounds(points, out _, out _);

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in points)
        {
            var local = p - center;
            double sx = Dot(local, right);
            double sy = -Dot(local, up);
            minX = Math.Min(minX, sx); maxX = Math.Max(maxX, sx);
            minY = Math.Min(minY, sy); maxY = Math.Max(maxY, sy);
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

    private static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}
