using System.Text.Json;
using AgentPing.Bridge.Core;
using AgentPing.Bridge.Protocol;
using AgentPing.Bridge.Transport;

namespace AgentPing.Bridge.Tests;

public sealed class DeviceConnectionHubTests
{
    [Fact]
    public async Task Out_of_order_commit_publications_are_drained_in_server_sequence_order()
    {
        var hub = new DeviceConnectionHub();
        hub.InitializeLastPublishedSequence(0);
        await using var subscription = hub.Subscribe();
        var first = HistoryItem(1, "session-1");
        var second = HistoryItem(2, "session-2");

        hub.Publish(second);
        Assert.False(subscription.Reader.TryRead(out _));
        hub.Publish(first);

        Assert.Equal<ulong>(1, (await subscription.Reader.ReadAsync()).ServerSequence);
        Assert.Equal<ulong>(2, (await subscription.Reader.ReadAsync()).ServerSequence);
        hub.Publish(first);
        Assert.False(subscription.Reader.TryRead(out _));
    }

    private static BridgeHistoryItem HistoryItem(ulong serverSequence, string sessionId) => new(
        serverSequence,
        MessageKind.Session,
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        JsonSerializer.SerializeToElement(new { sessionId }));
}
