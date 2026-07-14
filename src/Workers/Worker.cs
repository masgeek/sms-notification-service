using SmsNotificationService.Data;
using SmsNotificationService.Models;
using SmsNotificationService.Services;

namespace SmsNotificationService.Workers;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly INotificationRepository _repository;
    private readonly ISmsSender _smsSender;
    private readonly SqlDependencyListener _listener;

    private readonly SemaphoreSlim _processingLock = new(1, 1);
    private int _inFlightCount;
    private bool _listenerActive;

    private const int ShutdownTimeoutSeconds = 30;

    public bool IsListenerActive => _listenerActive;

    public Worker(ILogger<Worker> logger, INotificationRepository repository, ISmsSender smsSender, SqlDependencyListener listener)
    {
        _logger = logger;
        _repository = repository;
        _smsSender = smsSender;
        _listener = listener;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Start] Initializing SMS notification service");

        try
        {
            await ProcessPendingNotifications(stoppingToken);

            _listener.Start();
            _logger.LogInformation("[Listener] SqlDependency started, registering query...");

            _listener.RegisterQueryWithRetry(
                onChanges: () => ProcessPendingNotifications(stoppingToken).GetAwaiter().GetResult(),
                stoppingToken: stoppingToken);

            _listenerActive = true;
        }
        catch (Exception ex)
        {
            _listenerActive = false;
            _logger.LogCritical(ex, "[Listener] Failed to start SqlDependency — Service Broker may not be enabled on the target database");
        }
    }

    private async Task ProcessPendingNotifications(CancellationToken stoppingToken)
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
        var notifId = notification.id;
        var phone = notification.phone_number;

        Interlocked.Increment(ref _inFlightCount);
        try
        {
            var sent = await _smsSender.SendAsync(notification);

            if (sent)
            {
                await _repository.UpdateStatusAsync(notifId, NotificationStatus.PROCESSED);
                return;
            }

            var nextRetry = notification.retry_count + 1;
            if (nextRetry >= notification.max_retries)
            {
                _logger.LogError("[SMS] Notification {Id} to {Phone} failed after {Count}/{Limit} retries — CANCELLED",
                    notifId, phone, nextRetry, notification.max_retries);
                await _repository.UpdateStatusAsync(notifId, NotificationStatus.CANCELLED);
            }
            else
            {
                var retryAfter = _smsSender.CalculateRetryAfter(nextRetry);
                _logger.LogWarning("[SMS] Notification {Id} to {Phone} failed — retry {Count}/{Limit} scheduled at {RetryAfter}",
                    notifId, phone, nextRetry, notification.max_retries, retryAfter);
                await _repository.UpdateRetryAsync(notifId, nextRetry, retryAfter);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _inFlightCount);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[Shutdown] Stopping SMS notification service...");

        _listenerActive = false;
        _listener.Stop();
        _logger.LogInformation("[Shutdown] SqlDependency listener stopped");

        var timeout = TimeSpan.FromSeconds(ShutdownTimeoutSeconds);
        var deadline = DateTime.UtcNow + timeout;

        while (_inFlightCount > 0 && DateTime.UtcNow < deadline)
        {
            _logger.LogInformation("[Shutdown] Waiting for {Count} in-flight send(s) to complete...", _inFlightCount);
            await Task.Delay(500, cancellationToken);
        }

        if (_inFlightCount > 0)
            _logger.LogWarning("[Shutdown] Timed out after {Timeout}s — {Count} send(s) still in progress", timeout.TotalSeconds, _inFlightCount);
        else
            _logger.LogInformation("[Shutdown] All operations completed cleanly");

        await base.StopAsync(cancellationToken);
    }
}
