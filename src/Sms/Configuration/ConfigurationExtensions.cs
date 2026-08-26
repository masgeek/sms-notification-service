using FeeSyncer.Shared;

namespace FeeSyncer.Sms.Configuration;

public static class ConfigurationExtensions
{
    public static IConfigurationBuilder AddProductionConfig(
        this IConfigurationBuilder builder, string environment)
    {
        var appDir = ConfigPathResolver.GetAppDir();
        var candidates = new List<string>();

        if (environment.Equals("Production", StringComparison.OrdinalIgnoreCase))
        {
            var programDataPath = Path.Combine(ConfigPathResolver.GetProgramDataDir(), Constants.ConfigFileName);
            candidates.Add(programDataPath);
        }

        candidates.Add(Path.Combine(appDir, "appsettings.Development.json"));
        candidates.Add(Path.Combine(appDir, Constants.ConfigFileName));

        var loaded = false;

        foreach (var configPath in candidates)
        {
            try
            {
                if (File.Exists(configPath))
                {
                    WriteConsole(LogLevel.Information, $"Loaded {configPath}");
                    builder.AddJsonFile(configPath, optional: true, reloadOnChange: false);
                    loaded = true;
                }
            }
            catch (UnauthorizedAccessException)
            {
                WriteConsole(LogLevel.Warning, $"Access denied to {configPath}; skipping");
            }
            catch (Exception ex)
            {
                WriteConsole(LogLevel.Warning, $"Could not load {configPath}: {ex.Message}; skipping");
            }
        }

        if (!loaded)
            WriteConsole(LogLevel.Information, "No configuration file found; using environment variables or defaults");

        var configuration = builder.Build();
        var baseUrl = configuration["FeeSyncer:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            builder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SmsService:SmsApiUrl"] = ConfigReader.CombineUrl(
                    baseUrl,
                    configuration["FeeSyncer:ApiEndpoints:SmsNotifications"] ?? Constants.DefaultSmsNotificationsEndpoint)
            });
        }

        return builder;
    }

    public static void ValidateSmsServiceOptions(this IConfiguration configuration)
    {
        var options = configuration.GetSection(SmsServiceOptions.SectionName).Get<SmsServiceOptions>()
            ?? throw new InvalidOperationException("[Config] Missing configuration section: SmsService");

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException("[Config] SmsService:ConnectionString is not configured. Set via appsettings.json or SmsService__ConnectionString.");

        if (options.ConnectionString.Contains("TrustServerCertificate=", StringComparison.OrdinalIgnoreCase) == false)
            WriteConsole(LogLevel.Warning, "Connection string does not contain TrustServerCertificate=True");

        if (string.IsNullOrWhiteSpace(options.SmsApiUrl))
            throw new InvalidOperationException("[Config] SmsService:SmsApiUrl is not configured. Set via appsettings.json or SmsService__SmsApiUrl.");

        if (!Uri.TryCreate(options.SmsApiUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException($"[Config] SmsService:SmsApiUrl is not a valid URI: {options.SmsApiUrl}");

        if (string.IsNullOrWhiteSpace(options.AuthorizationToken))
            throw new InvalidOperationException("[Config] SmsService:AuthorizationToken is not configured. Set via appsettings.json or SmsService__AuthorizationToken.");

        if (options.RetryBackoffSeconds <= 0)
            throw new InvalidOperationException($"[Config] SmsService:RetryBackoffSeconds must be > 0, got {options.RetryBackoffSeconds}.");

        if (options.RetryPollIntervalSeconds <= 0)
            throw new InvalidOperationException($"[Config] SmsService:RetryPollIntervalSeconds must be > 0, got {options.RetryPollIntervalSeconds}.");

        if (options.LogRetentionDays <= 0)
            throw new InvalidOperationException($"[Config] SmsService:LogRetentionDays must be > 0, got {options.LogRetentionDays}.");

        if (options.MaxLogFileSizeMb <= 0)
            throw new InvalidOperationException($"[Config] SmsService:MaxLogFileSizeMb must be > 0, got {options.MaxLogFileSizeMb}.");
    }

    private static void WriteConsole(LogLevel level, string message) =>
        Serilog.Log.ForContext("SourceContext", "Sms.Config").Write(
            level switch
            {
                LogLevel.Trace => Serilog.Events.LogEventLevel.Verbose,
                LogLevel.Debug => Serilog.Events.LogEventLevel.Debug,
                LogLevel.Warning => Serilog.Events.LogEventLevel.Warning,
                LogLevel.Error => Serilog.Events.LogEventLevel.Error,
                LogLevel.Critical => Serilog.Events.LogEventLevel.Fatal,
                _ => Serilog.Events.LogEventLevel.Information,
            },
            "{Message}",
            message);
}
