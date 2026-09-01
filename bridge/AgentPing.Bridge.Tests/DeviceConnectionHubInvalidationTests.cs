using AgentPing.Bridge.Transport;

namespace AgentPing.Bridge.Tests;

public sealed class DeviceConnectionHubInvalidationTests
{
    [Fact]
    public async Task Invalidation_completes_only_selected_device_queues_fail_closed()
    {
        var hub = new DeviceConnectionHub();
        hub.InitializeLastPublishedSequence(0);
        await using var selected = hub.Subscribe("display-1");
        await using var other = hub.Subscribe("display-2");

        await hub.InvalidateAsync("display-1");

        await Assert.ThrowsAsync<System.Threading.Channels.ChannelClosedException>(() => selected.Reader.ReadAsync().AsTask());
        Assert.False(other.Reader.Completion.IsCompleted);
    }
}
