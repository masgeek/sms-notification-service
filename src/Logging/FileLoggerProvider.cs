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
    private readonly int _retentionDays;
    private readonly long _maxFileSizeBytes;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public FileLoggerProvider(string logDirectory, int retentionDays, long maxFileSizeMb)
    {
        _logDirectory = logDirectory;
        _retentionDays = retentionDays;
        _maxFileSizeBytes = maxFileSizeMb * 1024 * 1024;
        Directory.CreateDirectory(_logDirectory);
        CleanupOldLogs();
    }

    private void CleanupOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-_retentionDays);
            foreach (var file in Directory.GetFiles(_logDirectory, "*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Best effort — don't crash the service over log cleanup
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        var safeName = categoryName.Replace('.', '_');
        return _loggers.GetOrAdd(safeName, name => new FileLogger(_logDirectory, name, _maxFileSizeBytes));
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
    private readonly string _logDirectory;
    private readonly string _categoryName;
    private readonly long _maxFileSizeBytes;
    private readonly Lock _lock = new();
    private string _filePath;
    private StreamWriter _writer;
    private long _currentFileSize;
    private int _linesSinceLastCheck;

    private const int CheckInterval = 100;

    public FileLogger(string logDirectory, string categoryName, long maxFileSizeBytes)
    {
        _logDirectory = logDirectory;
        _categoryName = categoryName;
        _maxFileSizeBytes = maxFileSizeBytes;
        _filePath = GetFilePath();
        _writer = CreateWriter();
        _currentFileSize = new FileInfo(_filePath).Length;
    }

    private string GetFilePath()
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        return Path.Combine(_logDirectory, $"{today}_{_categoryName}.log");
    }

    private StreamWriter CreateWriter()
    {
        var stream = new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
        return new StreamWriter(stream) { AutoFlush = true };
    }

    private void RotateIfNeeded()
    {
        _linesSinceLastCheck++;
        if (_linesSinceLastCheck < CheckInterval) return;
        _linesSinceLastCheck = 0;

        _currentFileSize = new FileInfo(_filePath).Length;
        if (_currentFileSize < _maxFileSizeBytes) return;

        _writer.Flush();
        _writer.Dispose();

        var timestamp = DateTime.Now.ToString("HHmmss");
        var rotatedPath = Path.Combine(_logDirectory, $"{DateTime.Now:yyyy-MM-dd}_{_categoryName}_{timestamp}.log");
        File.Move(_filePath, rotatedPath);

        _filePath = GetFilePath();
        _writer = CreateWriter();
        _currentFileSize = 0;
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
            RotateIfNeeded();
            _writer.WriteLine(line);
        }
    }

    public void Dispose()
    {
        _writer.Flush();
        _writer.Dispose();
    }
}
