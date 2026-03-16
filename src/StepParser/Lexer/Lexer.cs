using System.Globalization;
using System.Text;
using StepParser.Diagnostics;
using StepParser.Logging;

namespace StepParser.Lexer;

public static class Lexer
{
    [Log]
    public static IReadOnlyList<Token> Lex(IReadOnlyList<RawToken> rawTokens, ICollection<ParseDiagnostic> diagnostics)
    {
        List<Token> tokens = new(rawTokens.Count);
        foreach (RawToken raw in rawTokens)
        {
            tokens.Add(LexToken(raw, diagnostics));
        }

        return tokens;
    }

    private static Token LexToken(RawToken raw, ICollection<ParseDiagnostic> diagnostics)
    {
        if (raw.Lexeme.Length == 0)
        {
            return new Token(TokenKind.EndOfFile, string.Empty, null, raw.Line, raw.Column);
        }

        return raw.Lexeme switch
        {
            "=" => new Token(TokenKind.Equals, raw.Lexeme, null, raw.Line, raw.Column),
            ";" => new Token(TokenKind.Semicolon, raw.Lexeme, null, raw.Line, raw.Column),
            "," => new Token(TokenKind.Comma, raw.Lexeme, null, raw.Line, raw.Column),
            "(" => new Token(TokenKind.LeftParen, raw.Lexeme, null, raw.Line, raw.Column),
            ")" => new Token(TokenKind.RightParen, raw.Lexeme, null, raw.Line, raw.Column),
            "$" => new Token(TokenKind.Unset, raw.Lexeme, null, raw.Line, raw.Column),
            "*" => new Token(TokenKind.Inherited, raw.Lexeme, null, raw.Line, raw.Column),
            _ => LexComplex(raw, diagnostics)
        };
    }

    private static Token LexComplex(RawToken raw, ICollection<ParseDiagnostic> diagnostics)
    {
        if (raw.Lexeme.StartsWith("#", StringComparison.Ordinal))
        {
            if (int.TryParse(raw.Lexeme.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int entityId))
            {
                return new Token(TokenKind.EntityRef, raw.Lexeme, entityId, raw.Line, raw.Column);
            }

            diagnostics.Add(new ParseDiagnostic(
                DiagnosticSeverity.Error,
                raw.Line,
                raw.Column,
                $"Invalid entity reference '{raw.Lexeme}'."));
            return new Token(TokenKind.Unknown, raw.Lexeme, null, raw.Line, raw.Column);
        }

        if (raw.Lexeme.StartsWith("'", StringComparison.Ordinal))
        {
            return new Token(TokenKind.String, raw.Lexeme, DecodeString(raw.Lexeme, raw, diagnostics), raw.Line, raw.Column);
        }

        if (raw.Lexeme.StartsWith("\"", StringComparison.Ordinal))
        {
            return new Token(TokenKind.Binary, raw.Lexeme, raw.Lexeme.Trim('"'), raw.Line, raw.Column);
        }

        if (raw.Lexeme.StartsWith(".", StringComparison.Ordinal) && raw.Lexeme.EndsWith(".", StringComparison.Ordinal))
        {
            return new Token(
                TokenKind.Enumeration,
                raw.Lexeme,
                raw.Lexeme.Trim('.').ToUpperInvariant(),
                raw.Line,
                raw.Column);
        }

        if (LooksLikeNumber(raw.Lexeme))
        {
            if (raw.Lexeme.Contains('E', StringComparison.OrdinalIgnoreCase) || raw.Lexeme.Contains('.'))
            {
                if (double.TryParse(raw.Lexeme, NumberStyles.Float, CultureInfo.InvariantCulture, out double real))
                {
                    return new Token(TokenKind.Real, raw.Lexeme, real, raw.Line, raw.Column);
                }
            }
            else if (long.TryParse(raw.Lexeme, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer))
            {
                return new Token(TokenKind.Integer, raw.Lexeme, integer, raw.Line, raw.Column);
            }
        }

        string normalized = raw.Lexeme.ToUpperInvariant();
        return normalized switch
        {
            "ISO-10303-21" => new Token(TokenKind.IsoOpen, raw.Lexeme, normalized, raw.Line, raw.Column),
            "END-ISO-10303-21" => new Token(TokenKind.IsoClose, raw.Lexeme, normalized, raw.Line, raw.Column),
            "HEADER" => new Token(TokenKind.Header, raw.Lexeme, normalized, raw.Line, raw.Column),
            "ENDSEC" => new Token(TokenKind.EndSec, raw.Lexeme, normalized, raw.Line, raw.Column),
            "DATA" => new Token(TokenKind.Data, raw.Lexeme, normalized, raw.Line, raw.Column),
            "ANCHOR" => new Token(TokenKind.Anchor, raw.Lexeme, normalized, raw.Line, raw.Column),
            "REFERENCE" => new Token(TokenKind.Reference, raw.Lexeme, normalized, raw.Line, raw.Column),
            "SIGNATURE" => new Token(TokenKind.Signature, raw.Lexeme, normalized, raw.Line, raw.Column),
            _ => new Token(TokenKind.Keyword, raw.Lexeme, normalized, raw.Line, raw.Column)
        };
    }

