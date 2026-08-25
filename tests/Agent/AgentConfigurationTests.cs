using FeeSyncer.Agent;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using Xunit;

namespace FeeSyncer.Agent.Tests;

public sealed class AgentConfigurationTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(Path.GetTempPath(), $"feesyncer-agent-config-{Guid.NewGuid():N}");

    [Fact]
    public void Machine_agent_settings_override_packaged_and_shared_settings()
    {
        Directory.CreateDirectory(tempDirectory);
        var sharedConfig = Path.Combine(tempDirectory, "machine-appsettings.json");
        var agentConfig = Path.Combine(tempDirectory, "agentsettings.json");
        File.WriteAllText(Path.Combine(tempDirectory, "appsettings.json"),
            """{"Agent":{"AgentToken":"packaged-token"},"FeeSyncer":{"BaseUrl":"https://packaged.example/"}}""");
        File.WriteAllText(Path.Combine(tempDirectory, "appsettings.Development.json"),
            """{"Agent":{"AgentToken":"development-token"},"FeeSyncer":{"BaseUrl":"https://development.example/"}}""");
        File.WriteAllText(sharedConfig,
            """{"FeeSyncer":{"BaseUrl":"https://shared.example/"}}""");
        File.WriteAllText(agentConfig,
            """{"Agent":{"AgentToken":"machine-token"},"FeeSyncer":{"BaseUrl":"https://machine.example/"}}""");
        var configuration = new ConfigurationManager();

        AgentConfiguration.Configure(
            configuration,
            tempDirectory,
            "Development",
            sharedConfig,
            agentConfig,
            [],
            includeEnvironmentVariables: false);

        Assert.Equal("machine-token", configuration["Agent:AgentToken"]);
        Assert.Equal("https://machine.example/", configuration["FeeSyncer:BaseUrl"]);
    }

    [Fact]
    public void Command_line_remains_the_highest_precedence_source()
    {
        Directory.CreateDirectory(tempDirectory);
        var agentConfig = Path.Combine(tempDirectory, "agentsettings.json");
        File.WriteAllText(agentConfig,
            """{"Agent":{"AgentToken":"machine-token"},"FeeSyncer":{"BaseUrl":"https://machine.example/"}}""");
        var configuration = new ConfigurationManager();

        AgentConfiguration.Configure(
            configuration,
            tempDirectory,
            "Production",
            Path.Combine(tempDirectory, "appsettings.Production.json"),
            agentConfig,
            ["--Agent:AgentToken=command-token", "--FeeSyncer:BaseUrl=https://command.example/"],
            includeEnvironmentVariables: false);

        Assert.Equal("command-token", configuration["Agent:AgentToken"]);
        Assert.Equal("https://command.example/", configuration["FeeSyncer:BaseUrl"]);
    }

    [Fact]
    public void Service_and_console_use_the_same_split_machine_settings()
    {
        var serviceDirectory = Path.Combine(tempDirectory, "service");
        var consoleDirectory = Path.Combine(tempDirectory, "console");
        Directory.CreateDirectory(serviceDirectory);
        Directory.CreateDirectory(consoleDirectory);
        File.WriteAllText(Path.Combine(serviceDirectory, "appsettings.json"),
            """{"Agent":{"AgentToken":"service-default"},"FeeSyncer":{"BaseUrl":"https://service-default.example/"}}""");
        File.WriteAllText(Path.Combine(consoleDirectory, "appsettings.json"),
            """{"Agent":{"AgentToken":"console-default"},"FeeSyncer":{"BaseUrl":"https://console-default.example/"}}""");
        var sharedConfig = Path.Combine(tempDirectory, "appsettings.Production.json");
        var agentConfig = Path.Combine(tempDirectory, "agentsettings.json");
        File.WriteAllText(sharedConfig,
            """{"FeeSyncer":{"BaseUrl":"https://enrolled-server.example/"}}""");
        File.WriteAllText(agentConfig,
            """{"Agent":{"AgentToken":"enrolled-machine-token"}}""");

        var service = new ConfigurationManager();
        AgentConfiguration.Configure(
            service, serviceDirectory, "Production", sharedConfig, agentConfig, [],
            includeEnvironmentVariables: false);
        var console = new ConfigurationManager();
        AgentConfiguration.Configure(
            console, consoleDirectory, "Development", sharedConfig, agentConfig, [],
            includeEnvironmentVariables: false);

        Assert.Equal(service["Agent:AgentToken"], console["Agent:AgentToken"]);
        Assert.Equal("enrolled-machine-token", console["Agent:AgentToken"]);
        Assert.Equal(service["FeeSyncer:BaseUrl"], console["FeeSyncer:BaseUrl"]);
        Assert.Equal("https://enrolled-server.example/", console["FeeSyncer:BaseUrl"]);
    }

    [Fact]
    public void Debug_report_lists_sources_in_precedence_order_without_values()
    {
        Directory.CreateDirectory(tempDirectory);
        var sharedConfig = Path.Combine(tempDirectory, "appsettings.Production.json");
        var agentConfig = Path.Combine(tempDirectory, "agentsettings.json");
        File.WriteAllText(sharedConfig, "{}");
        File.WriteAllText(agentConfig, """{"Agent":{"AgentToken":"private-agent-token"}}""");
        var configuration = new ConfigurationManager();

        var report = AgentConfiguration.Configure(
            configuration, tempDirectory, "Production", sharedConfig, agentConfig, [],
            includeEnvironmentVariables: false);

        Assert.Equal(
            ["Packaged defaults", "Environment defaults", "Shared machine settings", "Agent machine settings", "Environment variables", "Command line"],
            report.Sources.Select(source => source.Name));
        Assert.Equal([1, 2, 3, 4, 5, 6], report.Sources.Select(source => source.Order));
        Assert.DoesNotContain("private-agent-token", JsonSerializer.Serialize(report), StringComparison.Ordinal);
    }

    [Fact]
    public void Effective_settings_snapshot_redacts_all_credentials()
    {
        var configuration = new ConfigurationManager
        {
            ["Agent:AgentToken"] = "private-agent-token",
            ["Agent:LocalApiUsername"] = "private-local-user",
            ["Agent:LocalApiPassword"] = "private-local-password",
            ["Agent:MqttUsername"] = "private-mqtt-user",
            ["Agent:MqttPassword"] = "private-mqtt-password",
            ["FeeSyncer:BaseUrl"] = "https://fees.example.test/",
        };

        var snapshotJson = JsonSerializer.Serialize(AgentConfiguration.SafeSnapshot(configuration));

        Assert.Contains("https://fees.example.test/", snapshotJson, StringComparison.Ordinal);
        Assert.Contains("AgentTokenConfigured\":true", snapshotJson, StringComparison.Ordinal);
        Assert.Contains("LocalApiCredentialsConfigured\":true", snapshotJson, StringComparison.Ordinal);
        Assert.Contains("MqttCredentialsConfigured\":true", snapshotJson, StringComparison.Ordinal);
        Assert.DoesNotContain("private-agent-token", snapshotJson, StringComparison.Ordinal);
        Assert.DoesNotContain("private-local-user", snapshotJson, StringComparison.Ordinal);
        Assert.DoesNotContain("private-local-password", snapshotJson, StringComparison.Ordinal);
        Assert.DoesNotContain("private-mqtt-user", snapshotJson, StringComparison.Ordinal);
        Assert.DoesNotContain("private-mqtt-password", snapshotJson, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
            Directory.Delete(tempDirectory, recursive: true);
    }
}
