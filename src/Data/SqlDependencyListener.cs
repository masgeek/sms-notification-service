using Microsoft.Data.SqlClient;

namespace SmsNotificationService.Data;

public class SqlDependencyListener(string connectionString, ILogger<SqlDependencyListener> logger)
{
    private readonly ILogger<SqlDependencyListener> _logger = logger;

    private const int MaxReRegisterAttempts = 5;

    public void Start()
    {
        SqlDependency.Start(connectionString);
    }

    public void Stop()
    {
        SqlDependency.Stop(connectionString);
    }

    public void RegisterQueryWithRetry(Action onChanges, CancellationToken stoppingToken)
    {
        for (int attempt = 1; attempt <= MaxReRegisterAttempts; attempt++)
        {
            try
            {
                RegisterQuery(onChanges, stoppingToken);
                _logger.LogInformation("[Listener] Query registered successfully. Waiting for table changes...");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Listener] Registration failed (attempt {Attempt}/{Max})", attempt, MaxReRegisterAttempts);

                if (attempt < MaxReRegisterAttempts && !stoppingToken.IsCancellationRequested)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    _logger.LogInformation("[Listener] Retrying in {Delay}s...", delay.TotalSeconds);
                    Thread.Sleep(delay);
                }
            }
        }

        _logger.LogCritical("[Listener] Registration failed after {Max} attempts — listener is inactive", MaxReRegisterAttempts);
    }

    private void RegisterQuery(Action onChanges, CancellationToken stoppingToken)
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();

        using var command = new SqlCommand(
            "SELECT id, phone_number, mpesa_code, adm_no as admission_no, stud_names AS student_name, amount, receipt_no, dated, status, created_at, updated_at FROM sms_notifications", connection);

        var dependency = new SqlDependency(command);
        dependency.OnChange += (sender, e) =>
        {
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
                RegisterQueryWithRetry(onChanges, stoppingToken);
            }
        };

        command.ExecuteReader();
    }
}
