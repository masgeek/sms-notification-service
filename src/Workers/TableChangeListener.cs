using SmsNotificationService.Data;

namespace SmsNotificationService.Workers;

/// <summary>
/// Listens for SQL Server table changes via SqlDependency (Service Broker)
/// and triggers notification processing in real-time.
/// </summary>
public sealed class TableChangeListener : BackgroundService
{
    private readonly ILogger<TableChangeListener> _logger;
    private readonly NotificationProcessor _processor;
    private readonly SqlDependencyListener _listener;

    private const int ShutdownTimeoutSeconds = 30;

    public TableChangeListener(ILogger<TableChangeListener> logger, NotificationProcessor processor, SqlDependencyListener listener)
    {
        _logger = logger;
        _processor = processor;
        _listener = listener;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Listener] Starting SqlDependency table change listener");

        try
        {
            await _processor.ProcessPendingAsync(stoppingToken);

            _listener.Start();
            _logger.LogInformation("[Listener] SqlDependency started, registering query...");

            _listener.RegisterQueryWithRetry(
                onChanges: () => _processor.ProcessPendingAsync(stoppingToken).GetAwaiter().GetResult(),
                stoppingToken: stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "[Listener] Failed to start SqlDependency — Service Broker may not be enabled on the target database");
        }

        // Keep the service alive until shutdown
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[Listener] Stopping table change listener...");

        _listener.Stop();
        _logger.LogInformation("[Listener] SqlDependency listener stopped");

        var timeout = TimeSpan.FromSeconds(ShutdownTimeoutSeconds);
        var deadline = DateTime.UtcNow + timeout;

        while (_processor.InFlightCount > 0 && DateTime.UtcNow < deadline)
        {
            _logger.LogInformation("[Listener] Waiting for {Count} in-flight send(s)...", _processor.InFlightCount);
            await Task.Delay(500, cancellationToken);
        }

        if (_processor.InFlightCount > 0)
            _logger.LogWarning("[Listener] Timed out after {Timeout}s — {Count} send(s) still in progress", timeout.TotalSeconds, _processor.InFlightCount);
        else
            _logger.LogInformation("[Listener] All operations completed cleanly");

        await base.StopAsync(cancellationToken);
    }
}
