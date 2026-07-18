using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Data.SqlClient;
using SmsNotificationService.Shared;

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
            ConfigPathText.Text = ConfigPathResolver.FindConfigFile();
            LoadConfig();
        };
    }

    private void LoadConfig()
    {
        try
        {
            var configPath = ConfigPathResolver.FindConfigFile();
            if (!File.Exists(configPath)) return;
            var json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("SmsService", out var sms))
            {
                if (sms.TryGetProperty("ConnectionString", out var connStr))
                    ParseConnectionString(connStr.GetString() ?? string.Empty);

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

                if (sms.TryGetProperty("LogRetentionDays", out var retention) &&
                    retention.TryGetInt32(out var retentionVal))
                    RetentionBox.Text = retentionVal.ToString();

                if (sms.TryGetProperty("MaxLogFileSizeMb", out var maxSize) &&
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
            var (server, database, userId, password, _) = ConfigReader.ParseConnectionString(connectionString);
            DbServerBox.Text = server;
            DbNameBox.Text = database;
            DbUserIdBox.Text = userId;
            DbPasswordBox.Password = password;
        }
        catch
        {
            // If parsing fails, leave fields empty
        }
    }

    private string BuildConnectionString()
    {
        return ConfigReader.BuildConnectionString(
            DbServerBox.Text, DbNameBox.Text, DbUserIdBox.Text, DbPasswordBox.Password);
    }

    private void ToggleTokenButton_Click(object sender, RoutedEventArgs e)
    {
        _tokenVisible = !_tokenVisible;
        UpdateTokenDisplay();
        ToggleTokenButton.Content = _tokenVisible ? "Hide" : "Show";
    }

    private void UpdateTokenDisplay()
    {
        TokenBox.Text = _tokenVisible ? _rawToken : new string('*', Math.Min(_rawToken.Length, 20));
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
            var configPath = ConfigPathResolver.FindConfigFile();

            if (!File.Exists(configPath))
            {
                MessageBox.Show("Config file not found. Cannot save.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var connectionString = BuildConnectionString();

            var json = await File.ReadAllTextAsync(configPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement.Clone();

            var mutable = new Dictionary<string, object?>();

            foreach (var prop in root.EnumerateObject())
                mutable[prop.Name] = prop.Value.Clone();

            if (mutable.TryGetValue("SmsService", out var smsObj) && smsObj is JsonElement smsElement)
            {
                var smsDict = new Dictionary<string, object?>();
                foreach (var prop in smsElement.EnumerateObject())
                    smsDict[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                        ? (object)prop.Value.GetString()!
                        : prop.Value.GetRawText();
                smsDict["ConnectionString"] = connectionString;
                smsDict["SmsApiUrl"] = ApiUrlBox.Text;
                smsDict["AuthorizationToken"] = _rawToken;
                if (int.TryParse(BackoffBox.Text, out var backoff)) smsDict["RetryBackoffSeconds"] = backoff;
                if (int.TryParse(PollIntervalBox.Text, out var poll)) smsDict["RetryPollIntervalSeconds"] = poll;
                if (int.TryParse(RetentionBox.Text, out var retention)) smsDict["LogRetentionDays"] = retention;
                if (int.TryParse(MaxSizeBox.Text, out var maxSize)) smsDict["MaxLogFileSizeMb"] = maxSize;
                mutable["SmsService"] = smsDict;
            }

            var output = JsonSerializer.Serialize(mutable, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(configPath, output);

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

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
