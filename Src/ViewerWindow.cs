using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;

namespace Modeler;

/// <summary>
/// Оконный просмотрщик: сверху 6 плиток (Front/Back/Left/Right/Top/Bottom),
/// снизу большое полотно — модель крутится перетаскиванием мыши по горизонтали.
/// Наклон всегда один (игровой, −52°), меняется только азимут.
/// Перерисовка при сохранении .cubes.
/// </summary>
public sealed class ViewerWindow : Window
{
    /// <summary>Наклон камеры: как в игре (−52°). Мышью не меняется.</summary>
    private const double InteractivePitch = -Projector.IsoPitch;

    /// <summary>Сколько градусов азимута на пиксель перетаскивания.</summary>
    private const double YawPerPixel = 0.4;

    private const int TileW = 192, TileH = 208, TilePad = 8, TileCount = 6;
    private const int BigW = 1120, BigH = 430;

    private readonly string _cubesPath;
    private readonly SKColor? _forceColor;
    private readonly bool _isoVariants;

    private readonly Image _tilesImage = new();
    private readonly Image _interactiveImage = new();
    private readonly Border _interactiveHost = new();
    private readonly TextBlock _status = new();
    private readonly TextBlock _yawLabel = new();

    private List<WorldCube> _cubes = new();
    private double _yaw = Projector.IsoYaw;   // стартовый азимут — игровой (45°)
    private bool _dragging;
    private double _lastX;
    private WriteTimeSet _timestamps = new();

    private sealed class WriteTimeSet
    {
        public DateTime Cubes;
        public readonly List<(string Path, DateTime Time)> Includes = new();
    }

    public ViewerWindow(string cubesPath, SKColor? forceColor, bool isoVariants = false)
    {
        _cubesPath = Path.GetFullPath(cubesPath);
        _forceColor = forceColor;
        _isoVariants = isoVariants;

        Title = Path.GetFileName(_cubesPath) + " — Modeler";
        Width = 1180;
        Height = 900;
        Background = new SolidColorBrush(Color.Parse("#f2f4f7"));

        _tilesImage.Stretch = Stretch.Uniform;
        _interactiveImage.Stretch = Stretch.Uniform;

        _interactiveHost.Background = Brushes.Transparent; // чтобы ловил события мыши
        _yawLabel.Margin = new Thickness(12, 8, 0, 0);
        _yawLabel.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        _yawLabel.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        _yawLabel.FontFamily = new FontFamily("Segoe UI");
        _yawLabel.FontSize = 14;
        _yawLabel.Foreground = new SolidColorBrush(Color.Parse("#555555"));
        _yawLabel.IsHitTestVisible = false; // не перехватывает перетаскивание
        _interactiveHost.Child = new Grid { Children = { _interactiveImage, _yawLabel } };
        _interactiveHost.Cursor = new Cursor(StandardCursorType.SizeWestEast);
        _interactiveHost.PointerPressed += OnPressed;
        _interactiveHost.PointerMoved += OnMoved;
        _interactiveHost.PointerReleased += OnReleased;

        _status.Margin = new Thickness(10, 6, 10, 2);
        _status.FontFamily = new FontFamily("Segoe UI");
        _status.Foreground = new SolidColorBrush(Color.Parse("#333333"));

        Content = new DockPanel
        {
            Children =
            {
                TopDock(_status),
                TopDock(_tilesImage),
                _interactiveHost,
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

    // --- Перетаскивание: меняем только азимут ---

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_interactiveHost).Properties.IsLeftButtonPressed) return;
        _dragging = true;
        _lastX = e.GetPosition(_interactiveHost).X;
        e.Pointer.Capture(_interactiveHost);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;
        double x = e.GetPosition(_interactiveHost).X;
        _yaw -= (x - _lastX) * YawPerPixel;
        _lastX = x;
        DragRedraw();
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        RenderInteractive(fast: false); // уточняем картинку после отпускания
        UpdateStatus();
    }

