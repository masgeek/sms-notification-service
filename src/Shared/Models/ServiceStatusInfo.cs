using System.ServiceProcess;

namespace FeeSyncer.Shared.Models;

public enum ServiceDetectionMethod
{
    Unknown,
    ServiceController,
    Process,
    NotRunning,
    Error,
}

public sealed class ServiceStatusInfo
{
    public ServiceControllerStatus Status { get; set; }
    public string Version { get; set; } = string.Empty;
    public TimeSpan Uptime { get; set; }
    public DateTime LastCheck { get; set; }
    public ServiceDetectionMethod DetectionMethod { get; set; }
}
