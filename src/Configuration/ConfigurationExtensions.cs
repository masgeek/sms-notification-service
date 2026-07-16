using Microsoft.Extensions.Configuration;

namespace SmsNotificationService.Configuration;

public static class ConfigurationExtensions
{
    public static IConfigurationBuilder AddProductionConfig(
        this IConfigurationBuilder builder, string appDataDir)
    {
        var prodConfigPath = Path.Combine(appDataDir, "appsettings.Production.json");
        try
        {
            if (File.Exists(prodConfigPath))
                builder.AddJsonFile(prodConfigPath, optional: true, reloadOnChange: false);
        }
        catch (UnauthorizedAccessException)
        {
            Console.WriteLine($"[Config] Warning: Access denied to {prodConfigPath} — using environment variables or defaults");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Warning: Could not load {prodConfigPath}: {ex.Message} — using environment variables or defaults");
        }

        return builder;
    }

    public static void ValidateSmsServiceOptions(this IConfiguration configuration)
    {
        var options = configuration.GetSection(SmsServiceOptions.SectionName).Get<SmsServiceOptions>()
            ?? throw new InvalidOperationException("[Config] Missing configuration section: SmsService");

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException("[Config] SmsService:ConnectionString is not configured. Set via appsettings.json or SmsService__ConnectionString.");

        if (string.IsNullOrWhiteSpace(options.SmsApiUrl))
            throw new InvalidOperationException("[Config] SmsService:SmsApiUrl is not configured. Set via appsettings.json or SmsService__SmsApiUrl.");

        if (!Uri.TryCreate(options.SmsApiUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException($"[Config] SmsService:SmsApiUrl is not a valid URI: {options.SmsApiUrl}");

        if (string.IsNullOrWhiteSpace(options.AuthorizationToken))
            throw new InvalidOperationException("[Config] SmsService:AuthorizationToken is not configured. Set via appsettings.json or SmsService__AuthorizationToken.");
    }
}
