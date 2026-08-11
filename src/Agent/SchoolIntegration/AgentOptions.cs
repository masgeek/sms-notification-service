using System.ComponentModel.DataAnnotations;

namespace FeeSyncer.Agent.SchoolIntegration;

internal sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public bool Enabled { get; init; } = true;

    [Required, Url]
    public string ServerUrl { get; init; } = "https://fees.munywele.co.ke/";

    public string AgentToken { get; init; } = string.Empty;

    [Range(1, 300)]
    public int RequestTimeoutSeconds { get; init; } = 30;

    [Range(1, 300)]
    public int IdleDelaySeconds { get; init; } = 5;

    [Range(10, 3600)]
    public int HeartbeatSeconds { get; init; } = 60;

    public bool MqttEnabled { get; init; } = true;

    public string MqttBrokerHost { get; init; } = "mqtt.munywele.co.ke";

    [Range(1, 65535)]
    public int MqttBrokerPort { get; init; } = 8883;

    public bool MqttUseTls { get; init; } = true;

    public string MqttUsername { get; init; } = string.Empty;

    public string MqttPassword { get; init; } = string.Empty;

    public string MqttTopicPrefix { get; init; } = "fee-syncer/agent";

    [Range(5, 300)]
    public int MqttKeepAliveSeconds { get; init; } = 30;

    [Range(1, 60)]
    public int MqttReconnectMinSeconds { get; init; } = 1;

    [Range(1, 3600)]
    public int MqttReconnectMaxSeconds { get; init; } = 60;

    public string LocalApiBaseUrl { get; init; } = "http://127.0.0.1:8001/api/";

    public string LocalApiUsername { get; init; } = string.Empty;

    public string LocalApiPassword { get; init; } = string.Empty;
}
