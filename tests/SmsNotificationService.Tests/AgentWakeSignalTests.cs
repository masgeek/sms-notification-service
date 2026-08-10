using SmsNotificationService.SchoolIntegration;

namespace SmsNotificationService.SchoolIntegration.Tests;

public sealed class AgentWakeSignalTests
{
    [Fact]
    public async Task Signal_ReleasesWaitBeforeFallbackDelay()
    {
        var signal = new AgentWakeSignal();
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var wait = signal.WaitAsync(TimeSpan.FromSeconds(30), cancellationTokenSource.Token);

        signal.Signal();

        await wait;
        Assert.False(cancellationTokenSource.IsCancellationRequested);
    }
}
