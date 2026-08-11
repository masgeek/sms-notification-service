using System.Text.Json;
using System.Text.Json.Serialization;

namespace FeeSyncer.Tunnel.Protocol;

public static class TunnelJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };
}

public enum TunnelMessageType
{
    Register,
    RegisterAck,
    Heartbeat,
    RequestStart,
    RequestBody,
    RequestEnd,
    ResponseStart,
    ResponseBody,
    ResponseEnd,
    Close,
}

public sealed record RegisterMessage(
    [property: JsonPropertyName("type")] TunnelMessageType Type,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("tunnel_id")] string TunnelId,
    [property: JsonPropertyName("school_slug")] string SchoolSlug,
    [property: JsonPropertyName("credential")] string Credential,
    [property: JsonPropertyName("client_version")] string ClientVersion);

public sealed record RegisterAckMessage(
    [property: JsonPropertyName("type")] TunnelMessageType Type,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("hostname")] string Hostname,
    [property: JsonPropertyName("heartbeat_seconds")] int HeartbeatSeconds);

public sealed record HeartbeatMessage(
    [property: JsonPropertyName("type")] TunnelMessageType Type,
    [property: JsonPropertyName("tunnel_id")] string TunnelId,
    [property: JsonPropertyName("sent_at")] DateTimeOffset SentAt,
    [property: JsonPropertyName("origin_healthy")] bool OriginHealthy);

public sealed record RequestStartMessage(
    [property: JsonPropertyName("type")] TunnelMessageType Type,
    [property: JsonPropertyName("request_id")] string RequestId,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("query")] string? Query,
    [property: JsonPropertyName("headers")] IReadOnlyDictionary<string, string[]> Headers,
    [property: JsonPropertyName("body_base64")] string? BodyBase64);

public sealed record ResponseEndMessage(
    [property: JsonPropertyName("type")] TunnelMessageType Type,
    [property: JsonPropertyName("request_id")] string RequestId,
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("headers")] IReadOnlyDictionary<string, string[]> Headers,
    [property: JsonPropertyName("body_base64")] string? BodyBase64);

public sealed record CloseMessage(
    [property: JsonPropertyName("type")] TunnelMessageType Type,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);
