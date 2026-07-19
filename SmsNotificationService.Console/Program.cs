using SmsNotificationService.Shared;
using SmsNotificationService.Shared.Models;

AppLogger.Initialize("ConsoleApp");
AppLogger.Info("App", $"SmsNotificationService Console Monitor starting (v{VersionHelper.GetCurrentVersion()})");
AppLogger.Info("App", $"OS: {Environment.OSVersion}, .NET: {Environment.Version}");

Console.WriteLine($"SmsNotificationService Console Monitor v{VersionHelper.GetCurrentVersion()}");
Console.WriteLine($"OS: {Environment.OSVersion}, .NET: {Environment.Version}");
Console.WriteLine("Monitoring service... (Ctrl+C to exit)\n");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    AppLogger.Info("App", "Ctrl+C received, shutting down");
    cts.Cancel();
};

using var monitor = new ServiceMonitor();
var lastStatus = (System.ServiceProcess.ServiceControllerStatus)(-1);

monitor.StatusChanged += info =>
{
    var statusStr = StatusHelper.FormatStatus(info.Status);
    var uptimeStr = info.Uptime > TimeSpan.Zero ? $" | Uptime: {StatusHelper.FormatUptime(info.Uptime)}" : "";
    var methodStr = $" (via {StatusHelper.FormatDetection(info.DetectionMethod)})";

    var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {statusStr}{methodStr}{uptimeStr} | Version: {info.Version}";
    Console.WriteLine(line);

    if (info.Status != lastStatus)
    {
        AppLogger.Info("Monitor", $"Status changed: {statusStr} (via {info.DetectionMethod})");
        lastStatus = info.Status;
    }
};

_ = monitor.StartAsync();

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    // Expected
}

AppLogger.Info("App", "Console monitor stopped");
AppLogger.Dispose();
Console.WriteLine("\nStopped.");
