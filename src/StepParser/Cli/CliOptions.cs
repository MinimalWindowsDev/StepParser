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
}
