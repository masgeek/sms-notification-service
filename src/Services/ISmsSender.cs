using SmsNotificationService.Models;

namespace SmsNotificationService.Services;

public interface ISmsSender
{
    Task<SendResult> SendAsync(SmsNotification notification);
    DateTimeOffset CalculateRetryAfter(int retryCount);
}

public class SendResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public static SendResult Ok() => new() { Success = true };
    public static SendResult Fail(string? errorMessage) => new() { Success = false, ErrorMessage = errorMessage };
}
