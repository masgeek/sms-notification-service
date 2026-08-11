using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;

namespace FeeSyncer.Agent.SchoolIntegration;

internal sealed record WorkNotification(string Type, int Version, string EventId, string JobId, string Operation, DateTimeOffset SentAt);

internal sealed class MqttAgentConnection(
    IOptions<AgentOptions> options,
    AgentWakeSignal wakeSignal,
    MqttAgentState state,
    ILogger<MqttAgentConnection> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly MqttNotificationGate notificationGate = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.MqttEnabled)
        {
            return;
        }

        var factory = new MqttClientFactory();
        using var client = factory.CreateMqttClient();
        client.ApplicationMessageReceivedAsync += eventArgs => HandleMessageAsync(eventArgs, stoppingToken);
        client.DisconnectedAsync += eventArgs =>
        {
            state.SetConnected(false);
            AgentMetrics.MqttDisconnected();
            logger.LogWarning("MQTT disconnected. Reason={Reason}", eventArgs.Reason);
            return Task.CompletedTask;
        };

        var retrySeconds = Math.Max(1, options.Value.MqttReconnectMinSeconds);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    logger.LogInformation("MQTT connection attempt. Broker={BrokerHost}:{BrokerPort}",
                        options.Value.MqttBrokerHost, options.Value.MqttBrokerPort);
                    AgentMetrics.MqttAttempt();
                    await ConnectAndSubscribeAsync(client, stoppingToken);
                    state.SetConnected(true);
                    AgentMetrics.MqttConnected();
                    retrySeconds = Math.Max(1, options.Value.MqttReconnectMinSeconds);
                    wakeSignal.Signal();
                    AgentMetrics.MqttCheckTriggered();
                    logger.LogInformation("MQTT connected and subscribed.");
                    await WaitUntilDisconnectedAsync(client, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    state.SetConnected(false);
                    logger.LogWarning(exception, "MQTT connection attempt failed; work discovery is paused until MQTT reconnects.");
                }

                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                var delay = JitteredDelay(retrySeconds);
                logger.LogInformation("MQTT reconnect scheduled. DelaySeconds={DelaySeconds}", delay.TotalSeconds);
                await Task.Delay(delay, stoppingToken);
                retrySeconds = Math.Min(Math.Max(retrySeconds * 2, 1), Math.Max(1, options.Value.MqttReconnectMaxSeconds));
            }
        }
        finally
        {
            state.SetConnected(false);
            if (client.IsConnected)
            {
                await client.DisconnectAsync(cancellationToken: CancellationToken.None);
            }
        }
    }

    private async Task ConnectAndSubscribeAsync(IMqttClient client, CancellationToken cancellationToken)
    {
        var agentOptions = options.Value;
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
        var topic = BuildTopic(agentOptions);
        await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(topic, MqttQualityOfServiceLevel.AtLeastOnce)
            .Build(), cancellationToken);
    }

    private static async Task WaitUntilDisconnectedAsync(IMqttClient client, CancellationToken cancellationToken)
    {
        while (client.IsConnected && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }

    private Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs eventArgs, CancellationToken cancellationToken)
    {
        if (!string.Equals(eventArgs.ApplicationMessage.Topic, BuildTopic(options.Value), StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        try
        {
            var payload = eventArgs.ApplicationMessage.Payload;
            if (payload.Length > 16 * 1024)
            {
                return Task.CompletedTask;
            }

            var payloadBytes = new byte[(int)payload.Length];
            var payloadOffset = 0;
            foreach (var segment in payload)
            {
                segment.Span.CopyTo(payloadBytes.AsSpan(payloadOffset));
                payloadOffset += segment.Length;
            }
            var notification = JsonSerializer.Deserialize<WorkNotification>(payloadBytes, JsonOptions);
            if (notification is null || !notificationGate.TryAccept(notification, DateTimeOffset.UtcNow))
            {
                return Task.CompletedTask;
            }

            logger.LogDebug("MQTT work notification received. EventId={EventId} Operation={Operation}",
                notification.EventId, notification.Operation);
            AgentMetrics.NotificationReceived();
            AgentMetrics.MqttCheckTriggered();
            wakeSignal.Signal();
        }
        catch (JsonException)
        {
            logger.LogWarning("Ignoring malformed MQTT work notification.");
        }

        return Task.CompletedTask;
    }

    private static TimeSpan JitteredDelay(int seconds)
    {
        var jitter = Random.Shared.NextDouble() * 0.2 - 0.1;
        return TimeSpan.FromSeconds(Math.Max(1, seconds * (1 + jitter)));
    }

    private static string TopicKey(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    internal static string BuildTopic(AgentOptions options) =>
        $"{options.MqttTopicPrefix.Trim('/')}/key/{TopicKey(options.AgentToken)}/work";
}
