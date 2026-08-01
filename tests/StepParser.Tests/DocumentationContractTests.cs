using System.Text.Json;
using System.Text.RegularExpressions;

namespace StepParser.Tests;

/// <summary>
/// Executable checks that the prose matches the program.
///
/// These exist because documentation drift is silent: the repository previously advertised an
/// AP242 semantic layer that nothing called, and tail-remark (<c>//</c>) comment support the
/// tokenizer has never implemented. Neither could be caught by a conventional unit test, because
/// the code was self-consistent — only the claims about it were wrong.
/// </summary>
public sealed class DocumentationContractTests
{
    private static string Readme => File.ReadAllText(Path.Combine(Sut.RepoRoot(), "README.md"));
    private static string ClaudeMd => File.ReadAllText(Path.Combine(Sut.RepoRoot(), "CLAUDE.md"));

    private static string HelpText => Sut.RunCliRaw("--help").Stdout;

    private static IReadOnlyList<string> LongOptionsIn(string text) =>
        Regex.Matches(text, @"(?<!\S)--[a-z][a-z-]+")
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(option => option, StringComparer.Ordinal)
            .ToList();

    // ── Flags ───────────────────────────────────────────────────────────────

    [Fact]
    public void ReadmeDocumentsEveryOptionTheCliAdvertises()
    {
        IReadOnlyList<string> missing = LongOptionsIn(HelpText)
            .Where(option => !Readme.Contains(option, StringComparison.Ordinal))
            .ToList();

        Assert.True(missing.Count == 0, $"README omits documented CLI options: {string.Join(", ", missing)}");
    }

    [Fact]
    public void CliAcceptsEveryOptionTheReadmeDocuments()
    {
        // The README's fenced usage block is the contract; anything it lists must not be rejected.
        string usageBlock = Regex.Match(Readme, @"```\s*Usage: StepParser.*?```", RegexOptions.Singleline).Value;
        Assert.False(string.IsNullOrEmpty(usageBlock), "README no longer contains a usage block");

        using TempFile input = new(StepSample.Minimal);
        foreach (string option in LongOptionsIn(usageBlock))
        {
            CliRun run = Sut.RunCliRaw(input.Path, option);
            Assert.False(
                run.Stderr.Contains($"Unknown option: {option}", StringComparison.Ordinal),
                $"README documents {option} but the CLI rejects it as unknown");
        }
    }

    [Fact]
    public void HelpTextAndReadmeAgreeOnTheOptionSet()
    {
        string usageBlock = Regex.Match(Readme, @"```\s*Usage: StepParser.*?```", RegexOptions.Singleline).Value;

        Assert.Equal(LongOptionsIn(HelpText), LongOptionsIn(usageBlock));
    }

