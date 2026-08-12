using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Data.SqlClient;
using FeeSyncer.Shared;

namespace FeeSyncer.Tray;

public partial class ConfigEditor : Window
{
    private readonly ServiceMonitor _monitor;

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
            LoadSmsDefaults();
            var configPath = ConfigPathResolver.FindConfigFile();
            if (!File.Exists(configPath))
            {
                LoadAgentConfig();
                return;
            }
            var json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("SmsService", out var sms))
            {
                if (sms.TryGetProperty("ConnectionString", out var connStr))
                    ParseConnectionString(connStr.GetString() ?? string.Empty);

                if (sms.TryGetProperty("SmsApiUrl", out var url))
                    ApiUrlBox.Text = url.GetString() ?? string.Empty;

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
        AgentServerUrlBox.Text = StringValue(agent, "ServerUrl", "https://fees.munywele.co.ke/");
        AgentTokenBox.Text = StringValue(agent, "AgentToken");
        RequestTimeoutBox.Text = NumberValue(agent, "RequestTimeoutSeconds", 30);
        IdleDelayBox.Text = NumberValue(agent, "IdleDelaySeconds", 5);
        HeartbeatBox.Text = NumberValue(agent, "HeartbeatSeconds", 60);
        LeaseRenewalBox.Text = NumberValue(agent, "LeaseRenewalSeconds", 30);
        LocalApiUrlBox.Text = StringValue(agent, "LocalApiBaseUrl", "http://127.0.0.1:8001/api/");
        LocalApiUsernameBox.Text = StringValue(agent, "LocalApiUsername");
        LocalApiPasswordBox.Password = StringValue(agent, "LocalApiPassword");
        MqttEnabledBox.IsChecked = BoolValue(agent, "MqttEnabled", true);
        MqttHostBox.Text = StringValue(agent, "MqttBrokerHost", "mqtt.munywele.co.ke");
        MqttPortBox.Text = NumberValue(agent, "MqttBrokerPort", 8883);
        MqttTlsBox.IsChecked = BoolValue(agent, "MqttUseTls", true);
        MqttUsernameBox.Text = StringValue(agent, "MqttUsername");
        MqttPasswordBox.Password = StringValue(agent, "MqttPassword");
        MqttTopicBox.Text = StringValue(agent, "MqttTopicPrefix", "fee-syncer/agent");
        MqttKeepAliveBox.Text = NumberValue(agent, "MqttKeepAliveSeconds", 30);
        MqttReconnectMinBox.Text = NumberValue(agent, "MqttReconnectMinSeconds", 1);
        MqttReconnectMaxBox.Text = NumberValue(agent, "MqttReconnectMaxSeconds", 60);
        FeeUpdateEnabledBox.IsChecked = BoolValue(agent, "FeeProcessorUpdateEnabled", false);
        FeeUpdatePathBox.Text = StringValue(agent, "FeeProcessorPath");
        FeeUpdateRepositoryBox.Text = StringValue(agent, "FeeProcessorRepository");
        FeeUpdateBranchBox.Text = StringValue(agent, "FeeProcessorBranch", "main");
        FeeUpdateTagBox.Text = StringValue(agent, "FeeProcessorTag");
        FeeUpdateIntervalBox.Text = NumberValue(agent, "FeeProcessorUpdateIntervalHours", 24);
        FeeUpdateBackupBox.Text = StringValue(agent, "FeeProcessorBackupPath", "C:\\fee-processor-backups");
        AgentStatusText.Text = AgentTokenBox.Text.StartsWith("fsk_", StringComparison.Ordinal)
            ? "Enrolled"
            : "Not enrolled";
        AgentStatusSummaryText.Text = AgentStatusText.Text;
    }

    private void LoadSmsDefaults()
    {
        ApiUrlBox.Text = "https://fees.munywele.co.ke/api/v1/notifications";
        TokenBox.Password = string.Empty;
        BackoffBox.Text = "30";
        PollIntervalBox.Text = "30";
        RetentionBox.Text = "7";
        MaxSizeBox.Text = "10";
    }

    private void LoadAgentDefaults()
    {
        AgentEnabledBox.IsChecked = true;
        AgentServerUrlBox.Text = "https://fees.munywele.co.ke/";
        AgentTokenBox.Text = string.Empty;
        RequestTimeoutBox.Text = "30";
        IdleDelayBox.Text = "5";
        HeartbeatBox.Text = "60";
        LeaseRenewalBox.Text = "30";
        LocalApiUrlBox.Text = "http://127.0.0.1:8001/api/";
        LocalApiUsernameBox.Text = string.Empty;
        LocalApiPasswordBox.Password = string.Empty;
        MqttEnabledBox.IsChecked = true;
        MqttHostBox.Text = "mqtt.munywele.co.ke";
        MqttPortBox.Text = "8883";
        MqttTlsBox.IsChecked = true;
        MqttUsernameBox.Text = string.Empty;
        MqttPasswordBox.Password = string.Empty;
        MqttTopicBox.Text = "fee-syncer/agent";
        MqttKeepAliveBox.Text = "30";
        MqttReconnectMinBox.Text = "1";
        MqttReconnectMaxBox.Text = "60";
        FeeUpdateEnabledBox.IsChecked = false;
        FeeUpdatePathBox.Text = string.Empty;
        FeeUpdateRepositoryBox.Text = string.Empty;
        FeeUpdateBranchBox.Text = "main";
        FeeUpdateTagBox.Text = string.Empty;
        FeeUpdateIntervalBox.Text = "24";
        FeeUpdateBackupBox.Text = "C:\\fee-processor-backups";
    }

    private static string StringValue(JsonElement element, string name, string fallback = "") =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
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
                AgentServerUrlBox.Text.TrimEnd('/') + "/api/agent/enroll",
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
        var configPath = ConfigPathResolver.FindAgentConfigFile();
        var root = File.Exists(configPath)
            ? JsonNode.Parse(await File.ReadAllTextAsync(configPath))?.AsObject() ?? new JsonObject()
            : new JsonObject();
        var agent = root["Agent"] as JsonObject ?? new JsonObject();
        agent["Enabled"] = AgentEnabledBox.IsChecked == true;
        agent["ServerUrl"] = AgentServerUrlBox.Text.Trim();
        agent["AgentToken"] = string.IsNullOrWhiteSpace(token) ? AgentTokenBox.Text.Trim() : token;
        agent["RequestTimeoutSeconds"] = ParsedInt(RequestTimeoutBox.Text, 30);
        agent["IdleDelaySeconds"] = ParsedInt(IdleDelayBox.Text, 5);
        agent["HeartbeatSeconds"] = ParsedInt(HeartbeatBox.Text, 60);
        agent["LeaseRenewalSeconds"] = ParsedInt(LeaseRenewalBox.Text, 30);
        agent["LocalApiBaseUrl"] = LocalApiUrlBox.Text.Trim();
        agent["LocalApiUsername"] = LocalApiUsernameBox.Text.Trim();
        agent["LocalApiPassword"] = LocalApiPasswordBox.Password;
        agent["MqttEnabled"] = MqttEnabledBox.IsChecked == true;
        agent["MqttBrokerHost"] = MqttHostBox.Text.Trim();
        agent["MqttBrokerPort"] = ParsedInt(MqttPortBox.Text, 8883);
        agent["MqttUseTls"] = MqttTlsBox.IsChecked == true;
        agent["MqttUsername"] = MqttUsernameBox.Text.Trim();
        agent["MqttPassword"] = MqttPasswordBox.Password;
        agent["MqttTopicPrefix"] = MqttTopicBox.Text.Trim();
        agent["MqttKeepAliveSeconds"] = ParsedInt(MqttKeepAliveBox.Text, 30);
        agent["MqttReconnectMinSeconds"] = ParsedInt(MqttReconnectMinBox.Text, 1);
        agent["MqttReconnectMaxSeconds"] = ParsedInt(MqttReconnectMaxBox.Text, 60);
        agent["FeeProcessorUpdateEnabled"] = FeeUpdateEnabledBox.IsChecked == true;
        agent["FeeProcessorUpdateIntervalHours"] = ParsedInt(FeeUpdateIntervalBox.Text, 24);
        agent["FeeProcessorPath"] = FeeUpdatePathBox.Text.Trim();
        agent["FeeProcessorRepository"] = FeeUpdateRepositoryBox.Text.Trim();
        agent["FeeProcessorBranch"] = FeeUpdateBranchBox.Text.Trim();
        agent["FeeProcessorTag"] = FeeUpdateTagBox.Text.Trim();
        agent["FeeProcessorBackupPath"] = FeeUpdateBackupBox.Text.Trim();
        root["Agent"] = agent;
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        await File.WriteAllTextAsync(configPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed record EnrollmentResponse(EnrollmentData? Data);
    private sealed record EnrollmentData(string? Token);

    private static int ParsedInt(string value, int fallback) =>
        int.TryParse(value, out var number) ? number : fallback;

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

    private async void TestSmsButton_Click(object sender, RoutedEventArgs e)
    {
        TestSmsButton.IsEnabled = false;
        TestSmsButton.Content = "Testing...";

        try
        {
            var validator = new ConnectionValidator();
            var result = await validator.ValidateAsync();

            var msg = $"Database: {(result.DbStatus.Passed ? "OK" : "FAIL")} {result.DbStatus.Details}\n" +
                      $"SMS API: {(result.ApiStatus.Passed ? "OK" : "FAIL")} {result.ApiStatus.Details}\n" +
                      $"Broker: {(result.BrokerStatus.Passed ? "OK" : "FAIL")} {result.BrokerStatus.Details}";

            MessageBox.Show(msg, "SMS Connection Test",
                MessageBoxButton.OK,
                result.AllPassed ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"SMS test failed: {ex.Message}", "SMS Connection Test", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TestSmsButton.IsEnabled = true;
            TestSmsButton.Content = "Test SMS Connections";
        }
    }

    private async void TestAgentButton_Click(object sender, RoutedEventArgs e)
    {
        TestAgentButton.IsEnabled = false;
        TestAgentButton.Content = "Testing...";
        try
        {
            var gateway = await ConnectionValidator.ValidateHttpAsync(
                AgentServerUrlBox.Text.TrimEnd('/') + "/api/agent/work?wait=0", AgentTokenBox.Text.Trim());
            var local = await ConnectionValidator.ValidateHttpAsync(LocalApiUrlBox.Text.TrimEnd('/') + "/");
            var message = $"Central gateway: {(gateway.Passed ? "OK" : "FAIL")} {gateway.Details}\n" +
                          $"Local fee processor: {(local.Passed ? "OK" : "FAIL")} {local.Details}";
            MessageBox.Show(message, "Agent Connection Test", MessageBoxButton.OK,
                gateway.Passed && local.Passed ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Agent test failed: {ex.Message}", "Agent Connection Test", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            TestAgentButton.IsEnabled = true;
            TestAgentButton.Content = "Test Agent Connections";
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
                smsDict["AuthorizationToken"] = TokenBox.Password;
                if (int.TryParse(BackoffBox.Text, out var backoff)) smsDict["RetryBackoffSeconds"] = backoff;
                if (int.TryParse(PollIntervalBox.Text, out var poll)) smsDict["RetryPollIntervalSeconds"] = poll;
                if (int.TryParse(RetentionBox.Text, out var retention)) smsDict["LogRetentionDays"] = retention;
                if (int.TryParse(MaxSizeBox.Text, out var maxSize)) smsDict["MaxLogFileSizeMb"] = maxSize;
                mutable["SmsService"] = smsDict;
            }

            var output = JsonSerializer.Serialize(mutable, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(configPath, output);
            await SaveAgentConfigAsync(string.Empty);

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

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

}
