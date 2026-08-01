using System.Reflection;
using System.Text.Json;
using StepParser.Diagnostics;
using StepParser.Output;
using StepParser.Parser;

namespace StepParser.Tests;

/// <summary>
/// Properties the tool must satisfy for its own output to be trustworthy. These are deliberately
/// not example-based: they assert that the reported result cannot contradict itself, so a change
/// that made the parser "succeed" by dropping information would fail here even if every
/// example-based test still passed.
/// </summary>
public sealed class InvariantTests
{
    public static TheoryData<string, string> Corpus => new()
    {
        { nameof(StepSample.Minimal), StepSample.Minimal },
        { nameof(StepSample.AllParameterKinds), StepSample.AllParameterKinds },
        { nameof(StepSample.Ap242Pmi), StepSample.Ap242Pmi },
        { "MalformedComplex", StepSample.WithData("#1=LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.);") },
        { "BareKeyword", StepSample.WithData("#1=A(BARE);") },
        { "UnexpectedToken", StepSample.WithData("#1=A(=);") },
        { "EmptyData", StepSample.WithData("") },
    };

    // ── Exit code cannot contradict the diagnostics ─────────────────────────

    [Theory]
    [MemberData(nameof(Corpus))]
    public void ExitCodeAgreesWithTheDiagnosticsItReported(string name, string source)
    {
        CliRun run = Sut.RunCli(source);
        ParseResult result = Sut.ParseToResult(source);

        bool hasErrors = result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
        bool hasWarnings = result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning);

