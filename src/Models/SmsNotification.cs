namespace SmsNotificationService.Models;

public class SmsNotification
{
    public long Id { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string MpesaCode { get; set; } = string.Empty;
    public string AdmNo { get; set; } = string.Empty;
    public string? StudNames { get; set; }
    public decimal? Amount { get; set; }
    public string? ReceiptNo { get; set; }
    public DateTime? Dated { get; set; }
    public string? Description { get; set; }
    public NotificationStatus Status { get; set; } = NotificationStatus.PENDING;
    public int MaxRetries { get; set; }
    public int RetryCount { get; set; }
    public DateTimeOffset? RetryAfter { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
