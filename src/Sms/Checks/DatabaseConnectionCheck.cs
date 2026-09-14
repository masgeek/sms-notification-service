using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace FeeSyncer.Sms.Checks;

public static class DatabaseConnectionCheck
{
    private const int ConnectTimeoutSeconds = 10;

    /// Best-effort pre-flight probe. Failures are logged as warnings rather than
    /// thrown so the service survives a boot that races SQL Server startup; the
    /// SqlDependency listener retries its own connection afterwards.
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
            LogUnavailable(logger, "[DB] Connection timed out after {Elapsed}ms (limit: {Timeout}s) — SQL Server may still be starting", sw.ElapsedMilliseconds, ConnectTimeoutSeconds);
        }
        catch (SqlException ex)
        {
            sw.Stop();
            LogUnavailable(logger, ex, "[DB] Connection failed after {Elapsed}ms — {Error} — SQL Server may still be starting", sw.ElapsedMilliseconds, ex.Message);
        }
        catch (Exception ex)
        {
            sw.Stop();
            LogUnavailable(logger, ex, "[DB] Unexpected error after {Elapsed}ms — {Error} — SQL Server may still be starting", sw.ElapsedMilliseconds, ex.Message);
        }
    }

    private static void LogUnavailable(ILogger logger, string message, params object[] args)
    {
        logger.LogWarning(message, args);
        logger.LogWarning("[DB] Continuing startup; the SqlDependency listener will retry the connection.");
    }

    private static void LogUnavailable(ILogger logger, Exception ex, string message, params object[] args)
    {
        logger.LogWarning(ex, message, args);
        logger.LogWarning("[DB] Continuing startup; the SqlDependency listener will retry the connection.");
    }
}