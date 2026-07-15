using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace SmsNotificationService.Checks;

public static class DatabaseConnectionCheck
{
    private const int ConnectTimeoutSeconds = 10;

    public static async Task RunAsync(string connectionString, ILogger logger, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[DB] Checking database connectivity (timeout: {Timeout}s)...", ConnectTimeoutSeconds);

        var sw = Stopwatch.StartNew();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(ConnectTimeoutSeconds));

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cts.Token);

            sw.Stop();

            logger.LogInformation("[DB] Connected to {Database} on {Server} ({Version}) in {Elapsed}ms",
                connection.Database, connection.DataSource, connection.ServerVersion, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            logger.LogCritical("[DB] Connection timed out after {Elapsed}ms (limit: {Timeout}s)", sw.ElapsedMilliseconds, ConnectTimeoutSeconds);
            throw new TimeoutException($"Database connection timed out after {ConnectTimeoutSeconds}s");
        }
        catch (SqlException ex)
        {
            sw.Stop();
            logger.LogCritical(ex, "[DB] Connection failed after {Elapsed}ms — {Error}", sw.ElapsedMilliseconds, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogCritical(ex, "[DB] Unexpected error after {Elapsed}ms — {Error}", sw.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }
}
