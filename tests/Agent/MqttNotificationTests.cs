using FeeSyncer.Agent.SchoolIntegration;

namespace FeeSyncer.Agent.Tests;

public sealed class MqttNotificationTests
{
    [Fact]
    public void Topic_uses_a_hash_and_never_contains_the_raw_token()
    {
        var options = new AgentOptions { AgentToken = "agent-token-that-must-not-appear" };

        var topic = MqttAgentConnection.BuildTopic(options);

        Assert.DoesNotContain(options.AgentToken, topic, StringComparison.Ordinal);
        Assert.EndsWith("/work", topic, StringComparison.Ordinal);
    }

    [Fact]
    public void Broker_uri_uses_secure_websockets_by_default()
    {
        var options = new AgentOptions
        {
            MqttBrokerHost = "mqtt.example.test",
            MqttBrokerPort = 443,
            MqttBrokerPath = "/mqtt",
            MqttUseTls = true
        };

        Assert.Equal("wss://mqtt.example.test:443/mqtt", MqttAgentConnection.BuildBrokerUri(options));
    }

    [Fact]
    public void Broker_uri_supports_plain_websockets_for_development()
    {
        var options = new AgentOptions
        {
            MqttBrokerHost = "127.0.0.1",
            MqttBrokerPort = 8083,
            MqttBrokerPath = "mqtt",
            MqttUseTls = false
        };

        Assert.Equal("ws://127.0.0.1:8083/mqtt", MqttAgentConnection.BuildBrokerUri(options));
    }

    [Fact]
    public void Gate_ignores_duplicates_and_stale_notifications()
    {
        var now = DateTimeOffset.UtcNow;
        var gate = new MqttNotificationGate();
        var notification = new WorkNotification("work_available", 1, "event-1", "job-1", "students.snapshot.v1", now);

        Assert.True(gate.TryAccept(notification, now));
        Assert.False(gate.TryAccept(notification, now));
        Assert.False(gate.TryAccept(notification with { EventId = "event-2", SentAt = now.AddMinutes(-11) }, now));
    }

    [Fact]
    public async Task MqttState_Notifies_waiters_when_connection_changes()
    {
        var state = new MqttAgentState();
        var wait = state.WaitForChangeAsync(CancellationToken.None);

        state.SetConnected(true);

        await wait.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(state.IsConnected);
    }
}
