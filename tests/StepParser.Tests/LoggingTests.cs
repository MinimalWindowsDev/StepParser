using StepParser.Diagnostics;
using StepParser.Lexer;
using StepParser.Logging;

namespace StepParser.Tests;

/// <summary>
/// The [Log] aspect is woven by Fody into the <c>StepParser</c> assembly only, so it cannot be
/// exercised by decorating a method here — these tests drive the real decorated methods instead.
/// </summary>
public sealed class LoggingTests : IDisposable
{
    public void Dispose() => StepLogger.CloseAndFlush();

    [Fact]
    public void DisabledByDefault_AndEveryMethodIsASafeNoOp()
    {
        StepLogger.Configure(false, null);

        Assert.False(StepLogger.IsEnabled);
        StepLogger.Verbose("v {A}", 1);
        StepLogger.Debug("d {A}", 1);
        StepLogger.Info("i {A}", 1);
        StepLogger.Warning("w {A}", 1);
        StepLogger.Error("e {A}", 1);
        StepLogger.Error(new InvalidOperationException("x"), "e {A}", 1);
    }

    [Fact]
    public void ConsoleOnlyConfiguration_EnablesLoggingWithoutAFile()
    {
        StepLogger.Configure(true, null);

        Assert.True(StepLogger.IsEnabled);
        StepLogger.Info("console only");
    }

    [Fact]
    public void FileConfiguration_WritesEveryLevel()
    {
        string logPath = TempFile.ReservePath(".log");
        try
        {
            StepLogger.Configure(true, logPath);
            StepLogger.Verbose("verbose-line");
            StepLogger.Debug("debug-line");
            StepLogger.Info("info-line");
            StepLogger.Warning("warning-line");
            StepLogger.Error("error-line");
            StepLogger.Error(new InvalidOperationException("boom"), "exception-line");
            StepLogger.CloseAndFlush();

            string contents = File.ReadAllText(logPath);
            foreach (string line in new[]
                     {
                         "verbose-line", "debug-line", "info-line",
                         "warning-line", "error-line", "exception-line"
                     })
            {
                Assert.Contains(line, contents);
            }

            Assert.Contains("boom", contents);
        }
        finally
        {
            StepLogger.CloseAndFlush();
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }
    }

    [Fact]
    public void CloseAndFlush_ReleasesTheFileHandle()
    {
        string logPath = TempFile.ReservePath(".log");
        try
        {
            StepLogger.Configure(true, logPath);
            StepLogger.Info("held");
            StepLogger.CloseAndFlush();

            Assert.False(StepLogger.IsEnabled);
            File.Delete(logPath);          // would throw IOException if the sink still held it
            Assert.False(File.Exists(logPath));
        }
        finally
        {
            StepLogger.CloseAndFlush();
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }
    }

    [Fact]
    public void ReconfiguringTwice_DoesNotLeakTheFirstHandle()
    {
        string first = TempFile.ReservePath(".log");
        string second = TempFile.ReservePath(".log");
        try
        {
            StepLogger.Configure(true, first);
            StepLogger.Info("one");
            StepLogger.Configure(true, second);
            StepLogger.Info("two");
            StepLogger.CloseAndFlush();

            File.Delete(first);            // the first sink must already have let go
            Assert.Contains("two", File.ReadAllText(second));
        }
        finally
        {
            StepLogger.CloseAndFlush();
            foreach (string path in new[] { first, second })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    // ── The woven aspect ────────────────────────────────────────────────────

    [Fact]
    public void Aspect_RecordsEntryAndExitForDecoratedMethods()
    {
        string logPath = TempFile.ReservePath(".log");
        using TempFile input = new(StepSample.Minimal);
        try
        {
            // The CLI configures the logger from its own flags, so it must be switched on via the
            // command line — a logger configured out here would be turned straight back off.
            Sut.RunCliOnPath(input.Path, "--log-file", logPath);
            StepLogger.CloseAndFlush();

            string contents = File.ReadAllText(logPath);
            Assert.Contains("→ Tokenizer.TokenizeFile", contents);
            Assert.Contains("← Tokenizer.TokenizeFile", contents);
            Assert.Contains("→ StepFileParser.Parse", contents);
            // ParseHeaderSection takes no arguments — the empty-parameter path in FormatArgs.
            Assert.Contains("→ StepFileParser.ParseHeaderSection()", contents);
        }
        finally
        {
            StepLogger.CloseAndFlush();
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }
    }

    [Fact]
    public void Aspect_SummarisesCollectionArgumentsByCount()
    {
        string logPath = TempFile.ReservePath(".log");
        try
        {
            StepLogger.Configure(true, logPath);
            List<ParseDiagnostic> diagnostics = new();
            IReadOnlyList<RawToken> raw = Tokenizer.Tokenize(new StringReader("HEADER"), diagnostics);
            Lexer.Lexer.Lex(raw, diagnostics);
            StepLogger.CloseAndFlush();

            Assert.Contains("items]", File.ReadAllText(logPath));
        }
        finally
        {
            StepLogger.CloseAndFlush();
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }
    }

    [Fact]
    public void Aspect_TruncatesLongStringArguments()
    {
        string logPath = TempFile.ReservePath(".log");
        // A path longer than the aspect's 120-character render limit.
        string deepDirectory = Path.Combine(Path.GetTempPath(), new string('d', 60), new string('e', 60));
        Directory.CreateDirectory(deepDirectory);
        string deepFile = Path.Combine(deepDirectory, "sample.stp");
        File.WriteAllText(deepFile, StepSample.Minimal);
        try
        {
            StepLogger.Configure(true, logPath);
            Tokenizer.TokenizeFile(deepFile, new List<ParseDiagnostic>());
            StepLogger.CloseAndFlush();

            Assert.Contains("…", File.ReadAllText(logPath));
        }
        finally
        {
            StepLogger.CloseAndFlush();
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }

            Directory.Delete(Path.Combine(Path.GetTempPath(), new string('d', 60)), recursive: true);
        }
    }

    [Fact]
    public void Aspect_RendersNullArgumentsAndRecordsThrownExceptions()
    {
        string logPath = TempFile.ReservePath(".log");
        try
        {
            StepLogger.Configure(true, logPath);

            Assert.ThrowsAny<Exception>(() => Tokenizer.TokenizeFile(null!, new List<ParseDiagnostic>()));
            StepLogger.CloseAndFlush();

            string contents = File.ReadAllText(logPath);
            Assert.Contains("path: null", contents);
            Assert.Contains("✗ Tokenizer.TokenizeFile", contents);
        }
        finally
        {
            StepLogger.CloseAndFlush();
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }
    }

    [Fact]
    public void Aspect_IsInertWhenLoggingIsDisabled()
    {
        StepLogger.Configure(false, null);

        // Exercises the early-return arms of OnEntry/OnExit/OnException with logging off.
        Assert.Equal(0, Sut.RunCli(StepSample.Minimal).ExitCode);
        Assert.ThrowsAny<Exception>(() => Tokenizer.TokenizeFile(null!, new List<ParseDiagnostic>()));
    }
}
