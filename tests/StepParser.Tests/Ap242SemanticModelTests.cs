using StepParser.Parser;

namespace StepParser.Tests;

public sealed class Ap242SemanticModelTests
{
    private static Ap242SemanticSummary Analyse(string source) => Sut.ParseToResult(source).Semantics;

    private static StepFile Synthetic(string? schema, params EntityInstance[] entities) =>
        new(
            Source: "synthetic.stp",
            FileSize: 0,
            Header: new HeaderSection([]),
            Data: entities.ToDictionary(entity => entity.Id),
            DetectedEdition: null,
            FileSchema: schema);

    // ── Schema detection ────────────────────────────────────────────────────

    [Fact]
    public void RecognisesAp242Schema()
    {
        Assert.True(Analyse(StepSample.Ap242Pmi).IsAp242);
    }

    [Fact]
    public void NonAp242Schema_IsNotFlagged()
    {
        Ap242SemanticSummary summary = Analyse(StepSample.Minimal);

        Assert.False(summary.IsAp242);
        Assert.False(summary.HasModelBasedDefinition);
    }

    [Fact]
    public void MissingSchema_IsTreatedAsNotAp242()
    {
        Assert.False(Ap242SemanticModelBuilder.Build(Synthetic(null)).IsAp242);
    }

    // ── Category counting ───────────────────────────────────────────────────

    [Fact]
    public void CountsPmiAndGdtFromTheCatalogs()
    {
        Ap242SemanticSummary summary = Analyse(StepSample.Ap242Pmi);

        Assert.True(summary.HasPmi);
        Assert.True(summary.HasGdt);
        Assert.True(summary.CategoryCounts["pmi"] > 0);
        Assert.True(summary.CategoryCounts["gdt"] > 0);
    }

    [Fact]
    public void ModelBasedDefinition_RequiresBothAp242AndPmi()
    {
        // PMI entities present, but the schema is AP214 — MBD must stay false.
        Ap242SemanticSummary summary = Analyse(StepSample.WithData("#1=FLATNESS_TOLERANCE('f','',#2,#3);", schema: "AUTOMOTIVE_DESIGN"));

        Assert.True(summary.HasPmi);
        Assert.False(summary.IsAp242);
        Assert.False(summary.HasModelBasedDefinition);
    }

    [Fact]
    public void ModelBasedDefinition_TrueWhenAp242AndPmiCoincide()
    {
        Assert.True(Analyse(StepSample.Ap242Pmi).HasModelBasedDefinition);
    }

    // These two counters use substring matching rather than the curated catalogs, so they are
    // deliberately loose. Pinning that behaviour here keeps a future "tidy-up" honest about it.
    [Theory]
    [InlineData("ADVANCED_FACE", "geometry")]
    [InlineData("MANIFOLD_SOLID_BREP", "geometry")]
    [InlineData("CLOSED_SHELL", "geometry")]
    [InlineData("B_SPLINE_CURVE", "geometry")]
    [InlineData("CYLINDRICAL_SURFACE", "geometry")]
    [InlineData("SHAPE_REPRESENTATION", "representation")]
    [InlineData("APPLICATION_CONTEXT", "representation")]
    public void CategorisesBySubstringMatch(string entityName, string category)
    {
        Ap242SemanticSummary summary = Analyse(StepSample.WithData($"#1={entityName}();"));

        Assert.Equal(1, summary.CategoryCounts[category]);
    }

    [Fact]
    public void ComplexInstance_ContributesEveryComponentName()
    {
        Ap242SemanticSummary summary = Analyse(
            StepSample.WithData("#1=(DATUM('A','',#2)FLATNESS_TOLERANCE('f','',#3,#4));"));

        Assert.Contains("DATUM", summary.DetectedEntityTypes);
        Assert.Contains("FLATNESS_TOLERANCE", summary.DetectedEntityTypes);
    }

    [Fact]
    public void EntityWithNoNameAndNoComponents_ContributesNothing()
    {
        StepFile file = Synthetic("AP242", new EntityInstance(1, null, [], null));

        Assert.Empty(Ap242SemanticModelBuilder.Build(file).DetectedEntityTypes);
    }

    [Fact]
    public void DetectedEntityTypes_AreSortedAndDeduplicated()
    {
        Ap242SemanticSummary summary = Analyse(StepSample.WithData("#1=BETA();\n#2=ALPHA();\n#3=ALPHA();"));

        Assert.Equal(["ALPHA", "BETA"], summary.DetectedEntityTypes);
    }

    // ── Dimensions ──────────────────────────────────────────────────────────

    [Fact]
    public void ExtractsDimensionalSize()
    {
        Ap242SemanticSummary summary = Analyse(StepSample.Ap242Pmi);

        PmiDimensionSummary dimension = Assert.Single(summary.Dimensions);
        Assert.Equal(2, dimension.Id);
        Assert.Equal("DIMENSIONAL_SIZE", dimension.EntityType);
        Assert.Equal("width", dimension.Name);
        Assert.Equal(10, dimension.TargetAspectId);
        Assert.Equal(25.0, dimension.NominalValue);
    }

