namespace SmsNotificationService;

public class SmsServiceOptions
{
    public const string SectionName = "SmsService";

    public string ConnectionString { get; set; } = string.Empty;
    public string SmsApiUrl { get; set; } = string.Empty;
}
