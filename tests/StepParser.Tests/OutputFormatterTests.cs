using System.Text.Json;
using StepParser.Diagnostics;
using StepParser.Lexer;
using StepParser.Output;
using StepParser.Parser;

namespace StepParser.Tests;

/// <summary>
/// A Parameter variant the projection has never heard of. Its only job is to prove the
/// <c>_ =&gt; unknown</c> arm is reachable — see InvariantTests for why that arm is dangerous.
/// </summary>
internal sealed record UnknownParameter : Parameter;

public sealed class OutputFormatterTests
{
    internal static Ap242SemanticSummary EmptySemantics { get; } = new(
        IsAp242: false,
        HasModelBasedDefinition: false,
        HasPmi: false,
        HasGdt: false,
        CategoryCounts: new Dictionary<string, int> { ["representation"] = 0, ["geometry"] = 0, ["pmi"] = 0, ["gdt"] = 0 },
        Dimensions: [],
        Datums: [],
        DetectedEntityTypes: []);

    internal static ParseResult ResultWith(
        IReadOnlyList<ParseDiagnostic>? diagnostics = null,
        IReadOnlyDictionary<string, int>? entityTypes = null,
        Ap242SemanticSummary? semantics = null,
        IReadOnlyList<EntitySummary>? entities = null) =>
        new(
            Source: "synthetic.stp",
            FileSize: 0,
            Edition: null,
            Schema: null,
            Header: new HeaderSummary(null, null, null, null, null, null, null, null, null, null),
            Stats: new StatsSummary(entities?.Count ?? 0, entityTypes ?? new Dictionary<string, int>()),
            Semantics: semantics ?? EmptySemantics,
            Entities: entities ?? [],
            Diagnostics: diagnostics ?? [],
            Elapsed: TimeSpan.Zero);

    // ── JsonFormatter ───────────────────────────────────────────────────────

    [Fact]
    public void Json_ProjectsParseResultWithAllTopLevelSections()
    {
        string json = new JsonFormatter().Format(Sut.ParseToResult(StepSample.Minimal));
        using JsonDocument document = JsonDocument.Parse(json);

        foreach (string section in new[] { "source", "fileSize", "diagnostics", "header", "stats", "semantics", "entities" })
        {
            Assert.True(document.RootElement.TryGetProperty(section, out _), $"missing '{section}'");
        }
    }

    [Fact]
    public void Json_EmitsEveryParameterKindDiscriminator()
    {
        string json = new JsonFormatter().Format(Sut.ParseToResult(StepSample.AllParameterKinds));

        foreach (string kind in new[]
                 {
                     "string", "integer", "real", "enum", "binary",
                     "unset", "inherited", "entityRef", "list", "typed"
                 })
        {
            Assert.Contains($"\"kind\": \"{kind}\"", json);
        }
    }

    [Fact]
    public void Json_UnrecognisedParameterVariant_FallsBackToUnknown()
    {
        ParseResult result = ResultWith(
            entities: [new EntitySummary(1, "simple", "X", [new UnknownParameter()], null)]);

        Assert.Contains("\"kind\": \"unknown\"", new JsonFormatter().Format(result));
    }

    [Fact]
    public void Json_NonParseResultPayload_IsSerialisedDirectly()
    {
        TokenizationResult payload = new("f.stp", [new RawToken("HEADER", 1, 1)], [], TimeSpan.Zero);

        string json = new JsonFormatter().Format(payload);

        Assert.Contains("RawTokens", json);
        Assert.Contains("HEADER", json);
    }

    [Fact]
    public void Json_OmitsNullProperties()
    {
        string json = new JsonFormatter().Format(ResultWith());

        Assert.DoesNotContain("\"schema\": null", json);
    }

    [Fact]
    public void Json_SemanticsCarriesDimensionsAndDatums()
    {
        string json = new JsonFormatter().Format(Sut.ParseToResult(StepSample.Ap242Pmi));
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement semantics = document.RootElement.GetProperty("semantics");

        Assert.True(semantics.GetProperty("isAp242").GetBoolean());
        Assert.Equal(1, semantics.GetProperty("dimensions").GetArrayLength());
        Assert.Equal(1, semantics.GetProperty("datums").GetArrayLength());
    }

    // ── TextFormatter ───────────────────────────────────────────────────────

    [Fact]
    public void Text_FormatsTokenizationResult()
    {
        TokenizationResult payload = new("f.stp", [new RawToken("HEADER", 3, 7)], [], TimeSpan.Zero);

        Assert.Equal("3:7 HEADER", new TextFormatter(noColor: true).Format(payload));
    }

    [Fact]
    public void Text_FormatsLexingResult()
    {
        LexingResult payload = new("f.stp", [new Token(TokenKind.Header, "HEADER", "HEADER", 3, 7)], [], TimeSpan.Zero);

        Assert.Equal("3:7 Header HEADER", new TextFormatter(noColor: true).Format(payload));
    }

    [Fact]
    public void Text_UnknownPayload_FallsBackToToString()
    {
        Assert.Equal("42", new TextFormatter(noColor: true).Format(42));
    }

    [Fact]
    public void Text_NoDiagnostics_ReportsZero()
    {
        string output = new TextFormatter(noColor: true).Format(ResultWith());

        Assert.Contains("Diagnostics: 0", output);
    }

    [Fact]
    public void Text_ErrorsOnly_ShowsErrorCount()
    {
        string output = new TextFormatter(noColor: true).Format(
            ResultWith([new ParseDiagnostic(DiagnosticSeverity.Error, 1, 1, "boom")]));

        Assert.Contains("Errors: 1", output);
        Assert.DoesNotContain("Warnings:", output);
    }

