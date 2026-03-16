using System.Text;
using StepParser.Diagnostics;
using StepParser.Logging;

namespace StepParser.Lexer;

public static class Tokenizer
{
    [Log]
    public static IReadOnlyList<RawToken> TokenizeFile(string path, ICollection<ParseDiagnostic> diagnostics)
    {
        using StreamReader reader = new(path, Encoding.UTF8, true);
        return Tokenize(reader, diagnostics);
    }

    [Log]
    public static IReadOnlyList<RawToken> Tokenize(TextReader reader, ICollection<ParseDiagnostic> diagnostics)
    {
        string text = reader.ReadToEnd();
        List<RawToken> tokens = new();
        int index = 0;
        int line = 1;
        int column = 1;

        while (index < text.Length)
        {
            char current = text[index];

            if (char.IsWhiteSpace(current))
            {
                Advance(text, ref index, ref line, ref column);
                continue;
            }

            if (current == '/' && Peek(text, index + 1) == '*')
            {
                int commentLine = line;
                int commentColumn = column;
                Advance(text, ref index, ref line, ref column);
                Advance(text, ref index, ref line, ref column);
                bool closed = false;
                while (index < text.Length)
                {
                    if (text[index] == '*' && Peek(text, index + 1) == '/')
                    {
                        Advance(text, ref index, ref line, ref column);
                        Advance(text, ref index, ref line, ref column);
                        closed = true;
                        break;
                    }

                    Advance(text, ref index, ref line, ref column);
                }

                if (!closed)
                {
                    diagnostics.Add(new ParseDiagnostic(
                        DiagnosticSeverity.Error,
                        commentLine,
                        commentColumn,
                        "Unterminated block comment."));
                }

                continue;
            }

            int startLine = line;
            int startColumn = column;
            if (TryReadPunctuation(text, ref index, ref line, ref column, out string? punctuation))
            {
                tokens.Add(new RawToken(punctuation!, startLine, startColumn));
                continue;
            }

            if (current == '\'')
            {
                tokens.Add(new RawToken(
                    ReadString(text, ref index, ref line, ref column, diagnostics, startLine, startColumn),
                    startLine,
                    startColumn));
                continue;
            }

            if (current == '#')
            {
                tokens.Add(new RawToken(
                    ReadWhile(text, ref index, ref line, ref column, character => character == '#' || char.IsDigit(character)),
                    startLine,
                    startColumn));
                continue;
            }

            if (current == '"')
            {
                tokens.Add(new RawToken(
                    ReadQuoted(text, ref index, ref line, ref column, '"', diagnostics, startLine, startColumn),
                    startLine,
                    startColumn));
                continue;
            }

            if (current == '.' && char.IsLetter(Peek(text, index + 1)))
            {
                tokens.Add(new RawToken(
                    ReadEnumeration(text, ref index, ref line, ref column, diagnostics, startLine, startColumn),
                    startLine,
                    startColumn));
                continue;
            }

            if (IsNumberStart(text, index))
            {
                tokens.Add(new RawToken(
                    ReadWhile(text, ref index, ref line, ref column, IsNumberCharacter),
                    startLine,
                    startColumn));
                continue;
            }

            if (IsIdentifierStart(current))
            {
                tokens.Add(new RawToken(
                    ReadWhile(text, ref index, ref line, ref column, IsIdentifierCharacter),
                    startLine,
                    startColumn));
                continue;
            }

            diagnostics.Add(new ParseDiagnostic(
                DiagnosticSeverity.Warning,
                startLine,
                startColumn,
                $"Unrecognized character '{current}'."));
            Advance(text, ref index, ref line, ref column);
        }

        tokens.Add(new RawToken(string.Empty, line, column));
        return tokens;
    }

    private static bool TryReadPunctuation(
        string text,
        ref int index,
        ref int line,
        ref int column,
        out string? punctuation)
    {
        punctuation = text[index] switch
        {
            '=' => "=",
            ';' => ";",
            ',' => ",",
            '(' => "(",
            ')' => ")",
            '$' => "$",
            '*' => "*",
            _ => null
        };

        if (punctuation is null)
        {
            return false;
        }

        Advance(text, ref index, ref line, ref column);
        return true;
    }

