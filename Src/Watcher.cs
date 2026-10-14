using System.Diagnostics;
using SkiaSharp;

namespace Modeler;

/// <summary>
/// Живой просмотрщик: перерисовывает PNG при изменении .cubes (вотчер файла).
/// Открывает картинку в программе просмотра по умолчанию; при изменении файла
/// перерисовывает и, если тот же просмотрщик не обновляет картинку сам, перезапускает его.
/// </summary>
public static class Watcher
{
    public static int Run(string cubesPath, string outPath, SKColor color, bool open)
    {
        string fullPath = Path.GetFullPath(cubesPath);
        if (!File.Exists(fullPath))
        {
            Console.Error.WriteLine($"Файл не найден: {fullPath}");
            return 2;
        }

        Console.WriteLine($"Модель:   {fullPath}");
        Console.WriteLine($"Превью:   {Path.GetFullPath(outPath)}");
        Console.WriteLine("Сохрани .cubes — картинка обновится. Ctrl+C для выхода.");

        RenderOnce(fullPath, outPath, color);
        if (open) OpenViewer(outPath);

        var lastWrite = File.GetLastWriteTimeUtc(fullPath);
        while (true)
        {
            Thread.Sleep(250);
            if (!File.Exists(fullPath)) continue;

            var write = File.GetLastWriteTimeUtc(fullPath);
            if (write == lastWrite) continue;
            lastWrite = write;

            // Небольшая пауза: редактор мог ещё держать файл на запись.
            Thread.Sleep(120);
            RenderOnce(fullPath, outPath, color);
            if (open) OpenViewer(outPath);
        }
    }

    private static void RenderOnce(string cubesPath, string outPath, SKColor color)
    {
        try
        {
            PreviewSheet.Render(cubesPath, outPath, color);
        }
        catch (CubeParseException ex)
        {
            // Ошибку показываем в консоли и в картинке-заглушке, чтобы было видно глазом.
            Console.Error.WriteLine("Ошибка разбора: " + ex.Message);
            ErrorImage.Render(outPath, ex.Message);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine("Файл занят: " + ex.Message);
        }
    }

    private static void OpenViewer(string outPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Path.GetFullPath(outPath)) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Не удалось открыть просмотрщик: " + ex.Message);
        }
    }
}

/// <summary>Картинка с текстом ошибки — вместо модели, когда .cubes не разбирается.</summary>
public static class ErrorImage
{
    public static void Render(string outPath, string message)
    {
        const int width = 900, height = 300;
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0x2b, 0x1d, 0x1d));

        using var title = new SKFont(SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default, 26);
        using var body = new SKFont(SKTypeface.FromFamilyName("Consolas") ?? SKTypeface.Default, 18);
        using var paint = new SKPaint { Color = new SKColor(0xff, 0x8a, 0x80), IsAntialias = true };

        canvas.DrawText("Ошибка в .cubes", 30, 60, title, paint);
        canvas.DrawText(message, 30, 120, body, paint);

        var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(outPath);
        data.SaveTo(stream);
    }
}
