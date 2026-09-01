using System.Collections.Concurrent;
using System.Threading.Channels;
using AgentPing.Bridge.Core;

namespace AgentPing.Bridge.Transport;

public sealed class DeviceConnectionHub : Security.IDeviceLifecycleInvalidator
{
    private readonly ConcurrentDictionary<Guid, Connection> _connections = new();
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

    public DeviceSubscription Subscribe(string deviceId)
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
        var cancellation = new CancellationTokenSource();
        if (!_connections.TryAdd(id, new Connection(deviceId, channel, cancellation)))
        {
            throw new InvalidOperationException("Could not register device connection.");
        }

        return new DeviceSubscription(id, channel.Reader, cancellation.Token, this);
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
            if (!connection.Value.Channel.Writer.TryWrite(item)
                && _connections.TryRemove(connection.Key, out var removed))
            {
                removed.Cancellation.Cancel();
                removed.Channel.Writer.TryComplete(new InvalidOperationException("Device outbound queue exceeded 256 messages."));
            }
        }
    }

    private void Unsubscribe(Guid id)
    {
        if (_connections.TryRemove(id, out var connection))
        {
            connection.Cancellation.Cancel();
            connection.Channel.Writer.TryComplete();
            connection.Cancellation.Dispose();
        }
    }

    public Task InvalidateAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        foreach (var pair in _connections.Where(pair => StringComparer.Ordinal.Equals(pair.Value.DeviceId, deviceId)).ToArray())
            Unsubscribe(pair.Key);
        return Task.CompletedTask;
    }

    private sealed record Connection(string DeviceId, Channel<BridgeHistoryItem> Channel, CancellationTokenSource Cancellation);

    public sealed class DeviceSubscription(
        Guid id,
        ChannelReader<BridgeHistoryItem> reader, CancellationToken invalidated,
        DeviceConnectionHub owner) : IAsyncDisposable
    {
        public ChannelReader<BridgeHistoryItem> Reader { get; } = reader;
        public CancellationToken Invalidated { get; } = invalidated;

        public ValueTask DisposeAsync()
        {
            owner.Unsubscribe(id);
            return ValueTask.CompletedTask;
        }
    }
}
