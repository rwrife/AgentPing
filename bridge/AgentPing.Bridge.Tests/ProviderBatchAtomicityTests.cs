using AgentPing.Bridge.Core;
using AgentPing.Bridge.Protocol;

namespace AgentPing.Bridge.Tests;

public sealed class ProviderBatchAtomicityTests
{
    [Fact]
    public async Task Event_and_attention_roll_back_together_when_persistence_fails()
    {
        var now = new DateTimeOffset(2026, 8, 25, 19, 0, 0, TimeSpan.Zero);
        var root = Path.Combine(Path.GetTempPath(), $"agentping-provider-atomic-{Guid.NewGuid():N}");
        var statePath = Path.Combine(root, "state.json");
        using var store = new BridgeStateStore(statePath, 16, TimeSpan.FromMinutes(5), TimeProvider.System);
        await store.InitializeAsync();
        Directory.CreateDirectory(statePath + ".tmp");
        var eventEnvelope = new ProtocolEnvelope<EventPayload>
        {
            ProtocolVersion = ProtocolV1.Version,
            MessageId = Guid.NewGuid(),
            Type = MessageKind.Event,
            SentAt = now,
            ConnectionId = "provider-atomic-test",
            Sequence = 1,
            Payload = new EventPayload
            {
                EventId = "provider-atomic-event",
                SessionId = "provider-atomic-session",
                Provider = "manual",
                EventKind = "message",
                Summary = "Atomic provider event",
                Severity = "info",
            },
        };

        await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(() => store.IngestProviderBatchAsync(
            eventEnvelope,
            session => new ProtocolEnvelope<AttentionPayload>
            {
                ProtocolVersion = ProtocolV1.Version,
                MessageId = Guid.NewGuid(),
                Type = MessageKind.Attention,
                SentAt = now,
                ConnectionId = eventEnvelope.ConnectionId,
                Sequence = 2,
                Payload = new AttentionPayload
                {
                    AttentionId = "provider-atomic-attention",
                    SessionId = session.SessionId,
                    Revision = session.Revision + 1,
                    Category = "approval",
                    Title = "Atomic attention",
                    Body = "Both state updates must commit or roll back.",
                    ResponseDeadlineAt = now.AddSeconds(30),
                    Destructive = true,
                    AllowedActions = ["approve", "deny"],
                },
            }));

        var snapshot = await store.GetSnapshotAsync();
        Assert.Empty(snapshot.Sessions);
        Assert.Empty(snapshot.Attentions);
        Assert.Empty(snapshot.History);
        Assert.Equal<ulong>(0, snapshot.LastServerSequence);
    }
}
