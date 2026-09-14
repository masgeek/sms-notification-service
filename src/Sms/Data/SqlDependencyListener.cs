using Microsoft.Data.SqlClient;

namespace FeeSyncer.Sms.Data;

public sealed class SqlDependencyListener : IDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<SqlDependencyListener> _logger;
    private readonly SemaphoreSlim _registrationGate = new(1, 1);

    private const int MaxReRegisterAttempts = 5;
    private volatile bool _registered;

    public bool IsRegistered => _registered;

    public SqlDependencyListener(string connectionString, ILogger<SqlDependencyListener> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public void Start()
    {
        SqlDependency.Start(_connectionString);
    }

    public void Stop()
    {
        SqlDependency.Stop(_connectionString);
    }

    public void Dispose()
    {
        Stop();
        _registrationGate.Dispose();
    }

    public async Task RegisterQueryWithRetryAsync(Action onChanges, CancellationToken stoppingToken)
    {
        await _registrationGate.WaitAsync(stoppingToken);
        try
        {
            if (_registered)
                return;

            await RegisterQueryCoreAsync(onChanges, stoppingToken);
            _logger.LogInformation("[Listener] Query registered successfully. Waiting for table changes...");
        }
        finally
        {
            _registrationGate.Release();
        }
    }

    public async Task EnsureRegisteredAsync(Action onChanges, CancellationToken stoppingToken)
    {
        if (_registered)
            return;

        await RegisterQueryWithRetryAsync(onChanges, stoppingToken);
    }

    private async Task RegisterQueryCoreAsync(Action onChanges, CancellationToken stoppingToken)
    {
        for (int attempt = 1; attempt <= MaxReRegisterAttempts; attempt++)
        {
            try
            {
                RegisterQuery(onChanges, stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Listener] Registration failed (attempt {Attempt}/{Max})", attempt, MaxReRegisterAttempts);

                if (attempt < MaxReRegisterAttempts && !stoppingToken.IsCancellationRequested)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    _logger.LogInformation("[Listener] Retrying in {Delay}s...", delay.TotalSeconds);
                    await Task.Delay(delay, stoppingToken);
                }
            }
        }

        _logger.LogCritical("[Listener] Registration failed after {Max} attempts — listener inactive until the periodic sweep retries", MaxReRegisterAttempts);
    }

    private void RegisterQuery(Action onChanges, CancellationToken stoppingToken)
    {
        using var connection = new SqlConnection(_connectionString);
        connection.Open();

        using var command = new SqlCommand(
            "SELECT id, phone_number, mpesa_code, adm_no, stud_names, amount, receipt_no, dated, status, created_at, updated_at FROM dbo.sms_notifications", connection);

        var dependency = new SqlDependency(command);
        dependency.OnChange += (sender, e) =>
        {
            _registered = false;
            if (e.Type == SqlNotificationType.Change)
            {
                _logger.LogDebug("[Listener] Change event fired (Info: {Info})", e.Info);
                onChanges();
            }
            else
            {
                _logger.LogDebug("[Listener] Subscription confirmation — Type: {Type}, Info: {Info}", e.Type, e.Info);
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                _ = EnsureRegisteredAsync(onChanges, stoppingToken);
            }
        };

        command.ExecuteReader();
        _registered = true;
    }
}