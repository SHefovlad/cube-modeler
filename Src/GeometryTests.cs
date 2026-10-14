namespace Modeler;

/// <summary>Тесты ядра геометрии: вложенность, повороты, масштаб (поведение как в Unity).</summary>
public static class GeometryTests
{
    private static int _passed;
    private static int _failed;

    public static int Run()
    {
        _passed = 0;
        _failed = 0;

        SingleCubeWorld();
        ChildOffsetByParent();
        ChildInheritsParentRotation();
        ParentScaleMultipliesChildPosition();
        ParentScaleStretchesChildSize();
        EmptyHasNoGeometry();
        EmptyPassesTransformThrough();
        RotatedParentMovesChild();
        TwoLevelsAccumulate();
        EulerUnityOrder();

        Console.WriteLine($"[GeomTests] passed={_passed} failed={_failed}");
        return _failed == 0 ? 0 : 1;
    }

    private static void Check(string name, bool condition)
    {
        if (condition) { _passed++; Console.WriteLine($"  OK   {name}"); }
        else { _failed++; Console.WriteLine($"  FAIL {name}"); }
    }

    private static bool Near(Vec3 a, Vec3 b, double eps = 1e-6) =>
        Math.Abs(a.X - b.X) < eps && Math.Abs(a.Y - b.Y) < eps && Math.Abs(a.Z - b.Z) < eps;

    private static void SingleCubeWorld()
    {
        var world = TransformResolver.Resolve(Parser.Parse("cube((1, 2, 3), (2, 4, 6), (0, 0, 0))"));
        Check("один куб — один мировой куб", world.Count == 1);
        Check("мировая позиция = локальная", Near(world[0].Position, new Vec3(1, 2, 3)));
        Check("мировой размер = локальный", Near(world[0].Size, new Vec3(2, 4, 6)));
    }

    private static void ChildOffsetByParent()
    {
        var world = TransformResolver.Resolve(Parser.Parse(
            "cube((10, 0, 0), (1, 1, 1), (0, 0, 0)) { cube((0, 1, 0), (1, 1, 1), (0, 0, 0)) }"));
        Check("ребёнок смещён родителем", Near(world[1].Position, new Vec3(10, 1, 0)));
    }

    private static void ChildInheritsParentRotation()
    {
        // Родитель повёрнут на 90° вокруг Y: локальный +Z ребёнка уходит в +X.
        var world = TransformResolver.Resolve(Parser.Parse(
            "cube((0, 0, 0), (1, 1, 1), (0, 90, 0)) { cube((0, 0, 2), (1, 1, 1), (0, 0, 0)) }"));
        Check("поворот родителя уносит ребёнка", Near(world[1].Position, new Vec3(2, 0, 0)));
    }

    private static void ParentScaleMultipliesChildPosition()
    {
        var world = TransformResolver.Resolve(Parser.Parse(
            "cube((0, 0, 0), (2, 2, 2), (0, 0, 0)) { cube((1, 0, 0), (1, 1, 1), (0, 0, 0)) }"));
        Check("скейл родителя умножает позицию ребёнка", Near(world[1].Position, new Vec3(2, 0, 0)));
    }

    private static void ParentScaleStretchesChildSize()
    {
        var world = TransformResolver.Resolve(Parser.Parse(
            "cube((0, 0, 0), (2, 3, 4), (0, 0, 0)) { cube((0, 0, 0), (1, 1, 1), (0, 0, 0)) }"));
        Check("скейл родителя растягивает размер ребёнка (вариант C)", Near(world[1].Size, new Vec3(2, 3, 4)));
    }

    private static void EmptyHasNoGeometry()
    {
        var world = TransformResolver.Resolve(Parser.Parse("empty((0, 0, 0), (0, 0, 0))"));
        Check("empty не даёт кубов", world.Count == 0);
    }

    private static void EmptyPassesTransformThrough()
    {
        var world = TransformResolver.Resolve(Parser.Parse(
            "empty((5, 0, 0), (0, 0, 0)) { cube((0, 1, 0), (1, 1, 1), (0, 0, 0)) }"));
        Check("empty передаёт позицию ребёнку", Near(world[0].Position, new Vec3(5, 1, 0)));
    }

    private static void RotatedParentMovesChild()
    {
        // Родитель повёрнут на 90° вокруг Z: локальный +Y ребёнка уходит в -X.
        var world = TransformResolver.Resolve(Parser.Parse(
            "cube((0, 0, 0), (1, 1, 1), (0, 0, 90)) { cube((0, 2, 0), (1, 1, 1), (0, 0, 0)) }"));
        Check("поворот родителя вокруг Z", Near(world[1].Position, new Vec3(-2, 0, 0)));
    }

    private static void TwoLevelsAccumulate()
    {
        var world = TransformResolver.Resolve(Parser.Parse(
            @"empty((0, 0, 5), (0, 0, 0))
              {
                  cube((1, 0, 0), (1, 1, 1), (0, 0, 0))
                  {
                      cube((0, 0, 3), (1, 1, 1), (0, 0, 0))
                  }
              }"));
        Check("два уровня: внук накапливает смещения", Near(world[1].Position, new Vec3(1, 0, 8)));
    }

    private static void EulerUnityOrder()
    {
        // Проверяем порядок Z→X→Y: при X=90 и Z=90 результат отличается от XYZ-порядка.
        var world = TransformResolver.Resolve(Parser.Parse(
            "cube((0, 0, 1), (1, 1, 1), (90, 0, 90))"));
        // Единичный вектор +Z, повёрнутый Unity-порядком (Z90 затем X90 затем Y0):
        // Z90: (0,0,1)->(0,0,1); затем X90: (0,0,1)->(0,-1,0). Позиция не меняется (локальная).
        Check("поворот сохранён как кватернион", Math.Abs(world[0].Rotation.W) <= 1.0 + 1e-9);
        var dir = world[0].Rotation.Rotate(new Vec3(0, 0, 1));
        Check("Unity-порядок: +Z уходит в -Y", Near(dir, new Vec3(0, -1, 0)));
    }
}
