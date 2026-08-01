using StepParser.Cli;
using StepParser.Diagnostics;
using StepParser.Lexer;
using StepParser.Parser;

namespace StepParser.Tests;

/// <summary>Result of driving the CLI end-to-end over a temporary input file.</summary>
internal sealed record CliRun(int ExitCode, string Stdout, string Stderr);

/// <summary>
/// A temp file that deletes itself. Every test that touches the filesystem uses this so a failing
/// assertion cannot leak files into %TEMP% and so tests never collide on a shared path.
/// </summary>
internal sealed class TempFile : IDisposable
{
    public string Path { get; }

    public TempFile(string content, string extension = ".stp")
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"stepparser_test_{Guid.NewGuid():N}{extension}");
        File.WriteAllText(Path, content);
    }

    /// <summary>A path inside the temp directory that does not yet exist (for --output targets).</summary>
    public static string ReservePath(string extension) =>
        System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"stepparser_test_{Guid.NewGuid():N}{extension}");

    public void Dispose()
    {
        try
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
        catch (IOException)
        {
            // A locked file must not turn a passing test red; the OS reclaims %TEMP%.
        }
    }
}

internal static class Sut
{
    /// <summary>Tokenize + lex a Part-21 string, returning both the tokens and the diagnostics.</summary>
    public static (IReadOnlyList<Token> Tokens, List<ParseDiagnostic> Diagnostics) Lex(string source)
    {
        List<ParseDiagnostic> diagnostics = new();
        IReadOnlyList<RawToken> raw = Tokenizer.Tokenize(new StringReader(source), diagnostics);
        IReadOnlyList<Token> tokens = Lexer.Lexer.Lex(raw, diagnostics);
        return (tokens, diagnostics);
    }

    public static (IReadOnlyList<RawToken> Tokens, List<ParseDiagnostic> Diagnostics) Tokenize(string source)
    {
        List<ParseDiagnostic> diagnostics = new();
        IReadOnlyList<RawToken> raw = Tokenizer.Tokenize(new StringReader(source), diagnostics);
        return (raw, diagnostics);
    }

    /// <summary>Full tokenize → lex → parse over a string, using a real temp file as the source path.</summary>
    public static (StepFile File, List<ParseDiagnostic> Diagnostics) Parse(string source)
    {
        (IReadOnlyList<Token> tokens, List<ParseDiagnostic> diagnostics) = Lex(source);
        StepFileParser parser = new(tokens, diagnostics);
        using TempFile file = new(source);
        return (parser.Parse(file.Path), diagnostics);
    }

    public static ParseResult ParseToResult(string source)
    {
        (StepFile file, List<ParseDiagnostic> diagnostics) = Parse(source);
        return ParseResult.FromStepFile(file, diagnostics, TimeSpan.Zero);
    }

    /// <summary>Run the CLI against a temp file holding <paramref name="source"/>.</summary>
    public static CliRun RunCli(string source, params string[] extraArgs)
    {
        using TempFile file = new(source);
        return RunCliOnPath(file.Path, extraArgs);
    }

    public static CliRun RunCliOnPath(string path, params string[] extraArgs)
    {
        string[] args = [path, .. extraArgs];
        StringWriter stdout = new();
        StringWriter stderr = new();
        int exitCode = StepParserCli.Run(args, stdout, stderr);
        return new CliRun(exitCode, stdout.ToString(), stderr.ToString());
    }

    /// <summary>Run the CLI with a raw argument vector — for argument-error and --help paths.</summary>
    public static CliRun RunCliRaw(params string[] args)
    {
        StringWriter stdout = new();
        StringWriter stderr = new();
        int exitCode = StepParserCli.Run(args, stdout, stderr);
        return new CliRun(exitCode, stdout.ToString(), stderr.ToString());
    }

    public static CliRun RunCliRawWithStdin(string stdin, params string[] args)
    {
        StringWriter stdout = new();
        StringWriter stderr = new();
        int exitCode = StepParserCli.Run(args, new StringReader(stdin), stdout, stderr);
        return new CliRun(exitCode, stdout.ToString(), stderr.ToString());
    }

    /// <summary>
    /// Locate the repository root by walking up from the test binary until StepParser.slnx appears.
    /// Used by the documentation-contract tests, which must read the real README/CLAUDE.md rather
    /// than a copy — a copy could drift and let the docs lie again.
    /// </summary>
    public static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "StepParser.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate StepParser.slnx above {AppContext.BaseDirectory}.");
    }
}
