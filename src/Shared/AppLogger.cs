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

    private static void Log(LogEventLevel level, string tag, string message, Exception? ex = null)
    {
        try
        {
            lock (_lock)
            {
                _instance?.logger?
                    .ForContext("SourceContext", tag)
                    .Write(level, ex, "{Message}", message);
            }
        }
        catch
        {
            // Best effort
        }
    }

    public static void Info(string tag, string message) => Log(LogEventLevel.Information, tag, message);
    public static void Warn(string tag, string message) => Log(LogEventLevel.Warning, tag, message);
    public static void Error(string tag, string message, Exception? ex = null) => Log(LogEventLevel.Error, tag, message, ex);

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
}
