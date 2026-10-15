namespace Modeler;

/// <summary>
/// Тесты растеризатора: порядок граней и интерполяция глубины.
/// Отдельный набор (не геометрия), чтобы баг z-буфера не вернулся незамеченным.
/// </summary>
public static class RenderTests
{
    private static int _passed;
    private static int _failed;

    public static int Run()
    {
        _passed = 0;
        _failed = 0;

        WeightOrderIsByVertex();
        NearerFragmentWins();
        CoversWholeTriangle();

        Console.WriteLine($"[RenderTests] passed={_passed} failed={_failed}");
        return _failed == 0 ? 0 : 1;
    }

    private static void Check(string name, bool condition)
    {
        if (condition) { _passed++; Console.WriteLine($"  OK   {name}"); }
        else { _failed++; Console.WriteLine($"  FAIL {name}"); }
    }

    /// <summary>
    /// Глубина должна интерполироваться по вершинам. У вершины a (z=1) точка почти в a
    /// даёт глубину ≈1, а не ≈5 (так было при перепутанных вершинах).
    /// </summary>
    private static void WeightOrderIsByVertex()
    {
        // Треугольник a=(0,0,az=1), b=(10,0,bz=3), c=(0,10,cz=5).
        float z = ZRenderer.InterpolateDepth(
            0, 0, 1,
            10, 0, 3,
            0, 10, 5,
            0.1f, 0.1f, out bool inside);
        Check("точка внутри треугольника", inside);
        Check("глубина у вершины a ≈ 1 (а не чужая)", z is > 1.0f and < 1.2f);
    }

    /// <summary>Ближний фрагмент перекрывает дальний на всём пересечении.</summary>
    private static void NearerFragmentWins()
    {
        // Два одинаковых по экрану треугольника; ближний (z=0) должен победить.
        float far = ZRenderer.InterpolateDepth(0, 0, 10, 10, 0, 10, 0, 10, 10, 2f, 2f, out _);
        float near = ZRenderer.InterpolateDepth(0, 0, 0, 10, 0, 0, 0, 10, 0, 2f, 2f, out _);
        Check("ближний фрагмент ближе дальнего", near < far);
    }

    /// <summary>Покрытие треугольника не должно давать дырок через пиксель.</summary>
    private static void CoversWholeTriangle()
    {
        int covered = 0;
        for (int y = 0; y < 10; y++)
        for (int x = 0; x < 10; x++)
        {
            ZRenderer.InterpolateDepth(0, 0, 0, 10, 0, 0, 0, 10, 0, x + 0.5f, y + 0.5f, out bool inside);
            if (inside) covered++;
        }
        // Прямоугольный треугольник 10x10 покрывает ~50 клеток.
        Check("треугольник покрывает ~половину квадрата", covered is > 45 and < 56);
    }
}
