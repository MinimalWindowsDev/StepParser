using Serilog;
using Serilog.Events;

namespace StepParser.Logging;

/// <summary>
/// Central logger for StepParser. Must be configured via <see cref="Configure"/> before use.
/// All methods are no-ops when logging is disabled so call sites need no guards.
/// </summary>
public static class StepLogger
{
    private static ILogger? _instance;
    private static bool _enabled;

    public static bool IsEnabled => _enabled;

    /// <summary>
    /// Configure the logger. Must be called before any parsing begins.
    /// </summary>
    /// <param name="enabled">Whether logging is active at all.</param>
    /// <param name="logFilePath">
    /// Absolute path for the log file. When null and <paramref name="enabled"/> is true,
    /// only console output is produced.
    /// </param>
    public static void Configure(bool enabled, string? logFilePath)
    {
        // Dispose any previous logger first: Serilog's file sink holds an exclusive handle on the
        // log file until it is disposed, so reconfiguring without this leaks both the handle and
        // any buffered lines that have not reached disk yet.
        (_instance as IDisposable)?.Dispose();
        _instance = null;

        _enabled = enabled;
        if (!enabled) return;

        var config = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                restrictedToMinimumLevel: LogEventLevel.Verbose);

        if (logFilePath is not null)
        {
            config = config.WriteTo.File(
                logFilePath,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                flushToDiskInterval: TimeSpan.FromSeconds(1),
                restrictedToMinimumLevel: LogEventLevel.Verbose);
        }

        _instance = config.CreateLogger();
    }

    /// <summary>
    /// Flush buffered log events and release the log file. The file sink batches writes, so a
    /// process that exits without calling this can lose its last lines.
    /// </summary>
    public static void CloseAndFlush() => Configure(false, null);

    // ── Structured log methods ──────────────────────────────────────────────

    public static void Verbose(string messageTemplate, params object?[] args) =>
        _instance?.Verbose(messageTemplate, args);

    public static void Debug(string messageTemplate, params object?[] args) =>
        _instance?.Debug(messageTemplate, args);

    public static void Info(string messageTemplate, params object?[] args) =>
        _instance?.Information(messageTemplate, args);

    public static void Warning(string messageTemplate, params object?[] args) =>
        _instance?.Warning(messageTemplate, args);

    public static void Error(string messageTemplate, params object?[] args) =>
        _instance?.Error(messageTemplate, args);

    public static void Error(Exception ex, string messageTemplate, params object?[] args) =>
        _instance?.Error(ex, messageTemplate, args);

    // ── AOP helpers called by LogAttribute ─────────────────────────────────

    internal static void Entry(string cls, string method, string parameters) =>
        _instance?.Verbose("→ {Class}.{Method}({Parameters})", cls, method, parameters);

    internal static void Exit(string cls, string method, string result, long elapsedMs) =>
        _instance?.Verbose("← {Class}.{Method} → {Result} ({ElapsedMs}ms)", cls, method, result, elapsedMs);

    internal static void ExceptionIn(string cls, string method, Exception ex) =>
        _instance?.Error(ex, "✗ {Class}.{Method} threw {ExceptionType}: {Message}",
            cls, method, ex.GetType().Name, ex.Message);
}
