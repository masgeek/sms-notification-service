using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace SmsNotificationService.Checks;

public static class DatabaseConnectionCheck
{
    public static async Task RunAsync(string connectionString, ILogger logger, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[DB] Checking database connectivity...");

        var sw = Stopwatch.StartNew();

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            sw.Stop();

            logger.LogInformation("[DB] Connected to {Database} on {Server} ({Version}) in {Elapsed}ms",
                connection.Database, connection.DataSource, connection.ServerVersion, sw.ElapsedMilliseconds);
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
