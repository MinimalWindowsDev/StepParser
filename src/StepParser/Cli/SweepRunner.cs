namespace StepParser.Cli;

/// <summary>One row of a corpus sweep: the outcome of parsing a single STEP file.</summary>
public sealed record SweepRow(
    string File,
    int ExitCode,
    long Milliseconds,
    long Size,
    string StdoutSample,
    string StderrSample);

/// <summary>
/// Bulk-parses a list of STEP file paths read from a <see cref="TextReader"/> (normally stdin),
/// writing one CSV row per file plus an exit-code histogram.
///
/// Each file is parsed in-process through <see cref="StepParserCli.Run(string[], TextWriter, TextWriter)"/>,
/// so a sweep exercises exactly the same code path as a single-file invocation.
/// </summary>
public static class SweepRunner
{
    /// <summary>Number of slowest / failing rows echoed to the summary output.</summary>
    internal const int SlowSampleCount = 10;
    internal const int FailSampleCount = 15;

    public static int Run(TextReader input, TextWriter output, string csvPath)
    {
        IReadOnlyList<string> files = ReadFileList(input);
        List<SweepRow> rows = new(files.Count);

        foreach (string file in files)
        {
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            StringWriter stdout = new();
            StringWriter stderr = new();
            int exitCode = StepParserCli.Run([file], stdout, stderr);
            stopwatch.Stop();

            rows.Add(new SweepRow(
                file,
                exitCode,
                stopwatch.ElapsedMilliseconds,
                new FileInfo(file).Length,
                FirstLine(stdout.ToString()),
                FirstLine(stderr.ToString())));
        }

        WriteCsv(csvPath, rows);
        WriteSummary(output, rows);
        return 0;
    }

    /// <summary>
    /// Reads candidate paths one per line, dropping blanks and paths that do not exist, then
    /// de-duplicates case-insensitively and orders deterministically so two sweeps over the same
    /// corpus produce byte-identical CSVs.
    /// </summary>
    internal static IReadOnlyList<string> ReadFileList(TextReader input)
    {
        List<string> files = new();
        string? line;
        while ((line = input.ReadLine()) is not null)
        {
            if (!string.IsNullOrWhiteSpace(line) && File.Exists(line))
            {
                files.Add(line);
            }
        }

        return files
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static void WriteCsv(string path, IReadOnlyList<SweepRow> rows)
    {
        using StreamWriter writer = new(path, false);
        writer.WriteLine("File,ExitCode,Milliseconds,Size,StdoutSample,StderrSample");
        foreach (SweepRow row in rows)
        {
            writer.WriteLine(string.Join(",",
                Csv(row.File),
                row.ExitCode,
                row.Milliseconds,
                row.Size,
                Csv(row.StdoutSample),
                Csv(row.StderrSample)));
        }
    }

    internal static void WriteSummary(TextWriter output, IReadOnlyList<SweepRow> rows)
    {
        output.WriteLine($"TOTAL={rows.Count}");
        foreach (IGrouping<int, SweepRow> group in rows.GroupBy(row => row.ExitCode).OrderBy(group => group.Key))
        {
            output.WriteLine($"EXIT_{group.Key}={group.Count()}");
        }

        output.WriteLine("TOP_SLOW=");
        foreach (SweepRow row in rows.OrderByDescending(row => row.Milliseconds).Take(SlowSampleCount))
        {
            output.WriteLine($"{row.ExitCode}\t{row.Milliseconds}ms\t{row.Size}\t{row.File}");
        }

        output.WriteLine("TOP_FAIL=");
        foreach (SweepRow row in rows.Where(row => row.ExitCode != 0).Take(FailSampleCount))
        {
            output.WriteLine($"{row.ExitCode}\t{row.Milliseconds}ms\t{row.File}\t{row.StdoutSample}\t{row.StderrSample}");
        }
    }

    /// <summary>RFC-4180 quoting: wrap in quotes and double any embedded quote.</summary>
    internal static string Csv(string? value)
    {
        value ??= string.Empty;
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    internal static string FirstLine(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        using StringReader reader = new(value);
        return reader.ReadLine() ?? string.Empty;
    }
}
