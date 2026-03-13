namespace StepParser.Parser;

public sealed record StepFile(
    string Source,
    long FileSize,
    HeaderSection Header,
    IReadOnlyDictionary<int, EntityInstance> Data,
    int? DetectedEdition,
    string? FileSchema);
