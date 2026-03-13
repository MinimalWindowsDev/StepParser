using StepParser.Diagnostics;

namespace StepParser.Parser;

public sealed record ParseResult(
    string Source,
    long FileSize,
    int? Edition,
    string? Schema,
    HeaderSummary Header,
    StatsSummary Stats,
    IReadOnlyList<EntitySummary> Entities,
    IReadOnlyList<ParseDiagnostic> Diagnostics,
    TimeSpan Elapsed)
{
    public static ParseResult FromStepFile(StepFile stepFile, IReadOnlyList<ParseDiagnostic> diagnostics, TimeSpan elapsed)
    {
        Dictionary<string, int> entityTypes = new(StringComparer.OrdinalIgnoreCase);
        List<EntitySummary> entities = new(stepFile.Data.Count);
        foreach (EntityInstance entity in stepFile.Data.OrderBy(pair => pair.Key).Select(pair => pair.Value))
        {
            if (entity.IsComplex)
            {
                foreach (EntityComponent component in entity.Components!)
                {
                    entityTypes[component.Name] = entityTypes.GetValueOrDefault(component.Name) + 1;
                }
            }
            else if (entity.Name is not null)
            {
                entityTypes[entity.Name] = entityTypes.GetValueOrDefault(entity.Name) + 1;
            }

            entities.Add(new EntitySummary(
                entity.Id,
                entity.IsComplex ? "complex" : "simple",
                entity.Name,
                entity.Parameters,
                entity.Components?.Select(component => component.Name).ToArray()));
        }

        return new ParseResult(
            stepFile.Source,
            stepFile.FileSize,
            stepFile.DetectedEdition,
            stepFile.FileSchema,
            HeaderSummary.FromSection(stepFile.Header),
            new StatsSummary(stepFile.Data.Count, entityTypes),
            entities,
            diagnostics.ToArray(),
            elapsed);
    }
}

public sealed record HeaderSummary(
    IReadOnlyList<string>? FileDescription,
    string? ImplementationLevel,
    string? Name,
    string? TimeStamp,
    IReadOnlyList<string>? Authors,
    IReadOnlyList<string>? Organizations,
    string? PreprocessorVersion,
    string? OriginatingSystem,
    string? Authorization,
    IReadOnlyList<string>? Schemas)
{
    public static HeaderSummary FromSection(HeaderSection section)
    {
        HeaderEntity? description = section.Entities.FirstOrDefault(entity => entity.Name == "FILE_DESCRIPTION");
        HeaderEntity? name = section.Entities.FirstOrDefault(entity => entity.Name == "FILE_NAME");
        HeaderEntity? schema = section.Entities.FirstOrDefault(entity => entity.Name == "FILE_SCHEMA");

        return new HeaderSummary(
            ExtractStrings(description, 0),
            ExtractString(description, 1),
            ExtractString(name, 0),
            ExtractString(name, 1),
            ExtractStrings(name, 2),
            ExtractStrings(name, 3),
            ExtractString(name, 4),
            ExtractString(name, 5),
            ExtractString(name, 6),
            ExtractStrings(schema, 0));
    }

    private static string? ExtractString(HeaderEntity? entity, int index)
    {
        if (entity is null || index >= entity.Parameters.Count)
        {
            return null;
        }

        return entity.Parameters[index] switch
        {
            Parameter.StringValue value => value.Value,
            _ => null
        };
    }

    private static IReadOnlyList<string>? ExtractStrings(HeaderEntity? entity, int index)
    {
        if (entity is null || index >= entity.Parameters.Count)
        {
            return null;
        }

        if (entity.Parameters[index] is not Parameter.ListValue list)
        {
            return null;
        }

        return list.Items.OfType<Parameter.StringValue>().Select(value => value.Value).ToArray();
    }
}

public sealed record StatsSummary(int EntityCount, IReadOnlyDictionary<string, int> EntityTypes);

public sealed record EntitySummary(
    int Id,
    string Type,
    string? Name,
    IReadOnlyList<Parameter> Params,
    IReadOnlyList<string>? Names);

internal static class ParseResultJsonProjection
{
    public static object Create(ParseResult result)
    {
        return new
        {
            source = result.Source,
            fileSize = result.FileSize,
            edition = result.Edition,
            schema = result.Schema,
            diagnostics = result.Diagnostics.Select(diagnostic => new
            {
                severity = diagnostic.Severity.ToString().ToLowerInvariant(),
                line = diagnostic.Line,
                col = diagnostic.Column,
                message = diagnostic.Message
            }),
            header = new
            {
                fileDescription = result.Header.FileDescription,
                implementationLevel = result.Header.ImplementationLevel,
                name = result.Header.Name,
                timeStamp = result.Header.TimeStamp,
                authors = result.Header.Authors,
                organizations = result.Header.Organizations,
                preprocessorVersion = result.Header.PreprocessorVersion,
                originatingSystem = result.Header.OriginatingSystem,
                authorization = result.Header.Authorization,
                schemas = result.Header.Schemas
            },
            stats = new
            {
                entityCount = result.Stats.EntityCount,
                entityTypes = result.Stats.EntityTypes
            },
            entities = result.Entities.Select(entity => new
            {
                id = entity.Id,
                type = entity.Type,
                name = entity.Name,
                @params = entity.Params.Select(ParameterJsonProjection.Create),
                names = entity.Names
            })
        };
    }
}

internal static class ParameterJsonProjection
{
    public static object Create(Parameter parameter)
    {
        return parameter switch
        {
            Parameter.EntityReference value => new { kind = "entityRef", id = value.Id },
            Parameter.StringValue value => new { kind = "string", value = value.Value },
            Parameter.IntegerValue value => new { kind = "integer", value = value.Value },
            Parameter.RealValue value => new { kind = "real", value = value.Value },
            Parameter.BinaryValue value => new { kind = "binary", value = value.Value },
            Parameter.EnumValue value => new { kind = "enum", value = value.Name },
            Parameter.ListValue value => new { kind = "list", items = value.Items.Select(Create) },
            Parameter.UnsetValue => new { kind = "unset" },
            Parameter.InheritedValue => new { kind = "inherited" },
            Parameter.TypedValue value => new { kind = "typed", type = value.TypeName, inner = Create(value.Inner) },
            _ => new { kind = "unknown" }
        };
    }
}
