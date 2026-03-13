using StepParser.Diagnostics;
using StepParser.Lexer;

namespace StepParser.Parser;

public sealed class StepFileParser
{
    private readonly IReadOnlyList<Token> _tokens;
    private readonly ICollection<ParseDiagnostic> _diagnostics;
    private int _position;

    public StepFileParser(IReadOnlyList<Token> tokens, ICollection<ParseDiagnostic> diagnostics)
    {
        _tokens = tokens;
        _diagnostics = diagnostics;
    }

    public StepFile Parse(string sourcePath)
    {
        Expect(TokenKind.IsoOpen, "Expected ISO-10303-21 opening marker.");
        ConsumeOptional(TokenKind.Semicolon);

        Expect(TokenKind.Header, "Expected HEADER section.");
        ConsumeOptional(TokenKind.Semicolon);
        HeaderSection header = ParseHeaderSection();
        Expect(TokenKind.EndSec, "Expected ENDSEC after HEADER.");
        ConsumeOptional(TokenKind.Semicolon);

        Expect(TokenKind.Data, "Expected DATA section.");
        ConsumeOptional(TokenKind.Semicolon);
        Dictionary<int, EntityInstance> entities = ParseDataSection();
        Expect(TokenKind.EndSec, "Expected ENDSEC after DATA.");
        ConsumeOptional(TokenKind.Semicolon);

        if (Match(TokenKind.Anchor) || Match(TokenKind.Reference) || Match(TokenKind.Signature))
        {
            SkipUnsupportedSections();
        }

        Expect(TokenKind.IsoClose, "Expected END-ISO-10303-21 closing marker.");
        ConsumeOptional(TokenKind.Semicolon);

        FileInfo fileInfo = new(sourcePath);
        HeaderSummary summary = HeaderSummary.FromSection(header);
        int? edition = DetectEdition(summary.ImplementationLevel);
        string? schema = summary.Schemas?.FirstOrDefault();
        return new StepFile(sourcePath, fileInfo.Exists ? fileInfo.Length : 0L, header, entities, edition, schema);
    }

    private HeaderSection ParseHeaderSection()
    {
        List<HeaderEntity> entities = new();
        while (!Match(TokenKind.EndSec) && !Match(TokenKind.EndOfFile))
        {
            Token nameToken = Expect(TokenKind.Keyword, "Expected HEADER entity name.");
            string entityName = CoerceName(nameToken, "HEADER entity name");
            IReadOnlyList<Parameter> parameters = ParseParameterList();
            ConsumeOptional(TokenKind.Semicolon);
            entities.Add(new HeaderEntity(entityName, parameters));
        }

        string[] required = { "FILE_DESCRIPTION", "FILE_NAME", "FILE_SCHEMA" };
        foreach (string requiredName in required)
        {
            if (entities.All(entity => entity.Name != requiredName))
            {
                _diagnostics.Add(new ParseDiagnostic(
                    DiagnosticSeverity.Error,
                    Current.Line,
                    Current.Column,
                    $"Missing required HEADER entity {requiredName}."));
            }
        }

        return new HeaderSection(entities);
    }

    private Dictionary<int, EntityInstance> ParseDataSection()
    {
        Dictionary<int, EntityInstance> entities = new();
        while (!Match(TokenKind.EndSec) && !Match(TokenKind.EndOfFile))
        {
            Token idToken = Expect(TokenKind.EntityRef, "Expected entity instance identifier.");
            if (!TryGetEntityId(idToken, out int id))
            {
                SkipToStatementBoundary();
                ConsumeOptional(TokenKind.Semicolon);
                continue;
            }

            Expect(TokenKind.Equals, "Expected '=' after entity identifier.");
            EntityInstance instance = ParseEntityInstance(id);
            ConsumeOptional(TokenKind.Semicolon);
            entities[instance.Id] = instance;
        }

        return entities;
    }

    private EntityInstance ParseEntityInstance(int id)
    {
        if (ConsumeOptional(TokenKind.LeftParen))
        {
            List<EntityComponent> components = new();
            while (!Match(TokenKind.RightParen) && !Match(TokenKind.EndOfFile))
            {
                Token componentName = Expect(TokenKind.Keyword, "Expected complex entity component.");
                IReadOnlyList<Parameter> parameters = ParseParameterList();
                components.Add(new EntityComponent(CoerceName(componentName, "complex entity component"), parameters));
            }

            Expect(TokenKind.RightParen, "Expected ')' after complex entity instance.");
            return new EntityInstance(id, null, Array.Empty<Parameter>(), components);
        }

        Token nameToken = Expect(TokenKind.Keyword, "Expected entity name.");
        IReadOnlyList<Parameter> entityParameters = ParseParameterList();
        return new EntityInstance(id, CoerceName(nameToken, "entity name"), entityParameters, null);
    }

