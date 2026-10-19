using SkiaSharp;

namespace Modeler;

/// <summary>
/// Проверка GPU-пути без окна: рендер в PNG, чтобы можно было сравнить с CPU-эталоном.
/// Запуск: Modeler --gpu-render file.cubes out.png [msaa] [yaw] [pitch]
/// </summary>
public static class GpuProbe
{
    public static int Run(string cubesPath, string outPath, int msaa, double yaw = double.NaN,
        double pitch = double.NaN, bool useTree = true)
    {
        var roots = Resolver.Load(cubesPath);
        var cubes = TransformResolver.Resolve(roots);

        var (forward, up, right) = double.IsNaN(yaw)
            ? Projector.IsoAxes(0)
            : Projector.IsoAxesByYawPitch(yaw, double.IsNaN(pitch) ? -Projector.IsoPitch : pitch);

        // Дерево строим всегда — в окне оно тоже строится на загрузку модели.
        var tree = useTree ? GpuRenderer.BuildTree(cubes, null) : null;

        using var bitmap = GpuRenderer.Render(1120, 430, cubes, forward, up, right,
            new SKColor(0xf2, 0xf4, 0xf7), null, margin: 16, msaa: msaa, tree: tree);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(outPath);
        data.SaveTo(stream);
        Console.WriteLine($"[GpuProbe] {outPath} (кубов: {cubes.Count}, msaa: {msaa}, "
            + (double.IsNaN(yaw) ? "iso" : $"yaw {yaw:0} pitch {(double.IsNaN(pitch) ? -Projector.IsoPitch : pitch):0}") + ")");
        return 0;
    }
}
