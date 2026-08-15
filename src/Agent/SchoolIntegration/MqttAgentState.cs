using System.Threading.Channels;

namespace FeeSyncer.Agent.SchoolIntegration;

internal sealed class MqttAgentState
{
    private readonly Channel<bool> changes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = false,
        SingleWriter = false,
    });
    private readonly Channel<bool> connections = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = false,
        SingleWriter = false,
    });
    private int connected;

    public bool IsConnected => Volatile.Read(ref connected) == 1;

    public void SetConnected(bool value)
    {
        var next = value ? 1 : 0;
        if (Interlocked.Exchange(ref connected, next) != next)
        {
            changes.Writer.TryWrite(value);
            if (value)
            {
                connections.Writer.TryWrite(true);
            }
        }
    }

    public async Task WaitForChangeAsync(CancellationToken cancellationToken)
    {
        await changes.Reader.ReadAsync(cancellationToken);
    }

    public async Task WaitForConnectionAsync(CancellationToken cancellationToken)
    {
        await connections.Reader.ReadAsync(cancellationToken);
    }
}
