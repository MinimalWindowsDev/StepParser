using StepParser.Cli;
using StepParser.Diagnostics;
using StepParser.Lexer;
using StepParser.Output;
using StepParser.Parser;

namespace StepParser.Tests;

internal static class Program
{
    public static int Main(string[]? args)
    {
        args ??= [];
        if (args.Length > 0 && string.Equals(args[0], "--sweep", StringComparison.OrdinalIgnoreCase))
        {
            return RunSweep(args.Skip(1).ToArray());
        }

        List<(string Name, Action Test)> tests =
        [
            (nameof(TokenizeMinimalSample_ProducesExpectedLeadingSequence), TokenizeMinimalSample_ProducesExpectedLeadingSequence),
            (nameof(Lexer_DecodesEscapedStringsAndEnums), Lexer_DecodesEscapedStringsAndEnums),
            (nameof(Parser_ParsesMinimalSample), Parser_ParsesMinimalSample),
            (nameof(Cli_ReturnsParseErrorForTruncatedInput), Cli_ReturnsParseErrorForTruncatedInput),
            // Regression: issue #1 — complex entity instance recovery (malformed, missing outer parens)
            (nameof(Parser_RecoversMalformedComplexEntityInstance), Parser_RecoversMalformedComplexEntityInstance),
            // Regression: issue #2 — --output writes file
            (nameof(Cli_OutputFlag_WritesFileAndStdoutIsEmpty), Cli_OutputFlag_WritesFileAndStdoutIsEmpty),
            // Regression: issue #3 — text format shows error/warning split and entity-type breakdown
            (nameof(TextFormatter_ShowsDiagnosticSplit_WhenErrorsAndWarnings), TextFormatter_ShowsDiagnosticSplit_WhenErrorsAndWarnings),
            (nameof(TextFormatter_ShowsEntityTypeBreakdown), TextFormatter_ShowsEntityTypeBreakdown),
            (nameof(TextFormatter_ShowsAllDiagnosticsWithoutCap), TextFormatter_ShowsAllDiagnosticsWithoutCap),
            // Regression: issue #5 — --strict is observable via stderr annotation
            (nameof(Cli_StrictMode_PrintsAnnotationAndExits2OnWarnings), Cli_StrictMode_PrintsAnnotationAndExits2OnWarnings),
        ];

        int failures = 0;
        foreach ((string name, Action test) in tests)
        {
            try
            {
                test();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.WriteLine($"FAIL {name}");
                Console.WriteLine(exception.Message);
            }
        }

        Console.WriteLine($"Executed {tests.Count} tests, failures: {failures}");
        return failures == 0 ? 0 : 1;
    }

    private static int RunSweep(string[] args)
    {
        string outputCsv = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("step_sweep_results.csv");
        List<string> files = new();
        string? line;
        while ((line = Console.In.ReadLine()) is not null)
        {
            if (!string.IsNullOrWhiteSpace(line) && File.Exists(line))
            {
                files.Add(line);
            }
        }

        files = files.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        List<SweepResult> results = new(files.Count);
        foreach (string file in files)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            StringWriter stdout = new();
            StringWriter stderr = new();
            int exitCode = StepParserCli.Run([file], stdout, stderr);
            stopwatch.Stop();
            results.Add(new SweepResult(
                file,
                exitCode,
                stopwatch.ElapsedMilliseconds,
                new FileInfo(file).Length,
                FirstLine(stdout.ToString()),
                FirstLine(stderr.ToString())));
        }

        WriteCsv(outputCsv, results);
        Console.WriteLine($"TOTAL={results.Count}");
        foreach (var group in results.GroupBy(result => result.ExitCode).OrderBy(group => group.Key))
        {
            Console.WriteLine($"EXIT_{group.Key}={group.Count()}");
        }

        Console.WriteLine("TOP_SLOW=");
        foreach (SweepResult result in results.OrderByDescending(result => result.Milliseconds).Take(10))
        {
            Console.WriteLine($"{result.ExitCode}\t{result.Milliseconds}ms\t{result.Size}\t{result.File}");
        }

