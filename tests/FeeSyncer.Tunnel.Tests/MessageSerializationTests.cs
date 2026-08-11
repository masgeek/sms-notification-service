using System.Text.Json;
using FeeSyncer.Tunnel.Protocol;

namespace FeeSyncer.Tunnel.Tests;

public sealed class MessageSerializationTests
{
    [Fact]
    public void Register_message_uses_versioned_snake_case_wire_fields()
    {
        var message = new RegisterMessage(TunnelMessageType.Register, 1, "tunnel-001", "kambui", "test", "1.0.0");
        var json = JsonSerializer.Serialize(message, TunnelJson.Options);

        Assert.Contains("\"type\":\"register\"", json);
        Assert.Contains("\"tunnel_id\":\"tunnel-001\"", json);
        Assert.Contains("\"school_slug\":\"kambui\"", json);
        Assert.DoesNotContain("TunnelId", json);
    }
}
