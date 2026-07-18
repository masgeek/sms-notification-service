using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using SmsNotificationService.Shared;

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
        StatusText.Text = StatusHelper.FormatStatus(info.Status);
        UptimeText.Text = StatusHelper.FormatUptime(info.Uptime);
        VersionText.Text = info.Version;
        LastCheckText.Text = info.LastCheck.ToString("yyyy-MM-dd HH:mm:ss");
        DetectionText.Text = StatusHelper.FormatDetection(info.DetectionMethod);

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
}
