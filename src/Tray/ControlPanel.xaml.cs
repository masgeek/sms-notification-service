using System.ComponentModel;
using System.ServiceProcess;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FeeSyncer.Shared;

namespace FeeSyncer.Tray;

public partial class ControlPanel : Window
{
    private readonly ServiceMonitor monitor;
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private TabItem? settingsTab;
    private TabItem? logsTab;

    public ControlPanel(ServiceMonitor monitor)
    {
        InitializeComponent();
        this.monitor = monitor;
        settingsTab = new TabItem
        {
            Header = "Settings",
            Content = new ConfigEditor(monitor),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        WorkspaceTabs.Items.Add(settingsTab);
        refreshTimer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { Refresh(); refreshTimer.Start(); };
    }

    private void Refresh()
    {
        var sms = monitor.GetServiceStatus(Constants.ServiceName);
        var agent = monitor.GetServiceStatus(Constants.AgentServiceName);
        SetStatus(SmsStatusText, SmsDetailsText, sms);
        SetStatus(AgentStatusText, AgentDetailsText, agent);
        SetButtons(sms, StartSmsButton, StopSmsButton, RestartSmsButton, InstallSmsButton, UninstallSmsButton);
        SetButtons(agent, StartAgentButton, StopAgentButton, RestartAgentButton, InstallAgentButton, UninstallAgentButton);
    }

    private static void SetStatus(System.Windows.Controls.TextBlock status, System.Windows.Controls.TextBlock details, ServiceControllerStatus? value)
    {
        status.Text = value?.ToString() ?? "Not installed";
        status.Foreground = value switch
        {
            ServiceControllerStatus.Running => System.Windows.Media.Brushes.ForestGreen,
            ServiceControllerStatus.Stopped => System.Windows.Media.Brushes.Firebrick,
            _ => System.Windows.Media.Brushes.DarkGoldenrod,
        };
        details.Text = value is null ? "The Windows service is not installed." : "Managed by FeeSyncer Control Panel.";
    }

    private static void SetButtons(ServiceControllerStatus? status, System.Windows.Controls.Button start,
        System.Windows.Controls.Button stop, System.Windows.Controls.Button restart,
        System.Windows.Controls.Button install, System.Windows.Controls.Button uninstall)
    {
        var installed = status is not null;
        var running = status == ServiceControllerStatus.Running;
        start.IsEnabled = installed && !running;
        stop.IsEnabled = running;
        restart.IsEnabled = running;
        install.IsEnabled = !installed;
        uninstall.IsEnabled = installed;
    }

    private async void StartSms_Click(object sender, RoutedEventArgs e) => await StartAfterValidationAsync(Constants.ServiceName);
    private void StopSms_Click(object sender, RoutedEventArgs e) => monitor.StopNamedService(Constants.ServiceName);
    private void RestartSms_Click(object sender, RoutedEventArgs e) => monitor.RestartNamedService(Constants.ServiceName);
    private async void StartAgent_Click(object sender, RoutedEventArgs e) => await StartAfterValidationAsync(Constants.AgentServiceName);
    private void StopAgent_Click(object sender, RoutedEventArgs e) => monitor.StopNamedService(Constants.AgentServiceName);
    private void RestartAgent_Click(object sender, RoutedEventArgs e) => monitor.RestartNamedService(Constants.AgentServiceName);

    private void InstallSms_Click(object sender, RoutedEventArgs e) => Install(Constants.ServiceName, "FeeSyncer SMS", Constants.SmsExecutableName);
    private void InstallAgent_Click(object sender, RoutedEventArgs e) => Install(Constants.AgentServiceName, "FeeSyncer Agent", Constants.AgentExecutableName);
    private void UninstallSms_Click(object sender, RoutedEventArgs e) => Uninstall(Constants.ServiceName);
    private void UninstallAgent_Click(object sender, RoutedEventArgs e) => Uninstall(Constants.AgentServiceName);

    private async Task StartAfterValidationAsync(string serviceName)
    {
        try
        {
            var result = await new ConnectionValidator().ValidateAsync();
            if (!result.AllPassed)
            {
                MessageBox.Show(result.Summary, "Connection Check Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            monitor.StartNamedService(serviceName);
            Refresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Connection check failed: {ex.Message}", "Connection Check Required", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Install(string name, string display, string executable)
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "..", executable);
        var ok = monitor.InstallService(name, display, System.IO.Path.GetFullPath(path));
        MessageBox.Show(ok ? $"{name} installed." : $"Could not install {name}.", "Service Management", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Error);
        Refresh();
    }

    private void Uninstall(string name)
    {
        if (MessageBox.Show($"Uninstall {name}?", "Service Management", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var ok = monitor.UninstallService(name);
        MessageBox.Show(ok ? $"{name} uninstalled." : $"Could not uninstall {name}.", "Service Management", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Error);
        Refresh();
    }

    public void OpenSettings()
    {
        if (settingsTab is not null)
        {
            WorkspaceTabs.SelectedItem = settingsTab;
            return;
        }

        WorkspaceTabs.SelectedItem = settingsTab;
    }

    private void Logs_Click(object sender, RoutedEventArgs e)
        => OpenLogs();

    public void OpenLogs()
    {
        if (logsTab is null)
        {
            logsTab = new TabItem
            {
                Header = "Logs",
                Content = new LogViewer(),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
            };
            WorkspaceTabs.Items.Add(logsTab);
        }
        WorkspaceTabs.SelectedItem = logsTab;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => Hide();

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
