using SmsNotificationService.Models;

namespace SmsNotificationService.Data;

public interface INotificationRepository
{
    Task<List<SmsNotification>> GetPendingAsync();
    Task UpdateStatusAsync(long notificationId, NotificationStatus status);
    Task UpdateRetryAsync(long notificationId, int retryCount, DateTimeOffset retryAfter);
}
