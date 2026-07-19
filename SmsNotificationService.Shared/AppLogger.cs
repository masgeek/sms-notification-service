using System.IO;

namespace SmsNotificationService.Shared;

public sealed class AppLogger
{
    private static readonly Lock _lock = new();
    private static AppLogger? _instance;

    private readonly string _logDirectory;
    private readonly string _filePath;
    private StreamWriter? _writer;

    private AppLogger(string logDirectory, string appName)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(_logDirectory);
        CleanupOldLogs();

        var today = DateTime.Now.ToString("yyyy-MM-dd");
        _filePath = Path.Combine(_logDirectory, $"{today}_{appName}.log");
        _writer = new StreamWriter(new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
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
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var line = $"[{timestamp}] [{level}] [{tag}] {message}";

                if (ex is not null)
                    line += Environment.NewLine + ex;

                _instance?._writer?.WriteLine(line);
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

    private void CleanupOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-7);
            foreach (var file in Directory.GetFiles(_logDirectory, "*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch
        {
            // Best effort
        }
    }

    public static void Dispose()
    {
        lock (_lock)
        {
            _instance?._writer?.Flush();
            _instance?._writer?.Dispose();
            _instance?._writer = null;
        }
    }
}
