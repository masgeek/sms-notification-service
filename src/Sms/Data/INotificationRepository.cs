using FeeSyncer.Sms.Models;

namespace FeeSyncer.Sms.Data;

public interface INotificationRepository
{
    Task<List<SmsNotification>> GetPendingAsync();
    Task UpdateStatusAsync(long notificationId, NotificationStatus status);
    Task UpdateRetryAsync(long notificationId, int retryCount, DateTimeOffset retryAfter);
    Task UpdateDescriptionAsync(long notificationId, string description);
}
