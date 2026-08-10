using System.ComponentModel.DataAnnotations;

namespace SmsNotificationService.SchoolIntegration;

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

    [Range(0, 55)]
    public int LongPollSeconds { get; init; } = 25;

    [Range(10, 3600)]
    public int HeartbeatSeconds { get; init; } = 60;

    public string LocalApiBaseUrl { get; init; } = "http://127.0.0.1:8001/api/";

    public string LocalApiUsername { get; init; } = string.Empty;

    public string LocalApiPassword { get; init; } = string.Empty;
}
