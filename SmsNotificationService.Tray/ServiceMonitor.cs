using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using SmsNotificationService.Shared;
using SmsNotificationService.Tray.Models;

namespace SmsNotificationService.Tray;

public sealed class ServiceMonitor : IDisposable
{
    private static readonly TimeSpan LogStaleThreshold = TimeSpan.FromSeconds(60);

    private readonly PeriodicTimer _timer;
    private readonly CancellationTokenSource _cts = new();
    private DateTime _startTime;

    public ServiceStatusInfo Current { get; private set; } = new();

    public event Action<ServiceStatusInfo>? StatusChanged;

    public ServiceMonitor()
    {
        _startTime = DateTime.Now;
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
    }

    public async Task StartAsync()
    {
        await PollAsync(_cts.Token);
    }

    private async Task PollAsync(CancellationToken ct)
    {
        var lastStatus = (ServiceControllerStatus)(-1);

        while (await _timer.WaitForNextTickAsync(ct))
        {
            try
            {
                var info = DetectStatus();

                if (info.Status != lastStatus)
                {
                    lastStatus = info.Status;
                    _startTime = DateTime.Now;
                }

                info.Uptime = info.Status == ServiceControllerStatus.Running
                    ? DateTime.Now - _startTime
                    : TimeSpan.Zero;
                info.LastCheck = DateTime.Now;
                info.Version = VersionHelper.GetCurrentVersion();

                Current = info;
                StatusChanged?.Invoke(info);
            }
            catch
            {
                var info = new ServiceStatusInfo
                {
                    Status = (ServiceControllerStatus)(-1),
                    Version = Current.Version,
                    LastCheck = DateTime.Now,
                    DetectionMethod = "Error"
                };
                Current = info;
                StatusChanged?.Invoke(info);
            }
        }
    }

    private static ServiceStatusInfo DetectStatus()
    {
        var svcResult = DetectByServiceController();
        if (svcResult is not null)
            return svcResult;

        var procResult = DetectByProcess();
        if (procResult is not null)
            return procResult;

        return new ServiceStatusInfo
        {
            Status = ServiceControllerStatus.Stopped,
            DetectionMethod = "NotRunning"
        };
    }

    private static ServiceStatusInfo? DetectByServiceController()
    {
        try
        {
            using var controller = new ServiceController(Constants.ServiceName);
            var status = controller.Status;
            return new ServiceStatusInfo
            {
                Status = status,
                DetectionMethod = "ServiceController"
            };
        }
        catch
        {
            return null;
        }
    }

    private static ServiceStatusInfo? DetectByProcess()
    {
        try
        {
            var processes = Process.GetProcessesByName(Constants.ServiceName);
            if (processes.Length == 0)
                return null;

            var proc = processes[0];
            var startTime = proc.StartTime.ToLocalTime();
            var logActive = IsLogRecentlyActive();

            var status = logActive
                ? ServiceControllerStatus.Running
                : ServiceControllerStatus.StartPending;

            return new ServiceStatusInfo
            {
                Status = status,
                DetectionMethod = "Process"
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool IsLogRecentlyActive()
    {
        try
        {
            var logDir = ConfigPathResolver.GetLogDir();
            if (!Directory.Exists(logDir))
                return false;

            var latestLog = Directory.GetFiles(logDir, "*.log")
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();

            if (latestLog is null)
                return false;

            var lastWrite = File.GetLastWriteTime(latestLog);
            return DateTime.Now - lastWrite < LogStaleThreshold;
        }
        catch
        {
            return false;
        }
    }

    public void StartService() => Execute("start");

    public void StopService()
    {
        Execute("stop");
        KillProcesses();
    }

    public void RestartService()
    {
        StopService();
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);
            StartService();
        });
    }

    private static void Execute(string action)
    {
        try { Process.Start("sc.exe", $"{action} {Constants.ServiceName}"); }
        catch { /* Best effort */ }
    }

    private static void KillProcesses()
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName(Constants.ServiceName))
            {
                try { proc.Kill(); }
                catch { /* process may have already exited */ }
            }
        }
        catch { /* best effort */ }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _timer.Dispose();
    }
}
