using StepParser.Diagnostics;
using StepParser.Lexer;
using StepParser.Logging;
using StepParser.Output;
using StepParser.Parser;

namespace StepParser.Tests;

/// <summary>A type whose ToString() returns null, to reach the null-coalescing render fallbacks.</summary>
internal sealed class NullStringer
{
    public override string? ToString() => null;
}

/// <summary>
/// Defensive and boundary paths that ordinary usage does not reach. Each one corresponds to a
/// specific guard in the product code; they are gathered here so the reason each exists stays
/// visible rather than being buried in a feature-shaped test.
/// </summary>
public sealed class BoundaryTests : IDisposable
{
    public void Dispose() => StepLogger.CloseAndFlush();

    // ── Process entry point ─────────────────────────────────────────────────

    [Fact]
    public void ProgramMain_RunsThePipelineAndFlushesLogging()
    {
        using TempFile input = new(StepSample.Minimal);
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        StringWriter captured = new();
        try
        {
            Console.SetOut(captured);
            Console.SetError(new StringWriter());

            int exitCode = Program.Main([input.Path]);

            Assert.Equal(0, exitCode);
            Assert.Contains("\"schema\"", captured.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void ProgramMain_PropagatesArgumentErrors()
    {
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        try
        {
            Console.SetOut(new StringWriter());
            Console.SetError(new StringWriter());

            Assert.Equal(4, Program.Main([]));
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    // ── Short option forms ──────────────────────────────────────────────────

    [Fact]
    public void ShortOutputFlag_WritesTheFile()
    {
        string outputPath = TempFile.ReservePath(".json");
        try
        {
            Assert.Equal(0, Sut.RunCli(StepSample.Minimal, "-o", outputPath).ExitCode);
            Assert.True(File.Exists(outputPath));
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void ShortFormatFlag_SelectsTextOutput()
    {
        CliRun run = Sut.RunCli(StepSample.Minimal, "-f", "text", "--no-color");

        Assert.Contains("Entity types:", run.Stdout);
    }

    [Fact]
    public void ShortVerboseFlag_PrintsProgress()
    {
        Assert.Contains("Parsed", Sut.RunCli(StepSample.Minimal, "-v").Stderr);
    }

    // ── Parser guards ───────────────────────────────────────────────────────

    [Fact]
    public void NonStringTokenInEntityNamePosition_IsReportedRatherThanCrashing()
    {
        // "123" lexes as an Integer, so Expect(Keyword) fails and hands CoerceName a token whose
        // Value is a long — the branch that upper-cases the raw lexeme and reports an error.
        (StepFile file, List<ParseDiagnostic> diagnostics) = Sut.Parse(StepSample.WithData("#1=123();"));

        Assert.Contains(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Invalid entity name token"));
        Assert.NotNull(file);
    }

    [Fact]
    public void EntityStatementStartingWithAKeyword_IsReportedRatherThanCrashing()
    {
        // Expect(EntityRef) hands TryGetEntityId a Keyword token: non-null Value, wrong type.
        (_, List<ParseDiagnostic> diagnostics) = Sut.Parse(StepSample.WithData("FOO();"));

        Assert.Contains(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Invalid entity identifier token"));
    }

    [Fact]
    public void ParsingASourcePathThatDoesNotExist_ReportsZeroFileSize()
    {
        (IReadOnlyList<Token> tokens, List<ParseDiagnostic> diagnostics) = Sut.Lex(StepSample.Minimal);
        StepFileParser parser = new(tokens, diagnostics);

        StepFile file = parser.Parse(Path.Combine(Path.GetTempPath(), $"absent_{Guid.NewGuid():N}.stp"));

        Assert.Equal(0L, file.FileSize);
    }

    // ── Header extraction guards ────────────────────────────────────────────

    [Fact]
    public void HeaderFieldExpectedToBeAListButIsNot_YieldsNull()
    {
        // FILE_SCHEMA's parameter is a bare string rather than the required list.
        (StepFile file, _) = Sut.Parse("""
            ISO-10303-21;
            HEADER;
            FILE_DESCRIPTION(('t'),'2;1');
            FILE_NAME('f','2024',('a'),('b'),' ','s',' ');
            FILE_SCHEMA('not a list');
            ENDSEC;
            DATA;
            ENDSEC;
            END-ISO-10303-21;
            """);

        Assert.Null(HeaderSummary.FromSection(file.Header).Schemas);
        Assert.Null(file.FileSchema);
    }

    [Fact]
    public void HeaderEntityWithTooFewParameters_YieldsNulls()
    {
        (StepFile file, _) = Sut.Parse("""
            ISO-10303-21;
            HEADER;
            FILE_DESCRIPTION();
            FILE_NAME();
            FILE_SCHEMA();
            ENDSEC;
            DATA;
            ENDSEC;
            END-ISO-10303-21;
            """);

        HeaderSummary summary = HeaderSummary.FromSection(file.Header);
        Assert.Null(summary.FileDescription);
        Assert.Null(summary.Name);
        Assert.Null(summary.Authors);
        Assert.Null(summary.Schemas);
    }

    // ── Lexer guards ────────────────────────────────────────────────────────

    [Fact]
    public void StringLiteralTooShortToUnwrap_DecodesToEmpty()
    {
        // An unterminated single quote reaches the lexer as the one-character lexeme "'".
        (IReadOnlyList<Token> tokens, _) = Sut.Lex("'");

        Assert.Equal(string.Empty, tokens.First(token => token.Kind == TokenKind.String).Value);
    }

    // ── Semantic-model guards ───────────────────────────────────────────────

    [Fact]
    public void SecondNegativeMeasure_DoesNotOverwriteTheLowerTolerance()
    {
        Ap242SemanticSummary summary = Sut.ParseToResult(StepSample.WithData("""
            #1=DIMENSIONAL_SIZE(#5,'d');
            #2=MEASURE_WITH_UNIT(LENGTH_MEASURE(25.0),#9);
            #3=MEASURE_WITH_UNIT(LENGTH_MEASURE(-0.1),#9);
            #4=MEASURE_WITH_UNIT(LENGTH_MEASURE(-0.9),#9);
            """)).Semantics;

        Assert.Equal(-0.1, Assert.Single(summary.Dimensions).LowerTolerance);
    }

    [Fact]
    public void SecondPositiveMeasure_DoesNotOverwriteTheUpperTolerance()
    {
        Ap242SemanticSummary summary = Sut.ParseToResult(StepSample.WithData("""
            #1=DIMENSIONAL_SIZE(#5,'d');
            #2=MEASURE_WITH_UNIT(LENGTH_MEASURE(25.0),#9);
            #3=MEASURE_WITH_UNIT(LENGTH_MEASURE(0.2),#9);
            #4=MEASURE_WITH_UNIT(LENGTH_MEASURE(0.9),#9);
            """)).Semantics;

        PmiDimensionSummary dimension = Assert.Single(summary.Dimensions);
        Assert.Equal(0.2, dimension.UpperTolerance);
        Assert.Null(dimension.LowerTolerance);
    }

    // ── Formatter guards ────────────────────────────────────────────────────

    [Fact]
    public void TextFormatter_PayloadWhoseToStringIsNull_RendersEmpty()
    {
        Assert.Equal(string.Empty, new TextFormatter(noColor: true).Format(new NullStringer()));
    }

    // ── Logger guards ───────────────────────────────────────────────────────

    [Fact]
    public void AspectHelpers_AreSafeWhenNoLoggerIsConfigured()
    {
        StepLogger.CloseAndFlush();     // leaves the instance null

        StepLogger.Entry("Class", "Method", "args");
        StepLogger.Exit("Class", "Method", "result", 1);
        StepLogger.ExceptionIn("Class", "Method", new InvalidOperationException("x"));
    }
}
