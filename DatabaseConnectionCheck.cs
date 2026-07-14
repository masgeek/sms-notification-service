using Microsoft.Data.SqlClient;

namespace SmsNotificationService;

public static class DatabaseConnectionCheck
{
    public static async Task RunAsync(string connectionString, ILogger logger, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Checking database connectivity...");

        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            var serverVersion = connection.ServerVersion;
            var databaseName = connection.Database;

            logger.LogInformation("Database connection successful. Server: {ServerVersion}, Database: {Database}",
                serverVersion, databaseName);
        }
        catch (SqlException ex)
        {
            logger.LogCritical(ex, "Database connection failed: {Message}", ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Unexpected error connecting to database: {Message}", ex.Message);
            throw;
        }
    }
}