    private IReadOnlyList<Parameter> ParseParameterList()
    {
        Expect(TokenKind.LeftParen, "Expected '(' to start parameter list.");
        List<Parameter> parameters = new();
        while (!Match(TokenKind.RightParen) && !Match(TokenKind.EndOfFile))
        {
            parameters.Add(ParseParameter());
            if (!ConsumeOptional(TokenKind.Comma))
            {
                break;
            }
        }

        Expect(TokenKind.RightParen, "Expected ')' to close parameter list.");
        return parameters;
    }

    private Parameter ParseParameter()
    {
        Token token = Current;
        switch (token.Kind)
        {
            case TokenKind.EntityRef:
                Advance();
                return new Parameter.EntityReference((int)token.Value!);
            case TokenKind.String:
                Advance();
                return new Parameter.StringValue((string)token.Value!);
            case TokenKind.Integer:
                Advance();
                return new Parameter.IntegerValue((long)token.Value!);
            case TokenKind.Real:
                Advance();
                return new Parameter.RealValue((double)token.Value!);
            case TokenKind.Binary:
                Advance();
                return new Parameter.BinaryValue((string)token.Value!);
            case TokenKind.Enumeration:
                Advance();
                return new Parameter.EnumValue((string)token.Value!);
            case TokenKind.Unset:
                Advance();
                return new Parameter.UnsetValue();
            case TokenKind.Inherited:
                Advance();
                return new Parameter.InheritedValue();
            case TokenKind.LeftParen:
                return new Parameter.ListValue(ParseParameterList());
            case TokenKind.Keyword:
            {
                string typeName = (string)token.Value!;
                Advance();
                if (Match(TokenKind.LeftParen))
                {
                    IReadOnlyList<Parameter> parameters = ParseParameterList();
                    if (parameters.Count == 1)
                    {
                        return new Parameter.TypedValue(typeName, parameters[0]);
                    }

                    return new Parameter.TypedValue(typeName, new Parameter.ListValue(parameters));
                }

                _diagnostics.Add(new ParseDiagnostic(
                    DiagnosticSeverity.Warning,
                    token.Line,
                    token.Column,
                    $"Bare keyword parameter '{token.Lexeme}' encountered."));
                return new Parameter.EnumValue(typeName);
            }
            default:
                _diagnostics.Add(new ParseDiagnostic(
                    DiagnosticSeverity.Error,
                    token.Line,
                    token.Column,
                    $"Unexpected token {token.Kind} in parameter list."));
                Advance();
                return new Parameter.UnsetValue();
        }
    }

    private bool TryGetEntityId(Token token, out int id)
    {
        if (token.Value is int entityId)
        {
            id = entityId;
            return true;
        }

        id = 0;
        _diagnostics.Add(new ParseDiagnostic(
            DiagnosticSeverity.Error,
            token.Line,
            token.Column,
            $"Invalid entity identifier token '{token.Lexeme}'."));
        return false;
    }

    private string CoerceName(Token token, string context)
    {
        if (token.Value is string text)
        {
            return text;
        }

        _diagnostics.Add(new ParseDiagnostic(
            DiagnosticSeverity.Error,
            token.Line,
            token.Column,
            $"Invalid {context} token '{token.Lexeme}'."));
        return token.Lexeme.ToUpperInvariant();
    }

    private void SkipToStatementBoundary()
    {
        while (!Match(TokenKind.Semicolon) && !Match(TokenKind.EndSec) && !Match(TokenKind.EndOfFile))
        {
            Advance();
        }
    }

    private void SkipUnsupportedSections()
    {
        while (!Match(TokenKind.IsoClose) && !Match(TokenKind.EndOfFile))
        {
            Advance();
        }
    }

    private int? DetectEdition(string? implementationLevel)
    {
        if (string.IsNullOrWhiteSpace(implementationLevel))
        {
            return null;
        }

        if (implementationLevel.StartsWith("3", StringComparison.Ordinal))
        {
            return 3;
        }

        if (implementationLevel.StartsWith("2", StringComparison.Ordinal))
        {
            return 2;
        }

        if (implementationLevel.StartsWith("1", StringComparison.Ordinal))
        {
            return 1;
        }

        return null;
    }

    private bool ConsumeOptional(TokenKind kind)
    {
        if (!Match(kind))
        {
            return false;
        }

        Advance();
        return true;
    }

    private Token Expect(TokenKind kind, string message)
    {
        Token token = Current;
        if (token.Kind == kind)
        {
            Advance();
            return token;
        }

        _diagnostics.Add(new ParseDiagnostic(
            DiagnosticSeverity.Error,
            token.Line,
            token.Column,
            message));
        return token;
    }

    private bool Match(TokenKind kind)
    {
        return Current.Kind == kind;
    }

    private Token Current => _position < _tokens.Count ? _tokens[_position] : _tokens[^1];

    private void Advance()
    {
        if (_position < _tokens.Count - 1)
        {
            _position++;
        }
    }
}
