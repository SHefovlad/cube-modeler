using SkiaSharp;

namespace Modeler;

/// <summary>Рендерит модель в PNG: 10 видов — 6 сторон + 4 угла (pitch −52°, yaw 45/135/225/315).</summary>
public static class PreviewSheet
{
    public const int PanelWidth = 380;
    public const int PanelHeight = 380;
    public const int Padding = 8;

    /// <summary>Плитки по порядку: 6 ортовидов, затем 4 угла «как в игре» (pitch −52°).</summary>
    public static readonly (ViewKind Kind, string Label)[] Panels =
    {
        (ViewKind.Front,  "Front (-Z)"),
        (ViewKind.Back,   "Back (+Z)"),
        (ViewKind.Left,   "Left (-X)"),
        (ViewKind.Right,  "Right (+X)"),
        (ViewKind.Top,    "Top (+Y)"),
        (ViewKind.Bottom, "Bottom (-Y)"),
        (ViewKind.Iso,    "Угол yaw 45° / pitch −52°"),
        (ViewKind.Iso,    "Угол yaw 135° / pitch −52°"),
        (ViewKind.Iso,    "Угол yaw 225° / pitch −52°"),
        (ViewKind.Iso,    "Угол yaw 315° / pitch −52°"),
    };

    /// <summary>yaw-углы четырёх плиток-ракурсов (столбцы позже считаются в порядке 0..3).</summary>
    public static readonly double[] PanelCornerYaws = { 45, 135, 225, 315 };

