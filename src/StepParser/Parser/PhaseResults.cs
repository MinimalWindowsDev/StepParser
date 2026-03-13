using StepParser.Diagnostics;
using StepParser.Lexer;

namespace StepParser.Parser;

public sealed record TokenizationResult(
    string Source,
    IReadOnlyList<RawToken> RawTokens,
    IReadOnlyList<ParseDiagnostic> Diagnostics,
    TimeSpan Elapsed);

public sealed record LexingResult(
    string Source,
    IReadOnlyList<Token> Tokens,
    IReadOnlyList<ParseDiagnostic> Diagnostics,
    TimeSpan Elapsed);
