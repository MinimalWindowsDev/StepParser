namespace StepParser.Diagnostics;

public sealed record ParseDiagnostic(
    DiagnosticSeverity Severity,
    int Line,
    int Column,
    string Message);
