using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace SmsNotificationService.Tray;

public partial class StatusWindow : Window
{
    private readonly ServiceMonitor _monitor;
    private readonly DispatcherTimer _refreshTimer;

    public StatusWindow(ServiceMonitor monitor)
    {
        InitializeComponent();
        _monitor = monitor;
        _monitor.StatusChanged += _ => Dispatcher.Invoke(RefreshDisplay);

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += (_, _) => RefreshDisplay();

        Loaded += (_, _) =>
        {
            RefreshDisplay();
            _refreshTimer.Start();
        };
    }

    private void RefreshDisplay()
    {
        if (StatusText is null) return;

        var info = _monitor.Current;
        StatusText.Text = FormatStatus(info.Status);
        UptimeText.Text = FormatUptime(info.Uptime);
        VersionText.Text = info.Version;
        LastCheckText.Text = info.LastCheck.ToString("yyyy-MM-dd HH:mm:ss");
        DetectionText.Text = FormatDetection(info.DetectionMethod);

        StartButton.IsEnabled = info.Status is System.ServiceProcess.ServiceControllerStatus.Stopped
            or System.ServiceProcess.ServiceControllerStatus.Paused;
        StopButton.IsEnabled = info.Status == System.ServiceProcess.ServiceControllerStatus.Running;
        RestartButton.IsEnabled = info.Status == System.ServiceProcess.ServiceControllerStatus.Running;
    }

    private void StartButton_Click(object sender, RoutedEventArgs e) => _monitor.StartService();
    private void StopButton_Click(object sender, RoutedEventArgs e) => _monitor.StopService();
    private void RestartButton_Click(object sender, RoutedEventArgs e) => _monitor.RestartService();

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }

    private static string FormatStatus(System.ServiceProcess.ServiceControllerStatus status) => status switch
    {
        System.ServiceProcess.ServiceControllerStatus.Running => "Running",
        System.ServiceProcess.ServiceControllerStatus.Stopped => "Stopped",
        System.ServiceProcess.ServiceControllerStatus.Paused => "Paused",
        System.ServiceProcess.ServiceControllerStatus.StartPending => "Starting...",
        System.ServiceProcess.ServiceControllerStatus.StopPending => "Stopping...",
        _ => "Unknown"
    };

    private static string FormatUptime(TimeSpan ts) => ts.TotalDays >= 1
        ? $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m"
        : ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}h {ts.Minutes}m"
            : $"{ts.Minutes}m {ts.Seconds}s";

    private static string FormatDetection(string method) => method switch
    {
        "ServiceController" => "Windows Service",
        "Process" => "Process (non-service mode)",
        "NotRunning" => "Not running",
        "Error" => "Detection failed",
        _ => method
    };
}