    private static string DecodeString(string lexeme, RawToken raw, ICollection<ParseDiagnostic> diagnostics)
    {
        string inner = lexeme.Length >= 2 ? lexeme[1..^1] : string.Empty;
        inner = inner.Replace("''", "'", StringComparison.Ordinal);

        StringBuilder builder = new();
        for (int index = 0; index < inner.Length; index++)
        {
            if (inner[index] == '\\' &&
                index + 3 < inner.Length &&
                inner[index + 1] == 'X' &&
                (inner[index + 2] == '2' || inner[index + 2] == '4') &&
                inner[index + 3] == '\\')
            {
                int codeUnitSize = inner[index + 2] == '2' ? 4 : 8;
                int cursor = index + 4;
                StringBuilder hexBuilder = new();
                while (cursor + 3 < inner.Length &&
                       !(inner[cursor] == '\\' && inner[cursor + 1] == 'X' && inner[cursor + 2] == '0' && inner[cursor + 3] == '\\'))
                {
                    int take = Math.Min(codeUnitSize, inner.Length - cursor);
                    hexBuilder.Append(inner.AsSpan(cursor, take));
                    cursor += take;
                }

                if (cursor + 3 >= inner.Length)
                {
                    diagnostics.Add(new ParseDiagnostic(
                        DiagnosticSeverity.Warning,
                        raw.Line,
                        raw.Column,
                        "Unterminated encoded string sequence."));
                    break;
                }

                AppendUnicode(builder, hexBuilder.ToString(), codeUnitSize, raw, diagnostics);
                index = cursor + 3;
                continue;
            }

            builder.Append(inner[index]);
        }

        return builder.ToString();
    }

    private static void AppendUnicode(
        StringBuilder builder,
        string hex,
        int width,
        RawToken raw,
        ICollection<ParseDiagnostic> diagnostics)
    {
        for (int offset = 0; offset + width <= hex.Length; offset += width)
        {
            string segment = hex.Substring(offset, width);
            if (!int.TryParse(segment, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int codePoint))
            {
                diagnostics.Add(new ParseDiagnostic(
                    DiagnosticSeverity.Warning,
                    raw.Line,
                    raw.Column,
                    $"Invalid encoded character segment '{segment}'."));
                continue;
            }

            builder.Append(char.ConvertFromUtf32(codePoint));
        }
    }

    private static bool LooksLikeNumber(string lexeme)
    {
        return lexeme.Any(char.IsDigit) &&
               lexeme.All(character => char.IsDigit(character) || character is '+' or '-' or '.' or 'E' or 'e');
    }
}
