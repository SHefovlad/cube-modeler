namespace Modeler;

/// <summary>Разбор .cubes: токенизация (лексер).</summary>
public enum TokenKind { Ident, Number, LParen, RParen, LBrace, RBrace, Comma, Eof }

public readonly record struct Token(TokenKind Kind, string Text, int Line);

/// <summary>Поточный лексер .cubes с однострочными комментариями «//» и отслеживанием строк.</summary>
public sealed class Lexer
{
    private readonly string _text;
    private int _pos;
    private int _line = 1;

    public Lexer(string text) => _text = text;

    /// <summary>Текущий токен без сдвига.</summary>
    public Token Peek()
    {
        int savePos = _pos, saveLine = _line;
        var token = Next();
        _pos = savePos;
        _line = saveLine;
        return token;
    }

    /// <summary>Следующий токен.</summary>
    public Token Next()
    {
        SkipTrivia();
        if (_pos >= _text.Length) return new Token(TokenKind.Eof, "", _line);

        char c = _text[_pos];

        switch (c)
        {
            case '(': _pos++; return new Token(TokenKind.LParen, "(", _line);
            case ')': _pos++; return new Token(TokenKind.RParen, ")", _line);
            case '{': _pos++; return new Token(TokenKind.LBrace, "{", _line);
            case '}': _pos++; return new Token(TokenKind.RBrace, "}", _line);
            case ',': _pos++; return new Token(TokenKind.Comma, ",", _line);
        }

        int startLine = _line;

        if (char.IsLetter(c) || c == '_')
        {
            int start = _pos;
            while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_')) _pos++;
            return new Token(TokenKind.Ident, _text.Substring(start, _pos - start), startLine);
        }

        if (char.IsDigit(c) || c == '-' || c == '+' || c == '.')
        {
            int start = _pos;
            if (_text[_pos] == '-' || _text[_pos] == '+') _pos++;
            bool hasDot = false;
            while (_pos < _text.Length &&
                   (char.IsDigit(_text[_pos]) || (_text[_pos] == '.' && !hasDot)))
            {
                if (_text[_pos] == '.') hasDot = true;
                _pos++;
            }
            return new Token(TokenKind.Number, _text.Substring(start, _pos - start), startLine);
        }

        throw Error(new Token(TokenKind.Eof, c.ToString(), startLine),
            $"неожиданный символ «{c}»");
    }

    /// <summary>Проверить, что следующий токен нужного вида, и забрать его.</summary>
    public Token Expect(TokenKind kind, string what)
    {
        var token = Next();
        if (token.Kind != kind)
            throw Error(token, $"ожидалось «{what}», а не «{token.Text}»");
        return token;
    }

    /// <summary>Сформировать ошибку разбора со строкой.</summary>
    public CubeParseException Error(Token token, string message) =>
        new($"Строка {token.Line}: {message}");

    private void SkipTrivia()
    {
        while (_pos < _text.Length)
        {
            char c = _text[_pos];

            if (c == '\n') { _line++; _pos++; continue; }
            if (char.IsWhiteSpace(c)) { _pos++; continue; }

            // Комментарий до конца строки.
            if (c == '/' && _pos + 1 < _text.Length && _text[_pos + 1] == '/')
            {
                while (_pos < _text.Length && _text[_pos] != '\n') _pos++;
                continue;
            }

            break;
        }
    }
}
