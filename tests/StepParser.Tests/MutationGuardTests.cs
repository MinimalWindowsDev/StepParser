using StepParser.Diagnostics;
using StepParser.Lexer;
using StepParser.Output;
using StepParser.Parser;

namespace StepParser.Tests;

/// <summary>
/// Tests added in response to specific surviving mutants. Each one closes a place where the suite
/// had coverage but no constraint — the code ran, and could have returned something different
/// without any test noticing. Named for the behaviour they pin, not for the mutant.
/// </summary>
public sealed class MutationGuardTests
{
    // ── Parser ──────────────────────────────────────────────────────────────

    [Fact]
    public void EntityWithAnUnparseableIdentifier_IsNotAddedToTheModel()
    {
        // If TryGetEntityId reported success on failure, a phantom entity #0 would appear.
        (StepFile file, _) = Sut.Parse(StepSample.WithData("#=BROKEN();\n#2=GOOD();"));

        Assert.DoesNotContain(0, file.Data.Keys);
        Assert.Equal([2], file.Data.Keys.OrderBy(id => id));
    }

    // ── CLI ─────────────────────────────────────────────────────────────────

    [Fact]
    public void StrictAnnotation_StatesHowManyWarningsWerePromoted()
    {
        // Two non-conformant complex entities ⇒ exactly two warnings.
        string source = StepSample.WithData("""
            #1=LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.);
            #2=PLANE_ANGLE_UNIT() NAMED_UNIT(*) SI_UNIT($,.RADIAN.);
            """);

        CliRun run = Sut.RunCli(source, "--strict", "--no-color");

        Assert.Contains("strict: 2 warning(s) promoted", run.Stderr);
    }

    [Fact]
    public void UnhandledFailure_ExplainsItselfOnStderr()
    {
        // Exit 2 with a silent stderr would leave the user with no idea what happened.
        CliRun run = Sut.RunCli(StepSample.Minimal, "--output", Path.GetTempPath());

        Assert.Equal(2, run.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(run.Stderr), "the exception path wrote nothing to stderr");
    }

    [Fact]
    public void NoColorFlag_ActuallySuppressesAnsiThatWouldOtherwiseBeEmitted()
    {
        string source = StepSample.WithData("#1=LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.);");

        string coloured = Sut.RunCli(source, "--format", "text").Stdout;
        string plain = Sut.RunCli(source, "--format", "text", "--no-color").Stdout;

        Assert.Contains("[", coloured);
        Assert.DoesNotContain("[", plain);
    }

    // ── Sweep ───────────────────────────────────────────────────────────────

    [Fact]
    public void SweepOverwritesItsCsvRatherThanAppending()
    {
        using TempFile input = new(StepSample.Minimal);
        string csv = TempFile.ReservePath(".csv");
        try
        {
            Sut.RunCliRawWithStdin(input.Path, "--sweep", csv);
            Sut.RunCliRawWithStdin(input.Path, "--sweep", csv);

            Assert.Equal(2, File.ReadAllLines(csv).Length);   // header + one row, not two runs' worth
        }
        finally
        {
            File.Delete(csv);
        }
    }

    [Fact]
    public void SweepHistogramIsOrderedByAscendingExitCode()
    {
        using TempFile clean = new(StepSample.Minimal);
        using TempFile warning = new(StepSample.WithData("#1=LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.);"));
        string csv = TempFile.ReservePath(".csv");
        try
        {
            string summary = Sut.RunCliRawWithStdin($"{clean.Path}\n{warning.Path}", "--sweep", csv).Stdout;

            Assert.True(
                summary.IndexOf("EXIT_0=", StringComparison.Ordinal) < summary.IndexOf("EXIT_3=", StringComparison.Ordinal),
                "exit-code histogram is not in ascending order");
        }
        finally
        {
            File.Delete(csv);
        }
    }

    [Fact]
    public void SweepListsTheSlowestFilesRatherThanSkippingThem()
    {
        using TempFile input = new(StepSample.Minimal);
        string csv = TempFile.ReservePath(".csv");
        try
        {
            string summary = Sut.RunCliRawWithStdin(input.Path, "--sweep", csv).Stdout;
            string slowSection = summary.Split("TOP_SLOW=")[1].Split("TOP_FAIL=")[0];

            Assert.Contains(input.Path, slowSection);
        }
        finally
        {
            File.Delete(csv);
        }
    }

