using AgentPing.Bridge.Core;
using AgentPing.Bridge.Protocol;

namespace AgentPing.Bridge.Tests;

public sealed class ProtocolValidationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 19, 0, 0, TimeSpan.Zero);
    private static readonly TimeProvider Clock = new FixedTimeProvider(Now);

    [Theory]
    [InlineData(-301)]
    [InlineData(301)]
    public void Event_rejects_sentAt_outside_clock_skew_window(int seconds)
    {
        var errors = ProtocolValidation.ValidateEvent(Event(Now.AddSeconds(seconds)), Clock);

        Assert.Contains("sentAt", errors.Keys);
    }

    [Fact]
    public void Event_rejects_non_utc_sentAt_even_for_same_instant()
    {
        var errors = ProtocolValidation.ValidateEvent(Event(Now.ToOffset(TimeSpan.FromHours(2))), Clock);

        Assert.Contains("sentAt", errors.Keys);
    }

    [Theory]
    [InlineData(-301, 0)]
    [InlineData(301, 0)]
    [InlineData(0, 2)]
    public void Attention_rejects_past_future_and_non_utc_sentAt(int seconds, int offsetHours)
    {
        var sentAt = Now.AddSeconds(seconds).ToOffset(TimeSpan.FromHours(offsetHours));
        var errors = ProtocolValidation.ValidateAttention(Attention(sentAt), Clock);

        Assert.Contains("sentAt", errors.Keys);
    }

    [Fact]
    public void Attention_rejects_non_utc_deadline_and_preserves_thirty_second_bound()
    {
        var envelope = Attention(Now) with
        {
            Payload = Attention(Now).Payload with
            {
                ResponseDeadlineAt = Now.AddSeconds(30).ToOffset(TimeSpan.FromHours(1)),
            },
        };
        var errors = ProtocolValidation.ValidateAttention(envelope, Clock);

        Assert.Contains("payload.responseDeadlineAt", errors.Keys);
        Assert.Empty(ProtocolValidation.ValidateAttention(
            envelope with { Payload = envelope.Payload with { ResponseDeadlineAt = Now.AddSeconds(30) } }, Clock));
    }

    private static ProtocolEnvelope<EventPayload> Event(DateTimeOffset sentAt) => new()
    {
        ProtocolVersion = ProtocolV1.Version,
        MessageId = Guid.Parse("a0000000-0000-4000-8000-000000000001"),
        Type = MessageKind.Event,
        SentAt = sentAt,
        ConnectionId = "validation",
        Sequence = 1,
        Payload = new EventPayload
        {
            EventId = "event",
            SessionId = "session",
            Provider = "codex",
            EventKind = "started",
            Summary = "Valid",
            Severity = "info",
        },
    };

    private static ProtocolEnvelope<AttentionPayload> Attention(DateTimeOffset sentAt) => new()
    {
        ProtocolVersion = ProtocolV1.Version,
        MessageId = Guid.Parse("a0000000-0000-4000-8000-000000000002"),
        Type = MessageKind.Attention,
        SentAt = sentAt,
        ConnectionId = "validation",
        Sequence = 1,
        Payload = new AttentionPayload
        {
            AttentionId = "attention",
            SessionId = "session",
            Revision = 1,
            Category = "approval",
            Title = "Proceed?",
            Body = "Choose.",
            ResponseDeadlineAt = sentAt.AddSeconds(30),
            Destructive = false,
            AllowedActions = ["deny"],
        },
    };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
