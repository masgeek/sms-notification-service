namespace SmsNotificationService;

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
    public string status { get; set; } = "PENDING";
    public DateTimeOffset? created_at { get; set; }
    public DateTimeOffset? updated_at { get; set; }
}
