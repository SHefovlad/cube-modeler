using Avalonia;
using SkiaSharp;

namespace Modeler;

public static class Program
{
    /// <summary>Avalonia требует STA и свою инициализацию — точка входа приложения.</summary>
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--test")
        {
            int a = ParserTests.Run();
            int b = GeometryTests.Run();
            return a == 0 && b == 0 ? 0 : 1;
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
                var color = args.Length > 3 ? SKColor.Parse(args[3]) : new SKColor(0xb0, 0x8d, 0x57);
                PreviewSheet.Render(args[1], args[2], color);
                return 0;
            }
            catch (CubeParseException ex)
            {
                Console.Error.WriteLine("Ошибка разбора: " + ex.Message);
                return 2;
            }
        }

        if (args.Length > 0 && args[0] != "--view")
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
        string? file = args.Length > 1 ? args[1] : args.Length > 0 && args[0] != "--view" ? args[0] : null;
        if (file == null)
        {
            Console.WriteLine("Modeler — конструктор моделей .cubes");
            Console.WriteLine("  Modeler --view <файл.cubes> [#rrggbb]      окно с видами (по умолчанию)");
            Console.WriteLine("  Modeler --test                              прогон тестов");
            Console.WriteLine("  Modeler --render <file.cubes> <out.png> [#color]   лист видов в PNG");
            Console.WriteLine("  Modeler <файл.cubes>                        разбор и печать дерева");
            return 0;
        }

        var viewerColor = args.FirstOrDefault(a => a.StartsWith("#")) is { } hex
            ? SKColor.Parse(hex)
            : new SKColor(0xb0, 0x8d, 0x57);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure(() => new ModelerApp(ResolvePath(args2: Environment.GetCommandLineArgs()), ParseColor(Environment.GetCommandLineArgs())))
            .UsePlatformDetect()
            .LogToTrace();

    private static string ResolvePath(string[] args2)
    {
        for (int i = 1; i < args2.Length; i++)
            if (!args2[i].StartsWith("--") && !args2[i].StartsWith("#"))
                return args2[i];
        return "model.cubes";
    }

    private static SKColor ParseColor(string[] args2)
    {
        foreach (var a in args2)
            if (a.StartsWith("#")) return SKColor.Parse(a);
        return new SKColor(0xb0, 0x8d, 0x57);
    }

    private static void Dump(List<ModelNode> nodes, int depth)
    {
        foreach (var node in nodes)
        {
            string indent = new string(' ', depth * 2);
            string head = node.IsCube
                ? $"cube pos={node.Position} scale={node.Scale} rot={node.Rotation}"
                : $"empty pos={node.Position} rot={node.Rotation}";
            Console.WriteLine($"{indent}{head}   (строка {node.Line})");
            Dump(node.Children, depth + 1);
        }
    }
}
