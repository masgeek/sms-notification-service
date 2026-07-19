using System.ServiceProcess;

namespace SmsNotificationService.Shared.Models;

public sealed class ServiceStatusInfo
{
    public ServiceControllerStatus Status { get; set; }
    public string Version { get; set; } = string.Empty;
    public TimeSpan Uptime { get; set; }
    public DateTime LastCheck { get; set; }
    public string DetectionMethod { get; set; } = string.Empty;
}
