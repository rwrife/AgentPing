using AgentPing.Bridge.Core;
using AgentPing.Bridge.Providers;
using AgentPing.Bridge.Protocol;

namespace AgentPing.Bridge.Tests;

public sealed class ProviderActionBrokerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Waiter_receives_only_a_post_commit_action_and_reply_text_stays_in_memory()
    {
        var clock = new FixedTimeProvider(Now);
        var broker = new ProviderActionBroker(clock);
        var wait = broker.WaitAsync("attention-1", Now.AddSeconds(30), CancellationToken.None);
        var request = new DeviceActionRequest(
            Guid.NewGuid(), MessageKind.Reply, Now, Guid.NewGuid(), "attention-1", 2,
            "display-1", false, null, null, null, "Run focused tests");
        var outcome = new ActionOutcome(
            request.ActionId, request.AttentionId, request.DeviceId, "manual", "reply",
            "recorded", Now, 3);

        broker.Complete(new ActionProcessResult(
            false,
            outcome,
            new BridgeSnapshot([], [], [], 3),
            new BridgeHistoryItem(3, MessageKind.Session, request.MessageId, Now, default),
            request));

        var result = await wait;
        Assert.Equal("reply", result.Action);
        Assert.Equal("recorded", result.Status);
        Assert.Equal("Run focused tests", result.Text);
    }

    [Fact]
    public async Task Expired_wait_returns_deny_without_an_action_completion()
    {
        var broker = new ProviderActionBroker(new FixedTimeProvider(Now));

        var result = await broker.WaitAsync("attention-expired", Now, CancellationToken.None);

        Assert.Equal("deny", result.Action);
        Assert.Equal("expired", result.Status);
        Assert.Null(result.Text);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
