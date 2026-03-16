using MethodBoundaryAspect.Fody.Attributes;
using StepParser.Logging;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace StepParser.Logging;

/// <summary>
/// AOP logging attribute powered by MethodBoundaryAspect.Fody.
/// When applied to a method, Fody weaves calls to OnEntry/OnExit/OnException at compile-time.
/// All behavior is gated on <see cref="StepLogger.IsEnabled"/> so there is zero runtime cost
/// when logging is disabled.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor, Inherited = false)]
public sealed class LogAttribute : OnMethodBoundaryAspect
{
    // One Stopwatch per aspect instance (= per decorated call site). Restarted on each entry.
    private readonly Stopwatch _stopwatch = new();

    public override void OnEntry(MethodExecutionArgs args)
    {
        if (!StepLogger.IsEnabled) return;
        _stopwatch.Restart();
        StepLogger.Entry(
            args.Method.DeclaringType?.Name ?? "?",
            args.Method.Name,
            FormatArgs(args.Method, args.Arguments));
    }

    public override void OnExit(MethodExecutionArgs args)
    {
        if (!StepLogger.IsEnabled) return;
        _stopwatch.Stop();
        StepLogger.Exit(
            args.Method.DeclaringType?.Name ?? "?",
            args.Method.Name,
            RenderValue(args.ReturnValue),
            _stopwatch.ElapsedMilliseconds);
    }

    public override void OnException(MethodExecutionArgs args)
    {
        if (!StepLogger.IsEnabled) return;
        StepLogger.ExceptionIn(
            args.Method.DeclaringType?.Name ?? "?",
            args.Method.Name,
            args.Exception);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static string FormatArgs(MethodBase method, object[] arguments)
    {
        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Length == 0) return string.Empty;

        StringBuilder sb = new();
        for (int i = 0; i < parameters.Length && i < arguments.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(parameters[i].Name).Append(": ").Append(RenderValue(arguments[i]));
        }

        return sb.ToString();
    }

    private static string RenderValue(object? value) => value switch
    {
        null => "null",
        string s when s.Length > 120 => $"\"{s[..120]}…\"",
        System.Collections.ICollection c => $"[{c.Count} items]",
        _ => value.ToString() ?? "?"
    };
}
