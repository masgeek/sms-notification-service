using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using FeeSyncer.Shared;

namespace FeeSyncer.Agent;

internal static class AgentConfiguration
{
    public static AgentConfigurationReport Configure(
        ConfigurationManager configuration,
        string basePath,
        string environmentName,
        string machineConfigFile,
        string machineAgentConfigFile,
        string[] args,
        bool includeEnvironmentVariables = true)
    {
        configuration.Sources.Clear();
        configuration
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: false)
            .AddJsonFile(machineConfigFile, optional: true, reloadOnChange: false)
            .AddJsonFile(machineAgentConfigFile, optional: true, reloadOnChange: false);

        if (includeEnvironmentVariables)
        {
            configuration.AddEnvironmentVariables();
        }

        configuration.AddCommandLine(args);

        return new AgentConfigurationReport(
            basePath,
            environmentName,
            [
                Source(1, "Packaged defaults", Path.Combine(basePath, "appsettings.json")),
                Source(2, "Environment defaults", Path.Combine(basePath, $"appsettings.{environmentName}.json")),
                Source(3, "Shared machine settings", machineConfigFile),
                Source(4, "Agent machine settings", machineAgentConfigFile),
                new AgentConfigurationSource(5, "Environment variables", null, includeEnvironmentVariables),
                new AgentConfigurationSource(6, "Command line", null, args.Length > 0),
            ]);
    }

    public static void LogDebug(
        ILogger logger,
        AgentConfigurationReport report,
        IConfiguration configuration,
        AgentRuntimeMode runtimeMode)
    {
        logger.LogDebug(
            "Loading Agent configuration. RuntimeMode={RuntimeMode} Environment={Environment} BasePath={BasePath}",
            runtimeMode,
            report.EnvironmentName,
            report.BasePath);

        foreach (var source in report.Sources)
        {
            logger.LogDebug(
                "Agent configuration source evaluated. Order={Order} Source={Source} Path={Path} Available={Available}",
                source.Order,
                source.Name,
                source.Path ?? "not applicable",
                source.Available);
        }

        var snapshot = SafeSnapshot(configuration);
        logger.LogDebug(
            "Agent configuration resolved. Enabled={Enabled} ServerUrl={ServerUrl} HeartbeatEndpoint={HeartbeatEndpoint} LocalApiBaseUrl={LocalApiBaseUrl} MqttEnabled={MqttEnabled} MqttBrokerHost={MqttBrokerHost} MqttBrokerPort={MqttBrokerPort} MqttUseTls={MqttUseTls} AgentTokenConfigured={AgentTokenConfigured} LocalApiCredentialsConfigured={LocalApiCredentialsConfigured} MqttCredentialsConfigured={MqttCredentialsConfigured}",
            snapshot.Enabled,
            snapshot.ServerUrl,
            snapshot.HeartbeatEndpoint,
            snapshot.LocalApiBaseUrl,
            snapshot.MqttEnabled,
            snapshot.MqttBrokerHost,
            snapshot.MqttBrokerPort,
            snapshot.MqttUseTls,
            snapshot.AgentTokenConfigured,
            snapshot.LocalApiCredentialsConfigured,
            snapshot.MqttCredentialsConfigured);
    }

    internal static AgentConfigurationSnapshot SafeSnapshot(IConfiguration configuration) => new(
        Enabled: BoolValue(configuration["Agent:Enabled"], true),
        ServerUrl: configuration["FeeSyncer:BaseUrl"] ?? configuration["Agent:ServerUrl"] ?? Constants.DefaultBaseUrl,
        HeartbeatEndpoint: configuration["FeeSyncer:ApiEndpoints:AgentHeartbeat"] ?? Constants.DefaultAgentHeartbeatEndpoint,
        LocalApiBaseUrl: configuration["Agent:LocalApiBaseUrl"] ?? "http://127.0.0.1:8001/api/",
        MqttEnabled: BoolValue(configuration["Agent:MqttEnabled"], true),
        MqttBrokerHost: configuration["Agent:MqttBrokerHost"] ?? "not configured",
        MqttBrokerPort: IntValue(configuration["Agent:MqttBrokerPort"], 443),
        MqttUseTls: BoolValue(configuration["Agent:MqttUseTls"], true),
        AgentTokenConfigured: Configured(configuration["Agent:AgentToken"]),
        LocalApiCredentialsConfigured: Configured(configuration["Agent:LocalApiUsername"])
            && Configured(configuration["Agent:LocalApiPassword"]),
        MqttCredentialsConfigured: Configured(configuration["Agent:MqttUsername"])
            || Configured(configuration["Agent:MqttPassword"]));

    private static AgentConfigurationSource Source(int order, string name, string path) =>
        new(order, name, Path.GetFullPath(path), File.Exists(path));

    private static bool Configured(string? value) => !string.IsNullOrWhiteSpace(value);

    private static bool BoolValue(string? value, bool fallback) => bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static int IntValue(string? value, int fallback) => int.TryParse(value, out var parsed) ? parsed : fallback;
}

internal enum AgentRuntimeMode
{
    WindowsService,
    InteractiveConsole,
    ServiceWrapper,
}

internal sealed record AgentConfigurationReport(
    string BasePath,
    string EnvironmentName,
    IReadOnlyList<AgentConfigurationSource> Sources);

internal sealed record AgentConfigurationSource(int Order, string Name, string? Path, bool Available);

internal sealed record AgentConfigurationSnapshot(
    bool Enabled,
    string ServerUrl,
    string HeartbeatEndpoint,
    string LocalApiBaseUrl,
    bool MqttEnabled,
    string MqttBrokerHost,
    int MqttBrokerPort,
    bool MqttUseTls,
    bool AgentTokenConfigured,
    bool LocalApiCredentialsConfigured,
    bool MqttCredentialsConfigured);
