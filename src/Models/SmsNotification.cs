namespace SmsNotificationService.Models;

public class SmsNotification
{
    public long id { get; set; }
    public string phone_number { get; set; } = string.Empty;
    public string mpesa_code { get; set; } = string.Empty;
    public string adm_no { get; set; } = string.Empty;
    public string? stud_names { get; set; }
    public decimal? amount { get; set; }
    public string? receipt_no { get; set; }
    public DateTime? dated { get; set; }
    public NotificationStatus status { get; set; } = NotificationStatus.PENDING;
    public int max_retries { get; set; }
    public int retry_count { get; set; }
    public DateTimeOffset? retry_after { get; set; }
    public DateTimeOffset? created_at { get; set; }
    public DateTimeOffset? updated_at { get; set; }
}
