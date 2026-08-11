using SmsNotificationService;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddAgentProductionConfig(builder.Environment.EnvironmentName);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "SmsNotificationService.Agent";
});
builder.Services.AddSchoolIntegrationServices(builder.Configuration);

var host = builder.Build();
await host.RunAsync();

internal static class AgentConfigurationExtensions
{
    public static IConfigurationBuilder AddAgentProductionConfig(
        this IConfigurationBuilder builder, string environment)
    {
        var appDir = SmsNotificationService.Shared.ConfigPathResolver.GetAppDir();
        var candidates = new List<string>();

        if (environment.Equals("Production", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(Path.Combine(
                SmsNotificationService.Shared.ConfigPathResolver.GetProgramDataDir(),
                SmsNotificationService.Shared.Constants.ConfigFileName));
        }

        candidates.Add(Path.Combine(appDir, "appsettings.Development.json"));
        candidates.Add(Path.Combine(appDir, SmsNotificationService.Shared.Constants.ConfigFileName));

        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(path))
            {
                Console.WriteLine($"[Config] Found: {path}");
                builder.AddJsonFile(path, optional: true, reloadOnChange: false);
            }
        }

        return builder;
    }
}
