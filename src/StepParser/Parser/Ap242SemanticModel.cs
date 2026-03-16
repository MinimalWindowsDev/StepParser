namespace StepParser.Parser;

public sealed record Ap242SemanticSummary(
    bool IsAp242,
    bool HasModelBasedDefinition,
    bool HasPmi,
    bool HasGdt,
    IReadOnlyDictionary<string, int> CategoryCounts,
    IReadOnlyList<PmiDimensionSummary> Dimensions,
    IReadOnlyList<DatumSummary> Datums,
    IReadOnlyList<string> DetectedEntityTypes);

public sealed record PmiDimensionSummary(
    int Id,
    string EntityType,
    string? Name,
    int? TargetAspectId,
    double? NominalValue,
    double? UpperTolerance,
    double? LowerTolerance);

public sealed record DatumSummary(
    int Id,
    string? Label,
    string? Description,
    int? TargetEntityId);

internal static class Ap242SemanticModelBuilder
{
    private static readonly HashSet<string> PmiEntityTypes =
    [
        "ANNOTATION_OCCURRENCE",
        "ANNOTATION_PLANE",
        "ANNOTATION_TEXT",
        "TESSELLATED_ANNOTATION_OCCURRENCE",
        "DRAUGHTING_CALLOUT",
        "DRAUGHTING_MODEL",
        "DIMENSIONAL_SIZE",
        "DIMENSIONAL_LOCATION",
        "GEOMETRIC_TOLERANCE",
        "POSITION_TOLERANCE",
        "STRAIGHTNESS_TOLERANCE",
        "FLATNESS_TOLERANCE",
        "PROFILE_TOLERANCE",
        "ANGULARITY_TOLERANCE",
        "CYLINDRICITY_TOLERANCE",
        "CIRCULAR_RUNOUT_TOLERANCE",
        "TOTAL_RUNOUT_TOLERANCE",
        "TOLERANCE_VALUE",
        "DATUM",
        "DATUM_FEATURE",
        "DATUM_REFERENCE",
        "SHAPE_ASPECT",
        "MEASURE_REPRESENTATION_ITEM"
    ];

    private static readonly HashSet<string> GdtEntityTypes =
    [
        "DIMENSIONAL_SIZE",
        "DIMENSIONAL_LOCATION",
        "GEOMETRIC_TOLERANCE",
        "POSITION_TOLERANCE",
        "STRAIGHTNESS_TOLERANCE",
        "FLATNESS_TOLERANCE",
        "PROFILE_TOLERANCE",
        "ANGULARITY_TOLERANCE",
        "CYLINDRICITY_TOLERANCE",
        "CIRCULAR_RUNOUT_TOLERANCE",
        "TOTAL_RUNOUT_TOLERANCE",
        "DATUM",
        "DATUM_REFERENCE",
        "TOLERANCE_VALUE"
    ];