    // ── Exit codes ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "clean")]
    [InlineData(1, "input file not found")]
    [InlineData(2, "errors")]
    [InlineData(3, "warnings only")]
    [InlineData(4, "bad arguments")]
    public void ReadmeDocumentsEachExitCodeTheCliCanReturn(int code, string description)
    {
        string exitLine = Readme.Split('\n').Single(line => line.StartsWith("Exit codes:", StringComparison.Ordinal));

        Assert.Contains($"`{code}`", exitLine);
        Assert.Contains(description, exitLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EachDocumentedExitCodeIsActuallyReachable()
    {
        string absent = Path.Combine(Path.GetTempPath(), $"absent_{Guid.NewGuid():N}.stp");
        string warningSource = StepSample.WithData("#1=LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.);");

        Assert.Equal(0, Sut.RunCli(StepSample.Minimal).ExitCode);
        Assert.Equal(1, Sut.RunCliRaw(absent).ExitCode);
        Assert.Equal(2, Sut.RunCli(warningSource, "--strict").ExitCode);
        Assert.Equal(3, Sut.RunCli(warningSource).ExitCode);
        Assert.Equal(4, Sut.RunCliRaw().ExitCode);
    }

    // ── Feature claims ──────────────────────────────────────────────────────

    [Fact]
    public void ClaimedAp242SemanticSectionIsActuallyEmitted()
    {
        Assert.Contains("semantics", Readme, StringComparison.OrdinalIgnoreCase);

        CliRun run = Sut.RunCli(StepSample.Ap242Pmi);
        using JsonDocument document = JsonDocument.Parse(run.Stdout);

        Assert.True(document.RootElement.TryGetProperty("semantics", out JsonElement semantics),
            "README advertises a semantics section that the JSON output does not contain");
        Assert.True(semantics.GetProperty("isAp242").GetBoolean());
    }

    [Fact]
    public void ClaimedTextSummaryLineIsActuallyEmitted()
    {
        Assert.Contains("AP242:", Readme, StringComparison.Ordinal);

        CliRun run = Sut.RunCli(StepSample.Ap242Pmi, "--format", "text", "--no-color");

        Assert.Contains("AP242:", run.Stdout);
        Assert.Contains("MBD:", run.Stdout);
    }

    [Fact]
    public void ClaimedComplexEntityRecoveryCitesTheClauseItActuallyEmits()
    {
        Assert.Contains("11.3.3", Readme, StringComparison.Ordinal);

        CliRun run = Sut.RunCli(
            StepSample.WithData("#1=LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.);"),
            "--format", "text", "--no-color");

        Assert.Contains("11.3.3", run.Stdout);
    }

    [Fact]
    public void ClaimedUnicodeEscapesAreActuallyDecoded()
    {
        Assert.Contains(@"\X2\", Readme, StringComparison.Ordinal);
        Assert.Contains(@"\X4\", Readme, StringComparison.Ordinal);

        Assert.Equal("A", Sut.Lex(@"'\X2\0041\X0\'").Tokens
            .First(token => token.Kind == Lexer.TokenKind.String).Value);
        Assert.Equal("\U0001F600", Sut.Lex(@"'\X4\0001F600\X0\'").Tokens
            .First(token => token.Kind == Lexer.TokenKind.String).Value);
    }

    [Fact]
    public void ClaimedSweepModeExistsAndProducesTheDocumentedCsvHeader()
    {
        Assert.Contains("--sweep", Readme, StringComparison.Ordinal);

        using TempFile input = new(StepSample.Minimal);
        string csv = TempFile.ReservePath(".csv");
        try
        {
            Sut.RunCliRawWithStdin(input.Path, "--sweep", csv);

            Assert.Equal("File,ExitCode,Milliseconds,Size,StdoutSample,StderrSample", File.ReadAllLines(csv)[0]);
        }
        finally
        {
            File.Delete(csv);
        }
    }

    // ── Claims that must NOT be made ────────────────────────────────────────

    /// <summary>
    /// The tokenizer implements block comments only; a <c>//</c> produces two
    /// "Unrecognized character" warnings (see <see cref="TokenizerTests.DoubleSlash_IsNotTreatedAsAComment"/>).
    /// If tail-remark support is ever added, delete this test — do not "fix" it by editing the docs.
    /// </summary>
    [Theory]
    [InlineData("README.md")]
    [InlineData("CLAUDE.md")]
    public void DocsDoNotClaimTailRemarkCommentSupport(string fileName)
    {
        string text = fileName == "README.md" ? Readme : ClaudeMd;
        string[] commentLines = text.Split('\n')
            .Where(line => line.Contains("comment", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.All(commentLines, line =>
            Assert.False(
                line.Contains("//", StringComparison.Ordinal),
                $"{fileName} claims '//' comment support, which Tokenizer.cs does not implement: {line.Trim()}"));
    }

    /// <summary>
    /// CLAUDE.md previously described the AP242 model as wired in while nothing called it. It is
    /// wired in now, so the guard is the reverse: if the call is ever removed, this fails.
    /// </summary>
    [Fact]
    public void SemanticModelIsReachableFromThePublicPipeline()
    {
        string json = Sut.RunCli(StepSample.Minimal).Stdout;

        Assert.Contains("\"semantics\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ClaudeMdDoesNotDescribeTheTestProjectAsDependencyFree()
    {
        // The test project now carries xUnit, the VSTest SDK and coverlet.
        string[] offending = ClaudeMd.Split('\n')
            .Where(line => line.Contains("zero-dependency", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("Zero NuGet", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("no external dependencies", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(offending.Length == 0,
            $"CLAUDE.md still claims a dependency-free project: {string.Join(" | ", offending.Select(l => l.Trim()))}");
    }
}
