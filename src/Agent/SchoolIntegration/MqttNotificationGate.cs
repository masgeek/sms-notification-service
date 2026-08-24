using System.Collections.Concurrent;

namespace FeeSyncer.Agent.SchoolIntegration;

internal sealed class MqttNotificationGate
{
    private const int MaximumSeenEvents = 2_048;
    private readonly ConcurrentDictionary<string, DateTimeOffset> seenEvents = new(StringComparer.Ordinal);

    public bool TryAccept(WorkNotification notification, DateTimeOffset now)
    {
        if (notification.Type != "work_available" || notification.Version != 1
            || string.IsNullOrWhiteSpace(notification.EventId)
            || notification.SentAt < now.AddMinutes(-10) || notification.SentAt > now.AddMinutes(5))
        {
            return false;
        }

        if (!seenEvents.TryAdd(notification.EventId, now))
        {
            return false;
        }

        foreach (var item in seenEvents.Where(item => item.Value < now.AddHours(-1)))
        {
            seenEvents.TryRemove(item.Key, out _);
        }

        if (seenEvents.Count > MaximumSeenEvents)
        {
            foreach (var item in seenEvents.OrderBy(item => item.Value).Take(seenEvents.Count - MaximumSeenEvents))
            {
                seenEvents.TryRemove(item.Key, out _);
            }
        }

        return true;
    }
}
