using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FeeSyncer.Shared;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

namespace FeeSyncer.Agent.SchoolIntegration;

internal sealed record WorkNotification(
    [property: System.Text.Json.Serialization.JsonPropertyName("type")] string Type,
    [property: System.Text.Json.Serialization.JsonPropertyName("version")] int Version,
    [property: System.Text.Json.Serialization.JsonPropertyName("event_id")] string EventId,
    [property: System.Text.Json.Serialization.JsonPropertyName("sent_at")] DateTimeOffset SentAt,
    [property: System.Text.Json.Serialization.JsonPropertyName("job_id")] string? JobId = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("operation")] string? Operation = null);

internal sealed class MqttAgentConnection(
    IOptions<AgentOptions> options,
    AgentWakeSignal wakeSignal,
    AgentMqttEventQueue eventQueue,
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
                    logger.LogInformation("MQTT connection attempt. Broker={BrokerHost}:{BrokerPort}{BrokerPath}",
                        options.Value.MqttBrokerHost, options.Value.MqttBrokerPort, options.Value.MqttBrokerPath);
                    AgentMetrics.MqttAttempt();
                    var sessionPresent = await ConnectAndSubscribeAsync(client, stoppingToken);
                    state.SetConnected(true);
                    AgentMetrics.MqttConnected();
                    retrySeconds = Math.Max(1, options.Value.MqttReconnectMinSeconds);
                    wakeSignal.Signal();
                    AgentMetrics.MqttCheckTriggered();
                    eventQueue.PublishHello(
                        VersionHelper.GetCurrentVersion(),
                        ["students.snapshot.v1", "fees.snapshot.v1", "payments.record.v1"]);
                    await PublishPresenceAsync(client, "online", stoppingToken);
                    logger.LogInformation("MQTT connected and subscribed. SessionPresent={SessionPresent}", sessionPresent);
                    await PublishEventsUntilDisconnectedAsync(client, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    state.SetConnected(false);
                    if (client.IsConnected)
                    {
                        try
                        {
                            await PublishPresenceAsync(client, "offline", CancellationToken.None);
                        }
                        catch (Exception presenceException)
                        {
                            logger.LogWarning(presenceException, "Could not publish MQTT offline presence before reconnecting.");
                        }

                        try
                        {
                            await client.DisconnectAsync(cancellationToken: CancellationToken.None);
                        }
                        catch (Exception disconnectException)
                        {
                            logger.LogWarning(disconnectException, "MQTT disconnect after connection failure did not complete cleanly.");
                        }
                    }
                    logger.LogWarning(exception, "MQTT connection attempt failed; periodic HTTP work polling remains active.");
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
                try
                {
                    await PublishPresenceAsync(client, "offline", CancellationToken.None);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Could not publish graceful MQTT offline presence.");
                }
                await client.DisconnectAsync(cancellationToken: CancellationToken.None);
            }
        }
    }

    private async Task<bool> ConnectAndSubscribeAsync(IMqttClient client, CancellationToken cancellationToken)
    {
        var agentOptions = options.Value;
        var clientId = string.IsNullOrWhiteSpace(agentOptions.MqttClientId)
            ? "fee-syncer-agent-" + TopicKey(agentOptions.AgentToken)[..24]
            : agentOptions.MqttClientId;
        var offlinePresence = SerializePresence("offline");
        var clientOptions = new MqttClientOptionsBuilder()
            .WithClientId(clientId)
            .WithWebSocketServer(webSocket => webSocket.WithUri(BuildBrokerUri(agentOptions)))
            .WithCredentials(
                string.IsNullOrWhiteSpace(agentOptions.MqttUsername) ? agentOptions.AgentToken : agentOptions.MqttUsername,
                agentOptions.MqttPassword)
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .WithCleanStart(false)
            .WithSessionExpiryInterval((uint)agentOptions.MqttSessionExpirySeconds)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(agentOptions.MqttKeepAliveSeconds))
            .WithTimeout(TimeSpan.FromSeconds(agentOptions.RequestTimeoutSeconds))
            .WithWillTopic(BuildPresenceTopic(agentOptions))
            .WithWillPayload(offlinePresence)
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithWillRetain(true);

        var connectResult = await client.ConnectAsync(clientOptions.Build(), cancellationToken);
        var topic = BuildTopic(agentOptions);
        var subscribeResult = await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(topic, MqttQualityOfServiceLevel.AtLeastOnce)
            .Build(), cancellationToken);
        if (subscribeResult.Items.Any(item => (int)item.ResultCode >= 128))
        {
            throw new InvalidOperationException("The MQTT broker rejected the agent command subscription.");
        }

        return connectResult.IsSessionPresent;
    }

    private async Task PublishEventsUntilDisconnectedAsync(IMqttClient client, CancellationToken cancellationToken)
    {
        var healthInterval = TimeSpan.FromSeconds(options.Value.MqttHealthSeconds);
        var nextHealth = DateTimeOffset.UtcNow.Add(healthInterval);
        while (client.IsConnected && !cancellationToken.IsCancellationRequested)
        {
            var untilHealth = nextHealth - DateTimeOffset.UtcNow;
            var wait = untilHealth <= TimeSpan.Zero
                ? TimeSpan.Zero
                : TimeSpan.FromSeconds(Math.Min(5, untilHealth.TotalSeconds));
            using var eventCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            eventCts.CancelAfter(wait);
            AgentMqttEvent mqttEvent;
            try
            {
                mqttEvent = await eventQueue.ReadAsync(eventCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow < nextHealth)
                {
                    continue;
                }

                mqttEvent = new AgentMqttEvent("health", 1, Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, Status: "healthy");
                nextHealth = DateTimeOffset.UtcNow.Add(healthInterval);
            }

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(BuildEventsTopic(options.Value))
                .WithPayload(JsonSerializer.SerializeToUtf8Bytes(mqttEvent, JsonOptions))
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(false)
                .Build();
            var result = await client.PublishAsync(message, cancellationToken);
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException($"MQTT event publication was rejected with reason {result.ReasonCode}.");
            }
        }
    }

    private async Task PublishPresenceAsync(IMqttClient client, string status, CancellationToken cancellationToken)
    {
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(BuildPresenceTopic(options.Value))
            .WithPayload(SerializePresence(status))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(true)
            .Build();
        var result = await client.PublishAsync(message, cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"MQTT presence publication was rejected with reason {result.ReasonCode}.");
        }
    }

    private static byte[] SerializePresence(string status) =>
        JsonSerializer.SerializeToUtf8Bytes(new AgentMqttEvent("presence", 1, Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, Status: status), JsonOptions);

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

    internal static string BuildEventsTopic(AgentOptions options) =>
        $"{options.MqttTopicPrefix.Trim('/')}/key/{TopicKey(options.AgentToken)}/events";

    internal static string BuildPresenceTopic(AgentOptions options) =>
        $"{options.MqttTopicPrefix.Trim('/')}/key/{TopicKey(options.AgentToken)}/presence";

    internal static string BuildBrokerUri(AgentOptions options)
    {
        if (Uri.TryCreate(options.MqttBrokerHost, UriKind.Absolute, out var configuredUri)
            && (configuredUri.Scheme == Uri.UriSchemeWs || configuredUri.Scheme == Uri.UriSchemeWss))
        {
            return configuredUri.ToString();
        }

        var scheme = options.MqttUseTls ? "wss" : "ws";
        var path = string.IsNullOrWhiteSpace(options.MqttBrokerPath) ? "/mqtt" : "/" + options.MqttBrokerPath.Trim('/');
        return $"{scheme}://{options.MqttBrokerHost.TrimEnd('/')}:{options.MqttBrokerPort}{path}";
    }
}
