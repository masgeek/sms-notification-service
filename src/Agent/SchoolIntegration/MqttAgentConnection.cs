using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;

namespace FeeSyncer.Agent.SchoolIntegration;

internal sealed class MqttAgentConnection(
    IOptions<AgentOptions> options,
    AgentWakeSignal wakeSignal,
    ILogger<MqttAgentConnection> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retrySeconds = options.Value.MqttReconnectMinSeconds;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunConnectionAsync(stoppingToken);
                retrySeconds = options.Value.MqttReconnectMinSeconds;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Agent MQTT connection failed; HTTP polling fallback remains active.");
            }

            await Task.Delay(TimeSpan.FromSeconds(retrySeconds), stoppingToken);
            retrySeconds = Math.Min(retrySeconds * 2, options.Value.MqttReconnectMaxSeconds);
        }
    }

    private async Task RunConnectionAsync(CancellationToken cancellationToken)
    {
        var agentOptions = options.Value;
        var factory = new MqttClientFactory();
        using var client = factory.CreateMqttClient();
        client.ApplicationMessageReceivedAsync += eventArgs =>
        {
            if (eventArgs.ApplicationMessage.Topic.EndsWith("/work", StringComparison.Ordinal))
            {
                wakeSignal.Signal();
            }

            return Task.CompletedTask;
        };

        var clientOptions = new MqttClientOptionsBuilder()
            .WithClientId("sms-agent-" + TopicKey(agentOptions.AgentToken)[..16])
            .WithTcpServer(agentOptions.MqttBrokerHost, agentOptions.MqttBrokerPort)
            .WithCredentials(
                string.IsNullOrWhiteSpace(agentOptions.MqttUsername) ? agentOptions.AgentToken : agentOptions.MqttUsername,
                agentOptions.MqttPassword)
            .WithCleanSession(false)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(agentOptions.MqttKeepAliveSeconds))
            .WithTimeout(TimeSpan.FromSeconds(agentOptions.RequestTimeoutSeconds));

        if (agentOptions.MqttUseTls)
        {
            clientOptions.WithTlsOptions(tls => tls.UseTls());
        }

        await client.ConnectAsync(clientOptions.Build(), cancellationToken);

        var topic = $"{agentOptions.MqttTopicPrefix.Trim('/')}/key/{TopicKey(agentOptions.AgentToken)}/work";
        var subscription = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(topic, MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();
        await client.SubscribeAsync(subscription, cancellationToken);
        wakeSignal.Signal();
        logger.LogInformation("Agent MQTT connection established.");

        while (client.IsConnected && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(agentOptions.MqttKeepAliveSeconds), cancellationToken);
            await client.PingAsync(cancellationToken);
        }
    }

    private static string TopicKey(string token)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