    /// <summary>Перерисовать большое полотно быстро (без сглаживания) на время перетаскивания.</summary>
    private void DragRedraw()
    {
        _interactiveImage.Source = ComposeInteractive(_cubes, fast: true);
        UpdateStatus();
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
            _cubes = TransformResolver.Resolve(roots);
            _timestamps = new WriteTimeSet
            {
                Cubes = File.GetLastWriteTimeUtc(_cubesPath),
            };
            _timestamps.Includes.AddRange(Resolver.CollectIncludes(_cubesPath));

            if (_isoVariants)
            {
                _tilesImage.Source = ComposeIsoVariants(_cubes);
                _interactiveHost.IsVisible = false;
            }
            else
            {
                _tilesImage.Source = ComposeTiles(_cubes);
                _interactiveHost.IsVisible = true;
                RenderInteractive(fast: false);
            }
            UpdateStatus();
        }
        catch (CubeParseException ex)
        {
            _tilesImage.Source = ErrorBitmap(ex.Message);
            _status.Text = "Ошибка: " + ex.Message;
        }
        catch (IOException)
        {
            // Файл занят редактором — попробуем на следующем тике.
        }
    }

    private void RenderInteractive(bool fast) =>
        _interactiveImage.Source = ComposeInteractive(_cubes, fast);

    private void UpdateStatus()
    {
        string mode = _isoVariants
            ? "изометрия"
            : $"азимут {Norm(_yaw):0}° · наклон {InteractivePitch:0}° (мышью — влево/вправо)";
        _status.Text = $"{Path.GetFileName(_cubesPath)}   ·   кубов: {_cubes.Count}"
            + $"   ·   {mode}   ·   обновлено {DateTime.Now:HH:mm:ss}";
        _yawLabel.Text = $"азимут {Norm(_yaw):0}°";
    }

    /// <summary>Привести угол к диапазону 0..360 для подписи.</summary>
    private static double Norm(double deg)
    {
        deg %= 360;
        return deg < 0 ? deg + 360 : deg;
    }

    /// <summary>6 плиток сторон в один ряд (Front/Back/Left/Right/Top/Bottom).</summary>
    private WriteableBitmap ComposeTiles(List<WorldCube> cubes)
    {
        int width = TileCount * TileW + (TileCount + 1) * TilePad;
        int height = TileH + 2 * TilePad;

        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(Renderer.Background);

        for (int i = 0; i < TileCount; i++)
            PreviewSheet.DrawPanel(canvas, TilePad + i * (TileW + TilePad), TilePad,
                TileW, TileH, cubes, i, _forceColor, margin: 12);

        return Snapshot(surface);
    }

    /// <summary>Большое полотно с поворотом по азимуту (наклон фиксирован).</summary>
    private WriteableBitmap ComposeInteractive(List<WorldCube> cubes, bool fast)
    {
        if (cubes.Count == 0)
            return ToWriteableBitmap(new byte[BigW * BigH * 4], BigW, BigH);

        var axes = Projector.IsoAxesByYawPitch(_yaw, InteractivePitch);
        var pixels = ZRenderer.RenderPixels(BigW, BigH, cubes, axes.Forward, axes.Up, axes.Right,
            Renderer.Background, _forceColor, margin: 26,
            samples: fast ? 1 : ZRenderer.DefaultSamples);
        return ToWriteableBitmap(pixels, BigW, BigH);
    }

    /// <summary>Сырые пиксели BGRA → WriteableBitmap напрямую (одна копия по строкам, без PNG).</summary>
    private static WriteableBitmap ToWriteableBitmap(byte[] pixels, int width, int height)
    {
        var wb = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = wb.Lock())
        {
            int rowBytes = width * 4;
            for (int y = 0; y < height; y++)
                Marshal.Copy(pixels, y * rowBytes, IntPtr.Add(fb.Address, y * fb.RowBytes), rowBytes);
        }
        return wb;
    }

    /// <summary>Лист вариантов изометрии (режим --iso-variants), 3×2.</summary>
    private WriteableBitmap ComposeIsoVariants(List<WorldCube> cubes)
    {
        const int cols = 3, pad = TilePad;
        int width = 3 * 380 + 4 * pad;
        int height = 2 * 380 + 3 * pad;

        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(Renderer.Background);
        PreviewSheet.ComposeIsoVariants(canvas, 380, 380, cols, pad, cubes);
        return Snapshot(surface);
    }

    /// <summary>Снимок поверхности в WriteableBitmap.</summary>
    private static WriteableBitmap Snapshot(SKSurface surface)
    {
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
