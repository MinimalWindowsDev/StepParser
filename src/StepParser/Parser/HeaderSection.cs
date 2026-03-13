namespace StepParser.Parser;

public sealed record HeaderEntity(string Name, IReadOnlyList<Parameter> Parameters);

public sealed record HeaderSection(IReadOnlyList<HeaderEntity> Entities);
