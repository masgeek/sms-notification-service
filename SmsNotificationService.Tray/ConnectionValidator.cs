using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using SmsNotificationService.Tray.Helpers;

namespace SmsNotificationService.Tray;

internal sealed class ConnectionValidator
{
    private DateTime _lastValidation = DateTime.MinValue;
    private ValidationResult? _lastResult;

    public async Task<ValidationResult> ValidateAsync()
    {
        if (DateTime.UtcNow - _lastValidation < TimeSpan.FromSeconds(30) && _lastResult is not null)
            return _lastResult;

        var connectionString = LoadConnectionString();
        var apiUrl = LoadApiUrl();

        var dbTask = ValidateDbAsync(connectionString);
        var apiTask = ValidateApiAsync(apiUrl);
        var brokerTask = ValidateBrokerAsync(connectionString);

        await Task.WhenAll(dbTask, apiTask, brokerTask);

        var result = new ValidationResult
        {
            DbStatus = await dbTask,
            ApiStatus = await apiTask,
            BrokerStatus = await brokerTask
        };

        _lastValidation = DateTime.UtcNow;
        _lastResult = result;
        return result;
    }

    private static async Task<CheckResult> ValidateDbAsync(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return new CheckResult { Passed = false, Details = "No connection string configured" };

        try
        {
            var sw = Stopwatch.StartNew();
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            sw.Stop();

            var tableExists = await conn.QuerySingleOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'sms_notifications'");

            return new CheckResult
            {
                Passed = true,
                ResponseTime = sw.ElapsedMilliseconds,
                Details = $"Connected ({sw.ElapsedMilliseconds}ms), sms_notifications table {(tableExists > 0 ? "exists" : "MISSING")}"
            };
        }
        catch (Exception ex)
        {
            return new CheckResult { Passed = false, Details = ex.Message };
        }
    }

    private static async Task<CheckResult> ValidateApiAsync(string apiUrl)
    {
        if (string.IsNullOrWhiteSpace(apiUrl))
            return new CheckResult { Passed = false, Details = "No API URL configured" };

        try
        {
            var sw = Stopwatch.StartNew();
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = await http.GetAsync(apiUrl);
            sw.Stop();

            return new CheckResult
            {
                Passed = response.IsSuccessStatusCode,
                ResponseTime = sw.ElapsedMilliseconds,
                Details = $"{(int)response.StatusCode} {response.ReasonPhrase} ({sw.ElapsedMilliseconds}ms)"
            };
        }
        catch (Exception ex)
        {
            return new CheckResult { Passed = false, Details = ex.Message };
        }
    }

    private static async Task<CheckResult> ValidateBrokerAsync(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return new CheckResult { Passed = false, Details = "No connection string configured" };

        try
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();
            var brokerActive = await conn.QuerySingleOrDefaultAsync<bool>(
                "SELECT is_broker_enabled FROM sys.databases WHERE name = DB_NAME()");

            return new CheckResult
            {
                Passed = brokerActive,
                Details = brokerActive ? "Active" : "Disabled"
            };
        }
        catch (Exception ex)
        {
            return new CheckResult { Passed = false, Details = ex.Message };
        }
    }

    private static string LoadConnectionString()
    {
        try
        {
            if (!File.Exists(Paths.ConfigFile)) return string.Empty;
            var json = File.ReadAllText(Paths.ConfigFile);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("ConnectionStrings", out var cs) &&
                cs.TryGetProperty("DefaultConnection", out var conn))
                return conn.GetString() ?? string.Empty;
        }
        catch { /* ignore */ }
        return string.Empty;
    }

    private static string LoadApiUrl()
    {
        try
        {
            if (!File.Exists(Paths.ConfigFile)) return string.Empty;
            var json = File.ReadAllText(Paths.ConfigFile);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("SmsService", out var sms) &&
                sms.TryGetProperty("SmsApiUrl", out var url))
                return url.GetString() ?? string.Empty;
        }
        catch { /* ignore */ }
        return string.Empty;
    }
}

internal sealed class ValidationResult
{
    public CheckResult DbStatus { get; set; } = new();
    public CheckResult ApiStatus { get; set; } = new();
    public CheckResult BrokerStatus { get; set; } = new();

    public bool AllPassed => DbStatus.Passed && ApiStatus.Passed && BrokerStatus.Passed;

    public string Summary => string.Join("\n", new[]
    {
        $"Database: {(DbStatus.Passed ? "OK" : "FAIL")} {DbStatus.Details}",
        $"SMS API: {(ApiStatus.Passed ? "OK" : "FAIL")} {ApiStatus.Details}",
        $"Broker: {(BrokerStatus.Passed ? "OK" : "FAIL")} {BrokerStatus.Details}"
    });
}

internal sealed class CheckResult
{
    public bool Passed { get; set; }
    public long ResponseTime { get; set; }
    public string Details { get; set; } = string.Empty;
}
