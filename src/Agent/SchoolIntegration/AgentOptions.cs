using System.ComponentModel.DataAnnotations;
using FeeSyncer.Shared;

namespace FeeSyncer.Agent.SchoolIntegration;

internal sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public bool Enabled { get; init; } = true;

    [Required, Url]
    public string ServerUrl { get; set; } = Constants.DefaultBaseUrl;

    public string AgentToken { get; init; } = string.Empty;

    [Range(1, 300)]
    public int RequestTimeoutSeconds { get; init; } = 30;

    [Range(1, 300)]
    public int IdleDelaySeconds { get; init; } = 5;

    [Range(10, 3600)]
    public int HeartbeatSeconds { get; init; } = 60;

    [Range(5, 3600)]
    public int LeaseRenewalSeconds { get; init; } = 30;

    public bool MqttEnabled { get; init; } = true;

    public string MqttBrokerHost { get; init; } = "wss://mqtt.munywele.co.ke/mqtt";

    [Range(1, 65535)]
    public int MqttBrokerPort { get; init; } = 443;

    public string MqttBrokerPath { get; init; } = "/mqtt";

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

    public bool FeeProcessorUpdateEnabled { get; init; } = false;
    public string FeeProcessorUpdateInterval { get; init; } = "24h";
    [Range(1, 168)]
    public int FeeProcessorUpdateIntervalHours { get; init; } = 24;
    public string FeeProcessorPath { get; init; } = string.Empty;
    public string FeeProcessorRepository { get; init; } = string.Empty;
    public string FeeProcessorBranch { get; init; } = "main";
    public string FeeProcessorTag { get; init; } = string.Empty;
    public string FeeProcessorBackupPath { get; init; } = "C:\\fee-processor-backups";
    public string PhpExecutablePath { get; init; } = string.Empty;
    public string ComposerExecutablePath { get; init; } = string.Empty;
    public string GitExecutablePath { get; init; } = string.Empty;
    public string FeeProcessorSshUsername { get; init; } = "git";
    public string FeeProcessorSshKeyPath { get; init; } = string.Empty;
    public string FeeProcessorSshPassphrase { get; init; } = string.Empty;
}