    [Fact]
    public void Text_WarningsOnly_ShowsWarningCount()
    {
        string output = new TextFormatter(noColor: true).Format(
            ResultWith([new ParseDiagnostic(DiagnosticSeverity.Warning, 1, 1, "careful")]));

        Assert.Contains("Warnings: 1", output);
        Assert.DoesNotContain("Errors:", output);
    }

    [Fact]
    public void Text_BothSeverities_ShowsSplitCounts()
    {
        string output = new TextFormatter(noColor: true).Format(ResultWith(
        [
            new ParseDiagnostic(DiagnosticSeverity.Error, 1, 1, "boom"),
            new ParseDiagnostic(DiagnosticSeverity.Warning, 2, 1, "careful")
        ]));

        Assert.Contains("Errors: 1 | Warnings: 1", output);
    }

    [Fact]
    public void Text_ListsEveryDiagnosticWithoutCap()
    {
        ParseDiagnostic[] diagnostics = Enumerable.Range(1, 25)
            .Select(i => new ParseDiagnostic(DiagnosticSeverity.Error, i, 1, $"Synthetic error #{i}"))
            .ToArray();

        string output = new TextFormatter(noColor: true).Format(ResultWith(diagnostics));

        Assert.Equal(25, output.Split('\n').Count(line => line.StartsWith("  ERROR", StringComparison.Ordinal)));
    }

    [Fact]
    public void Text_NoEntityTypes_OmitsTheSection()
    {
        Assert.DoesNotContain("Entity types:", new TextFormatter(noColor: true).Format(ResultWith()));
    }

    [Fact]
    public void Text_EntityTypes_AreOrderedByDescendingCount()
    {
        string output = new TextFormatter(noColor: true).Format(ResultWith(
            entityTypes: new Dictionary<string, int> { ["RARE"] = 1, ["COMMON"] = 9 }));

        Assert.True(output.IndexOf("COMMON: 9", StringComparison.Ordinal)
                    < output.IndexOf("RARE: 1", StringComparison.Ordinal));
    }

    [Fact]
    public void Text_MoreThanTwentyEntityTypes_TruncatesWithAnExplicitCountLine()
    {
        Dictionary<string, int> types = Enumerable.Range(1, 25)
            .ToDictionary(i => $"TYPE_{i:D2}", i => i);

        string output = new TextFormatter(noColor: true).Format(ResultWith(entityTypes: types));

        Assert.Contains("… and 5 more type(s)", output);
    }

    [Theory]
    [InlineData(DiagnosticSeverity.Error, "[31m")]
    [InlineData(DiagnosticSeverity.Warning, "[33m")]
    public void Text_ColourEnabled_WrapsSeverityInAnsi(DiagnosticSeverity severity, string escape)
    {
        string output = new TextFormatter(noColor: false).Format(
            ResultWith([new ParseDiagnostic(severity, 1, 1, "m")]));

        Assert.Contains(escape, output);
    }

    [Fact]
    public void Text_InfoSeverity_IsNeverColoured()
    {
        string output = new TextFormatter(noColor: false).Format(
            ResultWith([new ParseDiagnostic(DiagnosticSeverity.Info, 1, 1, "note")]));

        Assert.DoesNotContain("[", output);
        Assert.Contains("INFO", output);
    }

    [Fact]
    public void Text_NoColour_EmitsNoEscapeSequences()
    {
        string output = new TextFormatter(noColor: true).Format(
            ResultWith([new ParseDiagnostic(DiagnosticSeverity.Error, 1, 1, "m")]));

        Assert.DoesNotContain("[", output);
    }

    [Fact]
    public void Text_ShowsSemanticSummaryLine()
    {
        string output = new TextFormatter(noColor: true).Format(Sut.ParseToResult(StepSample.Ap242Pmi));

        Assert.Contains("AP242: yes", output);
        Assert.Contains("MBD: yes", output);
    }

    [Fact]
    public void Text_NonAp242File_ShowsNegativeSemanticSummary()
    {
        string output = new TextFormatter(noColor: true).Format(Sut.ParseToResult(StepSample.Minimal));

        Assert.Contains("AP242: no", output);
        Assert.Contains("MBD: no", output);
    }

    [Fact]
    public void Text_ListsDimensionsAndDatums()
    {
        string output = new TextFormatter(noColor: true).Format(Sut.ParseToResult(StepSample.Ap242Pmi));

        Assert.Contains("DIM #2 width nominal=25", output);
        Assert.Contains("DATUM #4 A", output);
    }

    [Fact]
    public void Text_DimensionWithoutValues_RendersPlaceholders()
    {
        ParseResult result = ResultWith(semantics: EmptySemantics with
        {
            Dimensions = [new PmiDimensionSummary(7, "DIMENSIONAL_SIZE", null, null, null, null, null)],
            Datums = [new DatumSummary(8, null, null, null)]
        });

        string output = new TextFormatter(noColor: true).Format(result);

        Assert.Contains("DIM #7 <unnamed> nominal=<none> upper=<none> lower=<none>", output);
        Assert.Contains("DATUM #8 <unlabelled>", output);
    }

    [Fact]
    public void Text_UnknownSchemaAndEdition_RenderPlaceholders()
    {
        string output = new TextFormatter(noColor: true).Format(ResultWith());

        Assert.Contains("Schema: <unknown>", output);
        Assert.Contains("Edition: <unknown>", output);
    }
}
