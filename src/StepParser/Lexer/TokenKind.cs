namespace StepParser.Lexer;

public enum TokenKind
{
    IsoOpen,
    IsoClose,
    Header,
    EndSec,
    Data,
    Anchor,
    Reference,
    Signature,
    Equals,
    Semicolon,
    Comma,
    LeftParen,
    RightParen,
    EntityRef,
    Keyword,
    String,
    Integer,
    Real,
    Binary,
    Enumeration,
    Unset,
    Inherited,
    EndOfFile,
    Unknown
}
