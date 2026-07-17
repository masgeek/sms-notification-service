using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using Microsoft.Data.SqlClient;
using SmsNotificationService.Tray.Helpers;

namespace SmsNotificationService.Tray;

public partial class ConfigEditor : Window
{
    private readonly ServiceMonitor _monitor;
    private bool _tokenVisible;
    private string _rawToken = string.Empty;

    public ConfigEditor(ServiceMonitor monitor)
    {
        InitializeComponent();
        _monitor = monitor;

        Loaded += (_, _) =>
        {
            ConfigPathText.Text = Paths.ConfigFile;
            LoadConfig();
        };
    }

    private void LoadConfig()
    {
        try
        {
            if (!File.Exists(Paths.ConfigFile)) return;
            var json = File.ReadAllText(Paths.ConfigFile);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("ConnectionStrings", out var cs) &&
                cs.TryGetProperty("DefaultConnection", out var conn))
            {
                var connectionString = conn.GetString() ?? string.Empty;
                ParseConnectionString(connectionString);
            }

            if (doc.RootElement.TryGetProperty("SmsService", out var sms))
            {
                if (sms.TryGetProperty("SmsApiUrl", out var url))
                    ApiUrlBox.Text = url.GetString() ?? string.Empty;

                if (sms.TryGetProperty("AuthorizationToken", out var token))
                    _rawToken = token.GetString() ?? string.Empty;

                if (sms.TryGetProperty("RetryBackoffSeconds", out var backoff) &&
                    backoff.TryGetInt32(out var backoffVal))
                    BackoffBox.Text = backoffVal.ToString();

                if (sms.TryGetProperty("RetryPollIntervalSeconds", out var poll) &&
                    poll.TryGetInt32(out var pollVal))
                    PollIntervalBox.Text = pollVal.ToString();
            }

            if (doc.RootElement.TryGetProperty("Logging", out var logging) &&
                logging.TryGetProperty("FileLogging", out var fileLog))
            {
                if (fileLog.TryGetProperty("RetentionDays", out var retention) &&
                    retention.TryGetInt32(out var retentionVal))
                    RetentionBox.Text = retentionVal.ToString();

                if (fileLog.TryGetProperty("MaxFileSizeMb", out var maxSize) &&
                    maxSize.TryGetInt64(out var maxSizeVal))
                    MaxSizeBox.Text = maxSizeVal.ToString();
            }

            UpdateTokenDisplay();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading config: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ParseConnectionString(string connectionString)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            DbServerBox.Text = builder.DataSource;
            DbNameBox.Text = builder.InitialCatalog;
            DbUserIdBox.Text = builder.UserID;
            DbPasswordBox.Password = builder.Password;
            if (builder.Encrypt == SqlConnectionEncryptOption.Mandatory)
                DbEncryptBox.SelectedIndex = 0;
            else if (builder.Encrypt == SqlConnectionEncryptOption.Optional)
                DbEncryptBox.SelectedIndex = 1;
            else
                DbEncryptBox.SelectedIndex = 2;
        }
        catch
        {
            // If parsing fails, leave fields empty
        }
    }

    private string BuildConnectionString()
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = DbServerBox.Text,
            InitialCatalog = DbNameBox.Text,
            UserID = DbUserIdBox.Text,
            Password = DbPasswordBox.Password,
        };

        if (DbEncryptBox.SelectedIndex == 1)
            builder.Encrypt = SqlConnectionEncryptOption.Optional;
        else if (DbEncryptBox.SelectedIndex == 2)
            builder.Encrypt = SqlConnectionEncryptOption.Strict;
        else
            builder.Encrypt = SqlConnectionEncryptOption.Mandatory;

        return builder.ConnectionString;
    }

    private void ToggleTokenButton_Click(object sender, RoutedEventArgs e)
    {
        _tokenVisible = !_tokenVisible;
        UpdateTokenDisplay();
        ToggleTokenButton.Content = _tokenVisible ? "Hide" : "Show";
    }

    private void UpdateTokenDisplay()
    {
        TokenBox.Password = _tokenVisible ? _rawToken : new string('*', Math.Min(_rawToken.Length, 20));
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        TestButton.IsEnabled = false;
        TestButton.Content = "Testing...";

        try
        {
            var validator = new ConnectionValidator();
            var result = await validator.ValidateAsync();

            var msg = result.AllPassed
                ? "All connections OK."
                : $"Issues found:\n{result.Summary}";

            MessageBox.Show(msg, "Test Connection",
                MessageBoxButton.OK,
                result.AllPassed ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Test failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TestButton.IsEnabled = true;
            TestButton.Content = "Test Connection";
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(Paths.ConfigFile))
            {
                MessageBox.Show("Config file not found. Cannot save.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (Paths.IsInProgramData(Paths.ConfigFile) && !IsRunningAsAdmin())
            {
                var elevateResult = MessageBox.Show(
                    "Saving to ProgramData requires administrator privileges.\n\n" +
                    "Re-launch the config editor as administrator?",
                    "Elevated Permissions Required",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (elevateResult == MessageBoxResult.Yes)
                    RelaunchAsAdmin();

                return;
            }

            var connectionString = BuildConnectionString();

            var json = await File.ReadAllTextAsync(Paths.ConfigFile);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement.Clone();

            var mutable = new Dictionary<string, object?>();

            foreach (var prop in root.EnumerateObject())
                mutable[prop.Name] = prop.Value.Clone();

            if (mutable.TryGetValue("ConnectionStrings", out var csObj) && csObj is JsonElement csElement)
            {
                var csDict = new Dictionary<string, string?>();
                foreach (var prop in csElement.EnumerateObject())
                    csDict[prop.Name] = prop.Value.GetString();
                csDict["DefaultConnection"] = connectionString;
                mutable["ConnectionStrings"] = csDict;
            }

            if (mutable.TryGetValue("SmsService", out var smsObj) && smsObj is JsonElement smsElement)
            {
                var smsDict = new Dictionary<string, object?>();
                foreach (var prop in smsElement.EnumerateObject())
                    smsDict[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                        ? (object)prop.Value.GetString()!
                        : prop.Value.GetRawText();
                smsDict["SmsApiUrl"] = ApiUrlBox.Text;
                smsDict["AuthorizationToken"] = _rawToken;
                if (int.TryParse(BackoffBox.Text, out var backoff)) smsDict["RetryBackoffSeconds"] = backoff;
                if (int.TryParse(PollIntervalBox.Text, out var poll)) smsDict["RetryPollIntervalSeconds"] = poll;
                mutable["SmsService"] = smsDict;
            }

            var output = JsonSerializer.Serialize(mutable, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Paths.ConfigFile, output);

            var result = MessageBox.Show(
                "Configuration saved. Restart service now?",
                "Saved",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
                _monitor.RestartService();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void RelaunchAsAdmin()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrEmpty(exePath)) return;

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Verb = "runas",
                UseShellExecute = true
            });

            Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            MessageBox.Show("Administrator elevation was denied.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
