using SkiaSharp;

namespace Modeler;

/// <summary>Треугольник (или многоугольник после разбиения) в мировых координатах.</summary>
public sealed class BspPoly
{
    public Vec3[] V;
    public SKColor Fill;

    /// <summary>
    /// Мировая нормаль грани, взятая из таблицы граней, а не вычисленная по обходу вершин.
    /// Обход в <see cref="CubeGeometry.Faces"/> не согласован по часовой стрелке, поэтому
    /// нормаль «по трём точкам» у части граней выходит инвертированной.
    /// </summary>
    public Vec3 Normal;

    public BspPoly(Vec3[] vertices, SKColor fill, Vec3 normal)
    {
        V = vertices;
        Fill = fill;
        Normal = normal;
    }

}

/// <summary>
/// BSP-дерево для корректного порядка отрисовки без z-буфера (алгоритм художника
/// по дереву, а не по сортировке). Строится один раз в мировых координатах: при
/// пересечении плоскости узла многоугольник расщепляется, поэтому порядок остаётся
/// верным и для пересекающихся граней, и для любого угла камеры.
/// Обход от дальнего к ближнему зависит только от положения камеры.
/// </summary>
public sealed class BspTree
{
    private const double Eps = 1e-6;

    private sealed class Node
    {
        public Vec3 Normal;
        public double D;
        public List<BspPoly> Coplanar = new();
        public Node? Front;
        public Node? Back;
    }

    private Node? _root;

    public static BspTree Build(IEnumerable<BspPoly> polygons)
    {
        var tree = new BspTree();
        foreach (var poly in polygons)
            tree._root = Insert(tree._root, poly);
        return tree;
    }

    private static Node Insert(Node? node, BspPoly poly)
    {
        if (node is null)
        {
            var n = poly.Normal;
            return new Node
            {
                Normal = n,
                D = -(n.X * poly.V[0].X + n.Y * poly.V[0].Y + n.Z * poly.V[0].Z),
                Coplanar = { poly },
            };
        }

        int front = 0, back = 0, on = 0;
        foreach (var v in poly.V)
        {
            double dist = node.Normal.X * v.X + node.Normal.Y * v.Y + node.Normal.Z * v.Z + node.D;
            if (dist > Eps) front++;
            else if (dist < -Eps) back++;
            else on++;
        }

        if (back == 0 && front == 0)
        {
            node.Coplanar.Add(poly);
            return node;
        }

        if (back == 0)
        {
            node.Front = Insert(node.Front, poly);
            return node;
        }

        if (front == 0)
        {
            node.Back = Insert(node.Back, poly);
            return node;
        }

        // Многоугольник пересекает плоскость — расщепляем на две части.
        Split(poly, node.Normal, node.D, out var frontPart, out var backPart);
        if (frontPart != null) node.Front = Insert(node.Front, frontPart);
        if (backPart != null) node.Back = Insert(node.Back, backPart);
        return node;
    }

    /// <summary>Разрезать многоугольник плоскостью. Части сохраняют исходный цвет.</summary>
    private static void Split(BspPoly poly, Vec3 n, double d, out BspPoly? front, out BspPoly? back)
    {
        var f = new List<Vec3>();
        var b = new List<Vec3>();

        for (int i = 0; i < poly.V.Length; i++)
        {
            var cur = poly.V[i];
            var next = poly.V[(i + 1) % poly.V.Length];

            double dc = n.X * cur.X + n.Y * cur.Y + n.Z * cur.Z + d;
            double dn = n.X * next.X + n.Y * next.Y + n.Z * next.Z + d;

            if (dc >= -Eps) f.Add(cur);
            if (dc <= Eps) b.Add(cur);

            bool crosses = (dc > Eps && dn < -Eps) || (dc < -Eps && dn > Eps);
            if (crosses)
            {
                double t = dc / (dc - dn);
                var p = new Vec3(
                    cur.X + (next.X - cur.X) * t,
                    cur.Y + (next.Y - cur.Y) * t,
                    cur.Z + (next.Z - cur.Z) * t);
                f.Add(p);
                b.Add(p);
            }
        }

        var fClean = Dedupe(f);
        var bClean = Dedupe(b);
        front = fClean.Length >= 3 ? new BspPoly(fClean, poly.Fill, poly.Normal) : null;
        back = bClean.Length >= 3 ? new BspPoly(bClean, poly.Fill, poly.Normal) : null;
    }

