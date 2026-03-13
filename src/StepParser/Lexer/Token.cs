namespace StepParser.Lexer;

public sealed record Token(TokenKind Kind, string Lexeme, object? Value, int Line, int Column);