    [Theory]
    [InlineData("MEASURE_WITH_UNIT(LENGTH_MEASURE(4.5),#9)", 4.5)]   // TypedValue wrapping a real
    [InlineData("MEASURE_WITH_UNIT(LENGTH_MEASURE(4),#9)", 4.0)]     // TypedValue wrapping an integer
    [InlineData("MEASURE_WITH_UNIT(4.5,#9)", 4.5)]                   // bare real
    [InlineData("MEASURE_WITH_UNIT(4,#9)", 4.0)]                     // bare integer
    public void ReadsNominalValueFromEveryMeasureShape(string measureEntity, double expected)
    {
        Ap242SemanticSummary summary = Analyse(
            StepSample.WithData($"#1=DIMENSIONAL_SIZE(#5,'d');\n#2={measureEntity};"));

        Assert.Equal(expected, Assert.Single(summary.Dimensions).NominalValue);
    }

    [Fact]
    public void MeasureWithUnparseableValue_IsIgnored()
    {
        Ap242SemanticSummary summary = Analyse(
            StepSample.WithData("#1=DIMENSIONAL_SIZE(#5,'d');\n#2=MEASURE_WITH_UNIT('not a number',#9);"));

        Assert.Null(Assert.Single(summary.Dimensions).NominalValue);
    }

    [Fact]
    public void AssignsPositiveAndNegativeMeasuresAsUpperAndLowerTolerance()
    {
        Ap242SemanticSummary summary = Analyse(StepSample.WithData("""
            #1=DIMENSIONAL_SIZE(#5,'d');
            #2=MEASURE_WITH_UNIT(LENGTH_MEASURE(25.0),#9);
            #3=MEASURE_WITH_UNIT(LENGTH_MEASURE(0.2),#9);
            #4=MEASURE_WITH_UNIT(LENGTH_MEASURE(-0.1),#9);
            """));

        PmiDimensionSummary dimension = Assert.Single(summary.Dimensions);
        Assert.Equal(25.0, dimension.NominalValue);
        Assert.Equal(0.2, dimension.UpperTolerance);
        Assert.Equal(-0.1, dimension.LowerTolerance);
    }

    [Fact]
    public void DimensionWithNonStringName_LeavesNameNull()
    {
        Ap242SemanticSummary summary = Analyse(StepSample.WithData("#1=DIMENSIONAL_SIZE(#5,$);"));

        Assert.Null(Assert.Single(summary.Dimensions).Name);
    }

    [Fact]
    public void DimensionWithNonReferenceTarget_LeavesTargetNull()
    {
        Ap242SemanticSummary summary = Analyse(StepSample.WithData("#1=DIMENSIONAL_SIZE('notaref','d');"));

        Assert.Null(Assert.Single(summary.Dimensions).TargetAspectId);
    }

    [Fact]
    public void DimensionWithTooFewParameters_LeavesFieldsNull()
    {
        Ap242SemanticSummary summary = Analyse(StepSample.WithData("#1=DIMENSIONAL_SIZE();"));

        PmiDimensionSummary dimension = Assert.Single(summary.Dimensions);
        Assert.Null(dimension.TargetAspectId);
        Assert.Null(dimension.Name);
    }

    // KNOWN LIMITATION, pinned deliberately rather than asserted as correct:
    // BuildDimension scans every MEASURE_WITH_UNIT in the file instead of following the reference
    // graph from the dimension, so with two dimensions both receive the same values. See
    // Ap242SemanticModel.cs BuildDimension. This test documents the defect; when it is fixed this
    // test should FAIL and be rewritten to assert per-dimension resolution.
    [Fact]
    public void KnownLimitation_MultipleDimensionsShareGloballyScannedMeasures()
    {
        Ap242SemanticSummary summary = Analyse(StepSample.WithData("""
            #1=DIMENSIONAL_SIZE(#7,'first');
            #2=DIMENSIONAL_SIZE(#8,'second');
            #3=MEASURE_WITH_UNIT(LENGTH_MEASURE(25.0),#9);
            """));

        Assert.Equal(2, summary.Dimensions.Count);
        Assert.All(summary.Dimensions, dimension => Assert.Equal(25.0, dimension.NominalValue));
    }

    // ── Datums ──────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractsDatum()
    {
        DatumSummary datum = Assert.Single(Analyse(StepSample.Ap242Pmi).Datums);

        Assert.Equal(4, datum.Id);
        Assert.Equal("A", datum.Label);
        Assert.Equal("primary datum", datum.Description);
        Assert.Equal(10, datum.TargetEntityId);
    }

    [Fact]
    public void DatumWithMissingParameters_YieldsNulls()
    {
        DatumSummary datum = Assert.Single(Analyse(StepSample.WithData("#1=DATUM();")).Datums);

        Assert.Null(datum.Label);
        Assert.Null(datum.Description);
        Assert.Null(datum.TargetEntityId);
    }

    [Fact]
    public void FileWithoutPmi_HasEmptyDimensionAndDatumLists()
    {
        Ap242SemanticSummary summary = Analyse(StepSample.Minimal);

        Assert.Empty(summary.Dimensions);
        Assert.Empty(summary.Datums);
        Assert.False(summary.HasPmi);
        Assert.False(summary.HasGdt);
    }
}