    /// <summary>Убрать совпадающие вершины (в т.ч. замыкающую): они ломают нормаль.</summary>
    private static Vec3[] Dedupe(List<Vec3> pts)
    {
        var result = new List<Vec3>(pts.Count);
        foreach (var p in pts)
        {
            if (result.Count == 0 || !Same(result[^1], p)) result.Add(p);
        }
        if (result.Count > 1 && Same(result[0], result[^1])) result.RemoveAt(result.Count - 1);
        return result.ToArray();
    }

    private static bool Same(Vec3 a, Vec3 b) =>
        Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9 && Math.Abs(a.Z - b.Z) < 1e-9;

    /// <summary>
    /// Нарисовать содержимое от дальнего к ближнему. <paramref name="eye"/> — положение
    /// камеры в мире (для ортопроекции достаточно любой точки на луче взгляда).
    /// </summary>
    public void Draw(SKCanvas canvas, Vec3 eye, Vec3 forward,
        Vec3 focus, Vec3 right, Vec3 up, double scale, double offX, double offY,
        SKPaint paint, SKPath path)
    {
        Visit(_root, canvas, eye, forward, focus, right, up, scale, offX, offY, paint, path);
    }

    private static void Visit(Node? node, SKCanvas canvas, Vec3 eye, Vec3 forward,
        Vec3 focus, Vec3 right, Vec3 up, double scale, double offX, double offY,
        SKPaint paint, SKPath path)
    {
        if (node is null) return;

        double side = node.Normal.X * eye.X + node.Normal.Y * eye.Y + node.Normal.Z * eye.Z + node.D;

        if (side > 0)
        {
            Visit(node.Back, canvas, eye, forward, focus, right, up, scale, offX, offY, paint, path);
            DrawPolys(node.Coplanar, canvas, forward, focus, right, up, scale, offX, offY, paint, path);
            Visit(node.Front, canvas, eye, forward, focus, right, up, scale, offX, offY, paint, path);
        }
        else
        {
            Visit(node.Front, canvas, eye, forward, focus, right, up, scale, offX, offY, paint, path);
            DrawPolys(node.Coplanar, canvas, forward, focus, right, up, scale, offX, offY, paint, path);
            Visit(node.Back, canvas, eye, forward, focus, right, up, scale, offX, offY, paint, path);
        }
    }

    private static void DrawPolys(List<BspPoly> polys, SKCanvas canvas, Vec3 forward,
        Vec3 focus, Vec3 right, Vec3 up, double scale, double offX, double offY,
        SKPaint paint, SKPath path)
    {
        foreach (var poly in polys)
        {
            // Отсечение задних граней: нормаль смотрит от камеры.
            var n = poly.Normal;
            if (n.X * forward.X + n.Y * forward.Y + n.Z * forward.Z >= -1e-9) continue;

            path.Reset();
            var first = Project(poly.V[0], focus, right, up, scale, offX, offY);
            path.MoveTo(first);
            for (int i = 1; i < poly.V.Length; i++)
                path.LineTo(Project(poly.V[i], focus, right, up, scale, offX, offY));
            path.Close();

            paint.Color = poly.Fill;
            canvas.DrawPath(path, paint);
        }
    }

    private static SKPoint Project(Vec3 world, Vec3 focus, Vec3 right, Vec3 up,
        double scale, double offX, double offY)
    {
        var local = world - focus;
        float sx = (float)((local.X * right.X + local.Y * right.Y + local.Z * right.Z) * scale + offX);
        float sy = (float)(-(local.X * up.X + local.Y * up.Y + local.Z * up.Z) * scale + offY);
        return new SKPoint(sx, sy);
    }
}
