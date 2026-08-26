using FeeSyncer.Agent.SchoolIntegration;
using FeeSyncer.Shared.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Text.Json;
using Xunit;

namespace FeeSyncer.Agent.Tests;

public sealed class AgentHostingTests
{
    [Fact]
    public void Enabled_agent_registers_processing_worker_without_runtime_mode_condition()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:Enabled"] = "true",
                ["Agent:AgentToken"] = new string('a', 32),
                ["Agent:ServerUrl"] = "https://gateway.example.test/",
                ["Agent:LocalApiBaseUrl"] = "http://127.0.0.1:8001/api/",
                ["Agent:MqttEnabled"] = "false",
                ["Agent:MqttTopicPrefix"] = "fee-syncer/agent",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSchoolIntegrationServices(configuration);

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(SchoolIntegrationWorker));
    }

    [Fact]
    public void Agent_file_logger_writes_structured_json()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"fee-syncer-agent-logs-{Guid.NewGuid():N}");
        try
        {
            var serilog = SerilogLogging.CreateLogger(
                "Agent",
                "Test",
                directory,
                7,
                10);
            try
            {
                using var factory = LoggerFactory.Create(logging => logging.AddSerilog(serilog));
                var logger = factory.CreateLogger("FeeSyncer.Agent.Worker");
                logger.LogInformation("Agent service processing started for {SchoolId}", 42);
            }
            finally
            {
                (serilog as IDisposable)?.Dispose();
            }

            var file = Assert.Single(Directory.GetFiles(directory, "Agent-*.json"));
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;
            Assert.True(root.TryGetProperty("@t", out _));
            Assert.Equal("Agent service processing started for {SchoolId}", root.GetProperty("@mt").GetString());
            Assert.Equal(42, root.GetProperty("SchoolId").GetInt32());
            Assert.Equal("FeeSyncer.Agent.Worker", root.GetProperty("SourceContext").GetString());
            Assert.Equal("Agent", root.GetProperty("Application").GetString());
            Assert.Equal("Test", root.GetProperty("Environment").GetString());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Serilog_configuration_honors_microsoft_default_level()
    {
        Assert.Equal(LogLevel.Debug, SerilogLogging.GetMinimumLevel("Debug"));
        Assert.Equal(LogLevel.Warning, SerilogLogging.GetMinimumLevel("Warning"));
        Assert.Equal(LogLevel.Information, SerilogLogging.GetMinimumLevel(null));
    }

}
