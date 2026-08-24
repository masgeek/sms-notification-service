using FeeSyncer.Agent.SchoolIntegration;

namespace FeeSyncer.Agent.Tests;

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

    [Fact]
    public async Task Timed_out_wait_does_not_consume_a_later_signal()
    {
        var signal = new AgentWakeSignal();

        await signal.WaitAsync(TimeSpan.FromMilliseconds(10), CancellationToken.None);
        var nextWait = signal.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        signal.Signal();

        await nextWait.WaitAsync(TimeSpan.FromSeconds(1));
    }
}
