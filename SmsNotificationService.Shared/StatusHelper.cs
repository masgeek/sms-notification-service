using System.ServiceProcess;

namespace SmsNotificationService.Shared;

public static class StatusHelper
{
    public static string FormatStatus(ServiceControllerStatus status) => status switch
    {
        ServiceControllerStatus.Running => "Running",
        ServiceControllerStatus.Stopped => "Stopped",
        ServiceControllerStatus.Paused => "Paused",
        ServiceControllerStatus.StartPending => "Starting",
        ServiceControllerStatus.StopPending => "Stopping",
        _ => "Unknown"
    };

    public static string FormatUptime(TimeSpan ts) => ts.TotalDays >= 1
        ? $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m"
        : ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}h {ts.Minutes}m"
            : $"{ts.Minutes}m {ts.Seconds}s";

    public static string FormatDetection(string method) => method switch
    {
        "ServiceController" => "Windows Service",
        "Process" => "Process (non-service mode)",
        "NotRunning" => "Not running",
        "Error" => "Detection failed",
        _ => method
    };
}
