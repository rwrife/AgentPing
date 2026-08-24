using System.Text.RegularExpressions;
using AgentPing.Bridge.Protocol;

namespace AgentPing.Bridge.Core;

public static partial class ProtocolValidation
{
    private static readonly HashSet<string> EventKinds =
        ["started", "progress", "completed", "failed", "message"];

    private static readonly HashSet<string> Severities =
        ["info", "success", "warning", "error"];

    private static readonly HashSet<string> AttentionCategories =
        ["approval", "reply", "notification"];

    private static readonly HashSet<string> AllowedActions =
        ["approve", "deny", "reply"];

    private static readonly HashSet<string> AllowedFeatures =
        ["events", "sessions", "attention", "approve", "deny", "reply", "resume"];

    private static readonly HashSet<string> HeartbeatStatuses =
        ["ready", "busy", "degraded"];

    public static bool IsValidCapability(
        ProtocolEnvelope<CapabilityPayload>? envelope,
        string expectedDeviceId) =>
        envelope is not null
        && envelope.ProtocolVersion == ProtocolV1.Version
        && IsProtocolMessageId(envelope.MessageId)
        && envelope.Type == MessageKind.Capability
        && envelope.Sequence == 1
        && envelope.ServerSequence is null
        && IsConnectionIdentifier(envelope.ConnectionId)
        && envelope.Payload is not null
        && envelope.Payload.DeviceId == expectedDeviceId
        && IsIdentifier(envelope.Payload.DeviceId)
        && envelope.Payload.Role == "display"
        && envelope.Payload.SupportedVersions is { Count: >= 1 and <= 8 } supportedVersions
        && supportedVersions.Distinct(StringComparer.Ordinal).Count() == supportedVersions.Count
        && supportedVersions.All(version => version is not null && VersionPattern().IsMatch(version))
        && supportedVersions.Contains(ProtocolV1.Version, StringComparer.Ordinal)
        && envelope.Payload.Features is { Count: <= 16 } features
        && features.Distinct(StringComparer.Ordinal).Count() == features.Count
        && features.All(feature => feature is not null && AllowedFeatures.Contains(feature))
        && envelope.Payload.MaxMessageBytes == ProtocolV1.MaxMessageBytes
        && envelope.Payload.ResumeFromSequence <= 9_007_199_254_740_991UL
        && envelope.Payload.ResetState is null
        && envelope.Payload.SnapshotItemCount is null
        && envelope.Payload.SnapshotCheckpoint is null
        && (envelope.Payload.SoftwareVersion is null
            || envelope.Payload.SoftwareVersion.Length is >= 1 and <= 64);

    public static bool IsValidHeartbeat(
        ProtocolEnvelope<HeartbeatPayload>? envelope,
        string expectedConnectionId) =>
        envelope is not null
        && envelope.ProtocolVersion == ProtocolV1.Version
        && IsProtocolMessageId(envelope.MessageId)
        && envelope.Type == MessageKind.Heartbeat
        && envelope.ServerSequence is null
        && envelope.Sequence <= 9_007_199_254_740_991UL
        && envelope.ConnectionId == expectedConnectionId
        && IsConnectionIdentifier(envelope.ConnectionId)
        && envelope.Payload is not null
        && envelope.Payload.UptimeMs <= 9_007_199_254_740_991UL
        && HeartbeatStatuses.Contains(envelope.Payload.Status)
        && envelope.Payload.LastReceivedSequence <= 9_007_199_254_740_991UL
        && envelope.Payload.QueueDepth is >= 0 and <= ProtocolV1.MaxReplayWindowMessages;

    public static IReadOnlyDictionary<string, string[]> ValidateEvent(
        ProtocolEnvelope<EventPayload> envelope,
        TimeProvider timeProvider)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        AddIf(errors, envelope.ProtocolVersion != ProtocolV1.Version, "protocolVersion", "Protocol version must be 1.0.");
        AddIf(errors, !IsProtocolMessageId(envelope.MessageId), "messageId", "Message ID must be a canonical UUID.");
        AddIf(errors, envelope.Type != MessageKind.Event, "type", "Message type must be event.");
        AddIf(errors, envelope.Sequence is 0 or > 9_007_199_254_740_991UL, "sequence", "Sequence must be between 1 and 9007199254740991.");
        AddIf(errors, envelope.ServerSequence is not null, "serverSequence", "Server sequence is bridge-owned and cannot be supplied by a provider.");
        AddIf(errors, !IsConnectionIdentifier(envelope.ConnectionId), "connectionId", "Connection ID is invalid.");
        ValidateSentAt(errors, envelope.SentAt, timeProvider);

        var payload = envelope.Payload;
        if (payload is null)
        {
            errors["payload"] = ["Payload is required."];
            return errors;
        }

        AddIf(errors, !IsIdentifier(payload.EventId), "payload.eventId", "Event ID is invalid.");
        AddIf(errors, !IsIdentifier(payload.SessionId), "payload.sessionId", "Session ID is invalid.");
        AddIf(errors, string.IsNullOrEmpty(payload.Provider) || !ProviderPattern().IsMatch(payload.Provider), "payload.provider", "Provider is invalid.");
        AddIf(errors, !EventKinds.Contains(payload.EventKind), "payload.eventKind", "Event kind is unsupported.");
        AddIf(errors, string.IsNullOrEmpty(payload.Summary) || payload.Summary.Length > 512, "payload.summary", "Summary must contain 1 to 512 characters.");
        AddIf(errors, payload.Detail?.Length > 2048, "payload.detail", "Detail must contain at most 2048 characters.");
        AddIf(errors, !Severities.Contains(payload.Severity), "payload.severity", "Severity is unsupported.");

