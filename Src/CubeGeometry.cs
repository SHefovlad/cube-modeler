using System.Numerics;

namespace Modeler;

/// <summary>Один куб: 8 углов мира в порядке битов (x, y, z).</summary>
public static class CubeGeometry
{
    /// <summary>Локальные смещения углов (знак по каждой оси).</summary>
    public static readonly (int X, int Y, int Z)[] CornerSigns =
    {
        (-1, -1, -1), (+1, -1, -1), (+1, +1, -1), (-1, +1, -1),
        (-1, -1, +1), (+1, -1, +1), (+1, +1, +1), (-1, +1, +1),
    };

    /// <summary>Индексы 6 граней (по 4 угла каждая, против часовой извне).</summary>
    public static readonly int[][] Faces =
    {
        new[] { 0, 1, 2, 3 }, // -Z
        new[] { 4, 5, 6, 7 }, // +Z
        new[] { 0, 1, 5, 4 }, // -Y
        new[] { 3, 2, 6, 7 }, // +Y
        new[] { 0, 3, 7, 4 }, // -X
        new[] { 1, 2, 6, 5 }, // +X
    };

    /// <summary>Нормали граней в том же порядке, что <see cref="Faces"/> (локальные, до поворота).</summary>
    public static readonly Vec3[] FaceNormals =
    {
        new(0, 0, -1), new(0, 0, 1), new(0, -1, 0),
        new(0, 1, 0), new(-1, 0, 0), new(1, 0, 0),
    };

    /// <summary>Мировые углы куба.</summary>
    public static Vec3[] Corners(WorldCube cube, out Vec3[] localOffsets)
    {
        var corners = new Vec3[8];
        localOffsets = new Vec3[8];
        for (int i = 0; i < 8; i++)
        {
            var s = CornerSigns[i];
            var local = new Vec3(s.X * cube.Size.X / 2, s.Y * cube.Size.Y / 2, s.Z * cube.Size.Z / 2);
            localOffsets[i] = local;
            corners[i] = cube.Position + cube.Rotation.Rotate(local);
        }
        return corners;
    }

    /// <summary>Индекс грани, которой принадлежит пара углов (a, b) после сортировки; -1 если нет.</summary>
    public static int FaceIndex(int a, int b)
    {
        for (int f = 0; f < Faces.Length; f++)
        {
            var face = Faces[f];
            for (int i = 0; i < 4; i++)
            {
                if (face[i] == a && face[(i + 1) % 4] == b) return f;
                if (face[i] == b && face[(i + 1) % 4] == a) return f;
            }
        }
        return -1;
    }
}
