using SmsNotificationService.Data;
using SmsNotificationService.Models;
using SmsNotificationService.Services;

namespace SmsNotificationService.Workers;

/// <summary>
/// Shared notification processing logic used by both the table change listener
/// and the retry poller. Thread-safe via SemaphoreSlim.
/// </summary>
public sealed class NotificationProcessor
{
    private readonly ILogger<NotificationProcessor> _logger;
    private readonly INotificationRepository _repository;
    private readonly ISmsSender _smsSender;
    private readonly SemaphoreSlim _processingLock = new(1, 1);
    private int _inFlightCount;

    public int InFlightCount => _inFlightCount;

    public NotificationProcessor(ILogger<NotificationProcessor> logger, INotificationRepository repository, ISmsSender smsSender)
    {
        _logger = logger;
        _repository = repository;
        _smsSender = smsSender;
    }

    public async Task ProcessPendingAsync(CancellationToken stoppingToken)
    {
        if (!await _processingLock.WaitAsync(0, stoppingToken))
        {
            _logger.LogDebug("[Queue] Batch already in progress, skipping this event");
            return;
        }

        try
        {
            var notifications = await _repository.GetPendingAsync();

            if (notifications.Count == 0)
            {
                _logger.LogDebug("[Queue] No pending notifications");
                return;
            }

            _logger.LogInformation("[Queue] Found {Count} pending notification(s)", notifications.Count);

            foreach (var notification in notifications)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning("[Queue] Shutdown requested, {Remaining} notification(s) skipped",
                        notifications.Count - notifications.IndexOf(notification));
                    break;
                }

                await ProcessNotification(notification);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Queue] Failed to process pending notifications");
        }
        finally
        {
            _processingLock.Release();
        }
    }

    private async Task ProcessNotification(SmsNotification notification)
    {
        var notifId = notification.Id;
        var phone = notification.PhoneNumber;

        Interlocked.Increment(ref _inFlightCount);
        try
        {
            var result = await _smsSender.SendAsync(notification);

            if (result.Success)
            {
                await _repository.UpdateStatusAsync(notifId, NotificationStatus.PROCESSED);
                return;
            }

            // Save the error response from the API
            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                await _repository.UpdateDescriptionAsync(notifId, result.ErrorMessage);
            }

            // Non-retryable errors (e.g. 400 Bad Request) — cancel immediately
            if (!result.Retryable)
            {
                _logger.LogError("[SMS] Notification {Id} to {Phone} failed with non-retryable error — CANCELLED",
                    notifId, phone);
                await _repository.UpdateStatusAsync(notifId, NotificationStatus.CANCELLED);
                return;
            }

            var nextRetry = notification.RetryCount + 1;
            if (nextRetry >= notification.MaxRetries)
            {
                _logger.LogError("[SMS] Notification {Id} to {Phone} failed after {Count}/{Limit} retries — CANCELLED",
                    notifId, phone, nextRetry, notification.MaxRetries);
                await _repository.UpdateStatusAsync(notifId, NotificationStatus.CANCELLED);
            }
            else
            {
                var retryAfter = _smsSender.CalculateRetryAfter(nextRetry);
                _logger.LogWarning("[SMS] Notification {Id} to {Phone} failed — retry {Count}/{Limit} scheduled at {RetryAfter}",
                    notifId, phone, nextRetry, notification.MaxRetries, retryAfter);
                await _repository.UpdateRetryAsync(notifId, nextRetry, retryAfter);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _inFlightCount);
        }
    }
}
