using StepParser.Logging;

namespace StepParser.Tests;

public sealed class CliTests : IDisposable
{
    // Several tests switch logging on. It is process-wide static state, so it is reset after each
    // test rather than leaking into whatever runs next.
    public void Dispose() => StepLogger.Configure(false, null);

    private const string WarningFile = "#1=LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.);";
    private const string Truncated =
        "ISO-10303-21; HEADER; FILE_DESCRIPTION(('x'),'2;1'); FILE_NAME('a','b',('c'),('d'),'e','f','g'); FILE_SCHEMA(('T')); ENDSEC; DATA; #1=FOO(";

    // ── Exit codes ──────────────────────────────────────────────────────────

    [Fact]
    public void CleanFile_ExitsZero()
    {
        Assert.Equal(0, Sut.RunCli(StepSample.Minimal).ExitCode);
    }

    [Fact]
    public void ParseErrors_ExitTwo()
    {
        CliRun run = Sut.RunCli(Truncated);

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("diagnostics", run.Stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WarningsOnly_ExitThree()
    {
        Assert.Equal(3, Sut.RunCli(StepSample.WithData(WarningFile)).ExitCode);
    }

    [Fact]
    public void MissingInputFile_ExitsOne()
    {
        CliRun run = Sut.RunCliRaw(Path.Combine(Path.GetTempPath(), $"absent_{Guid.NewGuid():N}.stp"));

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Input file not found", run.Stderr);
    }

    [Fact]
    public void UnwritableOutputPath_IsCaughtAndExitsTwo()
    {
        // Pointing --output at a directory makes File.WriteAllText throw; the CLI must convert that
        // into exit 2 rather than letting the exception escape to the user as a stack trace.
        CliRun run = Sut.RunCli(StepSample.Minimal, "--output", Path.GetTempPath());

        Assert.Equal(2, run.ExitCode);
    }

    // ── Argument errors (exit 4) ────────────────────────────────────────────

    [Fact]
    public void NoArguments_PrintsUsageToStderrAndExitsFour()
    {
        CliRun run = Sut.RunCliRaw();

        Assert.Equal(4, run.ExitCode);
        Assert.Contains("Usage: StepParser", run.Stderr);
    }

    [Fact]
    public void UnknownOption_ExitsFour()
    {
        CliRun run = Sut.RunCliRaw("--nope", "f.stp");

        Assert.Equal(4, run.ExitCode);
        Assert.Contains("Unknown option: --nope", run.Stderr);
    }

    [Fact]
    public void TwoInputFiles_ExitsFour()
    {
        CliRun run = Sut.RunCliRaw("a.stp", "b.stp");

        Assert.Equal(4, run.ExitCode);
        Assert.Contains("Only one input file", run.Stderr);
    }

    [Fact]
    public void OnlyFlagsAndNoInput_ExitsFour()
    {
        CliRun run = Sut.RunCliRaw("--strict");

        Assert.Equal(4, run.ExitCode);
        Assert.Contains("An input file is required", run.Stderr);
    }

    [Theory]
    [InlineData("--output", "Missing value for --output")]
    [InlineData("--log-file", "Missing value for --log-file")]
    public void OptionWithoutItsValue_ExitsFour(string option, string expectedMessage)
    {
        CliRun run = Sut.RunCliRaw("f.stp", option);

        Assert.Equal(4, run.ExitCode);
        Assert.Contains(expectedMessage, run.Stderr);
    }

    [Theory]
    [InlineData("--format", "xml", "Invalid value for --format")]
    [InlineData("--phase", "semantic", "Invalid value for --phase")]
    public void InvalidEnumValue_ExitsFour(string option, string value, string expectedMessage)
    {
        CliRun run = Sut.RunCliRaw("f.stp", option, value);

        Assert.Equal(4, run.ExitCode);
        Assert.Contains(expectedMessage, run.Stderr);
    }

    [Theory]
    [InlineData("--format")]
    [InlineData("--phase")]
    public void EnumOptionAtEndOfArgv_ExitsFour(string option)
    {
        Assert.Equal(4, Sut.RunCliRaw("f.stp", option).ExitCode);
    }

    // ── Help and version ────────────────────────────────────────────────────

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void Help_PrintsUsageToStdoutAndExitsZero(string flag)
    {
        CliRun run = Sut.RunCliRaw(flag);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Usage: StepParser", run.Stdout);
    }

    [Fact]
    public void Version_PrintsVersionAndExitsZero()
    {
        CliRun run = Sut.RunCliRaw("--version");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("StepParser", run.Stdout);
    }

    // ── Phases ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("tokens")]
    [InlineData("lex")]
    [InlineData("parse")]
    public void EachPhaseProducesJsonOutput(string phase)
    {
        CliRun run = Sut.RunCli(StepSample.Minimal, "--phase", phase);

        Assert.Equal(0, run.ExitCode);
        Assert.StartsWith("{", run.Stdout.TrimStart());
    }

    [Theory]
    [InlineData("tokens")]
    [InlineData("lex")]
    public void EarlyPhasesEmitTokenListsInTextMode(string phase)
    {
        CliRun run = Sut.RunCli(StepSample.Minimal, "--phase", phase, "--format", "text", "--no-color");

        Assert.Contains("ISO-10303-21", run.Stdout);
    }

    [Fact]
    public void PhaseNameIsCaseInsensitive()
    {
        Assert.Equal(0, Sut.RunCli(StepSample.Minimal, "--phase", "TOKENS").ExitCode);
    }

    // ── Output routing ──────────────────────────────────────────────────────

    [Fact]
    public void OutputFlag_WritesFileAndLeavesStdoutEmpty()
    {
        string outputPath = TempFile.ReservePath(".json");
        try
        {
            CliRun run = Sut.RunCli(StepSample.Minimal, "--output", outputPath);

            Assert.Equal(0, run.ExitCode);
            Assert.True(File.Exists(outputPath));
            Assert.Contains("\"schema\"", File.ReadAllText(outputPath));
            Assert.True(string.IsNullOrWhiteSpace(run.Stdout));
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Theory]
    [InlineData("json", ".json")]
    [InlineData("text", ".txt")]
    public void OutputAuto_DerivesExtensionFromFormat(string format, string expectedExtension)
    {
        using TempFile input = new(StepSample.Minimal);
        string expected = Path.ChangeExtension(input.Path, expectedExtension);
        try
        {
            CliRun run = Sut.RunCliOnPath(input.Path, "--output", "auto", "--format", format);

            Assert.Equal(0, run.ExitCode);
            Assert.True(File.Exists(expected), $"expected {expected}");
        }
        finally
        {
            if (File.Exists(expected))
            {
                File.Delete(expected);
            }
        }
    }

    [Fact]
    public void OutputAuto_IsCaseInsensitive()
    {
        using TempFile input = new(StepSample.Minimal);
        string expected = Path.ChangeExtension(input.Path, ".json");
        try
        {
            Assert.Equal(0, Sut.RunCliOnPath(input.Path, "--output", "AUTO").ExitCode);
            Assert.True(File.Exists(expected));
        }
        finally
        {
            if (File.Exists(expected))
            {
                File.Delete(expected);
            }
        }
    }

    // ── Verbose ─────────────────────────────────────────────────────────────

    [Fact]
    public void Verbose_ReportsEachCompletedStageOnStderr()
    {
        CliRun run = Sut.RunCli(StepSample.Minimal, "--verbose");

        Assert.Contains("Tokenized", run.Stderr);
        Assert.Contains("Lexed", run.Stderr);
        Assert.Contains("Parsed", run.Stderr);
    }

    [Theory]
    [InlineData("tokens", "Tokenized", "Lexed")]
    [InlineData("lex", "Lexed", "Parsed")]
    public void Verbose_StopsReportingAtTheSelectedPhase(string phase, string expected, string notExpected)
    {
        CliRun run = Sut.RunCli(StepSample.Minimal, "--phase", phase, "--verbose");

        Assert.Contains(expected, run.Stderr);
        Assert.DoesNotContain(notExpected, run.Stderr);
    }

    [Fact]
    public void Verbose_AnnouncesTheOutputFileItWrote()
    {
        string outputPath = TempFile.ReservePath(".json");
        try
        {
            CliRun run = Sut.RunCli(StepSample.Minimal, "--output", outputPath, "--verbose");

            Assert.Contains("Wrote", run.Stderr);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    // ── Strict ──────────────────────────────────────────────────────────────

    [Fact]
    public void Strict_PromotesWarningsAndSaysSoOnStderr()
    {
        CliRun run = Sut.RunCli(StepSample.WithData(WarningFile), "--strict", "--no-color");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("strict", run.Stderr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("promoted", run.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WithoutStrict_TheSameFileExitsThreeAndSaysNothingAboutPromotion()
    {
        CliRun run = Sut.RunCli(StepSample.WithData(WarningFile), "--no-color");

        Assert.Equal(3, run.ExitCode);
        Assert.DoesNotContain("promoted", run.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Strict_OnACleanFile_StillExitsZero()
    {
        Assert.Equal(0, Sut.RunCli(StepSample.Minimal, "--strict").ExitCode);
    }

    // ── Logging ─────────────────────────────────────────────────────────────

    [Fact]
    public void LogFile_IsWrittenBesideTheInputByDefault()
    {
        using TempFile input = new(StepSample.Minimal);
        string expectedLog = Path.ChangeExtension(input.Path, ".log");
        try
        {
            Sut.RunCliOnPath(input.Path, "--log");
            StepLogger.CloseAndFlush();   // release the sink's handle before reading it back

            Assert.True(File.Exists(expectedLog), $"expected log at {expectedLog}");
            Assert.Contains("StepParser starting", File.ReadAllText(expectedLog));
        }
        finally
        {
            StepLogger.Configure(false, null);
            if (File.Exists(expectedLog))
            {
                File.Delete(expectedLog);
            }
        }
    }

    [Fact]
    public void LogFileFlag_ImpliesLogAndHonoursTheGivenPath()
    {
        string logPath = TempFile.ReservePath(".log");
        try
        {
            Sut.RunCli(StepSample.Minimal, "--log-file", logPath);

            Assert.True(File.Exists(logPath));
        }
        finally
        {
            StepLogger.Configure(false, null);
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }
    }

    [Fact]
    public void LoggingOffByDefault_WritesNoLogBesideTheInput()
    {
        using TempFile input = new(StepSample.Minimal);
        Sut.RunCliOnPath(input.Path);

        Assert.False(File.Exists(Path.ChangeExtension(input.Path, ".log")));
    }

    [Fact]
    public void MissingInputWithLoggingOn_StillReportsNotFound()
    {
        // Logging is configured before the existence check, so this exercises the "not found"
        // branch with the [Log] aspects live.
        string absent = Path.Combine(Path.GetTempPath(), $"absent_{Guid.NewGuid():N}.stp");
        string logPath = TempFile.ReservePath(".log");
        try
        {
            CliRun run = Sut.RunCliRaw(absent, "--log-file", logPath);

            Assert.Equal(1, run.ExitCode);
        }
        finally
        {
            StepLogger.Configure(false, null);
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }
    }
}
