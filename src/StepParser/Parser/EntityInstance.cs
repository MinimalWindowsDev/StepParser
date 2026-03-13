namespace StepParser.Parser;

public sealed record EntityComponent(string Name, IReadOnlyList<Parameter> Parameters);

public sealed record EntityInstance(
    int Id,
    string? Name,
    IReadOnlyList<Parameter> Parameters,
    IReadOnlyList<EntityComponent>? Components)
{
    public bool IsComplex => Components is { Count: > 0 };
}
