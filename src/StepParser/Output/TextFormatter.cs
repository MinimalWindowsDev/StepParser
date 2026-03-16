using System.Text;
using StepParser.Diagnostics;
using StepParser.Parser;

namespace StepParser.Output;

public sealed class TextFormatter : IOutputFormatter
{
    private readonly bool _noColor;

    public TextFormatter(bool noColor)
    {
        _noColor = noColor;
    }

    public string Format(object payload)
    {
        return payload switch
        {
            TokenizationResult tokenization => FormatTokens(tokenization.RawTokens.Select(token => $"{token.Line}:{token.Column} {token.Lexeme}")),
            LexingResult lexing => FormatTokens(lexing.Tokens.Select(token => $"{token.Line}:{token.Column} {token.Kind} {token.Lexeme}")),
            ParseResult parse => FormatParse(parse),
            _ => payload.ToString() ?? string.Empty
        };
    }

    private static string FormatTokens(IEnumerable<string> tokens)
    {
        return string.Join(Environment.NewLine, tokens);
    }

    private string FormatParse(ParseResult parse)
    {
        StringBuilder builder = new();
        builder.AppendLine($"Source: {parse.Source}");
        builder.AppendLine($"Schema: {parse.Schema ?? "<unknown>"}");
        builder.AppendLine($"Edition: {parse.Edition?.ToString() ?? "<unknown>"}");
        builder.AppendLine($"Entities: {parse.Stats.EntityCount}");

        int errorCount = parse.Diagnostics.Count(d => d.Severity == Diagnostics.DiagnosticSeverity.Error);
        int warnCount = parse.Diagnostics.Count(d => d.Severity == Diagnostics.DiagnosticSeverity.Warning);
        if (errorCount > 0 && warnCount > 0)
        {
            builder.AppendLine($"Errors: {errorCount} | Warnings: {warnCount}");
        }
        else if (errorCount > 0)
        {
            builder.AppendLine($"Errors: {errorCount}");
        }
        else if (warnCount > 0)
        {
            builder.AppendLine($"Warnings: {warnCount}");
        }
        else
        {
            builder.AppendLine("Diagnostics: 0");
        }

        foreach (ParseDiagnostic diagnostic in parse.Diagnostics)
        {
            builder.AppendLine(
                $"  {SeverityPrefix(diagnostic.Severity)} {diagnostic.Line}:{diagnostic.Column} {diagnostic.Message}");
        }

        if (parse.Stats.EntityTypes.Count > 0)
        {
            builder.AppendLine("Entity types:");
            foreach (KeyValuePair<string, int> pair in parse.Stats.EntityTypes
                         .OrderByDescending(pair => pair.Value)
                         .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                         .Take(20))
            {
                builder.AppendLine($"  {pair.Key}: {pair.Value}");
            }

            if (parse.Stats.EntityTypes.Count > 20)
            {
                builder.AppendLine($"  … and {parse.Stats.EntityTypes.Count - 20} more type(s)");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private string SeverityPrefix(Diagnostics.DiagnosticSeverity severity)
    {
        string text = severity.ToString().ToUpperInvariant();
        if (_noColor)
        {
            return text;
        }

        return severity switch
        {
            Diagnostics.DiagnosticSeverity.Error => $"\u001b[31m{text}\u001b[0m",
            Diagnostics.DiagnosticSeverity.Warning => $"\u001b[33m{text}\u001b[0m",
            _ => text
        };
    }
}
