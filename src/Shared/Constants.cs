namespace FeeSyncer.Shared;

public static class Constants
{
    public const string ServiceName = "FeeSyncer.Sms";
    public const string AgentServiceName = "FeeSyncer.Agent";
    public const string SmsExecutableName = "FeeSyncer.Sms.exe";
    public const string AgentExecutableName = "FeeSyncer.Agent.exe";
    public const string ConsoleExecutableName = "FeeSyncer.Console.exe";
    public const string TableName = "sms_notifications";
    public const string SubDir = "Munywele\\FeeSyncer";
    public const string ConfigFileName = "appsettings.Production.json";
    public const string DefaultBaseUrl = "https://fees.munywele.co.ke/";
    public const string DefaultSmsNotificationsEndpoint = "api/v1/notifications";
    public const string DefaultAgentEnrollEndpoint = "api/agent/enroll";
    public const string DefaultAgentWorkEndpoint = "api/agent/work";
    public const string DefaultAgentHeartbeatEndpoint = "api/agent/heartbeat";
    public const string DefaultAgentRenewEndpoint = "api/agent/sync-jobs/{jobId}/renew";
    public const string DefaultAgentPageEndpoint = "api/agent/sync-jobs/{jobId}/pages/{pageNumber}";
    public const string DefaultAgentProgressEndpoint = "api/agent/sync-jobs/{jobId}/progress";
    public const string DefaultAgentCompleteEndpoint = "api/agent/sync-jobs/{jobId}/complete";
    public const string DefaultAgentPaymentCompleteEndpoint = "api/agent/payment-jobs/{jobId}/complete";
    public const string DefaultAgentFailEndpoint = "api/agent/sync-jobs/{jobId}/fail";
}
