namespace StepParser.Parser;

public abstract record Parameter
{
    public sealed record EntityReference(int Id) : Parameter;

    public sealed record StringValue(string Value) : Parameter;

    public sealed record IntegerValue(long Value) : Parameter;

    public sealed record RealValue(double Value) : Parameter;

    public sealed record BinaryValue(string Value) : Parameter;

    public sealed record EnumValue(string Name) : Parameter;

    public sealed record ListValue(IReadOnlyList<Parameter> Items) : Parameter;

    public sealed record UnsetValue : Parameter;

    public sealed record InheritedValue : Parameter;

    public sealed record TypedValue(string TypeName, Parameter Inner) : Parameter;
}