    public static Ap242SemanticSummary Build(StepFile stepFile)
    {
        Dictionary<string, int> categoryCounts = new(StringComparer.OrdinalIgnoreCase)
        {
            ["representation"] = 0,
            ["geometry"] = 0,
            ["pmi"] = 0,
            ["gdt"] = 0
        };

        HashSet<string> detectedTypes = new(StringComparer.OrdinalIgnoreCase);
        List<PmiDimensionSummary> dimensions = new();
        List<DatumSummary> datums = new();
        string schema = stepFile.FileSchema ?? string.Empty;
        bool isAp242 = schema.Contains("AP242", StringComparison.OrdinalIgnoreCase);

        foreach (EntityInstance entity in stepFile.Data.Values)
        {
            foreach (string entityType in EnumerateEntityTypes(entity))
            {
                detectedTypes.Add(entityType);

                if (entityType.Contains("REPRESENTATION", StringComparison.OrdinalIgnoreCase) ||
                    entityType.Contains("CONTEXT", StringComparison.OrdinalIgnoreCase))
                {
                    categoryCounts["representation"]++;
                }

                if (entityType.Contains("FACE", StringComparison.OrdinalIgnoreCase) ||
                    entityType.Contains("BREP", StringComparison.OrdinalIgnoreCase) ||
                    entityType.Contains("SHELL", StringComparison.OrdinalIgnoreCase) ||
                    entityType.Contains("CURVE", StringComparison.OrdinalIgnoreCase) ||
                    entityType.Contains("SURFACE", StringComparison.OrdinalIgnoreCase))
                {
                    categoryCounts["geometry"]++;
                }

                if (PmiEntityTypes.Contains(entityType))
                {
                    categoryCounts["pmi"]++;
                }

                if (GdtEntityTypes.Contains(entityType))
                {
                    categoryCounts["gdt"]++;
                }
            }

            if (string.Equals(entity.Name, "DIMENSIONAL_SIZE", StringComparison.OrdinalIgnoreCase))
            {
                dimensions.Add(BuildDimension(stepFile.Data, entity));
            }

            if (string.Equals(entity.Name, "DATUM", StringComparison.OrdinalIgnoreCase))
            {
                datums.Add(BuildDatum(entity));
            }
        }

        bool hasPmi = categoryCounts["pmi"] > 0;
        bool hasGdt = categoryCounts["gdt"] > 0;
        bool hasMbd = isAp242 && (hasPmi || hasGdt);

        return new Ap242SemanticSummary(
            isAp242,
            hasMbd,
            hasPmi,
            hasGdt,
            categoryCounts,
            dimensions,
            datums,
            detectedTypes.OrderBy(type => type, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static IEnumerable<string> EnumerateEntityTypes(EntityInstance entity)
    {
        if (entity.IsComplex)
        {
            return entity.Components!.Select(component => component.Name);
        }

        return entity.Name is null ? [] : [entity.Name];
    }

    private static PmiDimensionSummary BuildDimension(
        IReadOnlyDictionary<int, EntityInstance> entities,
        EntityInstance entity)
    {
        int? targetAspectId = TryGetEntityRef(entity.Parameters, 0);
        string? name = TryGetString(entity.Parameters, 1);
        double? nominalValue = null;
        double? upperTolerance = null;
        double? lowerTolerance = null;

        foreach (EntityInstance candidate in entities.Values.OrderBy(e => e.Id))
        {
            if (!string.Equals(candidate.Name, "MEASURE_WITH_UNIT", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            double? value = TryGetNestedMeasureValue(candidate.Parameters.FirstOrDefault());
            if (value is null)
            {
                continue;
            }

            if (nominalValue is null)
            {
                nominalValue = value;
                continue;
            }

            if (value >= 0 && upperTolerance is null)
            {
                upperTolerance = value;
                continue;
            }

            if (value < 0 && lowerTolerance is null)
            {
                lowerTolerance = value;
            }
        }

        return new PmiDimensionSummary(
            entity.Id,
            entity.Name ?? "DIMENSIONAL_SIZE",
            name,
            targetAspectId,
            nominalValue,
            upperTolerance,
            lowerTolerance);
    }

    private static DatumSummary BuildDatum(EntityInstance entity)
    {
        return new DatumSummary(
            entity.Id,
            TryGetString(entity.Parameters, 0),
            TryGetString(entity.Parameters, 1),
            TryGetEntityRef(entity.Parameters, 2));
    }

    private static int? TryGetEntityRef(IReadOnlyList<Parameter> parameters, int index)
    {
        if (index >= parameters.Count)
        {
            return null;
        }

        return parameters[index] is Parameter.EntityReference entityReference ? entityReference.Id : null;
    }

    private static string? TryGetString(IReadOnlyList<Parameter> parameters, int index)
    {
        if (index >= parameters.Count)
        {
            return null;
        }

        return parameters[index] is Parameter.StringValue stringValue ? stringValue.Value : null;
    }

    private static double? TryGetNestedMeasureValue(Parameter? parameter)
    {
        return parameter switch
        {
            Parameter.TypedValue { Inner: Parameter.RealValue realValue } => realValue.Value,
            Parameter.TypedValue { Inner: Parameter.IntegerValue integerValue } => integerValue.Value,
            Parameter.RealValue realValue => realValue.Value,
            Parameter.IntegerValue integerValue => integerValue.Value,
            _ => null
        };
    }
}
