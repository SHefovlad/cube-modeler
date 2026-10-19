using Avalonia;
using SkiaSharp;

namespace Modeler;

public static class Program
{
    /// <summary>Avalonia требует STA и свою инициализацию — точка входа приложения.</summary>
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--axes")
        {
            AxisProbe.Run();
            return 0;
        }

        if (args.Length > 0 && args[0] == "--test")
        {
            int a = ParserTests.Run();
            int b = GeometryTests.Run();
            int c = RenderTests.Run();
            return a == 0 && b == 0 && c == 0 ? 0 : 1;
        }

        if (args.Length > 0 && args[0] == "--iso-variants")
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Использование: Modeler --iso-variants <file.cubes> [out.png]");
                return 2;
            }
            string cubesFile = args[1];
            string png = args.Length > 2 && !args[2].StartsWith("#")
                ? args[2]
                : Path.ChangeExtension(cubesFile, null) + "_iso.png";
            try
            {
                PreviewSheet.RenderIsoVariants(cubesFile, png);
                return 0;
            }
            catch (CubeParseException ex)
            {
                Console.Error.WriteLine("Ошибка разбора: " + ex.Message);
                return 2;
            }
        }

        if (args.Length > 0 && args[0] == "--all-views")
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Использование: Modeler --all-views <file.cubes> [out.png]");
                return 2;
            }
            string cubesFile = args[1];
            string png = args.Length > 2 && !args[2].StartsWith("#")
                ? args[2]
                : Path.ChangeExtension(cubesFile, null) + "_allviews.png";
            try
            {
                PreviewSheet.RenderAllViews(cubesFile, png);
                return 0;
            }
            catch (CubeParseException ex)
            {
                Console.Error.WriteLine("Ошибка разбора: " + ex.Message);
                return 2;
            }
        }

        if (args.Length > 0 && args[0] == "--gpu-render")
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Использование: Modeler --gpu-render <файл.cubes> <выход.png> [msaa]");
                return 2;
            }
            int msaa = args.Length > 3 && int.TryParse(args[3], out int m) ? m : 4;
            double yaw = args.Length > 4 && double.TryParse(args[4],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double y) ? y : double.NaN;
            double pitch = args.Length > 5 && double.TryParse(args[5],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double p) ? p : double.NaN;
            try
            {
                bool useTree = !args.Contains("notree");
                return GpuProbe.Run(args[1], args[2], msaa, yaw, pitch, useTree);
            }
            catch (CubeParseException ex)
            {
                Console.Error.WriteLine("Ошибка разбора: " + ex.Message);
                return 2;
            }
        }

        if (args.Length > 0 && args[0] == "--render")
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Использование: Modeler --render <файл.cubes> <выход.png> [#rrggbb]");
                return 2;
            }
            try
            {
                var color = args.Length > 3 ? SKColor.Parse(args[3]) : (SKColor?)null;
                PreviewSheet.Render(args[1], args[2], color);
                return 0;
            }
            catch (CubeParseException ex)
            {
                Console.Error.WriteLine("Ошибка разбора: " + ex.Message);
                return 2;
            }
        }

        bool classic = args.Contains("--classic");
        bool gpu = !args.Contains("--cpu");   // GPU-режим по умолчанию, --cpu возвращает старый

        if (args.Length > 0 && args[0] != "--view" && args[0] != "--classic"
            && args[0] != "--cpu" && args[0] != "--gpu")
        {
            try
            {
                var roots = Resolver.Load(args[0]);
                Dump(roots, 0);
                var world = TransformResolver.Resolve(roots);
                Console.WriteLine($"--- мировых кубов: {world.Count} ---");
                return 0;
            }
            catch (CubeParseException ex)
            {
                Console.Error.WriteLine("Ошибка разбора: " + ex.Message);
                return 2;
            }
        }

        // По умолчанию — оконный просмотрщик.
        string? file = args.Length > 1 && !args[1].StartsWith("--")
            ? args[1]
            : args.Length > 0 && args[0] != "--view" && args[0] != "--classic"
                && args[0] != "--cpu" && args[0] != "--gpu"
                ? args[0]
                : null;
        if (file == null)
        {
            Console.WriteLine("Modeler — конструктор моделей .cubes");
            Console.WriteLine("  Modeler --view <файл.cubes> [#rrggbb]      новый режим: плитки меняются местами");
            Console.WriteLine("  Modeler --view <файл.cubes> --classic      классический режим (как раньше)");
            Console.WriteLine("  Modeler --view <файл.cubes> --gpu          рендер на GPU через Skia (по умолчанию)");
            Console.WriteLine("  Modeler --view <файл.cubes> --cpu          старый CPU-растеризатор с SSAA");
            Console.WriteLine("  Modeler --test                              прогон тестов");
            Console.WriteLine("  Modeler --render <file.cubes> <out.png> [#color]   лист видов в PNG");
            Console.WriteLine("  Modeler --iso-variants <file.cubes> [out.png]      4 угла (для выбора ракурса)");
            Console.WriteLine("  Modeler --all-views <file.cubes> [out.png]         ВСЕ ракурсы: 8 азимутов × 10 наклонов");
            Console.WriteLine("  Modeler <файл.cubes>                        разбор и печать дерева");
            Console.WriteLine("  #rrggbb — принудительный цвет всех кубов (иначе цвет из разметки)");
            return 0;
        }

        var colorArg = args.FirstOrDefault(a => a.StartsWith("#"));
        bool isoVariants = args.Contains("--iso-variants");

        BuildAvaloniaApp(file, colorArg, isoVariants, classic, gpu).StartWithClassicDesktopLifetime(args);
        return 0;
    }

    private static AppBuilder BuildAvaloniaApp(string file, string? colorArg, bool isoVariants = false,
        bool classic = false, bool gpu = true) =>
        AppBuilder.Configure(() => new ModelerApp(file, ArgToColor(colorArg), isoVariants, classic, gpu))
            .UsePlatformDetect()
            .LogToTrace();

    /// <summary>Преобразовать аргумент-цвет в SKColor; null — цвета берутся из разметки.</summary>
    private static SKColor? ArgToColor(string? arg) =>
        string.IsNullOrEmpty(arg) ? null : SKColor.Parse(arg);

    private static void Dump(List<ModelNode> nodes, int depth)
    {
        foreach (var node in nodes)
        {
            string indent = new string(' ', depth * 2);
            string head = node.IsCube
                ? $"cube pos={node.Position} scale={node.Scale} rot={node.Rotation}"
                : $"empty pos={node.Position} rot={node.Rotation}";
            string color = node.Color != null ? $" color={node.Color}" : "";
            Console.WriteLine($"{indent}{head}{color}   (строка {node.Line})");
            Dump(node.Children, depth + 1);
        }
    }
}
