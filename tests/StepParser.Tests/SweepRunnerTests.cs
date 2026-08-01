using StepParser.Cli;

namespace StepParser.Tests;

public sealed class SweepRunnerTests
{
    [Fact]
    public void ReadFileList_KeepsOnlyExistingPaths()
    {
        using TempFile real = new(StepSample.Minimal);
        string absent = Path.Combine(Path.GetTempPath(), $"absent_{Guid.NewGuid():N}.stp");

        IReadOnlyList<string> files = SweepRunner.ReadFileList(new StringReader($"{real.Path}\n{absent}"));

        Assert.Equal([real.Path], files);
    }

    [Fact]
    public void ReadFileList_SkipsBlankLines()
    {
        using TempFile real = new(StepSample.Minimal);

        IReadOnlyList<string> files = SweepRunner.ReadFileList(new StringReader($"\n   \n{real.Path}\n\n"));

        Assert.Single(files);
    }

    [Fact]
    public void ReadFileList_DeduplicatesCaseInsensitivelyAndSorts()
    {
        using TempFile a = new(StepSample.Minimal);
        using TempFile b = new(StepSample.Minimal);
        string[] ordered = new[] { a.Path, b.Path }.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();

        IReadOnlyList<string> files = SweepRunner.ReadFileList(
            new StringReader($"{ordered[1]}\n{ordered[0]}\n{ordered[0].ToUpperInvariant()}"));

        Assert.Equal(ordered, files);
    }

    [Fact]
    public void ReadFileList_EmptyInput_YieldsNothing()
    {
        Assert.Empty(SweepRunner.ReadFileList(new StringReader(string.Empty)));
    }

    [Theory]
    [InlineData("plain", "\"plain\"")]
    [InlineData("has,comma", "\"has,comma\"")]
    [InlineData("has\"quote", "\"has\"\"quote\"")]
    [InlineData(null, "\"\"")]
    public void Csv_QuotesAndDoublesEmbeddedQuotes(string? input, string expected)
    {
        Assert.Equal(expected, SweepRunner.Csv(input));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("only line", "only line")]
    [InlineData("first\nsecond", "first")]
    public void FirstLine_ReturnsLeadingLineOrEmpty(string input, string expected)
    {
        Assert.Equal(expected, SweepRunner.FirstLine(input));
    }

    [Fact]
    public void Run_WritesCsvHeaderAndOneRowPerFile()
    {
        using TempFile clean = new(StepSample.Minimal);
        using TempFile warning = new(StepSample.WithData("#1=LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.);"));
        string csv = TempFile.ReservePath(".csv");
        try
        {
            StringWriter output = new();
            int exitCode = SweepRunner.Run(
                new StringReader($"{clean.Path}\n{warning.Path}"), output, csv);

            Assert.Equal(0, exitCode);
            string[] lines = File.ReadAllLines(csv);
            Assert.Equal("File,ExitCode,Milliseconds,Size,StdoutSample,StderrSample", lines[0]);
            Assert.Equal(3, lines.Length);
        }
        finally
        {
            File.Delete(csv);
        }
    }

    [Fact]
    public void Run_SummarisesTotalsAndExitCodeHistogram()
    {
        using TempFile clean = new(StepSample.Minimal);
        using TempFile warning = new(StepSample.WithData("#1=LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.);"));
        string csv = TempFile.ReservePath(".csv");
        try
        {
            StringWriter output = new();
            SweepRunner.Run(new StringReader($"{clean.Path}\n{warning.Path}"), output, csv);
            string summary = output.ToString();

            Assert.Contains("TOTAL=2", summary);
            Assert.Contains("EXIT_0=1", summary);
            Assert.Contains("EXIT_3=1", summary);
            Assert.Contains("TOP_SLOW=", summary);
            Assert.Contains("TOP_FAIL=", summary);
        }
        finally
        {
            File.Delete(csv);
        }
    }

    [Fact]
    public void Run_ListsNonZeroExitsUnderTopFail()
    {
        using TempFile warning = new(StepSample.WithData("#1=LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.);"));
        string csv = TempFile.ReservePath(".csv");
        try
        {
            StringWriter output = new();
            SweepRunner.Run(new StringReader(warning.Path), output, csv);

            string failSection = output.ToString().Split("TOP_FAIL=")[1];
            Assert.Contains(warning.Path, failSection);
        }
        finally
        {
            File.Delete(csv);
        }
    }

    [Fact]
    public void Run_EmptyCorpus_StillWritesAWellFormedCsv()
    {
        string csv = TempFile.ReservePath(".csv");
        try
        {
            StringWriter output = new();
            SweepRunner.Run(new StringReader(string.Empty), output, csv);

            Assert.Single(File.ReadAllLines(csv));
            Assert.Contains("TOTAL=0", output.ToString());
        }
        finally
        {
            File.Delete(csv);
        }
    }

    // ── CLI integration ─────────────────────────────────────────────────────

    [Fact]
    public void Cli_SweepFlag_ReadsPathsFromStdin()
    {
        using TempFile input = new(StepSample.Minimal);
        string csv = TempFile.ReservePath(".csv");
        try
        {
            CliRun run = Sut.RunCliRawWithStdin(input.Path, "--sweep", csv);

            Assert.Equal(0, run.ExitCode);
            Assert.Contains("TOTAL=1", run.Stdout);
            Assert.True(File.Exists(csv));
        }
        finally
        {
            File.Delete(csv);
        }
    }

    [Fact]
    public void Cli_SweepFlagIsCaseInsensitive()
    {
        string csv = TempFile.ReservePath(".csv");
        try
        {
            Assert.Equal(0, Sut.RunCliRawWithStdin(string.Empty, "--SWEEP", csv).ExitCode);
        }
        finally
        {
            File.Delete(csv);
        }
    }

    [Fact]
    public void Cli_SweepWithNoCsvArgument_ExitsFour()
    {
        CliRun run = Sut.RunCliRawWithStdin(string.Empty, "--sweep");

        Assert.Equal(4, run.ExitCode);
        Assert.Contains("Missing CSV path for --sweep", run.Stderr);
    }

    [Fact]
    public void Cli_SweepWithBlankCsvArgument_ExitsFour()
    {
        CliRun run = Sut.RunCliRawWithStdin(string.Empty, "--sweep", "   ");

        Assert.Equal(4, run.ExitCode);
        Assert.Contains("Missing CSV path for --sweep", run.Stderr);
    }

    [Fact]
    public void Cli_WithoutSweep_DoesNotConsumeStdin()
    {
        // Guards against a regression where the sweep short-circuit swallows a normal invocation.
        CliRun run = Sut.RunCliRawWithStdin("ignored", Path.Combine(Path.GetTempPath(), $"absent_{Guid.NewGuid():N}.stp"));

        Assert.Equal(1, run.ExitCode);
    }
}
