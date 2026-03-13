using System.Text.Json;
using System.Text.Json.Serialization;
using StepParser.Parser;

namespace StepParser.Output;

public sealed class JsonFormatter : IOutputFormatter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Format(object payload)
    {
        object normalized = payload is ParseResult parseResult
            ? ParseResultJsonProjection.Create(parseResult)
            : payload;
        return JsonSerializer.Serialize(normalized, Options);
    }
}
