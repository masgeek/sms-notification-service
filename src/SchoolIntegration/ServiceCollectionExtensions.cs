using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace SmsNotificationService;

public static class SchoolIntegrationServiceCollectionExtensions
{
    public static IServiceCollection AddSchoolIntegrationServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddSingleton<SmsNotificationService.SchoolIntegration.AgentWakeSignal>()
            .AddOptions<SmsNotificationService.SchoolIntegration.AgentOptions>()
            .Bind(configuration.GetSection(SmsNotificationService.SchoolIntegration.AgentOptions.SectionName))
            .Validate(ValidateOptions, "School integration options are invalid.")
            .Validate(options => IsSecureOrLoopback(options.ServerUrl), "Agent:ServerUrl must use HTTPS unless it targets loopback.")
            .Validate(IsLoopbackApi, "Agent:LocalApiBaseUrl must target loopback.")
            .ValidateOnStart();

        var agentOptions = configuration
            .GetSection(SmsNotificationService.SchoolIntegration.AgentOptions.SectionName)
            .Get<SmsNotificationService.SchoolIntegration.AgentOptions>();
        if (agentOptions?.Enabled != true)
        {
            return services;
        }

        services.AddHttpClient<SmsNotificationService.SchoolIntegration.GatewayClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<SmsNotificationService.SchoolIntegration.AgentOptions>>().Value;
            client.BaseAddress = new Uri(options.ServerUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
            client.DefaultRequestHeaders.Authorization = new("Bearer", options.AgentToken);
        });

        services.AddHttpClient<SmsNotificationService.SchoolIntegration.SchoolApiClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<SmsNotificationService.SchoolIntegration.AgentOptions>>().Value;
            client.BaseAddress = new Uri(options.LocalApiBaseUrl.TrimEnd('/') + '/', UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        });

        services.AddSingleton<SmsNotificationService.SchoolIntegration.IStudentAdapter, SmsNotificationService.SchoolIntegration.SchoolApiStudentAdapter>();
        services.AddHostedService<SmsNotificationService.SchoolIntegration.SchoolIntegrationWorker>();
        if (agentOptions.MqttEnabled)
        {
            services.AddHostedService<SmsNotificationService.SchoolIntegration.MqttAgentConnection>();
        }

        return services;
    }

    private static bool ValidateOptions(SmsNotificationService.SchoolIntegration.AgentOptions options)
    {
        if (!options.Enabled)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(options.AgentToken) || options.AgentToken.Length < 32)
        {
            return false;
        }

        var context = new ValidationContext(options);
        return Validator.TryValidateObject(options, context, null, true);
    }

    private static bool IsSecureOrLoopback(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback);
    }

    private static bool IsLoopbackApi(SmsNotificationService.SchoolIntegration.AgentOptions options)
    {
        return Uri.TryCreate(options.LocalApiBaseUrl, UriKind.Absolute, out var uri)
            && uri.IsLoopback
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
