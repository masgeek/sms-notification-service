using System.IO;
using SmsNotificationService.Shared;

namespace SmsNotificationService.Tray;

internal sealed class TrayLogger
{
    private static readonly Lock _lock = new();
    private static TrayLogger? _instance;

    private readonly string _logDirectory;
    private readonly string _filePath;
    private StreamWriter? _writer;

    private TrayLogger(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(_logDirectory);
        CleanupOldLogs();

        var today = DateTime.Now.ToString("yyyy-MM-dd");
        _filePath = Path.Combine(_logDirectory, $"{today}_TrayApp.log");
        _writer = new StreamWriter(new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
    }

    public static TrayLogger Instance
    {
        get
        {
            if (_instance is null)
            {
                var logDir = ConfigPathResolver.GetLogDir();
                _instance = new TrayLogger(logDir);
            }
            return _instance;
        }
    }

    public static void Log(string level, string message, Exception? ex = null)
    {
        try
        {
            lock (_lock)
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var line = $"[{timestamp}] [{level}] [TrayApp] {message}";

                if (ex is not null)
                    line += Environment.NewLine + ex;

                _instance?._writer?.WriteLine(line);
            }
        }
        catch
        {
            // Best effort — don't crash over logging
        }
    }

    public static void Info(string message) => Log("INFO", message);
    public static void Warn(string message) => Log("WARN", message);
    public static void Error(string message, Exception? ex = null) => Log("ERROR", message, ex);

    private void CleanupOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-7);
            foreach (var file in Directory.GetFiles(_logDirectory, "*_TrayApp.log"))
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
