namespace SmsNotificationService.Configuration;

public class SmsServiceOptions
{
    public const string SectionName = "SmsService";

    public string ConnectionString { get; set; } = string.Empty;
    public string SmsApiUrl { get; set; } = string.Empty;
    public string AuthorizationToken { get; set; } = string.Empty;
    public int RetryBackoffSeconds { get; set; } = 30;
    public int LogRetentionDays { get; set; } = 7;
    public long MaxLogFileSizeMb { get; set; } = 10;
}