        Console.WriteLine("TOP_FAIL=");
        foreach (SweepResult result in results.Where(result => result.ExitCode != 0).Take(15))
        {
            Console.WriteLine($"{result.ExitCode}\t{result.Milliseconds}ms\t{result.File}\t{result.StdoutSample}\t{result.StderrSample}");
        }

        return 0;
    }

    private static void WriteCsv(string path, IReadOnlyList<SweepResult> results)
    {
        using StreamWriter writer = new(path, false);
        writer.WriteLine("File,ExitCode,Milliseconds,Size,StdoutSample,StderrSample");
        foreach (SweepResult result in results)
        {
            writer.WriteLine(string.Join(",",
                Csv(result.File),
                result.ExitCode,
                result.Milliseconds,
                result.Size,
                Csv(result.StdoutSample),
                Csv(result.StderrSample)));
        }
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string FirstLine(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        using StringReader reader = new(value);
        return reader.ReadLine() ?? string.Empty;
    }

    private static void TokenizeMinimalSample_ProducesExpectedLeadingSequence()
    {
        List<ParseDiagnostic> diagnostics = new();
        IReadOnlyList<RawToken> tokens = Tokenizer.Tokenize(new StringReader(StepSample.Minimal), diagnostics);

        TestAssert.True(diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error), "Expected no tokenizer errors.");
        string[] first = tokens.Take(12).Select(token => token.Lexeme).ToArray();
        TestAssert.SequenceEqual(
            [
                "ISO-10303-21", ";", "HEADER", ";", "FILE_DESCRIPTION", "(", "(", "'A minimal AP214 example'",
                ")", ",", "'2;1'", ")"
            ],
            first,
            "Tokenizer leading sequence mismatch.");
    }

    private static void Lexer_DecodesEscapedStringsAndEnums()
    {
        const string input = """
            ISO-10303-21;
            HEADER;
            FILE_DESCRIPTION(('A''B \X2\0041\X0\'),'2;1');
            FILE_NAME('demo','2003',('a'),('b'),' ','sys',' ');
            FILE_SCHEMA(('TEST'));
            ENDSEC;
            DATA;
            #1=THING(.FOO.,"00FF");
            ENDSEC;
            END-ISO-10303-21;
            """;

        List<ParseDiagnostic> diagnostics = new();
        var raw = Tokenizer.Tokenize(new StringReader(input), diagnostics);
        var tokens = StepParser.Lexer.Lexer.Lex(raw, diagnostics);

        TestAssert.True(tokens.Any(token => token.Kind == TokenKind.String && Equals(token.Value, "A'B A")), "Expected decoded string token.");
        TestAssert.True(tokens.Any(token => token.Kind == TokenKind.Enumeration && Equals(token.Value, "FOO")), "Expected enumeration token.");
        TestAssert.True(tokens.Any(token => token.Kind == TokenKind.Binary && Equals(token.Value, "00FF")), "Expected binary token.");
    }

    private static void Parser_ParsesMinimalSample()
    {
        List<ParseDiagnostic> diagnostics = new();
        var raw = Tokenizer.Tokenize(new StringReader(StepSample.Minimal), diagnostics);
        var tokens = StepParser.Lexer.Lexer.Lex(raw, diagnostics);
        StepFileParser parser = new(tokens, diagnostics);

        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, StepSample.Minimal);
            StepFile result = parser.Parse(tempFile);

            TestAssert.Equal(11, result.Data.Count, "Entity count mismatch.");
            TestAssert.Equal("AUTOMOTIVE_DESIGN { 1 0 10303 214 2 1 1}", result.FileSchema, "Schema mismatch.");
            TestAssert.Equal("PRODUCT", result.Data[16].Name, "Entity #16 mismatch.");
            TestAssert.True(diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error), "Expected no parse errors.");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private static void Cli_ReturnsParseErrorForTruncatedInput()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "ISO-10303-21; HEADER; FILE_DESCRIPTION(('x'),'2;1'); FILE_NAME('a','b',('c'),('d'),'e','f','g'); FILE_SCHEMA(('TEST')); ENDSEC; DATA; #1=FOO(");
            StringWriter stdout = new();
            StringWriter stderr = new();

            int exitCode = StepParserCli.Run([tempFile], stdout, stderr);

            TestAssert.Equal(2, exitCode, "Expected parse error exit code.");
            TestAssert.True(stdout.ToString().Contains("diagnostics", StringComparison.OrdinalIgnoreCase), "Expected diagnostics in output.");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ── Issue #1 regression ────────────────────────────────────────────────
    // Non-conformant complex entity (#id = TYPE1(...) TYPE2(...); without outer parens)
    // must be recovered with a Warning diagnostic, not a hard parse error.
    private static void Parser_RecoversMalformedComplexEntityInstance()
    {
        const string input = """
            ISO-10303-21;
            HEADER;
            FILE_DESCRIPTION(('test'),'2;1');
            FILE_NAME('f','2024',('a'),('b'),' ','s',' ');
            FILE_SCHEMA(('TEST'));
            ENDSEC;
            DATA;
            #1=LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.);
            ENDSEC;
            END-ISO-10303-21;
            """;

        List<ParseDiagnostic> diagnostics = new();
        var raw = Tokenizer.Tokenize(new StringReader(input), diagnostics);
        var tokens = StepParser.Lexer.Lexer.Lex(raw, diagnostics);
        StepFileParser parser = new(tokens, diagnostics);
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, input);
            StepFile result = parser.Parse(tempFile);

            TestAssert.Equal(1, result.Data.Count, "Should recover exactly one entity.");
            TestAssert.True(result.Data[1].IsComplex, "Recovered entity should be complex.");
            TestAssert.Equal(3, result.Data[1].Components!.Count, "Should have 3 components: LENGTH_UNIT, NAMED_UNIT, SI_UNIT.");
            TestAssert.True(
                diagnostics.Any(d => d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("11.3.3")),
                "Should emit a Warning diagnostic referencing ISO 10303-21 §11.3.3.");
            TestAssert.True(
                diagnostics.All(d => d.Severity != DiagnosticSeverity.Error),
                "Should have no Error diagnostics — only Warning for the malformed form.");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ── Issue #2 regression ────────────────────────────────────────────────
    // --output <path> must write output to the given file and produce no stdout.
    private static void Cli_OutputFlag_WritesFileAndStdoutIsEmpty()
    {
        string tempInput = Path.GetTempFileName();
        string tempOutput = Path.ChangeExtension(Path.GetTempFileName(), ".json");
        try
        {
            File.WriteAllText(tempInput, StepSample.Minimal);
            StringWriter stdout = new();
            StringWriter stderr = new();

            int exitCode = StepParserCli.Run([tempInput, "--output", tempOutput], stdout, stderr);

            TestAssert.Equal(0, exitCode, "--output on a clean file should exit 0.");
            TestAssert.True(File.Exists(tempOutput), "--output file must be created.");
            TestAssert.True(File.ReadAllText(tempOutput).Contains("\"schema\""), "Output file must contain parsed JSON.");
            TestAssert.True(string.IsNullOrWhiteSpace(stdout.ToString()), "stdout must be empty when --output is used.");
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }

    // ── Issue #3 regressions ───────────────────────────────────────────────
    // Text format must show errors and warnings as separate counts, not a single total.
    private static void TextFormatter_ShowsDiagnosticSplit_WhenErrorsAndWarnings()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            // Truncated input → parse error
            File.WriteAllText(tempFile, "ISO-10303-21; HEADER; FILE_DESCRIPTION(('x'),'2;1'); FILE_NAME('a','b',('c'),('d'),'e','f','g'); FILE_SCHEMA(('T')); ENDSEC; DATA; #1=FOO(");
            StringWriter stdout = new();
            StringWriter stderr = new();

            StepParserCli.Run([tempFile, "--format", "text", "--no-color"], stdout, stderr);
            string output = stdout.ToString();

            TestAssert.True(output.Contains("Errors:"), "Text output must contain 'Errors:' when errors are present.");
            TestAssert.True(!output.Contains("Diagnostics:"), "Text output must not show opaque 'Diagnostics:' count when there are actual errors.");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // Text format must include entity-type breakdown.
    private static void TextFormatter_ShowsEntityTypeBreakdown()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, StepSample.Minimal);
            StringWriter stdout = new();
            StringWriter stderr = new();

            StepParserCli.Run([tempFile, "--format", "text", "--no-color"], stdout, stderr);
            string output = stdout.ToString();

            TestAssert.True(output.Contains("Entity types:"), "Text output must contain 'Entity types:' section.");
            TestAssert.True(output.Contains("PRODUCT:"), "Entity type breakdown must list PRODUCT.");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // Text format must not silently truncate diagnostics at 20.
    // Drive TextFormatter directly with a synthetic ParseResult containing 25 errors.
    private static void TextFormatter_ShowsAllDiagnosticsWithoutCap()
    {
        ParseDiagnostic[] diagnostics = Enumerable.Range(1, 25)
            .Select(i => new ParseDiagnostic(DiagnosticSeverity.Error, i, 1, $"Synthetic error #{i}"))
            .ToArray();

        ParseResult result = new(
            Source: "synthetic.stp",
            FileSize: 0,
            Edition: null,
            Schema: null,
            Header: new HeaderSummary(null, null, null, null, null, null, null, null, null, null),
            Stats: new StatsSummary(0, new Dictionary<string, int>()),
            Entities: Array.Empty<EntitySummary>(),
            Diagnostics: diagnostics,
            Elapsed: TimeSpan.Zero);

        string output = new TextFormatter(noColor: true).Format(result);

        // Diagnostic lines are indented ("  ERROR …"); the summary line ("Errors: 25") is not.
        int diagnosticLineCount = output.Split('\n')
            .Count(line => line.StartsWith("  ERROR", StringComparison.OrdinalIgnoreCase));

        TestAssert.True(diagnosticLineCount == 25,
            $"All 25 diagnostics must appear in text output without cap, got {diagnosticLineCount}.");
    }

    // ── Issue #5 regression ────────────────────────────────────────────────
    // --strict must print a human-readable annotation to stderr when it promotes
    // warnings to errors, so the effect is observable without inspecting the exit code.
    private static void Cli_StrictMode_PrintsAnnotationAndExits2OnWarnings()
    {
        // A file that generates a Warning (malformed complex entity, recovered)
        const string input = """
            ISO-10303-21;
            HEADER;
            FILE_DESCRIPTION(('test'),'2;1');
            FILE_NAME('f','2024',('a'),('b'),' ','s',' ');
            FILE_SCHEMA(('TEST'));
            ENDSEC;
            DATA;
            #1=LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.);
            ENDSEC;
            END-ISO-10303-21;
            """;

        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, input);
            StringWriter stdout = new();
            StringWriter stderr = new();

            int exitCode = StepParserCli.Run([tempFile, "--strict", "--no-color"], stdout, stderr);

            TestAssert.Equal(2, exitCode, "--strict must exit 2 when warnings are present.");
            TestAssert.True(
                stderr.ToString().Contains("strict", StringComparison.OrdinalIgnoreCase),
                "--strict must write an observable annotation to stderr.");
            TestAssert.True(
                stderr.ToString().Contains("promoted", StringComparison.OrdinalIgnoreCase),
                "The stderr annotation must mention 'promoted'.");

            // Without --strict the same file must exit 3 (warnings only)
            StringWriter stdout2 = new();
            StringWriter stderr2 = new();
            int exitCodeNoStrict = StepParserCli.Run([tempFile, "--no-color"], stdout2, stderr2);
            TestAssert.Equal(3, exitCodeNoStrict, "Without --strict warnings-only file must exit 3.");
            TestAssert.True(
                !stderr2.ToString().Contains("promoted", StringComparison.OrdinalIgnoreCase),
                "Without --strict the 'promoted' annotation must not appear.");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}

internal sealed record SweepResult(
    string File,
    int ExitCode,
    long Milliseconds,
    long Size,
    string StdoutSample,
    string StderrSample);

internal static class TestAssert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected '{expected}', got '{actual}'.");
        }
    }

    public static void SequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string message)
    {
        if (expected.Count != actual.Count)
        {
            throw new InvalidOperationException($"{message} Count mismatch {expected.Count} != {actual.Count}.");
        }

        for (int index = 0; index < expected.Count; index++)
        {
            if (!EqualityComparer<T>.Default.Equals(expected[index], actual[index]))
            {
                throw new InvalidOperationException($"{message} Item {index} mismatch '{expected[index]}' != '{actual[index]}'.");
            }
        }
    }
}
