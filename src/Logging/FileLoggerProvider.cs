using System.Collections.Concurrent;

namespace SmsNotificationService.Logging;

/// <summary>
/// Minimal file logging provider for Windows services.
/// Writes log files to ProgramData\Munywele\SmsNotificationService\logs\
/// with daily rotation (one file per day).
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public FileLoggerProvider(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(_logDirectory);
    }

    public ILogger CreateLogger(string categoryName)
    {
        var safeName = categoryName.Replace('.', '_');
        return _loggers.GetOrAdd(safeName, name => new FileLogger(_logDirectory, name));
    }

    public void Dispose()
    {
        foreach (var logger in _loggers.Values)
            logger.Dispose();
        _loggers.Clear();
    }
}

internal sealed class FileLogger : ILogger, IDisposable
{
    private readonly string _filePath;
    private readonly string _categoryName;
    private readonly StreamWriter _writer;
    private readonly object _lock = new();

    public FileLogger(string logDirectory, string categoryName)
    {
        _categoryName = categoryName;
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var fileName = $"{today}_{categoryName}.log";
        _filePath = Path.Combine(logDirectory, fileName);

        var stream = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream) { AutoFlush = true };
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var level = logLevel.ToString().ToUpperInvariant();
        var line = $"[{timestamp}] [{level}] [{_categoryName}] {message}";

        if (exception != null)
            line += Environment.NewLine + exception;

        lock (_lock)
        {
            _writer.WriteLine(line);
        }
    }

    public void Dispose()
    {
        _writer?.Flush();
        _writer?.Dispose();
    }
}
