using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Text.Json;
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
    private void RunSmsConsole_Click(object sender, RoutedEventArgs e) => RunConsole(Constants.SmsExecutableName, "SMS");
    private void StopSms_Click(object sender, RoutedEventArgs e) => monitor.StopNamedService(Constants.ServiceName);
    private void RestartSms_Click(object sender, RoutedEventArgs e) => monitor.RestartNamedService(Constants.ServiceName);
    private async void StartAgent_Click(object sender, RoutedEventArgs e) => await StartAfterValidationAsync(Constants.AgentServiceName);
    private void RunAgentConsole_Click(object sender, RoutedEventArgs e) => RunConsole(Constants.AgentExecutableName, "Agent");
    private void StopAgent_Click(object sender, RoutedEventArgs e) => monitor.StopNamedService(Constants.AgentServiceName);
    private void RestartAgent_Click(object sender, RoutedEventArgs e) => monitor.RestartNamedService(Constants.AgentServiceName);

    private void InstallSms_Click(object sender, RoutedEventArgs e) => Install(Constants.ServiceName, "FeeSyncer SMS", Constants.SmsExecutableName);
    private void InstallAgent_Click(object sender, RoutedEventArgs e) => Install(Constants.AgentServiceName, "FeeSyncer Agent", Constants.AgentExecutableName);
    private void UninstallSms_Click(object sender, RoutedEventArgs e) => Uninstall(Constants.ServiceName);
    private void UninstallAgent_Click(object sender, RoutedEventArgs e) => Uninstall(Constants.AgentServiceName);

    private void RunConsole(string executableName, string displayName)
    {
        var path = ExecutablePathResolver.FindServiceExecutable(executableName);
        if (path is null)
        {
            MessageBox.Show($"{displayName} console executable was not found. Publish or install the {displayName} executable first.", "Console Application", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            WorkingDirectory = Path.GetDirectoryName(path),
            UseShellExecute = true,
        });
    }

    private async Task StartAfterValidationAsync(string serviceName)
    {
        try
        {
            var validation = serviceName == Constants.AgentServiceName
                ? await ValidateAgentPrerequisitesAsync()
                : await ValidateSmsPrerequisitesAsync();
            if (!validation.Passed)
            {
                MessageBox.Show(validation.Summary, "Connection Check Required", MessageBoxButton.OK, MessageBoxImage.Warning);
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

    private static async Task<(bool Passed, string Summary)> ValidateSmsPrerequisitesAsync()
    {
        var result = await new ConnectionValidator().ValidateAsync();
        return (result.AllPassed, result.Summary);
    }

    private static async Task<(bool Passed, string Summary)> ValidateAgentPrerequisitesAsync()
    {
        try
        {
            var baseUrl = Constants.DefaultBaseUrl;
            var workEndpoint = Constants.DefaultAgentWorkEndpoint;
            var rootPath = ConfigPathResolver.FindConfigFile();
            if (File.Exists(rootPath))
            {
                using var root = JsonDocument.Parse(await File.ReadAllTextAsync(rootPath));
                if (root.RootElement.TryGetProperty("FeeSyncer", out var feeSyncer)
                    && feeSyncer.TryGetProperty("BaseUrl", out var configuredBaseUrl))
                    baseUrl = configuredBaseUrl.GetString() ?? baseUrl;

                if (root.RootElement.TryGetProperty("FeeSyncer", out feeSyncer) &&
                    feeSyncer.TryGetProperty("ApiEndpoints", out var endpoints) &&
                    endpoints.TryGetProperty("AgentWork", out var configuredWorkEndpoint) &&
                    configuredWorkEndpoint.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(configuredWorkEndpoint.GetString()))
                    workEndpoint = configuredWorkEndpoint.GetString()!;
            }

            var agentPath = ConfigPathResolver.FindAgentConfigFile();
            if (!File.Exists(agentPath))
                return (false, "Agent configuration file was not found.");

            using var agentDocument = JsonDocument.Parse(await File.ReadAllTextAsync(agentPath));
            if (!agentDocument.RootElement.TryGetProperty("Agent", out var agent))
                return (false, "Agent configuration section was not found.");

            var token = agent.TryGetProperty("AgentToken", out var tokenValue) ? tokenValue.GetString() : string.Empty;
            var localApi = agent.TryGetProperty("LocalApiBaseUrl", out var localValue)
                ? localValue.GetString()
                : "http://127.0.0.1:8001/api/";
            var gatewayTask = ConnectionValidator.ValidateHttpAsync(
                ConfigReader.CombineUrl(baseUrl, workEndpoint) + "?wait=0", token);
            var localTask = ConnectionValidator.ValidateHttpAsync(localApi?.TrimEnd('/') + "/", null);
            var results = await Task.WhenAll(gatewayTask, localTask);
            var summary = $"Agent gateway: {(results[0].Passed ? "OK" : "FAIL")} {results[0].Details}\n" +
                          $"Local API: {(results[1].Passed ? "OK" : "FAIL")} {results[1].Details}";
            return (results.All(result => result.Passed), summary);
        }
        catch (Exception exception)
        {
            return (false, $"Agent prerequisite check failed: {exception.Message}");
        }
    }

    private void Install(string name, string display, string executable)
    {
        var directory = string.Equals(executable, Constants.AgentExecutableName, StringComparison.OrdinalIgnoreCase)
            ? System.IO.Path.Combine("..", "Agent")
            : "..";
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, directory, executable);
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

    private void LaunchConsole_Click(object sender, RoutedEventArgs e)
    {
        var consolePath = Path.Combine(AppContext.BaseDirectory, "..", "Console", Constants.ConsoleExecutableName);
        consolePath = Path.GetFullPath(consolePath);
        if (!File.Exists(consolePath))
        {
            MessageBox.Show($"Console monitor was not found at:\n{consolePath}", "Console Monitor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = consolePath,
            WorkingDirectory = Path.GetDirectoryName(consolePath),
            UseShellExecute = true,
        });
    }

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

    private void About_Click(object sender, RoutedEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => Hide();

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
