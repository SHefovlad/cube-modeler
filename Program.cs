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

        if (args.Length > 0)
        {
            try
            {
                var roots = Parser.ParseFile(args[0]);
                Dump(roots, 0);
                return 0;
            }
            catch (CubeParseException ex)
            {
                Console.Error.WriteLine("Ошибка разбора: " + ex.Message);
                return 2;
            }
        }

        Console.WriteLine("Modeler — конструктор моделей .cubes");
        Console.WriteLine("  Modeler --test             прогон тестов парсера");
        Console.WriteLine("  Modeler <файл.cubes>       разбор и печать дерева");
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
