using FeeSyncer.Shared.Logging;
using Serilog;
using Serilog.Events;

namespace FeeSyncer.Shared;

public sealed class AppLogger
{
    private static readonly Lock _lock = new();
    private static AppLogger? _instance;

    private Serilog.ILogger? logger;

    private AppLogger(string logDirectory, string appName)
    {
        logger = SerilogLogging.CreateLogger(
            appName,
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production",
            logDirectory,
            retentionDays: 7,
            maxFileSizeMb: 10,
            writeToConsole: appName.Equals("ConsoleApp", StringComparison.Ordinal));
    }

    public static void Initialize(string appName)
    {
        if (_instance is not null) return;
        var logDir = ConfigPathResolver.GetLogDir();
        _instance = new AppLogger(logDir, appName);
    }

    public static void Log(string level, string tag, string message, Exception? ex = null)
    {
        try
        {
            lock (_lock)
            {
                _instance?.logger?
                    .ForContext("SourceContext", tag)
                    .Write(ToSerilogLevel(level), ex, "{Message}", message);
            }
        }
        catch
        {
            // Best effort
        }
    }

    public static void Info(string tag, string message) => Log("INFO", tag, message);
    public static void Warn(string tag, string message) => Log("WARN", tag, message);
    public static void Error(string tag, string message, Exception? ex = null) => Log("ERROR", tag, message, ex);

    public static void Dispose()
    {
        lock (_lock)
        {
            (_instance?.logger as IDisposable)?.Dispose();
            if (_instance is not null)
            {
                _instance.logger = null;
            }
            _instance = null;
        }
    }

    private static LogEventLevel ToSerilogLevel(string level) => level.ToUpperInvariant() switch
    {
        "TRACE" or "VERBOSE" => LogEventLevel.Verbose,
        "DEBUG" => LogEventLevel.Debug,
        "WARNING" or "WARN" => LogEventLevel.Warning,
        "ERROR" => LogEventLevel.Error,
        "CRITICAL" or "FATAL" => LogEventLevel.Fatal,
        _ => LogEventLevel.Information,
    };
}
