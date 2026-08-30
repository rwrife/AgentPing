using AgentPing.Bridge.Core;
using AgentPing.Bridge.Protocol;

namespace AgentPing.Bridge.Tests;

public sealed class ActionStateStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Approval_is_committed_before_pending_attention_is_removed_and_replay_is_idempotent()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"agentping-actions-{Guid.NewGuid():N}", "state.json");
        using var store = new BridgeStateStore(
            statePath,
            maxHistory: 16,
            staleAfter: TimeSpan.FromMinutes(5),
            new FixedTimeProvider(Now.AddSeconds(2)));
        await store.InitializeAsync();
        await SeedAttentionAsync(store, destructive: false, allowedActions: ["approve", "deny"]);
        var request = new DeviceActionRequest(
            MessageId: Guid.Parse("40000000-0000-4000-8000-000000000003"),
            Type: MessageKind.Approval,
            SentAt: Now.AddSeconds(2),
            ActionId: Guid.Parse("50000000-0000-4000-8000-000000000001"),
            AttentionId: "attention-1",
            ExpectedRevision: 2,
            DeviceId: "display-1",
            Destructive: false,
            Confirmation: null,
            Reason: null,
            Note: null,
            Text: null);

        var first = await store.ProcessActionAsync(request);
        var duplicate = await store.ProcessActionAsync(request);

        Assert.False(first.Duplicate);
        Assert.True(duplicate.Duplicate);
        Assert.Equal("approve", first.Outcome.Action);
        Assert.Equal("recorded", first.Outcome.Status);
        Assert.Equal(first.Outcome, duplicate.Outcome);
        Assert.Empty(first.Snapshot.Attentions);
        var session = Assert.Single(first.Snapshot.Sessions);
        Assert.Equal("running", session.State);
        Assert.Equal<ulong>(3, session.Revision);
        Assert.Equal(MessageKind.Session, first.CommittedItem.Type);
        Assert.Equal(first.CommittedItem.ServerSequence, first.Snapshot.LastServerSequence);
    }

    [Fact]
    public async Task Destructive_approval_rejects_wrong_prompt_digest_without_mutating_state()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"agentping-actions-{Guid.NewGuid():N}", "state.json");
        using var store = new BridgeStateStore(
            statePath,
            maxHistory: 16,
            staleAfter: TimeSpan.FromMinutes(5),
            new FixedTimeProvider(Now.AddSeconds(2)));
        await store.InitializeAsync();
        await SeedAttentionAsync(store, destructive: true, allowedActions: ["approve", "deny"]);
        var request = new DeviceActionRequest(
            MessageId: Guid.Parse("40000000-0000-4000-8000-000000000004"),
            Type: MessageKind.Approval,
            SentAt: Now.AddSeconds(2),
            ActionId: Guid.Parse("50000000-0000-4000-8000-000000000002"),
            AttentionId: "attention-1",
            ExpectedRevision: 2,
            DeviceId: "display-1",
            Destructive: true,
            Confirmation: new Confirmation
            {
                PresentedMessageId = Guid.Parse("40000000-0000-4000-8000-000000000002"),
                PromptDigest = new string('0', 64),
                ConfirmedAt = Now.AddSeconds(1),
            },
            Reason: null,
            Note: null,
            Text: null);

        var exception = await Assert.ThrowsAsync<BridgeActionRejectedException>(
            () => store.ProcessActionAsync(request));

        Assert.Equal("PROMPT_MISMATCH", exception.Code);
        var unchanged = await store.GetSnapshotAsync();
        Assert.Single(unchanged.Attentions);
        Assert.Equal<ulong>(2, unchanged.LastServerSequence);
    }

    [Fact]
    public async Task Accepted_action_is_idempotent_after_restart_and_conflicting_action_id_is_rejected()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"agentping-actions-{Guid.NewGuid():N}", "state.json");
        var request = ApprovalRequest(Now.AddSeconds(2));
        using (var firstStore = new BridgeStateStore(
                   statePath, 16, TimeSpan.FromMinutes(5), new FixedTimeProvider(Now.AddSeconds(2))))
        {
            await firstStore.InitializeAsync();
            await SeedAttentionAsync(firstStore, false, ["approve", "deny"]);
            Assert.False((await firstStore.ProcessActionAsync(request)).Duplicate);
        }

        using var restarted = new BridgeStateStore(
            statePath, 16, TimeSpan.FromMinutes(5), new FixedTimeProvider(Now.AddSeconds(3)));
        await restarted.InitializeAsync();
        var replay = await restarted.ProcessActionAsync(request);
        Assert.True(replay.Duplicate);
        var conflicting = request with
        {
            MessageId = Guid.Parse("40000000-0000-4000-8000-000000000009"),
            Type = MessageKind.Denial,
            Reason = "user_denied",
        };
        await Assert.ThrowsAsync<BridgeStateConflictException>(() => restarted.ProcessActionAsync(conflicting));
    }

    [Fact]
    public async Task Action_at_exact_deadline_is_rejected_without_mutation()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"agentping-actions-{Guid.NewGuid():N}", "state.json");
        using var store = new BridgeStateStore(
            statePath, 16, TimeSpan.FromMinutes(5), new FixedTimeProvider(Now.AddSeconds(30)));
        await store.InitializeAsync();
        await SeedAttentionAsync(store, false, ["approve", "deny"]);

        var exception = await Assert.ThrowsAsync<BridgeActionRejectedException>(
            () => store.ProcessActionAsync(ApprovalRequest(Now.AddSeconds(30))));

        Assert.Equal("ACTION_EXPIRED", exception.Code);
        Assert.Single((await store.GetSnapshotAsync()).Attentions);
    }

    private static DeviceActionRequest ApprovalRequest(DateTimeOffset sentAt) => new(
        MessageId: Guid.Parse("40000000-0000-4000-8000-000000000003"),
        Type: MessageKind.Approval,
        SentAt: sentAt,
        ActionId: Guid.Parse("50000000-0000-4000-8000-000000000001"),
        AttentionId: "attention-1",
        ExpectedRevision: 2,
        DeviceId: "display-1",
        Destructive: false,
        Confirmation: null,
        Reason: null,
        Note: null,
        Text: null);

    private static async Task SeedAttentionAsync(
        BridgeStateStore store,
        bool destructive,
        IReadOnlyList<string> allowedActions)
    {
        await store.IngestEventAsync(new ProtocolEnvelope<EventPayload>
        {
            ProtocolVersion = ProtocolV1.Version,
            MessageId = Guid.Parse("40000000-0000-4000-8000-000000000001"),
            Type = MessageKind.Event,
            SentAt = Now,
            ConnectionId = "provider-action-test",
            Sequence = 1,
            Payload = new EventPayload
            {
                EventId = "event-1",
                SessionId = "session-1",
                Provider = "manual",
                EventKind = "started",
                Summary = "Action test",
                Severity = "info",
            },
        });
        await store.IngestAttentionAsync(new ProtocolEnvelope<AttentionPayload>
        {
            ProtocolVersion = ProtocolV1.Version,
            MessageId = Guid.Parse("40000000-0000-4000-8000-000000000002"),
            Type = MessageKind.Attention,
            SentAt = Now.AddSeconds(1),
            ConnectionId = "provider-action-test",
            Sequence = 2,
            Payload = new AttentionPayload
            {
                AttentionId = "attention-1",
                SessionId = "session-1",
                Revision = 2,
                Category = "approval",
                Title = "Apply changes?",
                Body = "Run the bounded operation.",
                ResponseDeadlineAt = Now.AddSeconds(30),
                Destructive = destructive,
                AllowedActions = allowedActions,
            },
        });
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
