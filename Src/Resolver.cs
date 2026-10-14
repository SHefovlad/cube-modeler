using SkiaSharp;

namespace Modeler;

/// <summary>Разбор .cubes с поддержкой include других файлов.</summary>
public static class Resolver
{
    /// <summary>Директива подключения другого файла: include("путь.cubes").</summary>
    private const string IncludeKeyword = "include";

    /// <summary>Прочитать модель, раскрывая include. Путь к include — относительно включающего файла.</summary>
    public static List<ModelNode> Load(string path)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nodes = new List<ModelNode>();
        LoadInto(Path.GetFullPath(path), nodes, visited);
        return nodes;
    }

    /// <summary>Собрать все файлы (сам + include), чтобы отслеживать их время изменения.</summary>
    public static List<(string Path, DateTime Time)> CollectIncludes(string path)
    {
        var result = new List<(string, DateTime)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Collect(Path.GetFullPath(path), result, visited);
        return result;
    }

    private static void Collect(string fullPath, List<(string, DateTime)> result, HashSet<string> visited)
    {
        if (!visited.Add(fullPath) || !File.Exists(fullPath)) return;
        result.Add((fullPath, File.GetLastWriteTimeUtc(fullPath)));

        foreach (var include in FindIncludes(fullPath))
            Collect(include, result, visited);
    }

    private static void LoadInto(string fullPath, List<ModelNode> nodes, HashSet<string> visited)
    {
        if (!visited.Add(fullPath))
            throw new CubeParseException($"Циклический include: {fullPath}");
        if (!File.Exists(fullPath))
            throw new CubeParseException($"Файл не найден: {fullPath}");

        string text = File.ReadAllText(fullPath);
        var directory = Path.GetDirectoryName(fullPath)!;
        int line = 0;

        foreach (var rawLine in text.Split('\n'))
        {
            line++;
            string trimmed = rawLine.Trim();

            if (trimmed.StartsWith("include", StringComparison.Ordinal) && trimmed.Contains('('))
            {
                string inner = Between(trimmed, '(', ')');
                string includePath = Unquote(inner.Trim());
                LoadInto(Path.GetFullPath(Path.Combine(directory, includePath)), nodes, visited);
            }
        }

        // Обычный разбор файла; include-строки парсер пропускает как комментарии.
        var stripped = StripIncludes(text);
        nodes.AddRange(Parser.Parse(stripped));
    }

    private static IEnumerable<string> FindIncludes(string fullPath)
    {
        string text;
        try { text = File.ReadAllText(fullPath); }
        catch (IOException) { yield break; }

        var directory = Path.GetDirectoryName(fullPath)!;
        foreach (var rawLine in text.Split('\n'))
        {
            string trimmed = rawLine.Trim();
            if (trimmed.StartsWith("include", StringComparison.Ordinal) && trimmed.Contains('('))
            {
                string inner = Between(trimmed, '(', ')');
                yield return Path.GetFullPath(Path.Combine(directory, Unquote(inner.Trim())));
            }
        }
    }

    /// <summary>Заменить include-строки на пустые, чтобы обычный парсер их не видел.</summary>
    private static string StripIncludes(string text)
    {
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();
            if (trimmed.StartsWith("include", StringComparison.Ordinal) && trimmed.Contains('('))
                lines[i] = "// include";
        }
        return string.Join('\n', lines);
    }

    private static string Between(string s, char open, char close)
    {
        int a = s.IndexOf(open);
        int b = s.LastIndexOf(close);
        return a >= 0 && b > a ? s.Substring(a + 1, b - a - 1) : "";
    }

    private static string Unquote(string s) => s.Trim().Trim('"', '\'');
}
