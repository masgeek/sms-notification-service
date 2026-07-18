using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace SmsNotificationService.Shared;

public static class ConfigReader
{
    public static string LoadConnectionString(string configPath)
    {
        try
        {
            if (!File.Exists(configPath)) return string.Empty;
            var json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("SmsService", out var sms) &&
                sms.TryGetProperty("ConnectionString", out var conn))
                return conn.GetString() ?? string.Empty;
        }
        catch { /* ignore */ }
        return string.Empty;
    }

    public static string LoadApiUrl(string configPath)
    {
        try
        {
            if (!File.Exists(configPath)) return string.Empty;
            var json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("SmsService", out var sms) &&
                sms.TryGetProperty("SmsApiUrl", out var url))
                return url.GetString() ?? string.Empty;
        }
        catch { /* ignore */ }
        return string.Empty;
    }

    public static string LoadAuthorizationToken(string configPath)
    {
        try
        {
            if (!File.Exists(configPath)) return string.Empty;
            var json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("SmsService", out var sms) &&
                sms.TryGetProperty("AuthorizationToken", out var token))
                return token.GetString() ?? string.Empty;
        }
        catch { /* ignore */ }
        return string.Empty;
    }

    public static (string Server, string Database, string UserId, string Password, SqlConnectionEncryptOption Encrypt) ParseConnectionString(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        return (builder.DataSource, builder.InitialCatalog, builder.UserID, builder.Password, builder.Encrypt);
    }

    public static string BuildConnectionString(string server, string database, string userId, string password)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = database,
            UserID = userId,
            Password = password,
            TrustServerCertificate = true
        };
        return builder.ConnectionString;
    }
}
