using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;

namespace Modeler;

/// <summary>
/// Оконный просмотрщик модели: рисует все виды прямо на экране (без промежуточного PNG)
/// и перерисовывает их при сохранении .cubes.
/// </summary>
public sealed class ViewerWindow : Window
{
    private static readonly (ViewKind Kind, string Label)[] Panels =
    {
        (ViewKind.Front, "Front (-Z)"),
        (ViewKind.Back, "Back (+Z)"),
        (ViewKind.Left, "Left (-X)"),
        (ViewKind.Right, "Right (+X)"),
        (ViewKind.Top, "Top (+Y)"),
        (ViewKind.Iso, "Iso 52/45"),
    };

    private readonly string _cubesPath;
    private readonly SKColor? _forceColor;
    private readonly Image _image = new();
    private readonly TextBlock _status = new();
    private WriteTimeSet _timestamps = new();

    private sealed class WriteTimeSet
    {
        public DateTime Cubes;
        public readonly List<(string Path, DateTime Time)> Includes = new();
    }

    public ViewerWindow(string cubesPath, SKColor? forceColor)
    {
        _cubesPath = Path.GetFullPath(cubesPath);
        _forceColor = forceColor;

        Title = "Modeler — " + Path.GetFileName(_cubesPath);
        Width = 1180;
        Height = 820;
        Background = new SolidColorBrush(Color.Parse("#f2f4f7"));

        _image.Stretch = Stretch.Uniform;
        _status.Margin = new Thickness(10, 6, 10, 0);
        _status.FontFamily = new FontFamily("Segoe UI");
        _status.Foreground = new SolidColorBrush(Color.Parse("#333333"));

        Content = new DockPanel
        {
            Children =
            {
                TopDock(_status),
                _image,
            },
        };

        Opened += (_, _) =>
        {
            Reload();
            StartPolling();
        };
    }

    private static Control TopDock(Control child)
    {
        DockPanel.SetDock(child, Dock.Top);
        return child;
    }

    private void StartPolling()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) =>
        {
            if (Changed()) Reload();
        };
        timer.Start();
    }

    private bool Changed()
    {
        if (!File.Exists(_cubesPath)) return false;

        var cubesTime = File.GetLastWriteTimeUtc(_cubesPath);
        if (cubesTime != _timestamps.Cubes)
        {
            _timestamps.Cubes = cubesTime;
            return true;
        }

        // Перечитать список include-файлов и их время.
        var includes = Resolver.CollectIncludes(_cubesPath);
        if (includes.Count != _timestamps.Includes.Count) return RefreshIncludes(includes);

        for (int i = 0; i < includes.Count; i++)
            if (includes[i].Path != _timestamps.Includes[i].Path ||
                includes[i].Time != _timestamps.Includes[i].Time)
                return RefreshIncludes(includes);

        return false;
    }

    private bool RefreshIncludes(List<(string Path, DateTime Time)> includes)
    {
        _timestamps.Includes.Clear();
        _timestamps.Includes.AddRange(includes);
        return true;
    }

    private void Reload()
    {
        try
        {
            var roots = Resolver.Load(_cubesPath);
            var cubes = TransformResolver.Resolve(roots);
            _timestamps = new WriteTimeSet
            {
                Cubes = File.GetLastWriteTimeUtc(_cubesPath),
            };
            _timestamps.Includes.AddRange(Resolver.CollectIncludes(_cubesPath));
            _image.Source = Compose(cubes);
            _status.Text = $"{Path.GetFileName(_cubesPath)}   ·   кубов: {cubes.Count}   ·   обновлено {DateTime.Now:HH:mm:ss}";
        }
        catch (CubeParseException ex)
        {
            _image.Source = ErrorBitmap(ex.Message);
            _status.Text = "Ошибка: " + ex.Message;
        }
        catch (IOException)
        {
            // Файл занят редактором — попробуем на следующем тике.
        }
    }

    private WriteableBitmap Compose(List<WorldCube> cubes)
    {
        const int cols = 3;
        const int w = 380, h = 380, pad = 8;
        int rows = (Panels.Length + cols - 1) / cols;
        int width = cols * w + (cols + 1) * pad;
        int height = rows * h + (rows + 1) * pad;

        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0xf2, 0xf4, 0xf7));

        using var titleFont = new SKFont(SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default, 18);
        using var titlePaint = new SKPaint { Color = new SKColor(0x33, 0x33, 0x33), IsAntialias = true };

        for (int i = 0; i < Panels.Length; i++)
        {
            float x = pad + i % cols * (w + pad);
            float y = pad + i / cols * (h + pad);

            using (var bg = new SKPaint { Color = SKColors.White })
                canvas.DrawRect(x, y, w, h, bg);

            canvas.Save();
            canvas.Translate(x, y);
            canvas.ClipRect(new SKRect(0, 0, w, h));
            Renderer.Draw(canvas, w, h, cubes, Panels[i].Kind, _forceColor);
            canvas.Restore();

            canvas.DrawText(Panels[i].Label, x + 10, y + 24, titleFont, titlePaint);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream();
        data.SaveTo(ms);
        ms.Position = 0;
        return WriteableBitmap.Decode(ms);
    }

    private static WriteableBitmap ErrorBitmap(string message)
    {
        using var surface = SKSurface.Create(new SKImageInfo(900, 200));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0x2b, 0x1d, 0x1d));

        using var font = new SKFont(SKTypeface.FromFamilyName("Consolas") ?? SKTypeface.Default, 20);
        using var paint = new SKPaint { Color = new SKColor(0xff, 0x8a, 0x80), IsAntialias = true };
        canvas.DrawText("Ошибка в .cubes: " + message, 20, 100, font, paint);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream();
        data.SaveTo(ms);
        ms.Position = 0;
        return WriteableBitmap.Decode(ms);
    }
}
