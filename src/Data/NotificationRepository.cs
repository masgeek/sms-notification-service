using SmsNotificationService.Models;
using Microsoft.Data.SqlClient;
using Dapper;

namespace SmsNotificationService.Data;

public class NotificationRepository(string connectionString, ILogger<NotificationRepository> logger) : INotificationRepository
{

    private readonly ILogger<NotificationRepository> _logger = logger;

    public async Task<List<SmsNotification>> GetPendingAsync()
    {

        using var connection = new SqlConnection(connectionString);
        var notifications = await connection.QueryAsync<SmsNotification>(
            "SELECT * FROM sms_notifications WHERE status = @Status AND (retry_after IS NULL OR retry_after <= @Now)",
            new { Status = nameof(NotificationStatus.PENDING), Now = DateTime.UtcNow });

        return notifications.ToList();
    }

    public async Task UpdateStatusAsync(long notificationId, NotificationStatus status)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(
            "UPDATE sms_notifications SET status = @Status, updated_at = @UpdatedAt WHERE id = @Id",
            new { Status = status.ToString(), UpdatedAt = DateTime.UtcNow, Id = notificationId });

        _logger.LogDebug("[DB] Notification {Id} status → {Status}", notificationId, status);
    }

    public async Task UpdateRetryAsync(long notificationId, int retryCount, DateTimeOffset retryAfter)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(
            "UPDATE sms_notifications SET retry_count = @RetryCount, retry_after = @RetryAfter, updated_at = @UpdatedAt WHERE id = @Id",
            new { RetryCount = retryCount, RetryAfter = retryAfter, UpdatedAt = DateTime.UtcNow, Id = notificationId });

        _logger.LogDebug("[DB] Notification {Id} retry → {Count}, next attempt at {RetryAfter}", notificationId, retryCount, retryAfter);
    }

    public async Task UpdateDescriptionAsync(long notificationId, string description)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.ExecuteAsync(
            "UPDATE sms_notifications SET description_json = @Description, updated_at = @UpdatedAt WHERE id = @Id",
            new { Description = description, UpdatedAt = DateTime.UtcNow, Id = notificationId });

        _logger.LogDebug("[DB] Notification {Id} description updated", notificationId);
    }
}
