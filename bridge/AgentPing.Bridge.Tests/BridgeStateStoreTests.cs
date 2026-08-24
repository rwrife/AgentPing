using AgentPing.Bridge.Core;
using AgentPing.Bridge.Protocol;
using AgentPing.Bridge.Transport;

namespace AgentPing.Bridge.Tests;

public sealed class BridgeStateStoreTests
{
    [Fact]
    public async Task Started_event_creates_a_running_session()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"agentping-{Guid.NewGuid():N}", "state.json");
        var store = new BridgeStateStore(statePath, maxHistory: 4, staleAfter: TimeSpan.FromMinutes(5), TimeProvider.System);
        await store.InitializeAsync();

        var result = await store.IngestEventAsync(EventEnvelope(
            messageId: Guid.Parse("10000000-0000-4000-8000-000000000001"),
            eventId: "event-1",
            eventKind: "started",
            summary: "Build AgentPing"));

        Assert.False(result.Duplicate);
        var session = Assert.Single(result.Snapshot.Sessions);
        Assert.Equal("session-1", session.SessionId);
        Assert.Equal("codex", session.Provider);
        Assert.Equal("running", session.State);
        Assert.Equal("Build AgentPing", session.DisplayName);
        Assert.Equal<ulong>(1, session.Revision);
        Assert.Equal(1, session.UnreadCount);
        Assert.Single(result.Snapshot.History);
    }

    [Fact]
    public async Task Attention_is_queued_and_marks_the_session_waiting_for_input()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"agentping-{Guid.NewGuid():N}", "state.json");
        var store = new BridgeStateStore(statePath, maxHistory: 4, staleAfter: TimeSpan.FromMinutes(5), TimeProvider.System);
        await store.InitializeAsync();
        await store.IngestEventAsync(EventEnvelope(
            messageId: Guid.Parse("10000000-0000-4000-8000-000000000002"),
            eventId: "event-2",
            eventKind: "started",
            summary: "Build AgentPing"));

        var result = await store.IngestAttentionAsync(new ProtocolEnvelope<AttentionPayload>
        {
            ProtocolVersion = ProtocolV1.Version,
            MessageId = Guid.Parse("10000000-0000-4000-8000-000000000003"),
            Type = MessageKind.Attention,
            SentAt = DateTimeOffset.UtcNow,
            ConnectionId = "provider-test",
            Sequence = 2,
            Payload = new AttentionPayload
            {
                AttentionId = "attention-1",
                SessionId = "session-1",
                Revision = 7,
                Category = "approval",
                Title = "Apply changes?",
                Body = "The agent is waiting for approval.",
                ResponseDeadlineAt = DateTimeOffset.UtcNow.AddSeconds(30),
                Destructive = false,
                AllowedActions = ["approve", "deny"],
            },
        });

        Assert.False(result.Duplicate);
        Assert.Equal("attention-1", Assert.Single(result.Snapshot.Attentions).AttentionId);
        var session = Assert.Single(result.Snapshot.Sessions);
        Assert.Equal("waiting_for_input", session.State);
        Assert.Equal<ulong>(7, session.Revision);
        Assert.Equal(2, result.Snapshot.History.Count);
    }

    [Fact]
    public async Task Duplicate_event_is_idempotent()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"agentping-{Guid.NewGuid():N}", "state.json");
        var store = new BridgeStateStore(statePath, maxHistory: 4, staleAfter: TimeSpan.FromMinutes(5), TimeProvider.System);
        await store.InitializeAsync();
        var envelope = EventEnvelope(
            messageId: Guid.Parse("70000000-0000-4000-8000-000000000001"),
            eventId: "duplicate-event",
            eventKind: "progress",
            summary: "Only once");

        await store.IngestEventAsync(envelope);
        var duplicate = await store.IngestEventAsync(envelope with { MessageId = Guid.NewGuid() });

        Assert.True(duplicate.Duplicate);
        Assert.Equal<ulong>(1, Assert.Single(duplicate.Snapshot.Sessions).Revision);
        Assert.Single(duplicate.Snapshot.History);
    }

    [Fact]
    public async Task Noncontiguous_connection_sequence_is_rejected_without_mutating_state()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"agentping-{Guid.NewGuid():N}", "state.json");
        var store = new BridgeStateStore(statePath, maxHistory: 4, staleAfter: TimeSpan.FromMinutes(5), TimeProvider.System);
        await store.InitializeAsync();
        await store.IngestEventAsync(EventEnvelope(
            messageId: Guid.Parse("70000000-0000-4000-8000-000000000004"),
            eventId: "sequence-event-1",
            eventKind: "started",
            summary: "First"));
        var outOfSequence = EventEnvelope(
            messageId: Guid.Parse("70000000-0000-4000-8000-000000000005"),
            eventId: "sequence-event-3",
            eventKind: "completed",
            summary: "Must not apply") with
        { Sequence = 3 };

        await Assert.ThrowsAsync<BridgeStateConflictException>(() => store.IngestEventAsync(outOfSequence));

        var unchanged = await store.GetSnapshotAsync();
        Assert.Equal("First", Assert.Single(unchanged.Sessions).DisplayName);
        Assert.Equal<ulong>(1, unchanged.LastServerSequence);
        var contiguous = await store.IngestEventAsync(outOfSequence with
        {
            MessageId = Guid.Parse("70000000-0000-4000-8000-000000000006"),
            Payload = outOfSequence.Payload with { EventId = "sequence-event-2", Summary = "Second" },
            Sequence = 2,
        });
        Assert.False(contiguous.Duplicate);
        Assert.Equal("Second", Assert.Single(contiguous.Snapshot.Sessions).DisplayName);
    }

    [Fact]
    public async Task Persistence_failure_rolls_back_state_and_deduplication()
    {
        var root = Path.Combine(Path.GetTempPath(), $"agentping-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var blockedDirectory = Path.Combine(root, "blocked");
        var statePath = Path.Combine(blockedDirectory, "state.json");
        var store = new BridgeStateStore(statePath, maxHistory: 4, staleAfter: TimeSpan.FromMinutes(5), TimeProvider.System);
        await store.InitializeAsync();
        Directory.CreateDirectory(statePath + ".tmp");
        var envelope = EventEnvelope(
            messageId: Guid.Parse("70000000-0000-4000-8000-000000000007"),
            eventId: "persistence-failure-event",
            eventKind: "started",
            summary: "Commit only after persistence");

        await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(() => store.IngestEventAsync(envelope));

        var rolledBack = await store.GetSnapshotAsync();
        Assert.Empty(rolledBack.Sessions);
        Assert.Empty(rolledBack.History);
        Assert.Equal<ulong>(0, rolledBack.LastServerSequence);

        Directory.Delete(statePath + ".tmp");
        var retry = await store.IngestEventAsync(envelope);
        Assert.False(retry.Duplicate);
        Assert.Single(retry.Snapshot.Sessions);
        Assert.Equal<ulong>(1, retry.Snapshot.LastServerSequence);
    }

    [Fact]
    public async Task State_and_deduplication_survive_restart()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"agentping-{Guid.NewGuid():N}", "state.json");
        var first = new BridgeStateStore(statePath, maxHistory: 4, staleAfter: TimeSpan.FromMinutes(5), TimeProvider.System);
        await first.InitializeAsync();
        var envelope = EventEnvelope(
            messageId: Guid.Parse("70000000-0000-4000-8000-000000000002"),
            eventId: "restart-event",
            eventKind: "started",
            summary: "Persist me");
        await first.IngestEventAsync(envelope);
        first.Dispose();

        var restarted = new BridgeStateStore(statePath, maxHistory: 4, staleAfter: TimeSpan.FromMinutes(5), TimeProvider.System);
        await restarted.InitializeAsync();
        var duplicate = await restarted.IngestEventAsync(envelope);

        Assert.True(duplicate.Duplicate);
        Assert.Equal("Persist me", Assert.Single(duplicate.Snapshot.Sessions).DisplayName);
        Assert.Single(duplicate.Snapshot.History);
        restarted.Dispose();
    }

    [Fact]
    public async Task Identifier_fingerprints_distinguish_exact_retries_from_conflicting_reuse()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"agentping-{Guid.NewGuid():N}", "state.json");
        using var store = new BridgeStateStore(statePath, 4, TimeSpan.FromMinutes(5), TimeProvider.System);
        await store.InitializeAsync();
        var original = EventEnvelope(Guid.NewGuid(), "fingerprint-id", "started", "Original");
        await store.IngestEventAsync(original);
        await store.IngestEventAsync(EventEnvelope(Guid.NewGuid(), "later-id", "progress", "Later") with { Sequence = 2 });

        var duplicate = await store.IngestEventAsync(original with { MessageId = Guid.NewGuid() });
        Assert.True(duplicate.Duplicate);
        Assert.Equal<ulong>(1, duplicate.RecordedServerSequence);
        await Assert.ThrowsAsync<BridgeStateConflictException>(() => store.IngestEventAsync(
            original with { MessageId = Guid.NewGuid(), Payload = original.Payload with { Summary = "Changed" } }));
        await Assert.ThrowsAsync<BridgeStateConflictException>(() => store.IngestEventAsync(
            original with { Payload = original.Payload with { EventId = "different-id", Summary = "Changed" } }));

        var attention = new ProtocolEnvelope<AttentionPayload>
        {
            ProtocolVersion = ProtocolV1.Version,
            MessageId = Guid.NewGuid(),
            Type = MessageKind.Attention,
            SentAt = DateTimeOffset.UtcNow,
            ConnectionId = "other-provider",
            Sequence = 1,
            Payload = new AttentionPayload
            {
                AttentionId = "fingerprint-id",
                SessionId = "session-1",
                Revision = 3,
                Category = "approval",
                Title = "Reuse",
                Body = "Must conflict",
                ResponseDeadlineAt = DateTimeOffset.UtcNow.AddSeconds(10),
                Destructive = false,
                AllowedActions = ["deny"],
            },
        };
        await Assert.ThrowsAsync<BridgeStateConflictException>(() => store.IngestAttentionAsync(attention));
    }

    [Fact]
    public async Task Persistence_writer_lock_contends_and_is_released_on_disposal()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"agentping-{Guid.NewGuid():N}", "state.json");
        var first = new BridgeStateStore(statePath, 4, TimeSpan.FromMinutes(5), TimeProvider.System);
        await first.InitializeAsync();
        using var contender = new BridgeStateStore(statePath, 4, TimeSpan.FromMinutes(5), TimeProvider.System);
        var conflict = await Assert.ThrowsAsync<InvalidOperationException>(() => contender.InitializeAsync());
        Assert.Contains("Another bridge instance", conflict.Message, StringComparison.Ordinal);

        first.Dispose();
        await contender.InitializeAsync();
    }

    [Fact]
    public async Task History_never_exceeds_configured_bound()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"agentping-{Guid.NewGuid():N}", "state.json");
        var store = new BridgeStateStore(statePath, maxHistory: 2, staleAfter: TimeSpan.FromMinutes(5), TimeProvider.System);
        await store.InitializeAsync();
        for (var index = 0; index < 5; index++)
        {
            await store.IngestEventAsync(EventEnvelope(
                messageId: Guid.NewGuid(),
                eventId: $"bounded-event-{index}",
                eventKind: "progress",
                summary: $"Event {index}") with
            { Sequence = (ulong)index + 1 });
        }

        var snapshot = await store.GetSnapshotAsync();

        Assert.Equal(2, snapshot.History.Count);
        Assert.Equal<ulong>(5, snapshot.LastServerSequence);
        Assert.Equal(new ulong[] { 4, 5 }, snapshot.History.Select(item => item.ServerSequence).ToArray());
    }

    [Fact]
    public async Task Stale_session_monitor_publishes_idle_transition()
    {
        var now = new DateTimeOffset(2026, 8, 22, 19, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(now);
        var statePath = Path.Combine(Path.GetTempPath(), $"agentping-{Guid.NewGuid():N}", "state.json");
        var store = new BridgeStateStore(statePath, maxHistory: 4, staleAfter: TimeSpan.FromMinutes(5), clock);
        await store.InitializeAsync();
        await store.IngestEventAsync(EventEnvelope(
            messageId: Guid.Parse("70000000-0000-4000-8000-000000000003"),
            eventId: "monitor-event",
            eventKind: "started",
            summary: "Monitor me") with
        { SentAt = now });
        var hub = new DeviceConnectionHub();
        hub.InitializeLastPublishedSequence((await store.GetSnapshotAsync()).LastServerSequence);
        await using var subscription = hub.Subscribe();
        var monitor = new StaleSessionMonitor(store, hub, TimeSpan.FromSeconds(30));
        clock.Advance(TimeSpan.FromMinutes(6));

        Assert.True(await monitor.SweepOnceAsync());
        var published = await subscription.Reader.ReadAsync();

        Assert.Equal(MessageKind.Session, published.Type);
        Assert.Equal("idle", published.Payload.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Stale_running_session_transitions_to_idle()
    {
        var now = new DateTimeOffset(2026, 8, 22, 19, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(now);
        var statePath = Path.Combine(Path.GetTempPath(), $"agentping-{Guid.NewGuid():N}", "state.json");
        var store = new BridgeStateStore(statePath, maxHistory: 4, staleAfter: TimeSpan.FromMinutes(5), clock);
        await store.InitializeAsync();
        await store.IngestEventAsync(EventEnvelope(
            messageId: Guid.Parse("10000000-0000-4000-8000-000000000004"),
            eventId: "event-4",
            eventKind: "started",
            summary: "Build AgentPing") with
        { SentAt = now });
        clock.Advance(TimeSpan.FromMinutes(6));

        var committedItems = await store.MarkStaleSessionsAsync();

        Assert.Single(committedItems);
        var session = Assert.Single((await store.GetSnapshotAsync()).Sessions);
        Assert.Equal("idle", session.State);
        Assert.Equal<ulong>(2, session.Revision);
        Assert.Equal(clock.GetUtcNow(), session.UpdatedAt);
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan amount) => _utcNow += amount;
    }

    private static ProtocolEnvelope<EventPayload> EventEnvelope(
        Guid messageId,
        string eventId,
        string eventKind,
        string summary) => new()
        {
            ProtocolVersion = ProtocolV1.Version,
            MessageId = messageId,
            Type = MessageKind.Event,
            SentAt = DateTimeOffset.UtcNow,
            ConnectionId = "provider-test",
            Sequence = 1,
            Payload = new EventPayload
            {
                EventId = eventId,
                SessionId = "session-1",
                Provider = "codex",
                EventKind = eventKind,
                Summary = summary,
                Severity = "info",
            },
        };
}
