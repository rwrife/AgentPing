using System.Text.Json.Serialization;

namespace AgentPing.Bridge.Protocol;

public static class ProtocolV1
{
    public const string Version = "1.0";
    public const int MaxMessageBytes = 16_384;
    public const int MaxReplyCharacters = 512;
    public const int MaxReplayWindowMessages = 256;
    public const int ApprovalTimeoutSeconds = 30;
}

public enum MessageKind
{
    Event,
    Session,
    Attention,
    Approval,
    Denial,
    Reply,
    Heartbeat,
    Error,
    Capability,
}

public sealed record ProtocolEnvelope<TPayload>
{
    public required string ProtocolVersion { get; init; }
    public required Guid MessageId { get; init; }
    public required MessageKind Type { get; init; }
    public required DateTimeOffset SentAt { get; init; }
    public required string ConnectionId { get; init; }
    public required ulong Sequence { get; init; }
    public required TPayload Payload { get; init; }
}

public sealed record EventPayload
{
    public required string EventId { get; init; }
    public required string SessionId { get; init; }
    public required string Provider { get; init; }
    public required string EventKind { get; init; }
    public required string Summary { get; init; }
    public string? Detail { get; init; }
    public required string Severity { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed record SessionPayload
{
    public required string SessionId { get; init; }
    public required string Provider { get; init; }
    public required string State { get; init; }
    public required string DisplayName { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required ulong Revision { get; init; }
    public required int UnreadCount { get; init; }
}

public sealed record AttentionPayload
{
    public required string AttentionId { get; init; }
    public required string SessionId { get; init; }
    public required ulong Revision { get; init; }
    public required string Category { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public required DateTimeOffset ResponseDeadlineAt { get; init; }
    public required bool Destructive { get; init; }
    public required IReadOnlyList<string> AllowedActions { get; init; }
}

public sealed record ApprovalPayload
{
    public required Guid ActionId { get; init; }
    public required string AttentionId { get; init; }
    public required ulong ExpectedRevision { get; init; }
    public required bool Destructive { get; init; }
    public Confirmation? Confirmation { get; init; }
}

public sealed record Confirmation
{
    public required Guid PresentedMessageId { get; init; }
    public required string PromptDigest { get; init; }
    public required DateTimeOffset ConfirmedAt { get; init; }
}

public sealed record DenialPayload
{
    public required Guid ActionId { get; init; }
    public required string AttentionId { get; init; }
    public required ulong ExpectedRevision { get; init; }
    public required string Reason { get; init; }
    public string? Note { get; init; }
}

public sealed record ReplyPayload
{
    public required Guid ActionId { get; init; }
    public required string AttentionId { get; init; }
    public required ulong ExpectedRevision { get; init; }
    public required string Text { get; init; }
}

public sealed record HeartbeatPayload
{
    public required ulong UptimeMs { get; init; }
    public required string Status { get; init; }
    public required ulong LastReceivedSequence { get; init; }
    public required int QueueDepth { get; init; }
}

public sealed record ErrorPayload
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public required bool Retryable { get; init; }
    public Guid? RelatedMessageId { get; init; }
}

public sealed record CapabilityPayload
{
    public required string DeviceId { get; init; }
    public required string Role { get; init; }
    public required IReadOnlyList<string> SupportedVersions { get; init; }
    public required IReadOnlyList<string> Features { get; init; }
    public required int MaxMessageBytes { get; init; }
    public required ulong ResumeFromSequence { get; init; }
    public string? SoftwareVersion { get; init; }
}
