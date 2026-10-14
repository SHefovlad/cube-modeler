namespace Modeler;

/// <summary>Абсолютный (мировой) трансформ куба — во что лист превращается в мире.</summary>
public readonly record struct WorldCube(
    Vec3 Position,   // центр куба в мире
    Vec3 Size,       // размеры куба в мире (уже с поправкой на скейл родителей)
    Quat Rotation,   // мировой поворот
    string? Color);  // «#rrggbb» или null (дефолтный серый)

/// <summary>
/// Считает мировые трансформы листьев, сворачивая вложенность, как Unity:
/// мировой = мировой_родителя × локальный (позиция, скейл, поворот).
/// Ядро геометрии — общее для превью и для будущего Unity-моста.
/// </summary>
public static class TransformResolver
{
    /// <summary>Собрать все кубы модели в мировых координатах (обход дерева).</summary>
    public static List<WorldCube> Resolve(List<ModelNode> roots)
    {
        var result = new List<WorldCube>();
        foreach (var node in roots)
            ResolveNode(node, Vec3.Zero, Quat.Identity, Vec3.One, result);
        return result;
    }

    private static void ResolveNode(ModelNode node, Vec3 parentPos, Quat parentRot, Vec3 parentScale,
        List<WorldCube> result)
    {
        // Мировая позиция: локальная позиция, отмасштабированная родителем и повёрнутая им.
        Vec3 scaledLocal = new(
            node.Position.X * parentScale.X,
            node.Position.Y * parentScale.Y,
            node.Position.Z * parentScale.Z);

        Vec3 worldPos = parentPos + parentRot.Rotate(scaledLocal);
        Quat worldRot = parentRot * Quat.Euler(node.Rotation);
        Vec3 worldScale = new(
            parentScale.X * node.Scale.X,
            parentScale.Y * node.Scale.Y,
            parentScale.Z * node.Scale.Z);

        if (node.IsCube)
            result.Add(new WorldCube(worldPos, node.Scale * parentScale, worldRot, node.Color));

        foreach (var child in node.Children)
            ResolveNode(child, worldPos, worldRot, worldScale, result);
    }
}