    private static string ReadString(
        string text,
        ref int index,
        ref int line,
        ref int column,
        ICollection<ParseDiagnostic> diagnostics,
        int startLine,
        int startColumn)
    {
        StringBuilder builder = new();
        builder.Append(text[index]);
        Advance(text, ref index, ref line, ref column);

        while (index < text.Length)
        {
            char current = text[index];
            builder.Append(current);
            Advance(text, ref index, ref line, ref column);

            if (current == '\'')
            {
                if (index < text.Length && text[index] == '\'')
                {
                    builder.Append(text[index]);
                    Advance(text, ref index, ref line, ref column);
                    continue;
                }

                return builder.ToString();
            }
        }

        diagnostics.Add(new ParseDiagnostic(
            DiagnosticSeverity.Error,
            startLine,
            startColumn,
            "Unterminated string literal."));
        return builder.ToString();
    }

    private static string ReadQuoted(
        string text,
        ref int index,
        ref int line,
        ref int column,
        char terminator,
        ICollection<ParseDiagnostic> diagnostics,
        int startLine,
        int startColumn)
    {
        StringBuilder builder = new();
        builder.Append(text[index]);
        Advance(text, ref index, ref line, ref column);

        while (index < text.Length)
        {
            char current = text[index];
            builder.Append(current);
            Advance(text, ref index, ref line, ref column);
            if (current == terminator)
            {
                return builder.ToString();
            }
        }

        diagnostics.Add(new ParseDiagnostic(
            DiagnosticSeverity.Error,
            startLine,
            startColumn,
            "Unterminated quoted literal."));
        return builder.ToString();
    }

    private static string ReadEnumeration(
        string text,
        ref int index,
        ref int line,
        ref int column,
        ICollection<ParseDiagnostic> diagnostics,
        int startLine,
        int startColumn)
    {
        StringBuilder builder = new();
        builder.Append(text[index]);
        Advance(text, ref index, ref line, ref column);

        while (index < text.Length)
        {
            char current = text[index];
            builder.Append(current);
            Advance(text, ref index, ref line, ref column);
            if (current == '.')
            {
                return builder.ToString();
            }
        }

        diagnostics.Add(new ParseDiagnostic(
            DiagnosticSeverity.Error,
            startLine,
            startColumn,
            "Unterminated enumeration literal."));
        return builder.ToString();
    }

    private static string ReadWhile(
        string text,
        ref int index,
        ref int line,
        ref int column,
        Func<char, bool> predicate)
    {
        StringBuilder builder = new();
        while (index < text.Length && predicate(text[index]))
        {
            builder.Append(text[index]);
            Advance(text, ref index, ref line, ref column);
        }

        return builder.ToString();
    }

    private static bool IsIdentifierStart(char character)
    {
        return char.IsLetter(character) || character == '_';
    }

    private static bool IsIdentifierCharacter(char character)
    {
        return char.IsLetterOrDigit(character) || character is '_' or '-';
    }

    private static bool IsNumberStart(string text, int index)
    {
        char current = text[index];
        if (char.IsDigit(current))
        {
            return true;
        }

        if (current == '.' && char.IsDigit(Peek(text, index + 1)))
        {
            return true;
        }

        if ((current == '+' || current == '-') &&
            (char.IsDigit(Peek(text, index + 1)) || Peek(text, index + 1) == '.'))
        {
            return true;
        }

        return false;
    }

    private static bool IsNumberCharacter(char character)
    {
        return char.IsDigit(character) || character is '+' or '-' or '.' or 'E' or 'e';
    }

    private static char Peek(string text, int index)
    {
        return index >= 0 && index < text.Length ? text[index] : '\0';
    }

    private static void Advance(string text, ref int index, ref int line, ref int column)
    {
        if (text[index] == '\n')
        {
            line++;
            column = 1;
        }
        else
        {
            column++;
        }

        index++;
    }
}
