using System.Globalization;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Compact;

namespace FeeSyncer.Shared.Logging;

public static class SerilogLogging
{
    public static Serilog.ILogger Configure(
        ILoggingBuilder logging,
        string application,
        string environment,
        string logDirectory,
        int retentionDays,
        long maxFileSizeMb,
        LogLevel minimumLevel = LogLevel.Information,
        bool writeToConsole = true)
    {
        var logger = CreateLogger(
            application,
            environment,
            logDirectory,
            retentionDays,
            maxFileSizeMb,
            minimumLevel,
            writeToConsole);

        Log.Logger = logger;
        logging.ClearProviders();
        logging.AddSerilog(logger, dispose: true);
        return logger;
    }

    public static Serilog.ILogger CreateLogger(
        string application,
        string environment,
        string logDirectory,
        int retentionDays,
        long maxFileSizeMb,
        LogLevel minimumLevel = LogLevel.Information,
        bool writeToConsole = false)
    {
        Directory.CreateDirectory(logDirectory);
        var formatter = new CompactJsonFormatter();
        var configuration = new LoggerConfiguration()
            .MinimumLevel.Is(ToSerilogLevel(minimumLevel))
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", application)
            .Enrich.WithProperty("Environment", environment)
            .WriteTo.File(
                formatter,
                Path.Combine(logDirectory, $"{application}-.json"),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: Math.Clamp(maxFileSizeMb, 1, 1024) * 1024 * 1024,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: null,
                retainedFileTimeLimit: TimeSpan.FromDays(Math.Clamp(retentionDays, 1, 365)),
                shared: true,
                flushToDiskInterval: TimeSpan.FromSeconds(1));

        if (writeToConsole)
        {
            configuration.WriteTo.Console(new LongLevelConsoleFormatter(!Console.IsOutputRedirected));
        }

        return configuration.CreateLogger();
    }

    public static LogLevel GetMinimumLevel(string? configured)
    {
        return Enum.TryParse<LogLevel>(configured, ignoreCase: true, out var level)
            ? level
            : LogLevel.Information;
    }

    private static LogEventLevel ToSerilogLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => LogEventLevel.Verbose,
        LogLevel.Debug => LogEventLevel.Debug,
        LogLevel.Information => LogEventLevel.Information,
        LogLevel.Warning => LogEventLevel.Warning,
        LogLevel.Error => LogEventLevel.Error,
        LogLevel.Critical => LogEventLevel.Fatal,
        _ => LogEventLevel.Information,
    };
}

internal sealed class LongLevelConsoleFormatter(bool useColor) : ITextFormatter
{
    public void Format(LogEvent logEvent, TextWriter output)
    {
        var name = logEvent.Level switch
        {
            LogEventLevel.Verbose => "VERBOSE",
            LogEventLevel.Debug => "DEBUG",
            LogEventLevel.Information => "INFO",
            LogEventLevel.Warning => "WARNING",
            LogEventLevel.Error => "ERROR",
            LogEventLevel.Fatal => "CRITICAL",
            _ => logEvent.Level.ToString().ToUpperInvariant(),
        };
        var color = logEvent.Level switch
        {
            LogEventLevel.Verbose => "\u001b[90m",
            LogEventLevel.Debug => "\u001b[36m",
            LogEventLevel.Information => "\u001b[32m",
            LogEventLevel.Warning => "\u001b[33m",
            LogEventLevel.Error => "\u001b[31m",
            LogEventLevel.Fatal => "\u001b[91m",
            _ => string.Empty,
        };
        var source = logEvent.Properties.TryGetValue("SourceContext", out var sourceValue)
            ? (sourceValue as ScalarValue)?.Value?.ToString() ?? sourceValue.ToString()
            : "FeeSyncer";
        source = source[(source.LastIndexOf('.') + 1)..];

        output.Write($"[{logEvent.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm:ss}] [");
        if (useColor)
        {
            output.Write(color);
        }
        output.Write(name);
        if (useColor)
        {
            output.Write("\u001b[0m");
        }
        output.Write($"] [{source}] ");
        output.WriteLine(logEvent.RenderMessage(CultureInfo.InvariantCulture));
        if (logEvent.Exception is not null)
        {
            output.WriteLine(logEvent.Exception);
        }
    }
}
