using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using FeeSyncer.Shared.Models;

namespace FeeSyncer.Shared;

public sealed class ServiceMonitor : IDisposable
{
    private static readonly TimeSpan LogStaleThreshold = TimeSpan.FromSeconds(60);

    private readonly PeriodicTimer _timer;
    private readonly CancellationTokenSource _cts = new();
    private readonly string _serviceName;
    private DateTime _startTime;

    public ServiceStatusInfo Current { get; private set; } = new();

    public event Action<ServiceStatusInfo>? StatusChanged;

    public ServiceMonitor(string serviceName = Constants.ServiceName)
    {
        _serviceName = serviceName;
        _startTime = DateTime.Now;
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        AppLogger.Info("Monitor", "ServiceMonitor initialized");
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
            catch (Exception ex)
            {
                AppLogger.Error("Monitor", "Error polling service status", ex);
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

    private ServiceStatusInfo DetectStatus()
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

    private ServiceStatusInfo? DetectByServiceController()
    {
        try
        {
            using var controller = new ServiceController(_serviceName);
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

    private ServiceStatusInfo? DetectByProcess()
    {
        try
        {
            var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(_serviceName));
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

    public void StartService()
    {
        AppLogger.Info("Monitor", "Starting service...");
        Execute("start");
    }

    public void StopService()
    {
        AppLogger.Info("Monitor", "Stopping service...");
        Execute("stop");
        KillProcesses();
    }

    public void RestartService()
    {
        AppLogger.Info("Monitor", "Restarting service...");
        StopService();
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);
            StartService();
        });
    }

    public void RestartAgentService()
    {
        AppLogger.Info("Monitor", "Restarting agent service...");
        ExecuteService(Constants.AgentServiceName, "stop");
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);
            ExecuteService(Constants.AgentServiceName, "start");
        });
    }

    public ServiceControllerStatus? GetServiceStatus(string serviceName)
    {
        try
        {
            using var controller = new ServiceController(serviceName);
            return controller.Status;
        }
        catch
        {
            return null;
        }
    }

    public bool InstallService(string serviceName, string displayName, string description, string executablePath)
    {
        var servyCli = FindExecutableOnPath("servy-cli.exe");
        if (servyCli is not null)
        {
            var logPrefix = Path.Combine(ConfigPathResolver.GetLogDir(), $"servy-{serviceName.ToLowerInvariant()}");
            var arguments = $"install --name=\"{serviceName}\" --displayName=\"{displayName}\" " +
                $"--description=\"{description}\" --path=\"{executablePath}\" " +
                $"--startupDir=\"{Path.GetDirectoryName(executablePath)}\" --startupType=\"AutomaticDelayedStart\" " +
                $"--stdout=\"{logPrefix}-stdout.log\" --stderr=\"{logPrefix}-stderr.log\" " +
                "--enableSizeRotation --rotationSize=10 --enableDateRotation --dateRotationType=\"Daily\" " +
                "--maxRotations=7 --useLocalTimeForRotation --enableHealth --heartbeatInterval=10 " +
                "--maxFailedChecks=3 --recoveryAction=\"RestartProcess\" --maxRestartAttempts=3 --quiet";
            if (RunElevated(servyCli, arguments))
                return true;

            AppLogger.Warn("Monitor", $"Servy failed to install {serviceName}; falling back to sc.exe");
        }

        var action = GetServiceStatus(serviceName) is null ? "create" : "config";
        return RunElevated("sc.exe", $"{action} {serviceName} binPath= \"{executablePath}\" start= delayed-auto DisplayName= \"{displayName}\" obj= LocalSystem");
    }

    public bool UninstallService(string serviceName)
    {
        StopNamedService(serviceName);
        var servyCli = FindExecutableOnPath("servy-cli.exe");
        if (servyCli is not null && RunElevated(servyCli, $"uninstall --name=\"{serviceName}\" --quiet"))
            return true;

        return RunElevated("sc.exe", $"delete {serviceName}");
    }

    public void StartNamedService(string serviceName) => RunServiceCommand("start", serviceName);

    public void StopNamedService(string serviceName) => RunServiceCommand("stop", serviceName);

    public void RestartNamedService(string serviceName)
    {
        StopNamedService(serviceName);
        _ = Task.Run(async () =>
        {
            await Task.Delay(1500);
            StartNamedService(serviceName);
        });
    }

    private static void Execute(string action)
        => ExecuteService(Constants.ServiceName, action);

    private static void ExecuteService(string serviceName, string action)
    {
        try
        {
            AppLogger.Info("Monitor", $"Executing: sc.exe {action} {serviceName}");
            Process.Start("sc.exe", $"{action} {serviceName}");
        }
        catch (Exception ex)
        {
            AppLogger.Error("Monitor", $"Failed to {action} service", ex);
        }
    }

    private static void RunServiceCommand(string action, string serviceName)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("sc.exe", $"{action} {serviceName}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error("Monitor", $"Failed to {action} service {serviceName}", ex);
        }
    }

    private static bool RunElevated(string executable, string arguments)
    {
        try
        {
            AppLogger.Info("Monitor", $"Executing elevated: {Path.GetFileName(executable)} {arguments}");
            using var process = Process.Start(new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Monitor", $"Failed to execute {Path.GetFileName(executable)}", ex);
            return false;
        }
    }

    private static string? FindExecutableOnPath(string executable)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(Environment.ExpandEnvironmentVariables(directory.Trim('"')), executable);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // Ignore malformed PATH entries and continue to the native fallback.
            }
        }

        return null;
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
        AppLogger.Info("Monitor", "Disposing ServiceMonitor");
        _cts.Cancel();
        _cts.Dispose();
        _timer.Dispose();
    }
}
