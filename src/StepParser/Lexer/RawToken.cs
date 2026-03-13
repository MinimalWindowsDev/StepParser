namespace StepParser.Lexer;

public sealed record RawToken(string Lexeme, int Line, int Column);
