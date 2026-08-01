using StepParser.Diagnostics;
using StepParser.Parser;

namespace StepParser.Tests;

public sealed class ParserTests
{
    [Fact]
    public void ParsesMinimalSample()
    {
        (StepFile file, List<ParseDiagnostic> diagnostics) = Sut.Parse(StepSample.Minimal);

        Assert.Equal(11, file.Data.Count);
        Assert.Equal("AUTOMOTIVE_DESIGN { 1 0 10303 214 2 1 1}", file.FileSchema);
        Assert.Equal("PRODUCT", file.Data[16].Name);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void RecordsFileSizeFromDisk()
    {
        (StepFile file, _) = Sut.Parse(StepSample.Minimal);

        Assert.True(file.FileSize > 0);
    }

    // ── Parameter variants ──────────────────────────────────────────────────

    [Fact]
    public void ParsesEveryParameterVariant()
    {
        (StepFile file, List<ParseDiagnostic> diagnostics) = Sut.Parse(StepSample.AllParameterKinds);

        IReadOnlyList<Parameter> parameters = file.Data[1].Parameters;
        Assert.Equal("text", Assert.IsType<Parameter.StringValue>(parameters[0]).Value);
        Assert.Equal(42L, Assert.IsType<Parameter.IntegerValue>(parameters[1]).Value);
        Assert.Equal(3.5, Assert.IsType<Parameter.RealValue>(parameters[2]).Value);
        Assert.Equal(-1.25E-3, Assert.IsType<Parameter.RealValue>(parameters[3]).Value, 10);
        Assert.Equal("ENUM", Assert.IsType<Parameter.EnumValue>(parameters[4]).Name);
        Assert.Equal("00FF", Assert.IsType<Parameter.BinaryValue>(parameters[5]).Value);
        Assert.IsType<Parameter.UnsetValue>(parameters[6]);
        Assert.IsType<Parameter.InheritedValue>(parameters[7]);
        Assert.Equal(2, Assert.IsType<Parameter.EntityReference>(parameters[8]).Id);
        Assert.Equal(2, Assert.IsType<Parameter.ListValue>(parameters[9]).Items.Count);

        Parameter.TypedValue typed = Assert.IsType<Parameter.TypedValue>(parameters[10]);
        Assert.Equal("LENGTH_MEASURE", typed.TypeName);
        Assert.Equal(9.5, Assert.IsType<Parameter.RealValue>(typed.Inner).Value);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void TypedValueWithMultipleArguments_WrapsThemInAList()
    {
        (StepFile file, _) = Sut.Parse(StepSample.WithData("#1=A(PAIR(1,2));"));

        Parameter.TypedValue typed = Assert.IsType<Parameter.TypedValue>(file.Data[1].Parameters[0]);
        Assert.Equal(2, Assert.IsType<Parameter.ListValue>(typed.Inner).Items.Count);
    }

    [Fact]
    public void EmptyParameterList_IsAllowed()
    {
        (StepFile file, List<ParseDiagnostic> diagnostics) = Sut.Parse(StepSample.WithData("#1=EMPTY();"));

        Assert.Empty(file.Data[1].Parameters);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void NestedLists_ParseRecursively()
    {
        (StepFile file, _) = Sut.Parse(StepSample.WithData("#1=NESTED(((1,2),(3)));"));

        Parameter.ListValue outer = Assert.IsType<Parameter.ListValue>(file.Data[1].Parameters[0]);
        Parameter.ListValue inner = Assert.IsType<Parameter.ListValue>(outer.Items[0]);
        Assert.Equal(2, inner.Items.Count);
    }

    [Fact]
    public void BareKeywordParameter_WarnsAndBecomesEnum()
    {
        (StepFile file, List<ParseDiagnostic> diagnostics) = Sut.Parse(StepSample.WithData("#1=A(BARE);"));

        Assert.Equal("BARE", Assert.IsType<Parameter.EnumValue>(file.Data[1].Parameters[0]).Name);
        Assert.Contains(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Warning && d.Message.Contains("Bare keyword parameter"));
    }

    [Fact]
    public void UnexpectedTokenInParameterList_ErrorsAndYieldsUnset()
    {
        (StepFile file, List<ParseDiagnostic> diagnostics) = Sut.Parse(StepSample.WithData("#1=A(=);"));

        Assert.IsType<Parameter.UnsetValue>(file.Data[1].Parameters[0]);
        Assert.Contains(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Unexpected token"));
    }

    // ── Entity instances ────────────────────────────────────────────────────

    [Fact]
    public void ParsesConformantComplexInstance()
    {
        (StepFile file, List<ParseDiagnostic> diagnostics) =
            Sut.Parse(StepSample.WithData("#1=(LENGTH_UNIT()NAMED_UNIT(*)SI_UNIT(.MILLI.,.METRE.));"));

        Assert.True(file.Data[1].IsComplex);
        Assert.Equal(3, file.Data[1].Components!.Count);
        Assert.Null(file.Data[1].Name);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void DuplicateEntityId_LastOneWins()
    {
        (StepFile file, _) = Sut.Parse(StepSample.WithData("#1=FIRST();\n#1=SECOND();"));

        Assert.Single(file.Data);
        Assert.Equal("SECOND", file.Data[1].Name);
    }

    [Fact]
    public void InvalidEntityIdentifier_ErrorsAndSkipsToNextStatement()
    {
        (StepFile file, List<ParseDiagnostic> diagnostics) =
            Sut.Parse(StepSample.WithData("#=BROKEN();\n#2=GOOD();"));

        Assert.Contains(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.True(file.Data.ContainsKey(2));
        Assert.Equal("GOOD", file.Data[2].Name);
    }

    [Fact]
    public void SimpleEntity_HasNoComponents()
    {
        (StepFile file, _) = Sut.Parse(StepSample.WithData("#1=SIMPLE('x');"));

        Assert.False(file.Data[1].IsComplex);
        Assert.Null(file.Data[1].Components);
        Assert.Equal("SIMPLE", file.Data[1].Name);
    }

    // ── Structural validation ───────────────────────────────────────────────

    [Fact]
    public void MissingIsoOpenMarker_Errors()
    {
        (_, List<ParseDiagnostic> diagnostics) = Sut.Parse("HEADER;ENDSEC;DATA;ENDSEC;END-ISO-10303-21;");

        Assert.Contains(diagnostics, d => d.Message.Contains("ISO-10303-21 opening marker"));
    }

    [Fact]
    public void MissingIsoCloseMarker_Errors()
    {
        (_, List<ParseDiagnostic> diagnostics) = Sut.Parse(
            "ISO-10303-21;HEADER;FILE_DESCRIPTION(('t'),'2;1');FILE_NAME('f','2024',('a'),('b'),' ','s',' ');FILE_SCHEMA(('T'));ENDSEC;DATA;ENDSEC;");

        Assert.Contains(diagnostics, d => d.Message.Contains("END-ISO-10303-21"));
    }

    [Theory]
    [InlineData("FILE_DESCRIPTION")]
    [InlineData("FILE_NAME")]
    [InlineData("FILE_SCHEMA")]
    public void MissingRequiredHeaderEntity_Errors(string missing)
    {
        string header = string.Join("\n", new[]
            {
                "FILE_DESCRIPTION(('t'),'2;1');",
                "FILE_NAME('f','2024',('a'),('b'),' ','s',' ');",
                "FILE_SCHEMA(('T'));"
            }.Where(line => !line.StartsWith(missing, StringComparison.Ordinal)));

        (_, List<ParseDiagnostic> diagnostics) = Sut.Parse(
            $"ISO-10303-21;\nHEADER;\n{header}\nENDSEC;\nDATA;\nENDSEC;\nEND-ISO-10303-21;");

        Assert.Contains(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains($"Missing required HEADER entity {missing}"));
    }

    [Theory]
    [InlineData("ANCHOR")]
    [InlineData("REFERENCE")]
    [InlineData("SIGNATURE")]
    public void SkipsUnsupportedTrailingSections(string section)
    {
        string source = $"""
            ISO-10303-21;
            HEADER;
            FILE_DESCRIPTION(('t'),'2;1');
            FILE_NAME('f','2024',('a'),('b'),' ','s',' ');
            FILE_SCHEMA(('T'));
            ENDSEC;
            DATA;
            #1=A();
            ENDSEC;
            {section};
            <anything at all here>;
            ENDSEC;
            END-ISO-10303-21;
            """;

        (StepFile file, List<ParseDiagnostic> diagnostics) = Sut.Parse(source);

        Assert.Single(file.Data);
        Assert.DoesNotContain(diagnostics, d =>
            d.Severity == DiagnosticSeverity.Error && d.Message.Contains("END-ISO-10303-21"));
    }

    // ── Edition detection ───────────────────────────────────────────────────

    [Theory]
    [InlineData("1;1", 1)]
    [InlineData("2;1", 2)]
    [InlineData("3;1", 3)]
    public void DetectsEditionFromImplementationLevel(string implementationLevel, int expected)
    {
        (StepFile file, _) = Sut.Parse($"""
            ISO-10303-21;
            HEADER;
            FILE_DESCRIPTION(('t'),'{implementationLevel}');
            FILE_NAME('f','2024',('a'),('b'),' ','s',' ');
            FILE_SCHEMA(('T'));
            ENDSEC;
            DATA;
            ENDSEC;
            END-ISO-10303-21;
            """);

        Assert.Equal(expected, file.DetectedEdition);
    }

    [Fact]
    public void UnrecognisedImplementationLevel_LeavesEditionNull()
    {
        (StepFile file, _) = Sut.Parse("""
            ISO-10303-21;
            HEADER;
            FILE_DESCRIPTION(('t'),'x');
            FILE_NAME('f','2024',('a'),('b'),' ','s',' ');
            FILE_SCHEMA(('T'));
            ENDSEC;
            DATA;
            ENDSEC;
            END-ISO-10303-21;
            """);

        Assert.Null(file.DetectedEdition);
    }

    [Fact]
    public void NonStringImplementationLevel_LeavesEditionNull()
    {
        (StepFile file, _) = Sut.Parse("""
            ISO-10303-21;
            HEADER;
            FILE_DESCRIPTION(('t'),$);
            FILE_NAME('f','2024',('a'),('b'),' ','s',' ');
            FILE_SCHEMA(('T'));
            ENDSEC;
            DATA;
            ENDSEC;
            END-ISO-10303-21;
            """);

        Assert.Null(file.DetectedEdition);
    }
}