    /// <summary>Индекс плитки в <see cref="Panels"/> — это не всегда угол; 0..3 из них — углы.</summary>
    /// <summary>Нарисовать одну плитку вида (с подписью и белым фоном).</summary>
    public static void DrawPanel(SKCanvas canvas, float x, float y, int w, int h, List<WorldCube> cubes,
        int panelIndex, SKColor? forceColor, double margin = 24)
    {
        using (var bg = new SKPaint { Color = SKColors.White })
            canvas.DrawRect(x, y, w, h, bg);

        canvas.Save();
        canvas.Translate(x, y);
        canvas.ClipRect(new SKRect(0, 0, w, h));

        var (kind, _) = Panels[panelIndex];
        if (panelIndex >= 6)
        {
            var axes = Projector.IsoAxesByYawPitch(PanelCornerYaws[panelIndex - 6], -Projector.IsoPitch);
            Renderer.DrawWithAxes(canvas, w, h, cubes, axes.Forward, axes.Up, axes.Right, forceColor, margin);
        }
        else
        {
            Renderer.Draw(canvas, w, h, cubes, kind, forceColor, margin);
        }

        canvas.Restore();

        using var labelPaint = new SKPaint { Color = new SKColor(0x33, 0x33, 0x33), IsAntialias = true };
        using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default, 18);
        canvas.DrawText(Panels[panelIndex].Label, x + 10, y + 24, font, labelPaint);
    }

    /// <summary>Лист со всеми 6 вариантами изометрии — для выбора ракурса (общий для PNG и окна).</summary>
    public static void ComposeIsoVariants(SKCanvas canvas, int panelWidth, int panelHeight, int cols,
        int pad, List<WorldCube> cubes, int highlight = -1)
    {
        using var labelPaint = new SKPaint { Color = new SKColor(0x22, 0x22, 0x22) };
        using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default, 20);
        using var borderPaint = new SKPaint
        {
            Color = new SKColor(0x2d, 0x7d, 0xd2),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3,
            IsAntialias = true,
        };

        for (int i = 0; i < Projector.IsoVariantCount; i++)
        {
            float x = pad + i % cols * (panelWidth + pad);
            float y = pad + i / cols * (panelHeight + pad);

            using (var bg = new SKPaint { Color = SKColors.White })
                canvas.DrawRect(x, y, panelWidth, panelHeight, bg);
            if (i == highlight)
                canvas.DrawRect(x + 1.5f, y + 1.5f, panelWidth - 3, panelHeight - 3, borderPaint);

            canvas.Save();
            canvas.Translate(x, y);
            canvas.ClipRect(new SKRect(0, 0, panelWidth, panelHeight));
            var axes = Projector.IsoAxes(i);
            Renderer.DrawWithAxes(canvas, panelWidth, panelHeight, cubes, axes.Forward, axes.Up, axes.Right);
            canvas.Restore();

            canvas.DrawText($"вариант {i}  yaw={Projector.IsoCornerYaws[i]}°", x + 10, y + 26, font, labelPaint);
        }
    }

    /// <summary>Большая сетка ВСЕХ ракурсов: все азимуты (шаг 45°) × все наклоны.</summary>
    public static void RenderAllViews(string cubesPath, string outPath)
    {
        var roots = Resolver.Load(cubesPath);
        var cubes = TransformResolver.Resolve(roots);

        int cols = Projector.AllYaws45.Length;          // 8 азимутов
        int rows = Projector.AllPitches.Length;         // 10 наклонов → 80 плиток

        const int pw = 260, ph = 260, pad = 6;
        int width = cols * pw + (cols + 1) * pad;
        int height = rows * ph + (rows + 1) * pad;

        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0xf2, 0xf4, 0xf7));

        using var paint = new SKPaint { Color = new SKColor(0x22, 0x22, 0x22) };
        using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default, 13);
        using var border = new SKPaint
        {
            Color = new SKColor(0x2d, 0x7d, 0xd2),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3,
            IsAntialias = true,
        };

        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            float x = pad + c * (pw + pad);
            float y = pad + r * (ph + pad);
            double yaw = Projector.AllYaws45[c];
            double pitch = Projector.AllPitches[r];
            bool highlight = Math.Abs(pitch) == Projector.IsoPitch && yaw == 45; // игровой ракурс (yaw 45, pitch −52)

            using (var bg = new SKPaint { Color = SKColors.White })
                canvas.DrawRect(x, y, pw, ph, bg);
            if (highlight) canvas.DrawRect(x + 1.5f, y + 1.5f, pw - 3, ph - 3, border);

            canvas.Save();
            canvas.Translate(x, y);
            canvas.ClipRect(new SKRect(0, 0, pw, ph));
            var axes = Projector.IsoAxesByYawPitch(yaw, pitch);
            Renderer.DrawWithAxes(canvas, pw, ph, cubes, axes.Forward, axes.Up, axes.Right, margin: 10);
            canvas.Restore();

            canvas.DrawText($"yaw {yaw:0}° / pitch {pitch:0}°", x + 6, y + 16, font, paint);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var stream = File.OpenWrite(outPath);
        data.SaveTo(stream);

        Console.WriteLine($"[AllViews] {outPath}  ({cols * rows} плиток)");
    }

    /// <summary>Лист со всеми 6 вариантами изометрии (yaw = 0, 45, … 225°) — сохранить в PNG.</summary>
    public static void RenderIsoVariants(string cubesPath, string outPath)
    {
        var roots = Resolver.Load(cubesPath);
        var cubes = TransformResolver.Resolve(roots);

        int cols = 3;
        int rows = 2;
        int width = cols * PanelWidth + (cols + 1) * Padding;
        int height = rows * PanelHeight + (rows + 1) * Padding;

        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0xf2, 0xf4, 0xf7));
        ComposeIsoVariants(canvas, PanelWidth, PanelHeight, cols, Padding, cubes);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var stream = File.OpenWrite(outPath);
        data.SaveTo(stream);

        Console.WriteLine($"[IsoVariants] {outPath}");
    }

    /// <summary>Отрисовать лист со всеми видами и сохранить в PNG.</summary>
    public static void Render(string cubesPath, string outPath, SKColor? forceColor = null)
    {
        var roots = Resolver.Load(cubesPath);
        var cubes = TransformResolver.Resolve(roots);

        var outDir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

        int cols = 5;
        int rows = (Panels.Length + cols - 1) / cols;
        int width = cols * PanelWidth + (cols + 1) * Padding;
        int height = rows * PanelHeight + (rows + 1) * Padding;

        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0xf2, 0xf4, 0xf7));

        for (int i = 0; i < Panels.Length; i++)
        {
            int col = i % cols;
            int row = i / cols;
            float x = Padding + col * (PanelWidth + Padding);
            float y = Padding + row * (PanelHeight + Padding);
            DrawPanel(canvas, x, y, PanelWidth, PanelHeight, cubes, i, forceColor);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(outPath);
        data.SaveTo(stream);

        Console.WriteLine($"[Preview] {outPath}  (кубов: {cubes.Count})");
    }
}
