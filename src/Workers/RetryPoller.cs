using SmsNotificationService.Configuration;
using Microsoft.Extensions.Options;

namespace SmsNotificationService.Workers;

/// <summary>
/// Periodically polls for notifications whose retry_after has passed.
/// SqlDependency only fires on data changes, not time-based conditions,
/// so this worker catches scheduled retries.
/// </summary>
public sealed class RetryPoller : BackgroundService
{
    private readonly ILogger<RetryPoller> _logger;
    private readonly NotificationProcessor _processor;
    private readonly int _pollIntervalSeconds;

    private const int DefaultPollIntervalSeconds = 30;

    public RetryPoller(ILogger<RetryPoller> logger, NotificationProcessor processor, IOptions<SmsServiceOptions> options)
    {
        _logger = logger;
        _processor = processor;
        _pollIntervalSeconds = options.Value.RetryPollIntervalSeconds > 0
            ? options.Value.RetryPollIntervalSeconds
            : DefaultPollIntervalSeconds;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Poll] Retry poller started (every {Interval}s)", _pollIntervalSeconds);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_pollIntervalSeconds));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await _processor.ProcessPendingAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
    }
}
