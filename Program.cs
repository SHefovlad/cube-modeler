using SkiaSharp;

namespace Modeler;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--test")
        {
            int a = ParserTests.Run();
            int b = GeometryTests.Run();
            return a == 0 && b == 0 ? 0 : 1;
        }

        if (args.Length > 0 && args[0] == "--watch")
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Использование: Modeler --watch <файл.cubes> [выход.png] [#rrggbb] [--open]");
                return 2;
            }
            string cubesFile = args[1];
            string png = args.Length > 2 && !args[2].StartsWith("--") && !args[2].StartsWith("#")
                ? args[2]
                : Path.ChangeExtension(cubesFile, ".png");
            var watchColor = args.FirstOrDefault(a => a.StartsWith("#")) is { } hex
                ? SKColor.Parse(hex)
                : new SKColor(0xb0, 0x8d, 0x57);
            bool open = args.Contains("--open");
            return Watcher.Run(cubesFile, png, watchColor, open);
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

        if (args.Length > 0)
        {
            try
            {
                var roots = Parser.ParseFile(args[0]);
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

        Console.WriteLine("Modeler — конструктор моделей .cubes");
        Console.WriteLine("  Modeler --test                          прогон тестов");
        Console.WriteLine("  Modeler --render <file.cubes> <out.png> [#color]   лист видов в PNG");
        Console.WriteLine("  Modeler --watch <file.cubes> [out.png] [#color] [--open]   живой предпросмотр");
        Console.WriteLine("  Modeler <файл.cubes>                    разбор и печать дерева");
        return 0;
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
