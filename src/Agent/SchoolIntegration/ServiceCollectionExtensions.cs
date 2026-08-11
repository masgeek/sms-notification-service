using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace FeeSyncer.Agent;

public static class SchoolIntegrationServiceCollectionExtensions
{
    public static IServiceCollection AddSchoolIntegrationServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddSingleton<FeeSyncer.Agent.SchoolIntegration.AgentWakeSignal>()
            .AddSingleton<FeeSyncer.Agent.SchoolIntegration.MqttAgentState>()
            .AddOptions<FeeSyncer.Agent.SchoolIntegration.AgentOptions>()
            .Bind(configuration.GetSection(FeeSyncer.Agent.SchoolIntegration.AgentOptions.SectionName))
            .Validate(ValidateOptions, "School integration options are invalid.")
            .Validate(options => IsSecureOrLoopback(options.ServerUrl), "Agent:ServerUrl must use HTTPS unless it targets loopback.")
            .Validate(IsLoopbackApi, "Agent:LocalApiBaseUrl must target loopback.")
            .Validate(options => IsSecureMqttOrDevelopment(options, configuration),
                "Agent MQTT must use TLS outside Development.")
            .ValidateOnStart();

        var agentOptions = configuration
            .GetSection(FeeSyncer.Agent.SchoolIntegration.AgentOptions.SectionName)
            .Get<FeeSyncer.Agent.SchoolIntegration.AgentOptions>();
        if (agentOptions?.Enabled != true)
        {
            return services;
        }

        services.AddHttpClient<FeeSyncer.Agent.SchoolIntegration.GatewayClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<FeeSyncer.Agent.SchoolIntegration.AgentOptions>>().Value;
            client.BaseAddress = new Uri(options.ServerUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
            client.DefaultRequestHeaders.Authorization = new("Bearer", options.AgentToken);
        });

        services.AddHttpClient<FeeSyncer.Agent.SchoolIntegration.SchoolApiClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<FeeSyncer.Agent.SchoolIntegration.AgentOptions>>().Value;
            client.BaseAddress = new Uri(options.LocalApiBaseUrl.TrimEnd('/') + '/', UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        });

        services.AddSingleton<FeeSyncer.Agent.SchoolIntegration.IStudentAdapter, FeeSyncer.Agent.SchoolIntegration.SchoolApiStudentAdapter>();
        services.AddHostedService<FeeSyncer.Agent.SchoolIntegration.SchoolIntegrationWorker>();
        if (agentOptions.MqttEnabled)
        {
            services.AddHostedService<FeeSyncer.Agent.SchoolIntegration.MqttAgentConnection>();
        }

        return services;
    }

    private static bool ValidateOptions(FeeSyncer.Agent.SchoolIntegration.AgentOptions options)
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
        if (!Validator.TryValidateObject(options, context, null, true))
        {
            return false;
        }

        if (options.MqttEnabled && !options.MqttUseTls
            && (!Uri.TryCreate($"http://{options.MqttBrokerHost}", UriKind.Absolute, out var brokerUri) || !brokerUri.IsLoopback))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(options.MqttTopicPrefix);
    }

    private static bool IsSecureOrLoopback(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback);
    }

    private static bool IsLoopbackApi(FeeSyncer.Agent.SchoolIntegration.AgentOptions options)
    {
        return Uri.TryCreate(options.LocalApiBaseUrl, UriKind.Absolute, out var uri)
            && uri.IsLoopback
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static bool IsSecureMqttOrDevelopment(
        FeeSyncer.Agent.SchoolIntegration.AgentOptions options,
        IConfiguration configuration)
    {
        if (!options.MqttEnabled || options.MqttUseTls)
        {
            return true;
        }

        var environment = configuration["DOTNET_ENVIRONMENT"]
            ?? configuration["ASPNETCORE_ENVIRONMENT"]
            ?? Environments.Production;
        return string.Equals(environment, Environments.Development, StringComparison.OrdinalIgnoreCase);
    }
}
