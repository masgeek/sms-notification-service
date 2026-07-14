using SmsNotificationService.Models;

namespace SmsNotificationService.Services;

public interface ISmsSender
{
    Task<bool> SendAsync(SmsNotification notification);
    DateTimeOffset CalculateRetryAfter(int retryCount);
}
