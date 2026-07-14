using Microsoft.Data.SqlClient;
using System.Net.Http.Json;
using System.Threading;
using Dapper;
using Microsoft.Extensions.Options;

namespace SmsNotificationService;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _connectionString;
    private readonly string _smsApiUrl;

    private readonly SemaphoreSlim _processingLock = new(1, 1);
    private int _inFlightCount;
    private bool _listenerActive;

    private const int MaxRetries = 3;
    private const int MaxReRegisterAttempts = 5;
    private const int ShutdownTimeoutSeconds = 30;

    public bool IsListenerActive => _listenerActive;

    public Worker(ILogger<Worker> logger, IHttpClientFactory httpClientFactory, IOptions<SmsServiceOptions> appOptions)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _connectionString = appOptions.Value.ConnectionString;
        _smsApiUrl = appOptions.Value.SmsApiUrl;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Initializing Real-Time SQL listener via SqlDependency....");

        try
        {
            SqlDependency.Start(_connectionString);
            RegisterQueryWithRetry(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to start SqlDependency. Ensure SQL Server is running and Service Broker is enabled on the database.");
        }

        return Task.CompletedTask;
    }

    private void RegisterQueryWithRetry(CancellationToken stoppingToken)
    {
        for (int attempt = 1; attempt <= MaxReRegisterAttempts; attempt++)
        {
            try
            {
                RegisterQuery(stoppingToken);
                _listenerActive = true;
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to register SqlDependency query (Attempt {Attempt}/{MaxAttempts})", attempt, MaxReRegisterAttempts);

                if (attempt < MaxReRegisterAttempts && !stoppingToken.IsCancellationRequested)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    _logger.LogInformation("Retrying SqlDependency registration in {Delay}s...", delay.TotalSeconds);
                    Thread.Sleep(delay);
                }
            }
        }

        _logger.LogCritical("Failed to register SqlDependency query after {MaxAttempts} attempts. Listener is inactive.", MaxReRegisterAttempts);
        _listenerActive = false;
    }

    private void RegisterQuery(CancellationToken stoppingToken)
    {
        using var connection = new SqlConnection(_connectionString);
        connection.Open();

        using var command = new SqlCommand(
            "SELECT id, phone_number, mpesa_code, adm_no, stud_names, amount, receipt_no, dated, status, created_at, updated_at FROM [dbo].[sms_notifications]", connection);

        var dependency = new SqlDependency(command);
        dependency.OnChange += (sender, e) =>
        {
            if (e.Type == SqlNotificationType.Change)
            {
                _logger.LogInformation("Table change detected (Type: {Info}). Processing...", e.Info);
                ProcessPendingNotifications(stoppingToken).GetAwaiter().GetResult();
            }
            else
            {
                _logger.LogWarning("SqlDependency notification type: {Type}, Info: {Info}", e.Type, e.Info);
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                RegisterQueryWithRetry(stoppingToken);
            }
        };

        command.ExecuteReader();
    }

    private async Task ProcessPendingNotifications(CancellationToken stoppingToken)
    {
        if (!await _processingLock.WaitAsync(0, stoppingToken))
        {
            _logger.LogWarning("Previous notification batch still processing, skipping this event.");
            return;
        }

        try
        {
            using var connection = new SqlConnection(_connectionString);
            var notifications = await connection.QueryAsync<SmsNotification>(
                "SELECT * FROM [dbo].[sms_notifications] WHERE status = 'PENDING'");

            foreach (var notification in notifications)
            {
                if (stoppingToken.IsCancellationRequested) break;
                await SendSmsNotificationWithRetry(notification);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process pending notifications");
        }
        finally
        {
            _processingLock.Release();
        }
    }

    private async Task SendSmsNotificationWithRetry(SmsNotification notification)
    {
        Interlocked.Increment(ref _inFlightCount);
        try
        {
            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    var httpClient = _httpClientFactory.CreateClient();
                    var payload = new
                    {
                        phoneNumber = notification.phone_number,
                        message = $"Payment received. Mpesa Code: {notification.mpesa_code}, AdmNo: {notification.adm_no}, Amount: {notification.amount}, ReceiptNo: {notification.receipt_no}, Dated: {notification.dated}"
                    };
                    var response = await httpClient.PostAsJsonAsync(_smsApiUrl, payload);
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("SMS sent successfully for Notification ID: {Id}", notification.id);
                        await UpdateNotificationStatus(notification.id, "SENT");
                        return;
                    }

                    _logger.LogWarning("Failed to send SMS for Notification ID: {Id}. Status Code: {StatusCode} (Attempt {Attempt}/{MaxAttempts})",
                        notification.id, response.StatusCode, attempt, MaxRetries);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Exception sending SMS for Notification ID: {Id} (Attempt {Attempt}/{MaxAttempts})",
                        notification.id, attempt, MaxRetries);
                }

                if (attempt < MaxRetries)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    _logger.LogInformation("Retrying in {Delay}s...", delay.TotalSeconds);
                    await Task.Delay(delay);
                }
            }

            _logger.LogError("All {MaxRetries} attempts failed for Notification ID: {Id}. Marking as FAILED.", MaxRetries, notification.id);
            await UpdateNotificationStatus(notification.id, "FAILED");
        }
        finally
        {
            Interlocked.Decrement(ref _inFlightCount);
        }
    }

    private async Task UpdateNotificationStatus(long notificationId, string status)
    {
        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(
            "UPDATE [dbo].[sms_notifications] SET status = @Status, updated_at = @UpdatedAt WHERE id = @Id",
            new { Status = status, UpdatedAt = DateTimeOffset.UtcNow, Id = notificationId });
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Real-Time SQL listener...");

        _listenerActive = false;
        SqlDependency.Stop(_connectionString);

        var timeout = TimeSpan.FromSeconds(ShutdownTimeoutSeconds);
        var deadline = DateTime.UtcNow + timeout;

        while (_inFlightCount > 0 && DateTime.UtcNow < deadline)
        {
            _logger.LogInformation("Waiting for {Count} in-flight SMS send(s) to complete...", _inFlightCount);
            await Task.Delay(500, cancellationToken);
        }

        if (_inFlightCount > 0)
            _logger.LogWarning("Shutdown timed out after {Timeout}s with {Count} in-flight operation(s) remaining.", timeout.TotalSeconds, _inFlightCount);
        else
            _logger.LogInformation("All in-flight operations completed.");

        await base.StopAsync(cancellationToken);
    }
}
