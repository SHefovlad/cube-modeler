namespace Modeler;

/// <summary>Микро-тесты парсера (без внешних зависимостей — проверки при запуске).</summary>
public static class ParserTests
{
    private static int _passed;
    private static int _failed;

    public static int Run()
    {
        _passed = 0;
        _failed = 0;

        SingleCube();
        EmptyNode();
        NestedCube();
        ThreeLevels();
        SceneOfRoots();
        Comments();
        NegativeAndDecimals();
        RotParsed();
        ColorParsed();
        ErrorBadColor();
        ErrorUnclosedBrace();
        ErrorBadScale();
        ErrorUnknownNode();
        ErrorBadNumber();

        Console.WriteLine($"[Tests] passed={_passed} failed={_failed}");
        return _failed == 0 ? 0 : 1;
    }

    private static void Check(string name, bool condition)
    {
        if (condition) { _passed++; Console.WriteLine($"  OK   {name}"); }
        else { _failed++; Console.WriteLine($"  FAIL {name}"); }
    }

    private static void CheckThrows(string name, string source, string expectedFragment)
    {
        try
        {
            Parser.Parse(source);
            Check(name, false);
        }
        catch (CubeParseException ex)
        {
            Check(name, ex.Message.Contains(expectedFragment, StringComparison.Ordinal));
        }
    }

    private static void SingleCube()
    {
        var roots = Parser.Parse("cube((0, 0, 0), (1, 2, 3), (0, 0, 0))");
        Check("один куб — один корень", roots.Count == 1);
        var cube = roots[0];
        Check("это куб", cube.IsCube);
        Check("позиция", cube.Position == new Vec3(0, 0, 0));
        Check("размер", cube.Scale == new Vec3(1, 2, 3));
        Check("поворот", cube.Rotation == new Vec3(0, 0, 0));
        Check("нет детей", cube.Children.Count == 0);
    }

    private static void EmptyNode()
    {
        var roots = Parser.Parse("empty((1, 2, 3), (0, 90, 0))");
        Check("empty — не куб", !roots[0].IsCube);
        Check("empty позиция", roots[0].Position == new Vec3(1, 2, 3));
        Check("empty поворот", roots[0].Rotation == new Vec3(0, 90, 0));
        Check("empty скейл = 1", roots[0].Scale == new Vec3(1, 1, 1));
    }

    private static void NestedCube()
    {
        var roots = Parser.Parse(
            "cube((0, 0, 0), (2, 2, 2), (0, 0, 0)) { cube((0, 1, 0), (1, 1, 1), (0, 45, 0)) }");
        var parent = roots[0];
        Check("у родителя один ребёнок", parent.Children.Count == 1);
        Check("локальная позиция ребёнка", parent.Children[0].Position == new Vec3(0, 1, 0));
        Check("поворот ребёнка", parent.Children[0].Rotation == new Vec3(0, 45, 0));
    }

    private static void ThreeLevels()
    {
        var roots = Parser.Parse(
            @"empty((0, 0, 2), (0, 0, 0))
              {
                  cube((0, 0, 0), (2, 2, 2), (0, 0, 0))
                  {
                      cube((0, 0.6, 0), (0.4, 0.4, 0.4), (0, 0, 0))
                  }
              }");
        Check("три уровня: корень empty", !roots[0].IsCube && roots[0].Children.Count == 1);
        var inner = roots[0].Children[0].Children[0];
        Check("внук на месте", inner.Position == new Vec3(0, 0.6, 0));
        Check("внук — куб", inner.IsCube);
    }

    private static void SceneOfRoots()
    {
        var roots = Parser.Parse(
            @"cube((0, 2, 0), (4, 4, 4), (0, 0, 0))
              cube((0, 4.4, 0), (5, 0.4, 5), (0, 0, 0))
              cube((0, 0, 1.6), (2, 0.2, 0.6), (0, 0, 0))");
        Check("сцена из трёх корней", roots.Count == 3);
    }

    private static void Comments()
    {
        var roots = Parser.Parse(
            @"// шапка
              cube((0, 0, 0), (1, 1, 1), (0, 0, 0)) // хвост
              // между узлами
              cube((1, 0, 0), (1, 1, 1), (0, 0, 0))");
        Check("комментарии игнорируются", roots.Count == 2);
    }

    private static void NegativeAndDecimals()
    {
        var roots = Parser.Parse("cube((-1.5, 0.25, -0.5), (0.5, 0.5, 0.5), (0, 0, 0))");
        Check("отрицательные и дробные", roots[0].Position == new Vec3(-1.5, 0.25, -0.5));
    }

    private static void RotParsed()
    {
        var roots = Parser.Parse("cube((0, 0, 0), (1, 1, 1), (10, 20, 30))");
        Check("углы Эйлера читаются", roots[0].Rotation == new Vec3(10, 20, 30));
    }

    private static void ColorParsed()
    {
        var roots = Parser.Parse("cube((0, 0, 0), (1, 1, 1), (0, 0, 0), \"#8b5a2b\")");
        Check("цвет читается", roots[0].Color == "#8b5a2b");

        var noColor = Parser.Parse("cube((0, 0, 0), (1, 1, 1), (0, 0, 0))");
        Check("без цвета — null (серый по умолчанию)", noColor[0].Color == null);

        var nested = Parser.Parse(
            "cube((0,0,0),(1,1,1),(0,0,0), \"#111111\")\n{ cube((0,0,0),(1,1,1),(0,0,0), \"#222222\") }");
        Check("цвет у ребёнка свой", nested[0].Children[0].Color == "#222222");
    }

    private static void ErrorBadColor() =>
        CheckThrows("битый цвет", "cube((0,0,0),(1,1,1),(0,0,0), \"#zzz\")", "цвет");

    private static void ErrorUnclosedBrace() =>
        CheckThrows("незакрытая скобка {", "cube((0,0,0),(1,1,1),(0,0,0)) {", "не закрыта");

    private static void ErrorBadScale() =>
        CheckThrows("нулевой размер", "cube((0,0,0),(0,1,1),(0,0,0))", "положительным");

    private static void ErrorUnknownNode() =>
        CheckThrows("неизвестный узел", "box((0,0,0),(1,1,1),(0,0,0))", "неизвестный узел");

    private static void ErrorBadNumber() =>
        CheckThrows("мусор вместо числа", "cube((0, x, 0), (1,1,1), (0,0,0))", "ожидалось число");
}