        if (payload.Metadata is { } metadata)
        {
            AddIf(errors, metadata.Count > 16, "payload.metadata", "Metadata must contain at most 16 entries.");
            foreach (var pair in metadata)
            {
                AddIf(
                    errors,
                    !MetadataKeyPattern().IsMatch(pair.Key) || CredentialKeyPattern().IsMatch(pair.Key),
                    $"payload.metadata.{pair.Key}",
                    "Metadata key is invalid or credential-like.");
                AddIf(errors, pair.Value is null || pair.Value.Length > 256, $"payload.metadata.{pair.Key}", "Metadata value must contain at most 256 characters.");
            }
        }

        return errors;
    }

    public static IReadOnlyDictionary<string, string[]> ValidateAttention(
        ProtocolEnvelope<AttentionPayload> envelope,
        TimeProvider timeProvider)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        AddIf(errors, envelope.ProtocolVersion != ProtocolV1.Version, "protocolVersion", "Protocol version must be 1.0.");
        AddIf(errors, !IsProtocolMessageId(envelope.MessageId), "messageId", "Message ID must be a canonical UUID.");
        AddIf(errors, envelope.Type != MessageKind.Attention, "type", "Message type must be attention.");
        AddIf(errors, envelope.Sequence is 0 or > 9_007_199_254_740_991UL, "sequence", "Sequence must be between 1 and 9007199254740991.");
        AddIf(errors, envelope.ServerSequence is not null, "serverSequence", "Server sequence is bridge-owned and cannot be supplied by a provider.");
        AddIf(errors, !IsConnectionIdentifier(envelope.ConnectionId), "connectionId", "Connection ID is invalid.");
        ValidateSentAt(errors, envelope.SentAt, timeProvider);

        var payload = envelope.Payload;
        if (payload is null)
        {
            errors["payload"] = ["Payload is required."];
            return errors;
        }

        AddIf(errors, !IsIdentifier(payload.AttentionId), "payload.attentionId", "Attention ID is invalid.");
        AddIf(errors, !IsIdentifier(payload.SessionId), "payload.sessionId", "Session ID is invalid.");
        AddIf(errors, payload.Revision > 9_007_199_254_740_991UL, "payload.revision", "Revision is out of range.");
        AddIf(errors, !AttentionCategories.Contains(payload.Category), "payload.category", "Attention category is unsupported.");
        AddIf(errors, string.IsNullOrEmpty(payload.Title) || payload.Title.Length > 120, "payload.title", "Title must contain 1 to 120 characters.");
        AddIf(errors, string.IsNullOrEmpty(payload.Body) || payload.Body.Length > 1024, "payload.body", "Body must contain 1 to 1024 characters.");
        AddIf(
            errors,
            payload.ResponseDeadlineAt.Offset != TimeSpan.Zero
                || payload.ResponseDeadlineAt <= envelope.SentAt
                || payload.ResponseDeadlineAt > envelope.SentAt.AddSeconds(ProtocolV1.ApprovalTimeoutSeconds),
            "payload.responseDeadlineAt",
            "Response deadline must be after sentAt and no more than 30 seconds later.");
        AddIf(
            errors,
            payload.AllowedActions is null
                || payload.AllowedActions.Count is < 1 or > 3
                || payload.AllowedActions.Distinct(StringComparer.Ordinal).Count() != payload.AllowedActions.Count
                || payload.AllowedActions.Any(action => !AllowedActions.Contains(action)),
            "payload.allowedActions",
            "Allowed actions must contain 1 to 3 unique supported actions.");
        return errors;
    }

    private static void ValidateSentAt(
        IDictionary<string, string[]> errors,
        DateTimeOffset sentAt,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var now = timeProvider.GetUtcNow();
        AddIf(
            errors,
            sentAt.Offset != TimeSpan.Zero
                || sentAt < now.AddSeconds(-300)
                || sentAt > now.AddSeconds(300),
            "sentAt",
            "Sent time must use UTC offset zero and be within 300 seconds of bridge UTC time.");
    }

    private static bool IsProtocolMessageId(Guid value)
    {
        if (value == Guid.Empty)
        {
            return false;
        }

        var canonical = value.ToString("D");
        return canonical[14] is >= '1' and <= '8'
            && canonical[19] is '8' or '9' or 'a' or 'b';
    }

    private static bool IsConnectionIdentifier(string? value) =>
        value is not null && value.Length is >= 1 and <= 64 && IdentifierPattern().IsMatch(value);

    private static bool IsIdentifier(string? value) =>
        value is not null && value.Length is >= 1 and <= 128 && IdentifierPattern().IsMatch(value);

    private static void AddIf(
        IDictionary<string, string[]> errors,
        bool condition,
        string key,
        string message)
    {
        if (condition)
        {
            errors[key] = [message];
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("^[a-z][a-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ProviderPattern();

    [GeneratedRegex("^[a-z][a-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex MetadataKeyPattern();

    [GeneratedRegex("^[1-9][0-9]*\\.[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex("(^|[_.-])(token|secret|password|credential|authorization|cookie|api[_-]?key)([_.-]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CredentialKeyPattern();
}
