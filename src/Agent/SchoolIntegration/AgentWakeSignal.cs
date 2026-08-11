using System.Threading.Channels;

namespace FeeSyncer.Agent.SchoolIntegration;

internal sealed class AgentWakeSignal
{
    private readonly Channel<bool> _signals = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    public void Signal() => _signals.Writer.TryWrite(true);

    public async Task WaitAsync(TimeSpan fallbackDelay, CancellationToken cancellationToken)
    {
        var signalTask = _signals.Reader.WaitToReadAsync(cancellationToken).AsTask();
        var delayTask = Task.Delay(fallbackDelay, cancellationToken);
        await Task.WhenAny(signalTask, delayTask);

        if (signalTask.IsCompletedSuccessfully)
        {
            _signals.Reader.TryRead(out _);
        }
    }
}
