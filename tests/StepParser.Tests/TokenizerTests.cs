using StepParser.Diagnostics;
using StepParser.Lexer;

namespace StepParser.Tests;

public sealed class TokenizerTests
{
    [Fact]
    public void MinimalSample_ProducesExpectedLeadingSequence()
    {
        (IReadOnlyList<RawToken> tokens, List<ParseDiagnostic> diagnostics) = Sut.Tokenize(StepSample.Minimal);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(
            ["ISO-10303-21", ";", "HEADER", ";", "FILE_DESCRIPTION", "(", "(",
             "'A minimal AP214 example'", ")", ",", "'2;1'", ")"],
            tokens.Take(12).Select(token => token.Lexeme));
    }

    [Fact]
    public void EmitsTerminatingEmptyToken()
    {
        (IReadOnlyList<RawToken> tokens, _) = Sut.Tokenize("HEADER");

        Assert.Equal(string.Empty, tokens[^1].Lexeme);
    }

    [Theory]
    [InlineData("=")]
    [InlineData(";")]
    [InlineData(",")]
    [InlineData("(")]
    [InlineData(")")]
    [InlineData("$")]
    [InlineData("*")]
    public void ReadsEachPunctuationCharacterAsItsOwnToken(string punctuation)
    {
        (IReadOnlyList<RawToken> tokens, List<ParseDiagnostic> diagnostics) = Sut.Tokenize(punctuation);

        Assert.Empty(diagnostics);
        Assert.Equal(punctuation, tokens[0].Lexeme);
    }

    [Fact]
    public void StripsBlockComments()
    {
        (IReadOnlyList<RawToken> tokens, List<ParseDiagnostic> diagnostics) =
            Sut.Tokenize("HEADER /* this is dropped */ DATA");

        Assert.Empty(diagnostics);
        Assert.Equal(["HEADER", "DATA", ""], tokens.Select(token => token.Lexeme));
    }

    [Fact]
    public void UnterminatedBlockComment_ReportsErrorAtCommentStart()
    {
        (_, List<ParseDiagnostic> diagnostics) = Sut.Tokenize("HEADER /* never closed");

        ParseDiagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Unterminated block comment", diagnostic.Message);
        Assert.Equal(8, diagnostic.Column);
    }

    [Fact]
    public void TracksLineAndColumnAcrossNewlines()
    {
        (IReadOnlyList<RawToken> tokens, _) = Sut.Tokenize("A\nBB\n  CCC");

        Assert.Equal((1, 1), (tokens[0].Line, tokens[0].Column));
        Assert.Equal((2, 1), (tokens[1].Line, tokens[1].Column));
        Assert.Equal((3, 3), (tokens[2].Line, tokens[2].Column));
    }

    [Fact]
    public void ReadsStringWithDoubledQuoteEscape()
    {
        (IReadOnlyList<RawToken> tokens, List<ParseDiagnostic> diagnostics) = Sut.Tokenize("'a''b'");

        Assert.Empty(diagnostics);
        Assert.Equal("'a''b'", tokens[0].Lexeme);
    }

    [Fact]
    public void UnterminatedString_ReportsError()
    {
        (_, List<ParseDiagnostic> diagnostics) = Sut.Tokenize("'no closing quote");

        Assert.Contains(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Unterminated string literal"));
    }

    [Fact]
    public void UnterminatedBinaryLiteral_ReportsError()
    {
        (_, List<ParseDiagnostic> diagnostics) = Sut.Tokenize("\"00FF");

        Assert.Contains(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Unterminated quoted literal"));
    }

    [Fact]
    public void UnterminatedEnumeration_ReportsError()
    {
        (_, List<ParseDiagnostic> diagnostics) = Sut.Tokenize(".TRUE");

        Assert.Contains(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Unterminated enumeration literal"));
    }

    [Theory]
    [InlineData("123", "123")]
    [InlineData("1.5", "1.5")]
    [InlineData(".5", ".5")]
    [InlineData("+7", "+7")]
    [InlineData("-7", "-7")]
    [InlineData("-.5", "-.5")]
    [InlineData("1.2E-3", "1.2E-3")]
    public void ReadsNumberForms(string input, string expected)
    {
        (IReadOnlyList<RawToken> tokens, _) = Sut.Tokenize(input);

        Assert.Equal(expected, tokens[0].Lexeme);
    }

    [Theory]
    [InlineData("NAME", "NAME")]
    [InlineData("_LEADING", "_LEADING")]
    [InlineData("WITH_UNDER_1", "WITH_UNDER_1")]
    [InlineData("ISO-10303-21", "ISO-10303-21")]
    public void ReadsIdentifierForms(string input, string expected)
    {
        (IReadOnlyList<RawToken> tokens, _) = Sut.Tokenize(input);

        Assert.Equal(expected, tokens[0].Lexeme);
    }

    [Fact]
    public void ReadsEntityReference()
    {
        (IReadOnlyList<RawToken> tokens, _) = Sut.Tokenize("#4711");

        Assert.Equal("#4711", tokens[0].Lexeme);
    }

    [Fact]
    public void UnrecognizedCharacter_WarnsAndSkips()
    {
        (IReadOnlyList<RawToken> tokens, List<ParseDiagnostic> diagnostics) = Sut.Tokenize("A % B");

        ParseDiagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("Unrecognized character '%'", diagnostic.Message);
        Assert.Equal(["A", "B", ""], tokens.Select(token => token.Lexeme));
    }

    // A lone '/' is not punctuation, not an identifier start and not a number start, so it lands in
    // the unrecognized-character branch. This is what makes the documented "// comment" support
    // false; DocumentationContractTests asserts the docs do not claim otherwise.
    [Fact]
    public void DoubleSlash_IsNotTreatedAsAComment()
    {
        (_, List<ParseDiagnostic> diagnostics) = Sut.Tokenize("HEADER // tail remark");

        Assert.Equal(2, diagnostics.Count(d => d.Message.Contains("Unrecognized character '/'")));
    }

    [Fact]
    public void TokenizeFile_ReadsFromDisk()
    {
        using TempFile file = new(StepSample.Minimal);
        List<ParseDiagnostic> diagnostics = new();

        IReadOnlyList<RawToken> tokens = Tokenizer.TokenizeFile(file.Path, diagnostics);

        Assert.Equal("ISO-10303-21", tokens[0].Lexeme);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void EmptyInput_ProducesOnlyTheEndMarker()
    {
        (IReadOnlyList<RawToken> tokens, List<ParseDiagnostic> diagnostics) = Sut.Tokenize(string.Empty);

        Assert.Empty(diagnostics);
        RawToken only = Assert.Single(tokens);
        Assert.Equal(string.Empty, only.Lexeme);
    }
}
