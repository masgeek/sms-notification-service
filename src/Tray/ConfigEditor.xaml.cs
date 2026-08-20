using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.SqlClient;
using MQTTnet;
using FeeSyncer.Shared;

namespace FeeSyncer.Tray;

public partial class ConfigEditor : UserControl
{
    private readonly ServiceMonitor _monitor;
    private readonly UpdateChecker _updater;

    public ConfigEditor(ServiceMonitor monitor, UpdateChecker updater)
    {
        InitializeComponent();
        _monitor = monitor;
        _updater = updater;

        Loaded += async (_, _) =>
        {
            ConfigPathText.Text = $"SMS settings: {ConfigPathResolver.GetActiveConfigFile()}{Environment.NewLine}" +
                                  $"Agent settings: {ConfigPathResolver.GetActiveAgentConfigFile()}";
            LoadConfig();
            await UpdateFeeToolDetectionTextAsync();
        };
    }

    private void LoadConfig()
    {
        try
        {
            LoadSmsDefaults();
            LoadTrayDefaults();
            var configPath = ConfigPathResolver.FindConfigFile();
            if (!File.Exists(configPath))
            {
                LoadAgentConfig();
                return;
            }
            var json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);

            ApiUrlBox.Text = Constants.DefaultBaseUrl;
            if (doc.RootElement.TryGetProperty("FeeSyncer", out var feeSyncer)
                && feeSyncer.TryGetProperty("BaseUrl", out var baseUrl)
                && !string.IsNullOrWhiteSpace(baseUrl.GetString()))
                ApiUrlBox.Text = baseUrl.GetString()!;

            if (doc.RootElement.TryGetProperty("FeeSyncer", out feeSyncer))
                LoadApiEndpoints(feeSyncer);

            if (doc.RootElement.TryGetProperty("SmsService", out var sms))
            {
                if (sms.TryGetProperty("ConnectionString", out var connStr))
                {
                    var connectionString = connStr.GetString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(connectionString))
                        ParseConnectionString(connectionString);
                }

                if (sms.TryGetProperty("AuthorizationToken", out var token))
                    TokenBox.Password = token.GetString() ?? string.Empty;

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

            LoadAgentConfig();
            if (doc.RootElement.TryGetProperty("Tray", out var tray))
            {
                TrayStartMinimizedBox.IsChecked = BoolValue(tray, "StartMinimizedToTray", true);
                UpdateCheckIntervalBox.SelectedValue = UpdateCheckSchedule.Normalize(
                    StringValue(tray, "UpdateCheckInterval", UpdateCheckSchedule.DefaultValue));
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading config: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }


    private void LoadAgentConfig()
    {
        LoadAgentDefaults();
        var configPath = ConfigPathResolver.FindAgentConfigFile();
        if (!File.Exists(configPath))
        {
            AgentStatusText.Text = "Not enrolled";
            return;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
        if (!doc.RootElement.TryGetProperty("Agent", out var agent))
            return;

        AgentEnabledBox.IsChecked = BoolValue(agent, "Enabled", true);
        SetAgentToken(StringValue(agent, "AgentToken"));
        RequestTimeoutBox.Text = NumberValue(agent, "RequestTimeoutSeconds", 30);
        IdleDelayBox.Text = NumberValue(agent, "IdleDelaySeconds", 5);
        HeartbeatBox.Text = NumberValue(agent, "HeartbeatSeconds", 60);
        LeaseRenewalBox.Text = NumberValue(agent, "LeaseRenewalSeconds", 30);
        LocalApiUrlBox.Text = StringValue(agent, "LocalApiBaseUrl", "http://127.0.0.1:8001/api/");
        LocalApiUsernameBox.Text = StringValue(agent, "LocalApiUsername");
        LocalApiPasswordBox.Password = StringValue(agent, "LocalApiPassword");
        MqttEnabledBox.IsChecked = BoolValue(agent, "MqttEnabled", true);
        MqttHostBox.Text = StringValue(agent, "MqttBrokerHost", "wss://mqtt.munywele.co.ke/mqtt");
        MqttPortBox.Text = NumberValue(agent, "MqttBrokerPort", 443);
        MqttPathBox.Text = StringValue(agent, "MqttBrokerPath", "/mqtt");
        MqttTlsBox.IsChecked = BoolValue(agent, "MqttUseTls", true);
        SetMqttEnvironmentFromUrl();
        MqttUsernameBox.Text = StringValue(agent, "MqttUsername");
        MqttPasswordBox.Password = StringValue(agent, "MqttPassword");
        MqttTopicBox.Text = StringValue(agent, "MqttTopicPrefix", "fee-syncer/agent");
        MqttKeepAliveBox.Text = NumberValue(agent, "MqttKeepAliveSeconds", 30);
        MqttReconnectMinBox.Text = NumberValue(agent, "MqttReconnectMinSeconds", 1);
        MqttReconnectMaxBox.Text = NumberValue(agent, "MqttReconnectMaxSeconds", 60);
        FeeUpdateEnabledBox.IsChecked = BoolValue(agent, "FeeProcessorUpdateEnabled", false);
        FeeUpdatePathBox.Text = StringValue(agent, "FeeProcessorPath", "C:\\fee-processor");
        FeeUpdateRepositoryBox.Text = StringValue(agent, "FeeProcessorRepository", "git@github.com:masgeek/fee-processor.git");
        FeeUpdateBranchBox.Text = StringValue(agent, "FeeProcessorBranch", "main");
        FeeUpdateTagBox.Text = StringValue(agent, "FeeProcessorTag", "(none)");
        FeeUpdateIntervalBox.SelectedValue = FeeProcessorInterval.Normalize(
            StringValue(agent, "FeeProcessorUpdateInterval", NumberValue(agent, "FeeProcessorUpdateIntervalHours", 24) + "h"));
        FeeUpdateBackupBox.Text = StringValue(agent, "FeeProcessorBackupPath", "C:\\fee-processor-backups");
        PhpExecutablePathBox.Text = StringValue(agent, "PhpExecutablePath");
        ComposerExecutablePathBox.Text = StringValue(agent, "ComposerExecutablePath");
        GitExecutablePathBox.Text = StringValue(agent, "GitExecutablePath");
        FeeProcessorSshUsernameBox.Text = StringValue(agent, "FeeProcessorSshUsername", "git");
        FeeProcessorSshKeyPathBox.Text = StringValue(agent, "FeeProcessorSshKeyPath", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_ed25519"));
        FeeProcessorSshPassphraseBox.Password = StringValue(agent, "FeeProcessorSshPassphrase");
        AgentStatusText.Text = AgentTokenBox.Password.StartsWith("fsk_", StringComparison.Ordinal)
            ? "Enrolled"
            : "Not enrolled";
        AgentStatusSummaryText.Text = AgentStatusText.Text;
    }

    private void LoadSmsDefaults()
    {
        DbServerBox.Text = "127.0.0.1";
        DbNameBox.Text = "school";
        DbUserIdBox.Text = "sa";
        DbPasswordBox.Password = string.Empty;
        ApiUrlBox.Text = Constants.DefaultBaseUrl;
        SmsNotificationsEndpointBox.Text = Constants.DefaultSmsNotificationsEndpoint;
        AgentEnrollEndpointBox.Text = Constants.DefaultAgentEnrollEndpoint;
        AgentWorkEndpointBox.Text = Constants.DefaultAgentWorkEndpoint;
        AgentHeartbeatEndpointBox.Text = Constants.DefaultAgentHeartbeatEndpoint;
        AgentRenewEndpointBox.Text = Constants.DefaultAgentRenewEndpoint;
        AgentPageEndpointBox.Text = Constants.DefaultAgentPageEndpoint;
        AgentCompleteEndpointBox.Text = Constants.DefaultAgentCompleteEndpoint;
        AgentPaymentCompleteEndpointBox.Text = Constants.DefaultAgentPaymentCompleteEndpoint;
        AgentFailEndpointBox.Text = Constants.DefaultAgentFailEndpoint;
        TokenBox.Password = string.Empty;
        BackoffBox.Text = "30";
        PollIntervalBox.Text = "30";
        RetentionBox.Text = "7";
        MaxSizeBox.Text = "10";
    }

    private void LoadApiEndpoints(JsonElement feeSyncer)
    {
        if (!feeSyncer.TryGetProperty("ApiEndpoints", out var endpoints))
            return;

        SmsNotificationsEndpointBox.Text = StringValue(endpoints, "SmsNotifications", Constants.DefaultSmsNotificationsEndpoint);
        AgentEnrollEndpointBox.Text = StringValue(endpoints, "AgentEnroll", Constants.DefaultAgentEnrollEndpoint);
        AgentWorkEndpointBox.Text = StringValue(endpoints, "AgentWork", Constants.DefaultAgentWorkEndpoint);
        AgentHeartbeatEndpointBox.Text = StringValue(endpoints, "AgentHeartbeat", Constants.DefaultAgentHeartbeatEndpoint);
        AgentRenewEndpointBox.Text = StringValue(endpoints, "AgentRenew", Constants.DefaultAgentRenewEndpoint);
        AgentPageEndpointBox.Text = StringValue(endpoints, "AgentPage", Constants.DefaultAgentPageEndpoint);
        AgentCompleteEndpointBox.Text = StringValue(endpoints, "AgentComplete", Constants.DefaultAgentCompleteEndpoint);
        AgentPaymentCompleteEndpointBox.Text = StringValue(endpoints, "AgentPaymentComplete", Constants.DefaultAgentPaymentCompleteEndpoint);
        AgentFailEndpointBox.Text = StringValue(endpoints, "AgentFail", Constants.DefaultAgentFailEndpoint);
    }

    private void LoadTrayDefaults()
    {
        TrayStartMinimizedBox.IsChecked = false;
        UpdateCheckIntervalBox.SelectedValue = UpdateCheckSchedule.DefaultValue;
    }

    private void SetAgentToken(string token)
    {
        AgentTokenBox.Password = token;
        AgentTokenVisibleBox.Text = token;
    }

    private void TokenBox_PasswordChanged(object sender, RoutedEventArgs e) => TokenVisibleBox.Text = TokenBox.Password;

    private void TokenVisibleBox_TextChanged(object sender, TextChangedEventArgs e) => TokenBox.Password = TokenVisibleBox.Text;

    private void AgentTokenBox_PasswordChanged(object sender, RoutedEventArgs e) => AgentTokenVisibleBox.Text = AgentTokenBox.Password;

    private void AgentTokenVisibleBox_TextChanged(object sender, TextChangedEventArgs e) => AgentTokenBox.Password = AgentTokenVisibleBox.Text;

    private void TokenVisibilityButton_Click(object sender, RoutedEventArgs e) => ToggleTokenVisibility(TokenBox, TokenVisibleBox, TokenVisibilityButton);

    private void AgentTokenVisibilityButton_Click(object sender, RoutedEventArgs e) => ToggleTokenVisibility(AgentTokenBox, AgentTokenVisibleBox, AgentTokenVisibilityButton);

    private static void ToggleTokenVisibility(PasswordBox passwordBox, TextBox visibleBox, Button visibilityButton)
    {
        var show = passwordBox.Visibility == Visibility.Visible;
        visibleBox.Text = passwordBox.Password;
        passwordBox.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        visibleBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        visibilityButton.Content = show ? "Hide" : "Show";
    }

    private void MqttEnvironmentBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MqttHostBox is not null && sender is ComboBox combo && combo.SelectedValue is string environment)
            ApplyMqttEnvironment(environment);
    }

    private void SetMqttEnvironmentFromUrl()
    {
        if (MqttHostBox is null || MqttEnvironmentBox is null)
            return;

        var url = MqttHostBox.Text.Trim();
        MqttEnvironmentBox.SelectedValue = url.StartsWith("ws://127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("ws://localhost", StringComparison.OrdinalIgnoreCase)
            ? "Development"
            : url.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)
                ? "Production"
                : "Custom";
    }

    private void ApplyMqttEnvironment(string environment)
    {
        if (MqttHostBox is null || MqttPortBox is null || MqttPathBox is null || MqttTlsBox is null)
            return;

        switch (environment)
        {
            case "Development":
                MqttHostBox.Text = "ws://127.0.0.1:8083/mqtt";
                MqttPortBox.Text = "8083";
                MqttPathBox.Text = "/mqtt";
                MqttTlsBox.IsChecked = false;
                break;
            case "Production":
                MqttHostBox.Text = "wss://mqtt.munywele.co.ke/mqtt";
                MqttPortBox.Text = "443";
                MqttPathBox.Text = "/mqtt";
                MqttTlsBox.IsChecked = true;
                break;
        }
    }

    private void LoadAgentDefaults()
    {
        AgentEnabledBox.IsChecked = true;
        SetAgentToken(string.Empty);
        AgentNameBox.Text = Environment.MachineName;
        RequestTimeoutBox.Text = "30";
        IdleDelayBox.Text = "5";
        HeartbeatBox.Text = "60";
        LeaseRenewalBox.Text = "30";
        LocalApiUrlBox.Text = "http://127.0.0.1:8001/api/";
        LocalApiUsernameBox.Text = string.Empty;
        LocalApiPasswordBox.Password = string.Empty;
        MqttEnabledBox.IsChecked = true;
        MqttHostBox.Text = "wss://mqtt.munywele.co.ke/mqtt";
        MqttPortBox.Text = "443";
        MqttPathBox.Text = "/mqtt";
        MqttTlsBox.IsChecked = true;
        MqttEnvironmentBox.SelectedValue = "Production";
        MqttUsernameBox.Text = string.Empty;
        MqttPasswordBox.Password = string.Empty;
        MqttTopicBox.Text = "fee-syncer/agent";
        MqttKeepAliveBox.Text = "30";
        MqttReconnectMinBox.Text = "1";
        MqttReconnectMaxBox.Text = "60";
        FeeUpdateEnabledBox.IsChecked = false;
        FeeUpdatePathBox.Text = "C:\\fee-processor";
        FeeUpdateRepositoryBox.Text = "git@github.com:masgeek/fee-processor.git";
        FeeUpdateBranchBox.Text = "main";
        FeeUpdateTagBox.Text = "(none)";
        FeeUpdateIntervalBox.SelectedValue = "24h";
        FeeUpdateBackupBox.Text = "C:\\fee-processor-backups";
        PhpExecutablePathBox.Text = string.Empty;
        ComposerExecutablePathBox.Text = string.Empty;
        GitExecutablePathBox.Text = string.Empty;
        FeeProcessorSshUsernameBox.Text = "git";
        FeeProcessorSshKeyPathBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_ed25519");
        FeeProcessorSshPassphraseBox.Password = string.Empty;
        PhpDetectionText.Text = "PHP path is detected from PATH when blank.";
        ComposerDetectionText.Text = "Composer path is detected from PATH when blank.";
        GitDetectionText.Text = "Git path is detected from PATH when blank.";
    }

    private static string StringValue(JsonElement element, string name, string fallback = "") =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : fallback;

    private static string NumberValue(JsonElement element, string name, int fallback) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number.ToString() : fallback.ToString();

    private static bool BoolValue(JsonElement element, string name, bool fallback) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;

    private async void EnrollButton_Click(object sender, RoutedEventArgs e)
    {
        var code = EnrollmentCodeBox.Text.Trim();
        var name = AgentNameBox.Text.Trim();
        if (!code.StartsWith("enroll_", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Enter a valid enrollment code and agent name.", "Agent Enrollment", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        EnrollButton.IsEnabled = false;
        AgentStatusText.Text = "Enrolling...";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var response = await http.PostAsJsonAsync(
                ConfigReader.CombineUrl(ApiUrlBox.Text, AgentEnrollEndpointBox.Text),
                new { enrollment_code = code, agent_name = name });
            if (!response.IsSuccessStatusCode)
            {
                AgentStatusText.Text = "Enrollment failed";
                MessageBox.Show(response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity
                    ? "The enrollment code is invalid, expired, or already used."
                    : $"FeeSyncer returned HTTP {(int)response.StatusCode}.", "Agent Enrollment", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var payload = await response.Content.ReadFromJsonAsync<EnrollmentResponse>();
            if (payload?.Data?.Token is not { } token || !token.StartsWith("fsk_", StringComparison.Ordinal))
                throw new InvalidOperationException("FeeSyncer returned an invalid agent token.");

            SetAgentToken(token);
            await SaveAgentConfigAsync(token);
            EnrollmentCodeBox.Clear();
            AgentStatusText.Text = "Enrolled";
            _monitor.RestartAgentService();
            MessageBox.Show("Agent enrolled, configuration saved, and the agent service is restarting.", "Agent Enrollment", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AgentStatusText.Text = "Enrollment failed";
            MessageBox.Show($"Enrollment failed: {ex.Message}", "Agent Enrollment", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            EnrollButton.IsEnabled = true;
        }
    }

    private async Task SaveAgentConfigAsync(string token)
    {
        var configPath = ConfigPathResolver.GetMachineAgentConfigFile();
        var sourcePath = File.Exists(configPath) ? configPath : ConfigPathResolver.FindAgentConfigFile();
        var root = File.Exists(sourcePath)
            ? JsonNode.Parse(await File.ReadAllTextAsync(sourcePath))?.AsObject() ?? new JsonObject()
            : new JsonObject();
        var agent = root["Agent"] as JsonObject ?? new JsonObject();
        agent["Enabled"] = AgentEnabledBox.IsChecked == true;
        agent["AgentToken"] = string.IsNullOrWhiteSpace(token) ? AgentTokenBox.Password.Trim() : token;
        agent["RequestTimeoutSeconds"] = ParsedInt(RequestTimeoutBox.Text, 30);
        agent["IdleDelaySeconds"] = ParsedInt(IdleDelayBox.Text, 5);
        agent["HeartbeatSeconds"] = ParsedInt(HeartbeatBox.Text, 60);
        agent["LeaseRenewalSeconds"] = ParsedInt(LeaseRenewalBox.Text, 30);
        agent["LocalApiBaseUrl"] = LocalApiUrlBox.Text.Trim();
        agent["LocalApiUsername"] = LocalApiUsernameBox.Text.Trim();
        agent["LocalApiPassword"] = LocalApiPasswordBox.Password;
        agent["MqttEnabled"] = MqttEnabledBox.IsChecked == true;
        agent["MqttBrokerHost"] = MqttHostBox.Text.Trim();
        agent["MqttBrokerPort"] = ParsedInt(MqttPortBox.Text, 443);
        agent["MqttBrokerPath"] = NormalizeMqttPath(MqttPathBox.Text);
        agent["MqttUseTls"] = MqttTlsBox.IsChecked == true;
        agent["MqttUsername"] = MqttUsernameBox.Text.Trim();
        agent["MqttPassword"] = MqttPasswordBox.Password;
        agent["MqttTopicPrefix"] = MqttTopicBox.Text.Trim();
        agent["MqttKeepAliveSeconds"] = ParsedInt(MqttKeepAliveBox.Text, 30);
        agent["MqttReconnectMinSeconds"] = ParsedInt(MqttReconnectMinBox.Text, 1);
        agent["MqttReconnectMaxSeconds"] = ParsedInt(MqttReconnectMaxBox.Text, 60);
        agent["FeeProcessorUpdateEnabled"] = FeeUpdateEnabledBox.IsChecked == true;
        agent["FeeProcessorUpdateInterval"] = FeeProcessorInterval.Normalize(FeeUpdateIntervalBox.SelectedValue?.ToString());
        agent["FeeProcessorPath"] = FeeUpdatePathBox.Text.Trim();
        agent["FeeProcessorRepository"] = FeeUpdateRepositoryBox.Text.Trim();
        agent["FeeProcessorBranch"] = FeeUpdateBranchBox.Text.Trim();
        agent["FeeProcessorTag"] = string.Equals(FeeUpdateTagBox.Text.Trim(), "(none)", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : FeeUpdateTagBox.Text.Trim();
        agent["FeeProcessorBackupPath"] = FeeUpdateBackupBox.Text.Trim();
        agent["PhpExecutablePath"] = PhpExecutablePathBox.Text.Trim();
        agent["ComposerExecutablePath"] = ComposerExecutablePathBox.Text.Trim();
        agent["GitExecutablePath"] = GitExecutablePathBox.Text.Trim();
        agent["FeeProcessorSshUsername"] = FeeProcessorSshUsernameBox.Text.Trim();
        agent["FeeProcessorSshKeyPath"] = FeeProcessorSshKeyPathBox.Text.Trim();
        agent["FeeProcessorSshPassphrase"] = FeeProcessorSshPassphraseBox.Password;
        root["Agent"] = agent;
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        await File.WriteAllTextAsync(configPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed record EnrollmentResponse(EnrollmentData? Data);
    private sealed record EnrollmentData(string? Token);

    private static int ParsedInt(string value, int fallback) =>
        int.TryParse(value, out var number) ? number : fallback;

    private static string NormalizeEndpoint(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().TrimStart('/');

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

    private async void TestDatabaseButton_Click(object sender, RoutedEventArgs e)
    {
        TestDatabaseButton.IsEnabled = false;
        TestDatabaseButton.Content = "Testing...";
        try
        {
            var result = await new ConnectionValidator().ValidateDatabaseAsync();
            MessageBox.Show($"Database: {(result.Passed ? "OK" : "FAIL")} {result.Details}", "Database Connection Test",
                MessageBoxButton.OK, result.Passed ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Database test failed: {ex.Message}", "Database Connection Test", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TestDatabaseButton.IsEnabled = true;
            TestDatabaseButton.Content = "Test Database";
        }
    }

    private async void TestSmsButton_Click(object sender, RoutedEventArgs e)
    {
        TestSmsButton.IsEnabled = false;
        TestSmsButton.Content = "Testing...";

        try
        {
            var result = await new ConnectionValidator().ValidateSmsApiAsync();
            MessageBox.Show($"SMS API: {(result.Passed ? "OK" : "FAIL")} {result.Details}", "SMS API Test",
                MessageBoxButton.OK,
                result.Passed ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"SMS API test failed: {ex.Message}", "SMS API Test", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TestSmsButton.IsEnabled = true;
            TestSmsButton.Content = "Test SMS API";
        }
    }

    private async void TestAgentApiButton_Click(object sender, RoutedEventArgs e)
    {
        var url = ConfigReader.CombineUrl(ApiUrlBox.Text, AgentWorkEndpointBox.Text) + "?wait=0";
        await RunConnectionTestAsync(
            TestAgentApiButton,
            "Agent API Test",
            () => ConnectionValidator.ValidateHttpAsync(url, AgentTokenBox.Password.Trim()),
            [
                $"HTTP request: GET {url}",
                $"Bearer token: {Configured(AgentTokenBox.Password)}",
                "Timeout: 10 seconds",
            ]);
    }

    private async void TestMqttButton_Click(object sender, RoutedEventArgs e)
    {
        var timeoutSeconds = ParsedInt(RequestTimeoutBox.Text, 30);
        await RunConnectionTestAsync(
            TestMqttButton,
            "MQTT Test",
            ValidateMqttAsync,
            [
                $"WebSocket target: {BuildMqttUri()}",
                $"MQTT username: {(string.IsNullOrWhiteSpace(MqttUsernameBox.Text) ? "Agent token fallback" : "Configured")}",
                $"MQTT password: {Configured(MqttPasswordBox.Password)}",
                $"Timeout: {timeoutSeconds} seconds",
            ]);
    }

    private async void TestLocalAgentButton_Click(object sender, RoutedEventArgs e)
    {
        var timeoutSeconds = ParsedInt(RequestTimeoutBox.Text, 30);
        var loginUrl = LocalApiUrlBox.Text.TrimEnd('/') + "/v1/users/login";
        await RunConnectionTestAsync(
            TestLocalAgentButton,
            "Local Agent Test",
            () => ConnectionValidator.ValidateSchoolApiAsync(
                LocalApiUrlBox.Text,
                LocalApiUsernameBox.Text,
                LocalApiPasswordBox.Password,
                timeoutSeconds),
            [
                $"HTTP request: POST {loginUrl}",
                "Content type: application/json",
                "Request body: fixed-length UTF-8 JSON",
                "Proxy: bypassed for local API diagnostic",
                "Completion: response headers (login body is not downloaded)",
                $"Username: {Configured(LocalApiUsernameBox.Text)}",
                $"Password: {Configured(LocalApiPasswordBox.Password)}",
                $"Timeout: {timeoutSeconds} seconds",
                "Response body: omitted because a successful login may contain an access token",
            ]);
    }

    private async Task RunConnectionTestAsync(
        Button button,
        string title,
        Func<Task<CheckResult>> check,
        IReadOnlyList<string>? diagnostics = null)
    {
        button.IsEnabled = false;
        var originalContent = button.Content;
        button.Content = "Testing...";
        AgentTestOutputGroup.Visibility = Visibility.Visible;
        AgentTestOutputBox.Clear();
        AppendAgentTestOutput($"{DateTime.Now:HH:mm:ss} {title}: testing...");
        if (diagnostics is not null)
        {
            foreach (var diagnostic in diagnostics)
                AppendAgentTestOutput($"  {diagnostic}");
        }
        try
        {
            var result = await check();
            var logMessage = $"{(result.Passed ? "OK" : "FAIL")} {result.Details}";
            AppLogger.Info(title.Replace(" ", string.Empty), logMessage);
            AppendAgentTestOutput($"{DateTime.Now:HH:mm:ss} {title}: {logMessage}");
            MessageBox.Show(
                logMessage,
                title,
                MessageBoxButton.OK,
                result.Passed ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            AppLogger.Error(title.Replace(" ", string.Empty), "Connection test failed.", ex);
            AppendAgentTestOutput($"{DateTime.Now:HH:mm:ss} {title}: FAILED {ex.Message}");
            MessageBox.Show($"{title} failed: {ex.Message}", title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            button.IsEnabled = true;
            button.Content = originalContent;
        }
    }

    private static string Configured(string value) =>
        string.IsNullOrWhiteSpace(value) ? "not configured" : "configured (value hidden)";

    private void AppendAgentTestOutput(string message)
    {
        AgentTestOutputBox.AppendText(message + Environment.NewLine);
        AgentTestOutputBox.ScrollToEnd();
    }

    private async Task<CheckResult> ValidateMqttAsync()
    {
        if (MqttEnabledBox.IsChecked != true)
            return new CheckResult { Details = "MQTT is disabled" };
        if (string.IsNullOrWhiteSpace(MqttHostBox.Text))
            return new CheckResult { Details = "No MQTT broker host configured" };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var factory = new MqttClientFactory();
            using var client = factory.CreateMqttClient();
            var builder = new MqttClientOptionsBuilder()
                .WithClientId($"feesyncer-tray-test-{Guid.NewGuid():N}")
                .WithWebSocketServer(webSocket => webSocket.WithUri(BuildMqttUri()))
                .WithCredentials(
                    string.IsNullOrWhiteSpace(MqttUsernameBox.Text) ? AgentTokenBox.Password : MqttUsernameBox.Text.Trim(),
                    MqttPasswordBox.Password)
                .WithTimeout(TimeSpan.FromSeconds(ParsedInt(RequestTimeoutBox.Text, 30)));

            await client.ConnectAsync(builder.Build());
            await client.DisconnectAsync();
            stopwatch.Stop();
            return new CheckResult
            {
                Passed = true,
                ResponseTime = stopwatch.ElapsedMilliseconds,
                Details = $"Connected to {BuildMqttUri()} ({stopwatch.ElapsedMilliseconds}ms)"
            };
        }
        catch (Exception ex)
        {
            return new CheckResult { Details = ex.Message };
        }
    }

    private string BuildMqttUri()
    {
        if (Uri.TryCreate(MqttHostBox.Text.Trim(), UriKind.Absolute, out var configuredUri)
            && (configuredUri.Scheme == Uri.UriSchemeWs || configuredUri.Scheme == Uri.UriSchemeWss))
        {
            return configuredUri.ToString();
        }

        var scheme = MqttTlsBox.IsChecked == true ? "wss" : "ws";
        return $"{scheme}://{MqttHostBox.Text.Trim().TrimEnd('/')}:{ParsedInt(MqttPortBox.Text, 443)}{NormalizeMqttPath(MqttPathBox.Text)}";
    }

    private static string NormalizeMqttPath(string path) =>
        string.IsNullOrWhiteSpace(path) ? "/mqtt" : "/" + path.Trim().Trim('/');

    private async void PullFeeUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        PullFeeUpdateButton.IsEnabled = false;
        FeeUpdateStatusText.Text = "Updating...";
        try
        {
            await SaveAgentConfigAsync(string.Empty);
            var appPath = FeeUpdatePathBox.Text.Trim();
            var repository = FeeUpdateRepositoryBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(appPath) || string.IsNullOrWhiteSpace(repository))
                throw new InvalidOperationException("Fee Processor application path and repository are required.");

            var tag = FeeUpdateTagBox.Text.Trim();
            var branch = string.IsNullOrWhiteSpace(FeeUpdateBranchBox.Text) ? "main" : FeeUpdateBranchBox.Text.Trim();

            FeeUpdateOutputBox.Clear();
            FeeUpdateOutputBox.Visibility = Visibility.Visible;
            FeeUpdateProgressBar.Visibility = Visibility.Visible;
            var request = new FeeProcessorDeploymentRequest(
                    appPath,
                    repository,
                    branch,
                    tag.Equals("(none)", StringComparison.OrdinalIgnoreCase) ? string.Empty : tag,
                    FeeUpdateBackupBox.Text.Trim(),
                    PhpExecutablePathBox.Text.Trim(),
                    ComposerExecutablePathBox.Text.Trim(),
                    FeeProcessorSshUsernameBox.Text.Trim(),
                    FeeProcessorSshKeyPathBox.Text.Trim(),
                    FeeProcessorSshPassphraseBox.Password,
                    GitExecutablePath: GitExecutablePathBox.Text.Trim());
            await Task.Run(() => new FeeProcessorDeploymentRunner().RunAsync(request, AppendFeeUpdateOutput));

            FeeUpdateStatusText.Text = "Update completed";
            MessageBox.Show("Fee Processor update completed.", "Fee Processor Update", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            FeeUpdateStatusText.Text = "Update failed";
            MessageBox.Show($"Fee Processor update failed: {ex.Message}", "Fee Processor Update", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            FeeUpdateProgressBar.Visibility = Visibility.Collapsed;
            PullFeeUpdateButton.IsEnabled = true;
        }
    }

    private void AppendFeeUpdateOutput(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        FeeProcessorActivityLogger.Write(line);
        AppLogger.Info("FeeProcessorTools", line);
        Dispatcher.BeginInvoke(() =>
        {
            FeeUpdateOutputBox.AppendText(line + Environment.NewLine);
            FeeUpdateOutputBox.ScrollToEnd();
        });
    }

    private async void DetectPhpButton_Click(object sender, RoutedEventArgs e)
    {
        DetectPhpButton.IsEnabled = false;
        DetectPhpButton.Content = "Detecting...";
        PhpDetectionText.Text = "Checking PHP...";
        var configuredPath = PhpExecutablePathBox.Text.Trim();
        FeeUpdateOutputBox.Clear();
        FeeUpdateOutputBox.Visibility = Visibility.Visible;
        try
        {
            var result = await Task.Run(() => FeeProcessorToolResolver.Resolve(configuredPath, "php", AppendFeeUpdateOutput));
            PhpDetectionText.Text = $"PHP: {(string.IsNullOrWhiteSpace(result) ? "not found" : result)}";
        }
        finally
        {
            DetectPhpButton.Content = "Detect PHP";
            DetectPhpButton.IsEnabled = true;
        }
    }

    private async void DetectComposerButton_Click(object sender, RoutedEventArgs e)
    {
        DetectComposerButton.IsEnabled = false;
        DetectComposerButton.Content = "Detecting...";
        ComposerDetectionText.Text = "Checking Composer...";
        var configuredPath = ComposerExecutablePathBox.Text.Trim();
        FeeUpdateOutputBox.Clear();
        FeeUpdateOutputBox.Visibility = Visibility.Visible;
        try
        {
            var result = await Task.Run(() => FeeProcessorToolResolver.Resolve(configuredPath, "composer", AppendFeeUpdateOutput));
            ComposerDetectionText.Text = $"Composer: {(string.IsNullOrWhiteSpace(result) ? "not found" : result)}";
        }
        finally
        {
            DetectComposerButton.Content = "Detect Composer";
            DetectComposerButton.IsEnabled = true;
        }
    }

    private async void DetectGitButton_Click(object sender, RoutedEventArgs e)
    {
        DetectGitButton.IsEnabled = false;
        DetectGitButton.Content = "Detecting...";
        GitDetectionText.Text = "Checking Git...";
        var configuredPath = GitExecutablePathBox.Text.Trim();
        FeeUpdateOutputBox.Clear();
        FeeUpdateOutputBox.Visibility = Visibility.Visible;
        try
        {
            var result = await Task.Run(() => FeeProcessorToolResolver.Resolve(configuredPath, "git", AppendFeeUpdateOutput));
            GitDetectionText.Text = $"Git: {(string.IsNullOrWhiteSpace(result) ? "not found" : result)}";
        }
        finally
        {
            DetectGitButton.Content = "Detect Git";
            DetectGitButton.IsEnabled = true;
        }
    }

    private async Task UpdateFeeToolDetectionTextAsync()
    {
        var phpPath = PhpExecutablePathBox.Text.Trim();
        var composerPath = ComposerExecutablePathBox.Text.Trim();
        var gitPath = GitExecutablePathBox.Text.Trim();
        var results = await Task.WhenAll(
            Task.Run(() => FeeProcessorToolResolver.Resolve(phpPath, "php")),
            Task.Run(() => FeeProcessorToolResolver.Resolve(composerPath, "composer")),
            Task.Run(() => FeeProcessorToolResolver.Resolve(gitPath, "git")));
        PhpDetectionText.Text = $"PHP: {(string.IsNullOrWhiteSpace(results[0]) ? "not found" : results[0])}";
        ComposerDetectionText.Text = $"Composer: {(string.IsNullOrWhiteSpace(results[1]) ? "not found" : results[1])}";
        GitDetectionText.Text = $"Git: {(string.IsNullOrWhiteSpace(results[2]) ? "not found" : results[2])}";
    }

    private void BrowseFeeUpdatePath_Click(object sender, RoutedEventArgs e) => BrowseFolder(FeeUpdatePathBox);
    private void BrowseBackupPath_Click(object sender, RoutedEventArgs e) => BrowseFolder(FeeUpdateBackupBox);
    private void BrowseSshKey_Click(object sender, RoutedEventArgs e) => BrowseFile(FeeProcessorSshKeyPathBox, "SSH private key|*|All files|*.*");
    private void BrowsePhp_Click(object sender, RoutedEventArgs e) => BrowseFile(PhpExecutablePathBox, "PHP executable|php.exe;php.bat;php.cmd|All files|*.*");
    private void BrowseComposer_Click(object sender, RoutedEventArgs e) => BrowseFile(ComposerExecutablePathBox, "Composer executable|composer.phar;composer.exe;composer.bat|All files|*.*");
    private void BrowseGit_Click(object sender, RoutedEventArgs e) => BrowseFile(GitExecutablePathBox, "Git executable|git.exe|All files|*.*");
    private void ClearSshKey_Click(object sender, RoutedEventArgs e) => FeeProcessorSshKeyPathBox.Clear();
    private void ClearPhp_Click(object sender, RoutedEventArgs e) => PhpExecutablePathBox.Clear();
    private void ClearComposer_Click(object sender, RoutedEventArgs e) => ComposerExecutablePathBox.Clear();
    private void ClearGit_Click(object sender, RoutedEventArgs e) => GitExecutablePathBox.Clear();

    private static void BrowseFolder(TextBox target)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select folder", InitialDirectory = Directory.Exists(target.Text) ? target.Text : string.Empty };
        if (dialog.ShowDialog() == true) target.Text = dialog.FolderName;
    }

    private static void BrowseFile(TextBox target, string filter)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select executable or key file",
            Filter = filter,
            FileName = File.Exists(target.Text) ? target.Text : string.Empty
        };
        if (dialog.ShowDialog() == true) target.Text = dialog.FileName;
    }

    private void UpdatePhpDetectionText()
    {
        var php = FeeProcessorToolResolver.Resolve(PhpExecutablePathBox.Text.Trim(), "php");
        PhpDetectionText.Text = $"PHP: {(string.IsNullOrWhiteSpace(php) ? "not found" : php)}";
    }

    private void UpdateComposerDetectionText()
    {
        var composer = FeeProcessorToolResolver.Resolve(ComposerExecutablePathBox.Text.Trim(), "composer");
        ComposerDetectionText.Text = $"Composer: {(string.IsNullOrWhiteSpace(composer) ? "not found" : composer)}";
    }

    private void UpdateGitDetectionText()
    {
        var git = FeeProcessorToolResolver.Resolve(GitExecutablePathBox.Text.Trim(), "git");
        GitDetectionText.Text = $"Git: {(string.IsNullOrWhiteSpace(git) ? "not found" : git)}";
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var configPath = ConfigPathResolver.GetMachineConfigFile();
            var sourcePath = File.Exists(configPath) ? configPath : ConfigPathResolver.FindConfigFile();

            if (!File.Exists(sourcePath))
            {
                MessageBox.Show("Config file not found. Cannot save.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var connectionString = BuildConnectionString();

            var json = await File.ReadAllTextAsync(sourcePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement.Clone();

            var mutable = new Dictionary<string, object?>();

            foreach (var prop in root.EnumerateObject())
                mutable[prop.Name] = prop.Value.Clone();

            var smsDict = new Dictionary<string, object?>();
            if (mutable.TryGetValue("SmsService", out var smsObj) && smsObj is JsonElement smsElement)
            {
                foreach (var prop in smsElement.EnumerateObject())
                    smsDict[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                        ? (object)prop.Value.GetString()!
                        : prop.Value.GetRawText();
            }
            smsDict["ConnectionString"] = connectionString;
            smsDict["AuthorizationToken"] = TokenBox.Password;
            if (int.TryParse(BackoffBox.Text, out var backoff)) smsDict["RetryBackoffSeconds"] = backoff;
            if (int.TryParse(PollIntervalBox.Text, out var poll)) smsDict["RetryPollIntervalSeconds"] = poll;
            if (int.TryParse(RetentionBox.Text, out var retention)) smsDict["LogRetentionDays"] = retention;
            if (int.TryParse(MaxSizeBox.Text, out var maxSize)) smsDict["MaxLogFileSizeMb"] = maxSize;
            mutable["SmsService"] = smsDict;

            mutable["Tray"] = new Dictionary<string, object?>
            {
                ["StartMinimizedToTray"] = TrayStartMinimizedBox.IsChecked == true,
                ["UpdateCheckInterval"] = UpdateCheckSchedule.Normalize(UpdateCheckIntervalBox.SelectedValue?.ToString()),
            };

            mutable["FeeSyncer"] = new Dictionary<string, object?>
            {
                ["BaseUrl"] = ApiUrlBox.Text.TrimEnd('/') + "/",
                ["ApiEndpoints"] = new Dictionary<string, object?>
                {
                    ["SmsNotifications"] = NormalizeEndpoint(SmsNotificationsEndpointBox.Text, Constants.DefaultSmsNotificationsEndpoint),
                    ["AgentEnroll"] = NormalizeEndpoint(AgentEnrollEndpointBox.Text, Constants.DefaultAgentEnrollEndpoint),
                    ["AgentWork"] = NormalizeEndpoint(AgentWorkEndpointBox.Text, Constants.DefaultAgentWorkEndpoint),
                    ["AgentHeartbeat"] = NormalizeEndpoint(AgentHeartbeatEndpointBox.Text, Constants.DefaultAgentHeartbeatEndpoint),
                    ["AgentRenew"] = NormalizeEndpoint(AgentRenewEndpointBox.Text, Constants.DefaultAgentRenewEndpoint),
                    ["AgentPage"] = NormalizeEndpoint(AgentPageEndpointBox.Text, Constants.DefaultAgentPageEndpoint),
                    ["AgentComplete"] = NormalizeEndpoint(AgentCompleteEndpointBox.Text, Constants.DefaultAgentCompleteEndpoint),
                    ["AgentPaymentComplete"] = NormalizeEndpoint(AgentPaymentCompleteEndpointBox.Text, Constants.DefaultAgentPaymentCompleteEndpoint),
                    ["AgentFail"] = NormalizeEndpoint(AgentFailEndpointBox.Text, Constants.DefaultAgentFailEndpoint),
                }
            };

            var output = JsonSerializer.Serialize(mutable, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(configPath, output);
            await SaveAgentConfigAsync(string.Empty);
            _updater.SetCheckInterval(UpdateCheckSchedule.ParseOrDefault(UpdateCheckIntervalBox.SelectedValue?.ToString()));

            var result = MessageBox.Show(
                "Configuration saved. Restart SMS and Agent services now?",
                "Saved",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                _monitor.RestartService();
                _monitor.RestartAgentService();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

}
