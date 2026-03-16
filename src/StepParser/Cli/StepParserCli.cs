using System.Diagnostics;
using StepParser.Diagnostics;
using StepParser.Lexer;
using StepParser.Logging;
using StepParser.Output;
using StepParser.Parser;

namespace StepParser.Cli;

public static class StepParserCli
{
    [Log]
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        try
        {
            if (!TryParseArguments(args, stdout, stderr, out CliOptions? options, out int argumentExitCode))
            {
                return argumentExitCode;
            }

            Debug.Assert(options is not null);
            string inputPath = Path.GetFullPath(options.InputPath);

            // Configure logging before any work so [Log] aspects are live from here on.
            string? logFilePath = options.Log
                ? options.LogFile ?? Path.ChangeExtension(inputPath, ".log")
                : null;
            StepLogger.Configure(options.Log, logFilePath);

            if (StepLogger.IsEnabled)
            {
                StepLogger.Info("StepParser starting");
                StepLogger.Info("Input: {InputPath} ({Size})",
                    inputPath,
                    File.Exists(inputPath) ? $"{new FileInfo(inputPath).Length / 1024.0:F1} KB" : "not found");
                StepLogger.Info("Phase: {Phase} | Format: {Format} | Strict: {Strict}",
                    options.Phase, options.Format, options.Strict);
                if (logFilePath is not null)
                    StepLogger.Info("Log file: {LogFile}", logFilePath);
            }

            if (!File.Exists(inputPath))
            {
                stderr.WriteLine($"Input file not found: {inputPath}");
                return 1;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            List<ParseDiagnostic> diagnostics = new();

            IReadOnlyList<RawToken> rawTokens = Tokenizer.TokenizeFile(inputPath, diagnostics);
            if (options.Verbose)
            {
                stderr.WriteLine($"Tokenized {rawTokens.Count} raw tokens.");
            }

            if (options.Phase == ExecutionPhase.Tokens)
            {
                return WriteResult(
                    options,
                    stdout,
                    stderr,
                    new TokenizationResult(inputPath, rawTokens, diagnostics.ToArray(), stopwatch.Elapsed),
                    diagnostics);
            }

            IReadOnlyList<Token> tokens = StepParser.Lexer.Lexer.Lex(rawTokens, diagnostics);
            if (options.Verbose)
            {
                stderr.WriteLine($"Lexed {tokens.Count} typed tokens.");
            }

            if (options.Phase == ExecutionPhase.Lex)
            {
                return WriteResult(
                    options,
                    stdout,
                    stderr,
                    new LexingResult(inputPath, tokens, diagnostics.ToArray(), stopwatch.Elapsed),
                    diagnostics);
            }

            StepFileParser parser = new(tokens, diagnostics);
            StepFile stepFile = parser.Parse(inputPath);
            ParseResult result = ParseResult.FromStepFile(stepFile, diagnostics.ToArray(), stopwatch.Elapsed);

            if (options.Verbose)
            {
                stderr.WriteLine(
                    $"Parsed {result.Stats.EntityCount} entities in {result.Elapsed.TotalMilliseconds:F0} ms.");
            }

            return WriteResult(options, stdout, stderr, result, diagnostics);
        }
        catch (Exception exception)
        {
            stderr.WriteLine(exception.ToString());
            return 2;
        }
    }

    private static int WriteResult(
        CliOptions options,
        TextWriter stdout,
        TextWriter stderr,
        object payload,
        IReadOnlyCollection<ParseDiagnostic> diagnostics)
    {
        IOutputFormatter formatter = options.Format == OutputFormat.Json
            ? new JsonFormatter()
            : new TextFormatter(options.NoColor);

        string rendered = formatter.Format(payload);
        if (!string.IsNullOrWhiteSpace(options.OutputPath))
        {
            string outputPath = ResolveOutputPath(options);
            File.WriteAllText(outputPath, rendered);
            if (options.Verbose)
            {
                stderr.WriteLine($"Wrote {outputPath}");
            }
        }
        else
        {
            stdout.Write(rendered);
            if (!rendered.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            {
                stdout.WriteLine();
            }
        }

        bool hasErrors = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
        bool hasWarnings = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning);

        if (options.Strict && hasWarnings)
        {
            return 2;
        }

        if (hasErrors)
        {
            return 2;
        }

        if (hasWarnings)
        {
            return 3;
        }

        return 0;
    }