        int expected = hasErrors ? 2 : hasWarnings ? 3 : 0;
        Assert.True(expected == run.ExitCode,
            $"{name}: exit {run.ExitCode} but errors={hasErrors} warnings={hasWarnings}");
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void ExitZeroImpliesNothingWasReported(string name, string source)
    {
        CliRun run = Sut.RunCli(source);
        if (run.ExitCode != 0)
        {
            return;
        }

        using JsonDocument document = JsonDocument.Parse(run.Stdout);
        Assert.True(
            document.RootElement.GetProperty("diagnostics").GetArrayLength() == 0,
            $"{name}: exit 0 but diagnostics were reported");
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void StrictNeverReportsFewerProblemsThanNonStrict(string name, string source)
    {
        int relaxed = Sut.RunCli(source).ExitCode;
        int strict = Sut.RunCli(source, "--strict").ExitCode;

        // Under --strict a warning becomes an error; nothing may become *less* severe.
        int Severity(int exitCode) => exitCode switch { 0 => 0, 3 => 1, 2 => 2, _ => 3 };
        Assert.True(Severity(strict) >= Severity(relaxed), $"{name}: strict={strict} relaxed={relaxed}");
    }

    // ── Counts cannot contradict the payload ────────────────────────────────

    [Theory]
    [MemberData(nameof(Corpus))]
    public void ReportedEntityCountMatchesTheEntitiesEmitted(string name, string source)
    {
        string json = new JsonFormatter().Format(Sut.ParseToResult(source));
        using JsonDocument document = JsonDocument.Parse(json);

        int declared = document.RootElement.GetProperty("stats").GetProperty("entityCount").GetInt32();
        int actual = document.RootElement.GetProperty("entities").GetArrayLength();

        Assert.True(declared == actual, $"{name}: stats.entityCount={declared} but {actual} entities emitted");
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void EntityTypeFrequenciesSumToTheNumberOfNamedOccurrences(string name, string source)
    {
        ParseResult result = Sut.ParseToResult(source);

        int declaredTotal = result.Stats.EntityTypes.Values.Sum();
        int actualTotal = result.Entities.Sum(entity => entity.Names?.Count ?? (entity.Name is null ? 0 : 1));

        Assert.True(declaredTotal == actualTotal, $"{name}: frequency sum {declaredTotal} != occurrences {actualTotal}");
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void TextAndJsonAgreeOnTheDiagnosticCount(string name, string source)
    {
        ParseResult result = Sut.ParseToResult(source);
        string text = new TextFormatter(noColor: true).Format(result);

        int textLines = text.Split('\n')
            .Count(line => line.StartsWith("  ERROR", StringComparison.Ordinal)
                        || line.StartsWith("  WARNING", StringComparison.Ordinal)
                        || line.StartsWith("  INFO", StringComparison.Ordinal));

        Assert.True(result.Diagnostics.Count == textLines,
            $"{name}: {result.Diagnostics.Count} diagnostics but {textLines} text lines");
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void EveryDiagnosticCarriesUsableSourceCoordinates(string name, string source)
    {
        ParseResult result = Sut.ParseToResult(source);

        Assert.All(result.Diagnostics, diagnostic =>
        {
            Assert.True(diagnostic.Line >= 1, $"{name}: line {diagnostic.Line} for '{diagnostic.Message}'");
            Assert.True(diagnostic.Column >= 1, $"{name}: column {diagnostic.Column} for '{diagnostic.Message}'");
            Assert.False(string.IsNullOrWhiteSpace(diagnostic.Message), $"{name}: blank diagnostic message");
        });
    }

    // ── The projection cannot silently drop information ─────────────────────

    /// <summary>
    /// Every <see cref="Parameter"/> variant must map to a real JSON discriminator. The projection
    /// falls through to <c>kind:"unknown"</c>, which loses the value silently — so this walks the
    /// type hierarchy by reflection rather than an "everything I remembered" list. Adding a variant
    /// without extending the projection fails here.
    /// </summary>
    [Fact]
    public void EveryParameterVariantHasItsOwnJsonDiscriminator()
    {
        Type[] variants = typeof(Parameter)
            .GetNestedTypes(BindingFlags.Public)
            .Where(type => type.IsSubclassOf(typeof(Parameter)))
            .ToArray();

        Assert.NotEmpty(variants);

        HashSet<string> kinds = new(StringComparer.Ordinal);
        foreach (Type variant in variants)
        {
            Parameter instance = Instantiate(variant);
            object projected = ParameterJsonProjection.Create(instance);
            string kind = (string)projected.GetType().GetProperty("kind")!.GetValue(projected)!;

            Assert.True(kind != "unknown", $"{variant.Name} falls through to kind:\"unknown\"");
            Assert.True(kinds.Add(kind), $"{variant.Name} reuses discriminator '{kind}'");
        }
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void NoParsedParameterIsEverSerialisedAsUnknown(string name, string source)
    {
        string json = new JsonFormatter().Format(Sut.ParseToResult(source));

        Assert.False(json.Contains("\"kind\": \"unknown\"", StringComparison.Ordinal),
            $"{name}: a parameter was dropped by the JSON projection");
    }

    private static Parameter Instantiate(Type variant)
    {
        ConstructorInfo constructor = variant.GetConstructors()
            .OrderBy(candidate => candidate.GetParameters().Length)
            .First();

        object?[] arguments = constructor.GetParameters()
            .Select(parameter => DefaultFor(parameter.ParameterType))
            .ToArray();

        return (Parameter)constructor.Invoke(arguments);
    }

    private static object? DefaultFor(Type type)
    {
        if (type == typeof(string)) return string.Empty;
        if (type == typeof(int)) return 0;
        if (type == typeof(long)) return 0L;
        if (type == typeof(double)) return 0d;
        if (type == typeof(Parameter)) return new Parameter.UnsetValue();
        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type)) return Array.Empty<Parameter>();
        throw new NotSupportedException(
            $"No default for {type.Name}; extend DefaultFor so new Parameter variants stay covered.");
    }

    // ── Determinism and refusal to fake success ─────────────────────────────

    [Theory]
    [MemberData(nameof(Corpus))]
    public void ParsingTheSameInputTwiceProducesIdenticalJson(string name, string source)
    {
        // Same path both times: the source path is part of the payload, so a fresh temp file per
        // run would make this test fail for a reason that has nothing to do with determinism.
        using TempFile file = new(source);
        string first = Sut.RunCliOnPath(file.Path).Stdout;
        string second = Sut.RunCliOnPath(file.Path).Stdout;

        Assert.True(first == second, $"{name}: output is not deterministic");
    }

    [Theory]
    [InlineData("")]
    [InlineData("this is not a STEP file at all")]
    [InlineData("{\"json\": true}")]
    [InlineData("ISO-10303-21;")]
    public void NonPartTwentyOneInput_IsNeverReportedAsClean(string garbage)
    {
        CliRun run = Sut.RunCli(garbage);

        Assert.True(run.ExitCode != 0, $"garbage input was accepted as clean: '{garbage}'");
    }

    [Fact]
    public void TruncatingAValidFileAlwaysDegradesTheResult()
    {
        // A prefix of a valid file cannot parse as cleanly as the whole thing.
        string truncated = StepSample.Minimal[..(StepSample.Minimal.Length / 2)];

        Assert.Equal(0, Sut.RunCli(StepSample.Minimal).ExitCode);
        Assert.NotEqual(0, Sut.RunCli(truncated).ExitCode);
    }

    [Fact]
    public void SweepReportsTheSameExitCodeAsADirectRun()
    {
        using TempFile clean = new(StepSample.Minimal);
        using TempFile broken = new("not a step file");
        string csv = TempFile.ReservePath(".csv");
        try
        {
            Sut.RunCliRawWithStdin($"{clean.Path}\n{broken.Path}", "--sweep", csv);
            Dictionary<string, int> sweepCodes = File.ReadAllLines(csv)
                .Skip(1)
                .Select(line => line.Split("\","))
                .ToDictionary(parts => parts[0].TrimStart('"'), parts => int.Parse(parts[1].Split(',')[0]));

            foreach ((string path, int sweepExit) in sweepCodes)
            {
                Assert.Equal(Sut.RunCliOnPath(path).ExitCode, sweepExit);
            }
        }
        finally
        {
            File.Delete(csv);
        }
    }
}
