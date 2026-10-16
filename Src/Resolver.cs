namespace Modeler;

/// <summary>Разбор .cubes с поддержкой include других файлов.</summary>
public static class Resolver
{
    /// <summary>
    /// Прочитать модель, раскрывая include. Путь к include — относительно включающего файла.
    /// Узел include превращается в empty-контейнер с тем же трансформом, а содержимое
    /// подключённого файла становится его детьми (поэтому include уважает вложенность).
    /// </summary>
    public static List<ModelNode> Load(string path)
    {
        string full = Path.GetFullPath(path);
        var stack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return Expand(Parser.ParseFile(full), Path.GetDirectoryName(full)!, stack);
    }

    /// <summary>Собрать все файлы (сам + include), чтобы отслеживать их время изменения.</summary>
    public static List<(string Path, DateTime Time)> CollectIncludes(string path)
    {
        var result = new List<(string, DateTime)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Collect(Path.GetFullPath(path), result, visited);
        return result;
    }

    /// <summary>Заменить узлы include на empty-контейнер с детьми из подключённого файла.</summary>
    private static List<ModelNode> Expand(List<ModelNode> nodes, string directory, HashSet<string> stack)
    {
        var result = new List<ModelNode>(nodes.Count);
        foreach (var node in nodes)
        {
            if (node.Kind == "include")
            {
                string includePath = ResolveIncludePath(directory, node.IncludePath);
                if (!stack.Add(includePath))
                    throw new CubeParseException($"Циклический include: {includePath}");
                if (!File.Exists(includePath))
                    throw new CubeParseException($"Файл не найден: {includePath}");

                var included = Expand(Parser.ParseFile(includePath),
                    Path.GetDirectoryName(includePath)!, stack);
                stack.Remove(includePath);

                // include(путь, позиция, скейл, поворот) ≡ empty(позиция, скейл, поворот) { … }
                result.Add(new ModelNode
                {
                    IsCube = false,
                    Kind = "empty",
                    Position = node.Position,
                    Scale = node.Scale,
                    Rotation = node.Rotation,
                    Line = node.Line,
                    Children = included,
                });
                continue;
            }

            if (node.Children.Count > 0)
                node.Children = Expand(node.Children, directory, stack);
            result.Add(node);
        }
        return result;
    }

    private static string ResolveIncludePath(string directory, string? includePath)
        => Path.GetFullPath(Path.Combine(directory,
            (includePath ?? string.Empty).Replace('\\', Path.DirectorySeparatorChar)));

    private static void Collect(string fullPath, List<(string, DateTime)> result, HashSet<string> visited)
    {
        if (!visited.Add(fullPath) || !File.Exists(fullPath)) return;
        result.Add((fullPath, File.GetLastWriteTimeUtc(fullPath)));

        foreach (var include in FindIncludes(fullPath))
            Collect(include, result, visited);
    }

    private static IEnumerable<string> FindIncludes(string fullPath)
    {
        string text;
        try { text = File.ReadAllText(fullPath); }
        catch (IOException) { yield break; }

        var directory = Path.GetDirectoryName(fullPath)!;
        foreach (var rawLine in text.Split('\n'))
        {
            string? includePath = ExtractIncludePath(rawLine);
            if (includePath != null)
                yield return ResolveIncludePath(directory, includePath);
        }
    }

    /// <summary>Вытащить путь из строки вида include("путь", …). null — если строка не include.</summary>
    private static string? ExtractIncludePath(string rawLine)
    {
        string trimmed = rawLine.Trim();
        if (trimmed.StartsWith("//", StringComparison.Ordinal)) return null;
        if (!trimmed.StartsWith("include", StringComparison.Ordinal)) return null;

        int a = trimmed.IndexOf('"');
        if (a < 0) return null;
        int b = trimmed.IndexOf('"', a + 1);
        if (b < 0) return null;
        return trimmed.Substring(a + 1, b - a - 1);
    }
}
