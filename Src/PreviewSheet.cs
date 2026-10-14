using SkiaSharp;

namespace Modeler;

/// <summary>Рендерит модель в PNG: 6 ортогональных видов + изометрия 52°/45°.</summary>
public static class PreviewSheet
{
    public const int PanelWidth = 380;
    public const int PanelHeight = 380;
    public const int Padding = 8;

    public static readonly (ViewKind Kind, string Label)[] Panels =
    {
        (ViewKind.Front, "Front (-Z)"),
        (ViewKind.Back, "Back (+Z)"),
        (ViewKind.Left, "Left (-X)"),
        (ViewKind.Right, "Right (+X)"),
        (ViewKind.Top, "Top (+Y)"),
        (ViewKind.Iso, "Iso 52/45"),
    };

    /// <summary>Отрисовать лист со всеми видами и сохранить в PNG.</summary>
    public static void Render(string cubesPath, string outPath, SKColor color)
    {
        var roots = Parser.ParseFile(cubesPath);
        var cubes = TransformResolver.Resolve(roots);

        var outDir = Path.GetDirectoryName(Path.GetFullPath(outPath));
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

        int cols = 3;
        int rows = (Panels.Length + cols - 1) / cols;
        int width = cols * PanelWidth + (cols + 1) * Padding;
        int height = rows * PanelHeight + (rows + 1) * Padding;

        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0xf2, 0xf4, 0xf7));

        using var labelPaint = new SKPaint
        {
            Color = new SKColor(0x33, 0x33, 0x33),
            IsAntialias = true,
            TextSize = 18,
            Typeface = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default,
        };

        for (int i = 0; i < Panels.Length; i++)
        {
            int col = i % cols;
            int row = i / cols;
            float x = Padding + col * (PanelWidth + Padding);
            float y = Padding + row * (PanelHeight + Padding);

            using (var bg = new SKPaint { Color = SKColors.White, IsAntialias = false })
                canvas.DrawRect(x, y, PanelWidth, PanelHeight, bg);

            canvas.Save();
            canvas.Translate(x, y);
            canvas.ClipRect(new SKRect(0, 0, PanelWidth, PanelHeight));

            var (kind, label) = Panels[i];
            Renderer.Draw(canvas, PanelWidth, PanelHeight, cubes, kind, color);

            canvas.Restore();
            canvas.DrawText(label, x + 10, y + 24, labelPaint);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(outPath);
        data.SaveTo(stream);

        Console.WriteLine($"[Preview] {outPath}  (кубов: {cubes.Count})");
    }
}