    private static string ResolveOutputPath(CliOptions options)
    {
        if (!string.Equals(options.OutputPath, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(options.OutputPath!);
        }

        string inputPath = Path.GetFullPath(options.InputPath);
        string extension = options.Format == OutputFormat.Json ? ".json" : ".txt";
        return Path.ChangeExtension(inputPath, extension);
    }

    private static bool TryParseArguments(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        out CliOptions? options,
        out int exitCode)
    {
        options = null;
        exitCode = 0;

        if (args.Length == 0)
        {
            PrintUsage(stderr);
            exitCode = 4;
            return false;
        }

        string? inputPath = null;
        string? outputPath = null;
        OutputFormat format = OutputFormat.Json;
        ExecutionPhase phase = ExecutionPhase.Parse;
        bool strict = false;
        bool noColor = false;
        bool verbose = false;
        bool log = false;
        string? logFile = null;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            switch (argument)
            {
                case "-h":
                case "--help":
                    PrintUsage(stdout);
                    return false;
                case "--version":
                    stdout.WriteLine("StepParser 0.1.0");
                    return false;
                case "-o":
                case "--output":
                    if (!TryReadValue(args, ref index, out outputPath))
                    {
                        stderr.WriteLine("Missing value for --output.");
                        exitCode = 4;
                        return false;
                    }

                    break;
                case "-f":
                case "--format":
                    if (!TryReadValue(args, ref index, out string? formatValue) ||
                        !Enum.TryParse(formatValue, true, out format))
                    {
                        stderr.WriteLine("Invalid value for --format. Expected json or text.");
                        exitCode = 4;
                        return false;
                    }

                    break;
                case "--phase":
                    if (!TryReadValue(args, ref index, out string? phaseValue) ||
                        !Enum.TryParse(phaseValue, true, out phase))
                    {
                        stderr.WriteLine("Invalid value for --phase. Expected tokens, lex, or parse.");
                        exitCode = 4;
                        return false;
                    }

                    break;
                case "--strict":
                    strict = true;
                    break;
                case "--no-color":
                    noColor = true;
                    break;
                case "-v":
                case "--verbose":
                    verbose = true;
                    break;
                case "--log":
                    log = true;
                    break;
                case "--log-file":
                    if (!TryReadValue(args, ref index, out logFile))
                    {
                        stderr.WriteLine("Missing value for --log-file.");
                        exitCode = 4;
                        return false;
                    }

                    log = true; // --log-file implies --log
                    break;
                default:
                    if (argument.StartsWith("-", StringComparison.Ordinal))
                    {
                        stderr.WriteLine($"Unknown option: {argument}");
                        exitCode = 4;
                        return false;
                    }

                    if (inputPath is not null)
                    {
                        stderr.WriteLine("Only one input file may be specified.");
                        exitCode = 4;
                        return false;
                    }

                    inputPath = argument;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            stderr.WriteLine("An input file is required.");
            PrintUsage(stderr);
            exitCode = 4;
            return false;
        }

        options = new CliOptions
        {
            InputPath = inputPath,
            OutputPath = outputPath,
            Format = format,
            Phase = phase,
            Strict = strict,
            NoColor = noColor,
            Verbose = verbose,
            Log = log,
            LogFile = logFile
        };

        return true;
    }

    private static bool TryReadValue(string[] args, ref int index, out string? value)
    {
        int nextIndex = index + 1;
        if (nextIndex >= args.Length)
        {
            value = null;
            return false;
        }

        value = args[nextIndex];
        index = nextIndex;
        return true;
    }

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("Usage: StepParser [OPTIONS] <input-file>");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  -o, --output <file|auto>   Write output to a file. Use 'auto' for input-name default.");
        writer.WriteLine("  -f, --format <fmt>         Output format: json (default), text");
        writer.WriteLine("  --phase <phase>            Stop after phase: tokens, lex, parse (default: parse)");
        writer.WriteLine("  --strict                   Treat warnings as errors");
        writer.WriteLine("  --no-color                 Disable ANSI color in text output");
        writer.WriteLine("  -v, --verbose              Print progress to stderr");
        writer.WriteLine("  --log                      Enable AOP verbose debug logging (stdout + file)");
        writer.WriteLine("  --log-file <path>          Log file path (default: <input>.log beside the input file)");
        writer.WriteLine("  -h, --help                 Show help");
        writer.WriteLine("  --version                  Show version");
    }
}
