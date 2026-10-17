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
/// Новый режим просмотра: одна большая плитка и шесть маленьких. Клик по маленькой плите
/// меняет её местами с большой (та уменьшается и встаёт на её место).
/// Большая плитка: колесо — зум; ЛКМ — вращение (динамическая) или панорама (статичный вид).
/// </summary>
public sealed class SwapViewerWindow : Window
{
    private const int SmallW = 186, SmallH = 168, Gap = 6, SmallCount = 6;
    private const int BigW = 1120, BigH = 430;
    private const double YawPerPixel = 0.4;

    private sealed class Tile
    {
        public bool IsDynamic;
        public ViewKind View;
        public string Label = "";
        public double Yaw = Projector.IsoYaw;
        public double Zoom = 1;
        public double PanX, PanY;
    }

    private readonly string _cubesPath;
    private readonly SKColor? _forceColor;

    private readonly Tile[] _tiles;
    private int _mainTile;
    private readonly int[] _slotTiles;

    private readonly Image _mainImage = new();
    private readonly Border _mainHost = new();
    private readonly TextBlock _mainLabel = new();
    private readonly Image[] _slotImages = new Image[SmallCount];
    private readonly TextBlock[] _slotLabels = new TextBlock[SmallCount];
    private readonly TextBlock _status = new();

    private List<WorldCube> _cubes = new();
    private bool _dragging;
    private Point _lastPoint;
    private WriteTimeSet _timestamps = new();

    private sealed class WriteTimeSet
    {
        public DateTime Cubes;
        public readonly List<(string Path, DateTime Time)> Includes = new();
    }

    public SwapViewerWindow(string cubesPath, SKColor? forceColor)
    {
        _cubesPath = Path.GetFullPath(cubesPath);
        _forceColor = forceColor;

        Title = Path.GetFileName(_cubesPath) + " — Modeler (новый режим)";
        Width = 1180;
        Height = 900;
        Background = new SolidColorBrush(Color.Parse("#f2f4f7"));

        _tiles = new Tile[SmallCount + 1];
        _tiles[0] = new Tile { IsDynamic = true, Label = "динамическая" };

        var views = new[]
        {
            ViewKind.Front, ViewKind.Back, ViewKind.Left,
            ViewKind.Right, ViewKind.Top, ViewKind.Bottom,
        };
        var labels = new[]
        {
            "Front (-Z)", "Back (+Z)", "Left (-X)",
            "Right (+X)", "Top (+Y)", "Bottom (-Y)",
        };
        for (int i = 0; i < SmallCount; i++)
            _tiles[i + 1] = new Tile { View = views[i], Label = labels[i] };

        _mainTile = 0;                                   // по умолчанию большая — динамическая
        _slotTiles = new[] { 1, 2, 3, 4, 5, 6 };

        Content = BuildLayout();

        Opened += (_, _) =>
        {
            Reload();
            StartPolling();
        };
    }