    // ── Semantic model ──────────────────────────────────────────────────────

    [Fact]
    public void ModelBasedDefinition_NeedsOnlyOneOfPmiOrGdt()
    {
        // ANNOTATION_TEXT is in the PMI catalog but not the GD&T one.
        Ap242SemanticSummary summary = Sut
            .ParseToResult(StepSample.WithData("#1=ANNOTATION_TEXT('note');", schema: "AP242_TEST"))
            .Semantics;

        Assert.True(summary.HasPmi);
        Assert.False(summary.HasGdt);
        Assert.True(summary.HasModelBasedDefinition, "MBD must hold when only PMI is present");
    }

    [Fact]
    public void ZeroValuedMeasure_CountsAsAnUpperTolerance()
    {
        // Pins the >= 0 boundary: a symmetric-zero tolerance must not be discarded.
        Ap242SemanticSummary summary = Sut.ParseToResult(StepSample.WithData("""
            #1=DIMENSIONAL_SIZE(#5,'d');
            #2=MEASURE_WITH_UNIT(LENGTH_MEASURE(25.0),#9);
            #3=MEASURE_WITH_UNIT(LENGTH_MEASURE(0.0),#9);
            """)).Semantics;

        Assert.Equal(0.0, Assert.Single(summary.Dimensions).UpperTolerance);
    }

    [Fact]
    public void MeasureWithNoParameters_IsSkippedRatherThanThrowing()
    {
        Ap242SemanticSummary summary = Sut.ParseToResult(StepSample.WithData("""
            #1=DIMENSIONAL_SIZE(#5,'d');
            #2=MEASURE_WITH_UNIT();
            #3=MEASURE_WITH_UNIT(LENGTH_MEASURE(7.0),#9);
            """)).Semantics;

        Assert.Equal(7.0, Assert.Single(summary.Dimensions).NominalValue);
    }

    // ── Text output ─────────────────────────────────────────────────────────

    [Fact]
    public void TextOutput_PrintsTheActualHeaderValuesNotJustPlaceholders()
    {
        using TempFile input = new(StepSample.Ap242Pmi);

        string output = Sut.RunCliOnPath(input.Path, "--format", "text", "--no-color").Stdout;

        Assert.Contains($"Source: {input.Path}", output);
        Assert.Contains("Schema: AP242_MANAGED_MODEL_BASED_3D_ENGINEERING_MIM_LF", output);
        Assert.Contains("Edition: 2", output);
        Assert.Contains("Entities: 12", output);
    }

    [Fact]
    public void TextOutput_PrintsTheActualPmiAndGdtCounts()
    {
        string output = new TextFormatter(noColor: true).Format(Sut.ParseToResult(StepSample.Ap242Pmi));

        ParseResult result = Sut.ParseToResult(StepSample.Ap242Pmi);
        Assert.Contains($"PMI: {result.Semantics.CategoryCounts["pmi"]}", output);
        Assert.Contains($"GD&T: {result.Semantics.CategoryCounts["gdt"]}", output);
        Assert.DoesNotContain("PMI: 0", output);
    }

    [Fact]
    public void TextOutput_ExactlyTwentyEntityTypes_HasNoTruncationLine()
    {
        Dictionary<string, int> types = Enumerable.Range(1, 20).ToDictionary(i => $"TYPE_{i:D2}", i => i);

        string output = new TextFormatter(noColor: true).Format(
            OutputFormatterTests.ResultWith(entityTypes: types));

        Assert.DoesNotContain("more type(s)", output);
        Assert.Equal(20, output.Split('\n').Count(line => line.TrimStart().StartsWith("TYPE_", StringComparison.Ordinal)));
    }

