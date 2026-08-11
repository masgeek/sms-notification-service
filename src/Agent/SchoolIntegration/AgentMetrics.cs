using System.Diagnostics.Metrics;

namespace FeeSyncer.Agent.SchoolIntegration;

internal static class AgentMetrics
{
    private static readonly Meter Meter = new("FeeSyncer.Agent", "1.0");
    private static readonly Counter<long> MqttConnectionAttempts = Meter.CreateCounter<long>("agent.mqtt.connection.attempts");
    private static readonly Counter<long> MqttConnections = Meter.CreateCounter<long>("agent.mqtt.connections");
    private static readonly Counter<long> MqttDisconnects = Meter.CreateCounter<long>("agent.mqtt.disconnects");
    private static readonly Counter<long> MqttNotifications = Meter.CreateCounter<long>("agent.mqtt.notifications");
    private static readonly Counter<long> MqttTriggeredChecks = Meter.CreateCounter<long>("agent.work_checks.mqtt");
    private static readonly Histogram<double> LeaseLatency = Meter.CreateHistogram<double>("agent.lease.latency", "ms");

    public static void MqttAttempt() => MqttConnectionAttempts.Add(1);
    public static void MqttConnected() => MqttConnections.Add(1);
    public static void MqttDisconnected() => MqttDisconnects.Add(1);
    public static void NotificationReceived() => MqttNotifications.Add(1);
    public static void MqttCheckTriggered() => MqttTriggeredChecks.Add(1);
    public static void RecordLease(TimeSpan elapsed) => LeaseLatency.Record(elapsed.TotalMilliseconds);
}