    private Control BuildLayout()
    {
        var smallRow = new Grid();
        for (int i = 0; i < SmallCount; i++)
        {
            smallRow.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

            var image = new Image { Stretch = Stretch.Uniform };
            var label = new TextBlock
            {
                Margin = new Thickness(8, 6, 0, 0),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#666666")),
                IsHitTestVisible = false,
            };

            var host = new Border
            {
                Background = Brushes.Transparent,
                Height = SmallH,
                Margin = new Thickness(Gap / 2.0),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new Grid { Children = { image, label } },
            };
            int slot = i;
            host.PointerPressed += (_, _) => SwapWithSlot(slot);

            Grid.SetColumn(host, i);
            smallRow.Children.Add(host);

            _slotImages[i] = image;
            _slotLabels[i] = label;
        }

        _mainImage.Stretch = Stretch.Uniform;
        _mainLabel.Margin = new Thickness(12, 8, 0, 0);
        _mainLabel.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        _mainLabel.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        _mainLabel.FontFamily = new FontFamily("Segoe UI");
        _mainLabel.FontSize = 14;
        _mainLabel.Foreground = new SolidColorBrush(Color.Parse("#555555"));
        _mainLabel.IsHitTestVisible = false;

        _mainHost.Background = Brushes.Transparent;
        _mainHost.Child = new Grid { Children = { _mainImage, _mainLabel } };
        _mainHost.Cursor = new Cursor(StandardCursorType.SizeWestEast);
        _mainHost.PointerPressed += OnMainPressed;
        _mainHost.PointerMoved += OnMainMoved;
        _mainHost.PointerReleased += OnMainReleased;
        _mainHost.PointerWheelChanged += OnWheel;

        _status.Margin = new Thickness(10, 6, 10, 2);
        _status.FontFamily = new FontFamily("Segoe UI");
        _status.Foreground = new SolidColorBrush(Color.Parse("#333333"));

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(new GridLength(1, GridUnitType.Star)),
            },
        };
        Grid.SetRow(smallRow, 0);
        Grid.SetRow(_mainHost, 1);
        grid.Children.Add(smallRow);
        grid.Children.Add(_mainHost);

        var root = new DockPanel { Children = { TopDock(_status), grid } };
        return root;
    }

    private static Control TopDock(Control child)
    {
        DockPanel.SetDock(child, Dock.Top);
        return child;
    }

    // --- плитки и их перестановка ---

    private void SwapWithSlot(int slot)
    {
        // Плитка, которую «закрывают», возвращается в исходное состояние: без зума и сдвига.
        var demoted = _tiles[_mainTile];
        demoted.Zoom = 1;
        demoted.PanX = 0;
        demoted.PanY = 0;

        int clicked = _slotTiles[slot];
        _slotTiles[slot] = _mainTile;   // бывшая большая уходит в этот слот
        _mainTile = clicked;            // нажатая становится большой
        RenderAll();
    }

    private static (Vec3 Forward, Vec3 Up, Vec3 Right) AxesFor(Tile tile)
        => tile.IsDynamic
            ? Projector.IsoAxesByYawPitch(tile.Yaw, -Projector.IsoPitch)
            : Projector.CameraAxes(tile.View);

    private static string LabelFor(Tile tile)
        => tile.IsDynamic ? $"динамическая · {Norm(tile.Yaw):0}°" : tile.Label;

    private WriteableBitmap RenderBitmap(Tile tile, int w, int h, bool fast)
    {
        var (forward, up, right) = AxesFor(tile);
        var pixels = ZRenderer.RenderPixels(w, h, _cubes, forward, up, right, SKColors.White, _forceColor,
            margin: 16, samples: fast ? 1 : ZRenderer.DefaultSamples,
            zoom: tile.Zoom, panX: tile.PanX, panY: tile.PanY);
        return ToWriteableBitmap(pixels, w, h);
    }

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

    private void RenderAll()
    {
        for (int i = 0; i < SmallCount; i++)
        {
            var tile = _tiles[_slotTiles[i]];
            _slotImages[i].Source = RenderBitmap(tile, SmallW, SmallH, fast: false);
            _slotLabels[i].Text = LabelFor(tile);
        }

        RenderMain(fast: false);
    }

    private void RenderMain(bool fast)
    {
        var tile = _tiles[_mainTile];
        _mainImage.Source = RenderBitmap(tile, BigW, BigH, fast);
        _mainLabel.Text = LabelFor(tile);
        UpdateStatus();
    }

    // --- мышь на большой плитке ---

    private void OnMainPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_mainHost).Properties.IsLeftButtonPressed) return;
        _dragging = true;
        _lastPoint = e.GetPosition(_mainHost);
        e.Pointer.Capture(_mainHost);
    }

    private void OnMainMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;

        var pos = e.GetPosition(_mainHost);
        double dx = pos.X - _lastPoint.X;
        double dy = pos.Y - _lastPoint.Y;
        _lastPoint = pos;

        var tile = _tiles[_mainTile];
        if (tile.IsDynamic) tile.Yaw -= dx * YawPerPixel;      // вращаем азимут
        else
        {
            // Сдвиг в экранных пикселях, делённый на зум: картинка идёт за курсором 1:1
            // при любом приближении (иначе на большом зуме она убегала бы в zoom раз быстрее).
            tile.PanX += dx / tile.Zoom;
            tile.PanY += dy / tile.Zoom;
        }

        RenderMain(fast: true);                                 // без сглаживания — быстро
    }

    private void OnMainReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        RenderMain(fast: false);                                // уточняем картинку
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        var tile = _tiles[_mainTile];
        tile.Zoom = Math.Clamp(tile.Zoom * Math.Pow(1.15, e.Delta.Y), 0.15, 8.0);
        RenderMain(fast: false);
        e.Handled = true;
    }

    // --- состояние и слежение за файлом ---

    private void UpdateStatus()
    {
        var tile = _tiles[_mainTile];
        string hint = tile.IsDynamic
            ? "ЛКМ — вращать · колесо — зум"
            : "ЛКМ — двигать · колесо — зум";
        _status.Text = $"{Path.GetFileName(_cubesPath)}   ·   кубов: {_cubes.Count}"
            + $"   ·   {LabelFor(tile)}   ·   {hint}"
            + "   ·   клик по маленькой — поменять местами";
    }

    private static double Norm(double deg)
    {
        deg %= 360;
        return deg < 0 ? deg + 360 : deg;
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

            RenderAll();
        }
        catch (CubeParseException ex)
        {
            _status.Text = "Ошибка: " + ex.Message;
        }
        catch (IOException)
        {
            // Файл занят редактором — попробуем на следующем тике.
        }
    }
}
