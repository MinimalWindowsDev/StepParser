namespace StepParser.Cli;

internal sealed class CliOptions
{
    public required string InputPath { get; init; }

    public string? OutputPath { get; init; }

    public OutputFormat Format { get; init; } = OutputFormat.Json;

    public ExecutionPhase Phase { get; init; } = ExecutionPhase.Parse;

    public bool Strict { get; init; }

    public bool NoColor { get; init; }

    public bool Verbose { get; init; }

    /// <summary>Enable AOP verbose debug logging (Fody/Serilog).</summary>
    public bool Log { get; init; }

    /// <summary>
    /// Explicit log file path. When null and <see cref="Log"/> is true the logger writes to
    /// a file named &lt;inputfile&gt;.log beside the input file.
    /// </summary>
    public string? LogFile { get; init; }
}
