using System.Globalization;

namespace Modeler;

/// <summary>Узел модели: куб (с геометрией) или пустышка (контейнер), плюс дети.</summary>
public sealed class ModelNode
{
    /// <summary>Есть ли у узла геометрия (куб) или это пустой контейнер.</summary>
    public bool IsCube;

    /// <summary>Позиция центра (локально относительно родителя).</summary>
    public Vec3 Position;

    /// <summary>Размер по осям X, Y, Z. У пустышки всегда (1, 1, 1).</summary>
    public Vec3 Scale = new(1, 1, 1);

    /// <summary>Поворот в градусах Эйлера (порядок Unity).</summary>
    public Vec3 Rotation;

    /// <summary>Цвет в виде «#rrggbb»; null — дефолтный серый.</summary>
    public string? Color;

    /// <summary>Дети (локальные трансформы).</summary>
    public List<ModelNode> Children = new();

    /// <summary>Имя узла для отладки (заполняется парсером: "cube" / "empty").</summary>
    public string Kind = "cube";

    /// <summary>Строка исходника (1-based) — для сообщений об ошибках.</summary>
    public int Line;
}

/// <summary>Парсер формата `.cubes` (см. LANGUAGE.md).</summary>
public static class Parser
{
    /// <summary>Разобрать текст .cubes в список корневых узлов. Бросает <see cref="CubeParseException"/>.</summary>
    public static List<ModelNode> Parse(string text)
    {
        var lexer = new Lexer(text);
        var roots = new List<ModelNode>();
        while (lexer.Peek().Kind != TokenKind.Eof)
        {
            roots.Add(ParseNode(lexer));
        }
        return roots;
    }

    /// <summary>Разобрать файл .cubes.</summary>
    public static List<ModelNode> ParseFile(string path) => Parse(File.ReadAllText(path));

    private static ModelNode ParseNode(Lexer lexer)
    {
        var head = lexer.Next();
        if (head.Kind != TokenKind.Ident)
            throw lexer.Error(head, $"ожидался cube или empty, а не «{head.Text}»");

        var node = new ModelNode { Line = head.Line };

        if (head.Text == "cube")
        {
            node.IsCube = true;
            node.Kind = "cube";

            lexer.Expect(TokenKind.LParen, "(");
            node.Position = ParseVec(lexer);
            lexer.Expect(TokenKind.Comma, ",");
            node.Scale = ParseVec(lexer);
            lexer.Expect(TokenKind.Comma, ",");
            node.Rotation = ParseVec(lexer);

            if (node.Scale.X <= 0 || node.Scale.Y <= 0 || node.Scale.Z <= 0)
                throw lexer.Error(head, "размер куба должен быть положительным по всем осям");

            // Необязательный четвёртый аргумент — цвет «#rrggbb».
            if (lexer.Peek().Kind == TokenKind.Comma)
            {
                lexer.Next();
                var colorToken = lexer.Next();
                if (colorToken.Kind != TokenKind.Color)
                    throw lexer.Error(colorToken, $"ожидался цвет вида #rrggbb, а не «{colorToken.Text}»");
                node.Color = colorToken.Text;
            }

            lexer.Expect(TokenKind.RParen, ")");
        }
        else if (head.Text == "empty")
        {
            node.IsCube = false;
            node.Kind = "empty";
            node.Scale = new Vec3(1, 1, 1);

            lexer.Expect(TokenKind.LParen, "(");
            node.Position = ParseVec(lexer);
            lexer.Expect(TokenKind.Comma, ",");
            node.Rotation = ParseVec(lexer);
            lexer.Expect(TokenKind.RParen, ")");
        }
        else
        {
            throw lexer.Error(head, $"неизвестный узел «{head.Text}» (ожидалось cube или empty)");
        }

        // Необязательный блок детей.
        if (lexer.Peek().Kind == TokenKind.LBrace)
        {
            lexer.Next(); // {
            while (true)
            {
                var next = lexer.Peek();
                if (next.Kind == TokenKind.RBrace) { lexer.Next(); break; }
                if (next.Kind == TokenKind.Eof)
                    throw lexer.Error(next, "не закрыта фигурная скобка «{»");
                node.Children.Add(ParseNode(lexer));
            }
        }

        return node;
    }

    private static Vec3 ParseVec(Lexer lexer)
    {
        lexer.Expect(TokenKind.LParen, "(");
        double x = ParseNumber(lexer);
        lexer.Expect(TokenKind.Comma, ",");
        double y = ParseNumber(lexer);
        lexer.Expect(TokenKind.Comma, ",");
        double z = ParseNumber(lexer);
        lexer.Expect(TokenKind.RParen, ")");
        return new Vec3(x, y, z);
    }

    private static double ParseNumber(Lexer lexer)
    {
        var token = lexer.Next();
        if (token.Kind != TokenKind.Number)
            throw lexer.Error(token, $"ожидалось число, а не «{token.Text}»");

        if (!double.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            throw lexer.Error(token, $"не удалось прочитать число «{token.Text}»");

        return value;
    }
}

/// <summary>Ошибка разбора .cubes со ссылкой на строку.</summary>
public sealed class CubeParseException : Exception
{
    public CubeParseException(string message) : base(message) { }
}