    [Fact]
    public void TextOutput_TiedEntityCountsAreOrderedAlphabetically()
    {
        Dictionary<string, int> types = new() { ["ZULU"] = 5, ["ALPHA"] = 5, ["MIKE"] = 5 };

        string output = new TextFormatter(noColor: true).Format(
            OutputFormatterTests.ResultWith(entityTypes: types));

        int alpha = output.IndexOf("ALPHA: 5", StringComparison.Ordinal);
        int mike = output.IndexOf("MIKE: 5", StringComparison.Ordinal);
        int zulu = output.IndexOf("ZULU: 5", StringComparison.Ordinal);

        Assert.True(alpha < mike && mike < zulu, "tied entity types are not in ascending name order");
    }

    // ── Tokenizer and lexer boundaries ──────────────────────────────────────

    [Fact]
    public void BlockCommentContainingALoneAsterisk_IsNotClosedEarly()
    {
        (IReadOnlyList<RawToken> tokens, List<ParseDiagnostic> diagnostics) =
            Sut.Tokenize("HEADER /* a * b ** c */ DATA");

        Assert.Empty(diagnostics);
        Assert.Equal(["HEADER", "DATA", ""], tokens.Select(token => token.Lexeme));
    }

    [Fact]
    public void BlockCommentContainingASlash_IsNotClosedEarly()
    {
        (IReadOnlyList<RawToken> tokens, List<ParseDiagnostic> diagnostics) =
            Sut.Tokenize("HEADER /* a / b */ DATA");

        Assert.Empty(diagnostics);
        Assert.Equal(["HEADER", "DATA", ""], tokens.Select(token => token.Lexeme));
    }

    [Theory]
    [InlineData(@"'\X2'")]      // truncated introducer — must not read past the end
    [InlineData(@"'\X2\'")]     // introducer with no payload and no terminator
    [InlineData(@"'\X'")]
    [InlineData(@"'\'")]
    [InlineData(@"'\X0\'")]
    public void TruncatedEscapeIntroducers_AreHandledWithoutOverrunning(string literal)
    {
        // Any off-by-one in the escape-window bounds turns these into an IndexOutOfRangeException.
        (IReadOnlyList<Token> tokens, _) = Sut.Lex(literal);

        Assert.Equal(TokenKind.String, tokens[0].Kind);
        Assert.NotNull(tokens[0].Value);
    }

    /// <summary>
    /// The payload is exactly three characters, so the scan loop's window lands precisely on the
    /// end of the literal. One off-by-one either overruns the buffer or silently accepts the
    /// sequence as terminated; both must instead produce the unterminated-escape warning.
    /// </summary>
    [Fact]
    public void EscapeWindowEndingExactlyAtTheLiteralBoundary_WarnsRatherThanOverrunning()
    {
        (IReadOnlyList<Token> tokens, List<ParseDiagnostic> diagnostics) = Sut.Lex(@"'\X2\abc'");

        Assert.Equal(TokenKind.String, tokens[0].Kind);
        Assert.Contains(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("Unterminated encoded string"));
    }

    [Fact]
    public void ZeroValuedMeasureAfterAnUpperTolerance_IsNotRecordedAsALowerTolerance()
    {
        // Pins the strict "< 0" boundary: zero is not negative.
        Ap242SemanticSummary summary = Sut.ParseToResult(StepSample.WithData("""
            #1=DIMENSIONAL_SIZE(#5,'d');
            #2=MEASURE_WITH_UNIT(LENGTH_MEASURE(25.0),#9);
            #3=MEASURE_WITH_UNIT(LENGTH_MEASURE(0.2),#9);
            #4=MEASURE_WITH_UNIT(LENGTH_MEASURE(0.0),#9);
            """)).Semantics;

        PmiDimensionSummary dimension = Assert.Single(summary.Dimensions);
        Assert.Equal(0.2, dimension.UpperTolerance);
        Assert.Null(dimension.LowerTolerance);
    }

    [Fact]
    public void EscapeSequenceImmediatelyFollowedByMoreText_ResumesAfterTheTerminator()
    {
        Assert.Equal("AB", Sut.Lex(@"'\X2\0041\X0\B'").Tokens
            .First(token => token.Kind == TokenKind.String).Value);
    }

    [Fact]
    public void TextBeforeAnEscapeSequence_IsPreserved()
    {
        Assert.Equal("preA", Sut.Lex(@"'pre\X2\0041\X0\'").Tokens
            .First(token => token.Kind == TokenKind.String).Value);
    }
}
