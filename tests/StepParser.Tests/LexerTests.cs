using StepParser.Diagnostics;
using StepParser.Lexer;

namespace StepParser.Tests;

public sealed class LexerTests
{
    private static Token First(string source, TokenKind kind)
    {
        (IReadOnlyList<Token> tokens, _) = Sut.Lex(source);
        return tokens.First(token => token.Kind == kind);
    }

    [Theory]
    [InlineData("=", TokenKind.Equals)]
    [InlineData(";", TokenKind.Semicolon)]
    [InlineData(",", TokenKind.Comma)]
    [InlineData("(", TokenKind.LeftParen)]
    [InlineData(")", TokenKind.RightParen)]
    [InlineData("$", TokenKind.Unset)]
    [InlineData("*", TokenKind.Inherited)]
    public void ClassifiesPunctuation(string source, TokenKind expected)
    {
        (IReadOnlyList<Token> tokens, _) = Sut.Lex(source);

        Assert.Equal(expected, tokens[0].Kind);
        Assert.Null(tokens[0].Value);
    }

    [Theory]
    [InlineData("ISO-10303-21", TokenKind.IsoOpen)]
    [InlineData("END-ISO-10303-21", TokenKind.IsoClose)]
    [InlineData("HEADER", TokenKind.Header)]
    [InlineData("ENDSEC", TokenKind.EndSec)]
    [InlineData("DATA", TokenKind.Data)]
    [InlineData("ANCHOR", TokenKind.Anchor)]
    [InlineData("REFERENCE", TokenKind.Reference)]
    [InlineData("SIGNATURE", TokenKind.Signature)]
    public void ClassifiesStructuralKeywords(string source, TokenKind expected)
    {
        Assert.Equal(expected, Sut.Lex(source).Tokens[0].Kind);
    }

    [Fact]
    public void StructuralKeywordsAreCaseInsensitive()
    {
        Assert.Equal(TokenKind.Header, Sut.Lex("header").Tokens[0].Kind);
    }

    [Fact]
    public void UnknownWord_BecomesKeywordWithUppercasedValue()
    {
        Token token = First("cartesian_point", TokenKind.Keyword);

        Assert.Equal("CARTESIAN_POINT", token.Value);
        Assert.Equal("cartesian_point", token.Lexeme);
    }

    [Fact]
    public void EndOfInput_BecomesEndOfFileToken()
    {
        (IReadOnlyList<Token> tokens, _) = Sut.Lex("HEADER");

        Assert.Equal(TokenKind.EndOfFile, tokens[^1].Kind);
    }

    [Fact]
    public void EntityReference_CarriesIntegerValue()
    {
        Token token = First("#4711", TokenKind.EntityRef);

        Assert.Equal(4711, token.Value);
    }

    [Fact]
    public void BareHash_IsInvalidEntityReference()
    {
        (IReadOnlyList<Token> tokens, List<ParseDiagnostic> diagnostics) = Sut.Lex("#");

        Assert.Equal(TokenKind.Unknown, tokens[0].Kind);
        Assert.Contains(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Invalid entity reference"));
    }

    [Theory]
    [InlineData("42", 42L)]
    [InlineData("-42", -42L)]
    public void ClassifiesIntegers(string source, long expected)
    {
        Assert.Equal(expected, First(source, TokenKind.Integer).Value);
    }

    [Theory]
    [InlineData("3.5", 3.5)]
    [InlineData("-1.25E-3", -1.25E-3)]
    [InlineData("2E3", 2000.0)]
    public void ClassifiesReals(string source, double expected)
    {
        Assert.Equal(expected, (double)First(source, TokenKind.Real).Value!, 10);
    }

    // "1.2.3" satisfies LooksLikeNumber but fails double.TryParse, so it must fall through to the
    // keyword branch rather than silently becoming 1.2 or throwing.
    [Fact]
    public void MalformedNumber_FallsThroughToKeyword()
    {
        (IReadOnlyList<Token> tokens, _) = Sut.Lex("1.2.3");

        Assert.Equal(TokenKind.Keyword, tokens[0].Kind);
        Assert.Equal("1.2.3", tokens[0].Value);
    }

    [Fact]
    public void WordWithoutDigits_IsNotANumber()
    {
        Assert.Equal(TokenKind.Keyword, Sut.Lex("EEE").Tokens[0].Kind);
    }

    [Fact]
    public void Enumeration_IsUppercasedAndUnwrapped()
    {
        Assert.Equal("FOO", First(".foo.", TokenKind.Enumeration).Value);
    }

    [Fact]
    public void BinaryLiteral_IsUnwrapped()
    {
        Assert.Equal("00FF", First("\"00FF\"", TokenKind.Binary).Value);
    }

    [Fact]
    public void String_UnescapesDoubledQuote()
    {
        Assert.Equal("A'B", First("'A''B'", TokenKind.String).Value);
    }

    [Fact]
    public void String_DecodesX2Escape()
    {
        Assert.Equal("A", First(@"'\X2\0041\X0\'", TokenKind.String).Value);
    }

    [Fact]
    public void String_DecodesMultipleCodeUnitsInOneX2Sequence()
    {
        Assert.Equal("AB", First(@"'\X2\00410042\X0\'", TokenKind.String).Value);
    }

    [Fact]
    public void String_DecodesX4Escape()
    {
        // U+1F600 GRINNING FACE — a supplementary-plane code point, which is the reason \X4\ exists.
        Assert.Equal("\U0001F600", First(@"'\X4\0001F600\X0\'", TokenKind.String).Value);
    }

    [Fact]
    public void String_UnterminatedEscapeSequence_Warns()
    {
        (_, List<ParseDiagnostic> diagnostics) = Sut.Lex(@"'\X2\0041'");

        Assert.Contains(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("Unterminated encoded string"));
    }

    [Fact]
    public void String_InvalidHexInEscape_Warns()
    {
        (_, List<ParseDiagnostic> diagnostics) = Sut.Lex(@"'\X2\ZZZZ\X0\'");

        Assert.Contains(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("Invalid encoded character segment"));
    }

    [Fact]
    public void EmptyString_DecodesToEmpty()
    {
        Assert.Equal(string.Empty, First("''", TokenKind.String).Value);
    }

    [Fact]
    public void PreservesSourceCoordinates()
    {
        (IReadOnlyList<Token> tokens, _) = Sut.Lex("HEADER\n  DATA");

        Assert.Equal((2, 3), (tokens[1].Line, tokens[1].Column));
    }
}
