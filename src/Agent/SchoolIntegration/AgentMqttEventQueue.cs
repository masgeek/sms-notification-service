using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace FeeSyncer.Agent.SchoolIntegration;

internal sealed record AgentMqttEvent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("event_id")] string EventId,
    [property: JsonPropertyName("sent_at")] DateTimeOffset SentAt,
    [property: JsonPropertyName("status"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Status = null,
    [property: JsonPropertyName("operation"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Operation = null,
    [property: JsonPropertyName("stage"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Stage = null,
    [property: JsonPropertyName("agent_version"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AgentVersion = null,
    [property: JsonPropertyName("capabilities"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Capabilities = null);

internal sealed class AgentMqttEventQueue
{
    private readonly Channel<AgentMqttEvent> events = Channel.CreateBounded<AgentMqttEvent>(
        new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    public void Publish(string type, string? status = null, string? operation = null, string? stage = null)
    {
        events.Writer.TryWrite(new AgentMqttEvent(type, 1, Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, status, operation, stage));
    }

    public void PublishHello(string agentVersion, IReadOnlyList<string> capabilities)
    {
        events.Writer.TryWrite(new AgentMqttEvent(
            "hello",
            1,
            Guid.NewGuid().ToString(),
            DateTimeOffset.UtcNow,
            AgentVersion: agentVersion,
            Capabilities: capabilities));
    }

    public ValueTask<AgentMqttEvent> ReadAsync(CancellationToken cancellationToken) =>
        events.Reader.ReadAsync(cancellationToken);
}
