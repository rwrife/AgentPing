using System.Collections.Concurrent;
using System.Threading.Channels;
using AgentPing.Bridge.Core;

namespace AgentPing.Bridge.Transport;

public sealed class DeviceConnectionHub
{
    private readonly ConcurrentDictionary<Guid, Channel<BridgeHistoryItem>> _connections = new();
    private readonly SortedDictionary<ulong, BridgeHistoryItem> _pending = [];
    private readonly object _publishGate = new();
    private ulong _lastPublishedServerSequence;
    private bool _initialized;

    public void InitializeLastPublishedSequence(ulong serverSequence)
    {
        lock (_publishGate)
        {
            if (_initialized)
            {
                throw new InvalidOperationException("Device connection hub is already initialized.");
            }

            _lastPublishedServerSequence = serverSequence;
            _initialized = true;
        }
    }

    public DeviceSubscription Subscribe()
    {
        lock (_publishGate)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("Device connection hub must be initialized before subscribing.");
            }
        }

        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<BridgeHistoryItem>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        if (!_connections.TryAdd(id, channel))
        {
            throw new InvalidOperationException("Could not register device connection.");
        }

        return new DeviceSubscription(id, channel.Reader, this);
    }

    public void Publish(BridgeHistoryItem item)
    {
        lock (_publishGate)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("Device connection hub must be initialized before publishing.");
            }

            if (item.ServerSequence <= _lastPublishedServerSequence)
            {
                return;
            }

            _pending.TryAdd(item.ServerSequence, item);
            while (_lastPublishedServerSequence < ulong.MaxValue
                && _pending.Remove(_lastPublishedServerSequence + 1, out var next))
            {
                PublishToConnections(next);
                _lastPublishedServerSequence = next.ServerSequence;
            }
        }
    }

    private void PublishToConnections(BridgeHistoryItem item)
    {
        foreach (var connection in _connections)
        {
            if (!connection.Value.Writer.TryWrite(item)
                && _connections.TryRemove(connection.Key, out var channel))
            {
                channel.Writer.TryComplete(new InvalidOperationException("Device outbound queue exceeded 256 messages."));
            }
        }
    }

    private void Unsubscribe(Guid id)
    {
        if (_connections.TryRemove(id, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    public sealed class DeviceSubscription(
        Guid id,
        ChannelReader<BridgeHistoryItem> reader,
        DeviceConnectionHub owner) : IAsyncDisposable
    {
        public ChannelReader<BridgeHistoryItem> Reader { get; } = reader;

        public ValueTask DisposeAsync()
        {
            owner.Unsubscribe(id);
            return ValueTask.CompletedTask;
        }
    }
}
