using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using AgentPing.Bridge.Protocol;

namespace AgentPing.Bridge.Core;

public sealed class BridgeStateStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly string _persistencePath;
    private readonly int _maxHistory;
    private readonly TimeSpan _staleAfter;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FileStream? _writerLock;
    private PersistedState _state = new();

    public BridgeStateStore(
        string persistencePath,
        int maxHistory,
        TimeSpan staleAfter,
        TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(persistencePath);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxHistory, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxHistory, ProtocolV1.MaxReplayWindowMessages);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(staleAfter, TimeSpan.Zero);

        _persistencePath = Path.GetFullPath(persistencePath);
        _maxHistory = maxHistory;
        _staleAfter = staleAfter;
        _timeProvider = timeProvider;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            AcquireWriterLock();
            if (!File.Exists(_persistencePath))
            {
                return;
            }

            await using var stream = File.OpenRead(_persistencePath);
            _state = await JsonSerializer.DeserializeAsync<PersistedState>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("Bridge state file is empty.");
            TrimHistory();
            TrimDeduplicationWindows();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IngestResult> IngestEventAsync(
        ProtocolEnvelope<EventPayload> envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var fingerprint = CreateFingerprint(MessageKind.Event, envelope.Payload);
            var duplicateSequence = CheckDuplicate(envelope.MessageId, envelope.Payload.EventId, MessageKind.Event, fingerprint);
            if (duplicateSequence is not null)
            {
                return new IngestResult(true, CreateSnapshot(), duplicateSequence.Value);
            }

            EnsureNextInboundSequence(envelope.ConnectionId, envelope.Sequence);
            var previousState = _state;
            _state = CloneState(previousState);
            try
            {
                RecordInboundSequence(envelope.ConnectionId, envelope.Sequence);

                var revision = _state.Sessions.TryGetValue(envelope.Payload.SessionId, out var existing)
                    ? existing.Revision + 1
                    : 1;
                var unreadCount = Math.Min((existing?.UnreadCount ?? 0) + 1, 999);
                var session = new SessionPayload
                {
                    SessionId = envelope.Payload.SessionId,
                    Provider = envelope.Payload.Provider,
                    State = MapSessionState(envelope.Payload.EventKind, existing?.State),
                    DisplayName = Truncate(envelope.Payload.Summary, 120),
                    UpdatedAt = envelope.SentAt,
                    Revision = revision,
                    UnreadCount = unreadCount,
                };
                _state.Sessions[session.SessionId] = session;

                _state.LastServerSequence++;
                _state.History.Add(new BridgeHistoryItem(
                    _state.LastServerSequence,
                    MessageKind.Session,
                    envelope.MessageId,
                    envelope.SentAt,
                    JsonSerializer.SerializeToElement(session, JsonOptions)));
                RecordFingerprint(envelope.MessageId, envelope.Payload.EventId, MessageKind.Event, fingerprint, _state.LastServerSequence);
                TrimHistory();
                TrimDeduplicationWindows();
                await PersistAsync(cancellationToken);

                return new IngestResult(false, CreateSnapshot(), _state.LastServerSequence);
            }
            catch
            {
                _state = previousState;
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IngestResult> IngestAttentionAsync(
        ProtocolEnvelope<AttentionPayload> envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var fingerprint = CreateFingerprint(MessageKind.Attention, envelope.Payload);
            var duplicateSequence = CheckDuplicate(envelope.MessageId, envelope.Payload.AttentionId, MessageKind.Attention, fingerprint);
            if (duplicateSequence is not null)
            {
                return new IngestResult(true, CreateSnapshot(), duplicateSequence.Value);
            }

            if (!_state.Sessions.TryGetValue(envelope.Payload.SessionId, out var existing))
            {
                throw new BridgeStateConflictException("Attention references an unknown session.");
            }

            if (envelope.Payload.Revision <= existing.Revision)
            {
                throw new BridgeStateConflictException("Attention revision must be newer than the current session revision.");
            }

            EnsureNextInboundSequence(envelope.ConnectionId, envelope.Sequence);
            var previousState = _state;
            _state = CloneState(previousState);
            try
            {
                RecordInboundSequence(envelope.ConnectionId, envelope.Sequence);
                _state.Attentions[envelope.Payload.AttentionId] = envelope.Payload;
                _state.AttentionMessageIds[envelope.Payload.AttentionId] = envelope.MessageId;
                _state.Sessions[existing.SessionId] = existing with
                {
                    State = "waiting_for_input",
                    UpdatedAt = envelope.SentAt,
                    Revision = envelope.Payload.Revision,
                    UnreadCount = Math.Min(existing.UnreadCount + 1, 999),
                };

                _state.LastServerSequence++;
                _state.History.Add(new BridgeHistoryItem(
                    _state.LastServerSequence,
                    MessageKind.Attention,
                    envelope.MessageId,
                    envelope.SentAt,
                    JsonSerializer.SerializeToElement(envelope.Payload, JsonOptions)));
                RecordFingerprint(envelope.MessageId, envelope.Payload.AttentionId, MessageKind.Attention, fingerprint, _state.LastServerSequence);
                TrimHistory();
                TrimDeduplicationWindows();
                await PersistAsync(cancellationToken);

                return new IngestResult(false, CreateSnapshot(), _state.LastServerSequence);
            }
            catch
            {
                _state = previousState;
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ProviderIngestResult> IngestProviderBatchAsync(
        ProtocolEnvelope<EventPayload> eventEnvelope,
        Func<SessionPayload, ProtocolEnvelope<AttentionPayload>>? createAttention,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventEnvelope);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var eventFingerprint = CreateFingerprint(MessageKind.Event, eventEnvelope.Payload);
            var duplicateEventSequence = CheckDuplicate(
                eventEnvelope.MessageId,
                eventEnvelope.Payload.EventId,
                MessageKind.Event,
                eventFingerprint);
            var previousState = _state;
            _state = CloneState(previousState);
            try
            {
                var committedItems = new List<BridgeHistoryItem>(2);
                if (duplicateEventSequence is null)
                {
                    EnsureNextInboundSequence(eventEnvelope.ConnectionId, eventEnvelope.Sequence);
                    RecordInboundSequence(eventEnvelope.ConnectionId, eventEnvelope.Sequence);
                    var revision = _state.Sessions.TryGetValue(eventEnvelope.Payload.SessionId, out var existingSession)
                        ? existingSession.Revision + 1
                        : 1;
                    var session = new SessionPayload
                    {
                        SessionId = eventEnvelope.Payload.SessionId,
                        Provider = eventEnvelope.Payload.Provider,
                        State = MapSessionState(eventEnvelope.Payload.EventKind, existingSession?.State),
                        DisplayName = Truncate(eventEnvelope.Payload.Summary, 120),
                        UpdatedAt = eventEnvelope.SentAt,
                        Revision = revision,
                        UnreadCount = Math.Min((existingSession?.UnreadCount ?? 0) + 1, 999),
                    };
                    _state.Sessions[session.SessionId] = session;
                    _state.LastServerSequence++;
                    var eventItem = new BridgeHistoryItem(
                        _state.LastServerSequence,
                        MessageKind.Session,
                        eventEnvelope.MessageId,
                        eventEnvelope.SentAt,
                        JsonSerializer.SerializeToElement(session, JsonOptions));
                    _state.History.Add(eventItem);
                    committedItems.Add(eventItem);
                    RecordFingerprint(
                        eventEnvelope.MessageId,
                        eventEnvelope.Payload.EventId,
                        MessageKind.Event,
                        eventFingerprint,
                        _state.LastServerSequence);
                }

                var attentionDuplicate = false;
                if (createAttention is not null)
                {
                    var currentSession = _state.Sessions[eventEnvelope.Payload.SessionId];
                    var attentionEnvelope = createAttention(currentSession);
                    var payload = attentionEnvelope.Payload;
                    if (_state.Attentions.TryGetValue(payload.AttentionId, out var existingAttention))
                    {
                        var sameAttention = existingAttention.SessionId == payload.SessionId
                            && existingAttention.Category == payload.Category
                            && existingAttention.Title == payload.Title
                            && existingAttention.Body == payload.Body
                            && existingAttention.Destructive == payload.Destructive
                            && existingAttention.AllowedActions.SequenceEqual(payload.AllowedActions, StringComparer.Ordinal);
                        if (!sameAttention)
                        {
                            throw new BridgeStateConflictException(
                                "Provider attention identifier was reused with different content.");
                        }

                        attentionDuplicate = true;
                    }
                    else
                    {
                        var attentionFingerprint = CreateFingerprint(MessageKind.Attention, payload);
                        _ = CheckDuplicate(
                            attentionEnvelope.MessageId,
                            payload.AttentionId,
                            MessageKind.Attention,
                            attentionFingerprint);
                        if (payload.Revision <= currentSession.Revision)
                        {
                            throw new BridgeStateConflictException(
                                "Attention revision must be newer than the current session revision.");
                        }

                        EnsureNextInboundSequence(attentionEnvelope.ConnectionId, attentionEnvelope.Sequence);
                        RecordInboundSequence(attentionEnvelope.ConnectionId, attentionEnvelope.Sequence);
                        _state.Attentions[payload.AttentionId] = payload;
                        _state.AttentionMessageIds[payload.AttentionId] = attentionEnvelope.MessageId;
                        _state.Sessions[currentSession.SessionId] = currentSession with
                        {
                            State = "waiting_for_input",
                            UpdatedAt = attentionEnvelope.SentAt,
                            Revision = payload.Revision,
                            UnreadCount = Math.Min(currentSession.UnreadCount + 1, 999),
                        };
                        _state.LastServerSequence++;
                        var attentionItem = new BridgeHistoryItem(
                            _state.LastServerSequence,
                            MessageKind.Attention,
                            attentionEnvelope.MessageId,
                            attentionEnvelope.SentAt,
                            JsonSerializer.SerializeToElement(payload, JsonOptions));
                        _state.History.Add(attentionItem);
                        committedItems.Add(attentionItem);
                        RecordFingerprint(
                            attentionEnvelope.MessageId,
                            payload.AttentionId,
                            MessageKind.Attention,
                            attentionFingerprint,
                            _state.LastServerSequence);
                    }
                }

                if (committedItems.Count > 0)
                {
                    TrimHistory();
                    TrimDeduplicationWindows();
                    await PersistAsync(cancellationToken);
                }

                return new ProviderIngestResult(
                    duplicateEventSequence is not null,
                    attentionDuplicate,
                    CreateSnapshot(),
                    committedItems);
            }
            catch
            {
                _state = previousState;
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ActionProcessResult> ProcessActionAsync(
        DeviceActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var fingerprint = CreateFingerprint(request.Type, request.FingerprintPayload);
            var duplicateSequence = CheckDuplicate(
                request.MessageId,
                request.ActionId.ToString("D"),
                request.Type,
                fingerprint);
            if (duplicateSequence is not null)
            {
                if (!_state.ActionOutcomes.TryGetValue(request.ActionId, out var priorOutcome))
                {
                    throw new BridgeStateConflictException("Action deduplication record is incomplete.");
                }

                var priorItem = _state.History.LastOrDefault(item => item.ServerSequence == duplicateSequence.Value)
                    ?? throw new BridgeStateConflictException("Action history record is no longer available.");
                return new ActionProcessResult(true, priorOutcome, CreateSnapshot(), priorItem, request);
            }

            if (!_state.Attentions.TryGetValue(request.AttentionId, out var attention))
            {
                throw new BridgeActionRejectedException("UNKNOWN_ATTENTION", "The attention item is no longer pending.");
            }

            if (!_state.Sessions.TryGetValue(attention.SessionId, out var session))
            {
                throw new BridgeActionRejectedException("UNKNOWN_SESSION", "The attention session is unavailable.");
            }

            if (request.ExpectedRevision != attention.Revision || session.Revision != attention.Revision)
            {
                throw new BridgeActionRejectedException("STALE_REVISION", "The attention item changed before the action arrived.");
            }

            var now = _timeProvider.GetUtcNow();
            if (now >= attention.ResponseDeadlineAt || request.SentAt >= attention.ResponseDeadlineAt)
            {
                throw new BridgeActionRejectedException("ACTION_EXPIRED", "The attention response deadline has passed.");
            }

            var action = ActionPolicy.GetAction(request);
            if (!attention.AllowedActions.Contains(action, StringComparer.Ordinal))
            {
                throw new BridgeActionRejectedException("ACTION_NOT_ALLOWED", "The requested action is not allowed for this attention item.");
            }

            ValidateActionPayload(request, attention, now);
            var previousState = _state;
            _state = CloneState(previousState);
            try
            {
                _state.Attentions.Remove(attention.AttentionId);
                _state.AttentionMessageIds.Remove(attention.AttentionId);
                var updatedSession = session with
                {
                    State = "running",
                    UpdatedAt = now,
                    Revision = checked(attention.Revision + 1),
                    UnreadCount = 0,
                };
                _state.Sessions[session.SessionId] = updatedSession;
                _state.LastServerSequence++;
                var committedItem = new BridgeHistoryItem(
                    _state.LastServerSequence,
                    MessageKind.Session,
                    request.MessageId,
                    now,
                    JsonSerializer.SerializeToElement(updatedSession, JsonOptions));
                _state.History.Add(committedItem);
                var outcome = new ActionOutcome(
                    request.ActionId,
                    request.AttentionId,
                    request.DeviceId,
                    session.Provider,
                    action,
                    "recorded",
                    now,
                    committedItem.ServerSequence);
                _state.ActionOutcomes[request.ActionId] = outcome;
                _state.ActionOutcomeOrder.Add(request.ActionId);
                RecordFingerprint(
                    request.MessageId,
                    request.ActionId.ToString("D"),
                    request.Type,
                    fingerprint,
                    committedItem.ServerSequence);
                TrimHistory();
                TrimDeduplicationWindows();
                TrimActionOutcomes();
                await PersistAsync(cancellationToken);
                return new ActionProcessResult(false, outcome, CreateSnapshot(), committedItem, request);
            }
            catch
            {
                _state = previousState;
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<BridgeHistoryItem>> MarkStaleSessionsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var cutoff = _timeProvider.GetUtcNow() - _staleAfter;
            var staleSessions = _state.Sessions.Values
                .Where(session => session.State is "running" or "waiting_for_input")
                .Where(session => session.UpdatedAt <= cutoff)
                .ToArray();
            if (staleSessions.Length == 0)
            {
                return [];
            }

            var previousState = _state;
            _state = CloneState(previousState);
            try
            {
                var committedItems = new List<BridgeHistoryItem>(staleSessions.Length);
                foreach (var session in staleSessions)
                {
                    var updated = session with
                    {
                        State = "idle",
                        UpdatedAt = _timeProvider.GetUtcNow(),
                        Revision = session.Revision + 1,
                    };
                    _state.Sessions[session.SessionId] = updated;
                    _state.LastServerSequence++;
                    var committedItem = new BridgeHistoryItem(
                        _state.LastServerSequence,
                        MessageKind.Session,
                        Guid.Empty,
                        updated.UpdatedAt,
                        JsonSerializer.SerializeToElement(updated, JsonOptions));
                    _state.History.Add(committedItem);
                    committedItems.Add(committedItem);
                }

                TrimHistory();
                TrimDeduplicationWindows();
                await PersistAsync(cancellationToken);
                return committedItems;
            }
            catch
            {
                _state = previousState;
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<BridgeSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return CreateSnapshot();
        }
        finally
        {
            _gate.Release();
        }
    }

    private BridgeSnapshot CreateSnapshot() => new(
        _state.Sessions.Values.OrderBy(session => session.SessionId, StringComparer.Ordinal).ToArray(),
        _state.Attentions.Values.OrderBy(attention => attention.AttentionId, StringComparer.Ordinal).ToArray(),
        _state.History.ToArray(),
        _state.LastServerSequence);

    private void EnsureNextInboundSequence(string connectionId, ulong sequence)
    {
        ulong expected = 1;
        if (_state.LastInboundSequences.TryGetValue(connectionId, out var previousSequence))
        {
            if (previousSequence == ulong.MaxValue)
            {
                throw new BridgeStateConflictException("Connection sequence is exhausted; reconnect with a new connection ID.");
            }

            expected = previousSequence + 1;
        }

        if (sequence != expected)
        {
            throw new BridgeStateConflictException(
                $"Connection sequence must be contiguous; expected {expected}.");
        }
    }

    private void RecordInboundSequence(string connectionId, ulong sequence)
    {
        if (!_state.LastInboundSequences.ContainsKey(connectionId))
        {
            _state.InboundConnectionOrder.Add(connectionId);
        }

        _state.LastInboundSequences[connectionId] = sequence;
        while (_state.InboundConnectionOrder.Count > _maxHistory)
        {
            var expiredConnectionId = _state.InboundConnectionOrder[0];
            _state.InboundConnectionOrder.RemoveAt(0);
            _state.LastInboundSequences.Remove(expiredConnectionId);
        }
    }

    private static PersistedState CloneState(PersistedState state) => new()
    {
        Sessions = new Dictionary<string, SessionPayload>(state.Sessions, StringComparer.Ordinal),
        Attentions = new Dictionary<string, AttentionPayload>(state.Attentions, StringComparer.Ordinal),
        History = [.. state.History],
        ProcessedMessageIds = [.. state.ProcessedMessageIds],
        ProcessedEventIds = [.. state.ProcessedEventIds],
        ProcessedAttentionIds = [.. state.ProcessedAttentionIds],
        MessageFingerprints = [.. state.MessageFingerprints],
        IdentifierFingerprints = [.. state.IdentifierFingerprints],
        AttentionMessageIds = new Dictionary<string, Guid>(state.AttentionMessageIds, StringComparer.Ordinal),
        ActionOutcomes = new Dictionary<Guid, ActionOutcome>(state.ActionOutcomes),
        ActionOutcomeOrder = [.. state.ActionOutcomeOrder],
        LastInboundSequences = new Dictionary<string, ulong>(state.LastInboundSequences, StringComparer.Ordinal),
        InboundConnectionOrder = [.. state.InboundConnectionOrder],
        LastServerSequence = state.LastServerSequence,
    };

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_persistencePath)
            ?? throw new InvalidOperationException("Persistence path must have a parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = _persistencePath + ".tmp";
        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 16_384,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, _state, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, _persistencePath, overwrite: true);
    }

    private void TrimHistory()
    {
        if (_state.History.Count > _maxHistory)
        {
            _state.History.RemoveRange(0, _state.History.Count - _maxHistory);
        }
    }

    private void TrimDeduplicationWindows()
    {
        TrimOldest(_state.ProcessedMessageIds, _maxHistory);
        TrimOldest(_state.ProcessedEventIds, _maxHistory);
        TrimOldest(_state.ProcessedAttentionIds, _maxHistory);
        TrimOldest(_state.MessageFingerprints, _maxHistory);
        TrimOldest(_state.IdentifierFingerprints, _maxHistory);
    }

    private void TrimActionOutcomes()
    {
        while (_state.ActionOutcomeOrder.Count > _maxHistory)
        {
            var expired = _state.ActionOutcomeOrder[0];
            _state.ActionOutcomeOrder.RemoveAt(0);
            _state.ActionOutcomes.Remove(expired);
        }
    }

    private void ValidateActionPayload(
        DeviceActionRequest request,
        AttentionPayload attention,
        DateTimeOffset now)
    {
        if (request.Type == MessageKind.Approval)
        {
            if (request.Destructive != attention.Destructive)
            {
                throw new BridgeActionRejectedException("DESTRUCTIVE_MISMATCH", "The destructive flag does not match the pending attention.");
            }

            if (attention.Destructive)
            {
                if (request.Confirmation is null
                    || !_state.AttentionMessageIds.TryGetValue(attention.AttentionId, out var presentedMessageId)
                    || request.Confirmation.PresentedMessageId != presentedMessageId
                    || _state.History.LastOrDefault(item => item.SourceMessageId == presentedMessageId)
                        is not { SentAt: var presentedAt }
                    || request.Confirmation.ConfirmedAt < presentedAt
                    || request.Confirmation.ConfirmedAt.Offset != TimeSpan.Zero
                    || request.Confirmation.ConfirmedAt > now
                    || request.Confirmation.ConfirmedAt > attention.ResponseDeadlineAt)
                {
                    throw new BridgeActionRejectedException("CONFIRMATION_REQUIRED", "A current confirmation bound to the displayed request is required.");
                }

                var expectedDigest = ActionPolicy.ComputePromptDigest(attention);
                if (!ActionPolicy.FixedTimeDigestEquals(expectedDigest, request.Confirmation.PromptDigest))
                {
                    throw new BridgeActionRejectedException("PROMPT_MISMATCH", "The confirmed prompt does not match the current attention.");
                }
            }
        }

        if (request.Type == MessageKind.Reply
            && (string.IsNullOrWhiteSpace(request.Text)
                || request.Text.EnumerateRunes().Count() > ProtocolV1.MaxReplyCharacters))
        {
            throw new BridgeActionRejectedException("INVALID_REPLY", "Reply text must contain 1 to 512 Unicode characters.");
        }

        if (request.Note is not null && request.Note.EnumerateRunes().Count() > 256)
        {
            throw new BridgeActionRejectedException("INVALID_NOTE", "Action note must contain at most 256 Unicode characters.");
        }
    }

    private ulong? CheckDuplicate(Guid messageId, string identifier, MessageKind type, string fingerprint)
    {
        var byMessage = _state.MessageFingerprints.LastOrDefault(item => item.MessageId == messageId);
        var byIdentifier = _state.IdentifierFingerprints.LastOrDefault(item => item.Identifier == identifier);
        foreach (var existing in new[] { byMessage, byIdentifier })
        {
            if (existing is null)
            {
                continue;
            }

            if (existing.Type != type || !CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(existing.Fingerprint), Convert.FromHexString(fingerprint)))
            {
                throw new BridgeStateConflictException("Identifier was already used for a different protocol message.");
            }
        }

        return byMessage?.ServerSequence ?? byIdentifier?.ServerSequence;
    }

    private void RecordFingerprint(Guid messageId, string identifier, MessageKind type, string fingerprint, ulong serverSequence)
    {
        _state.MessageFingerprints.Add(new PersistedFingerprint(messageId, null, type, fingerprint, serverSequence));
        _state.IdentifierFingerprints.Add(new PersistedFingerprint(null, identifier, type, fingerprint, serverSequence));
    }

    private static string CreateFingerprint<TPayload>(MessageKind type, TPayload payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { Type = type, Payload = payload }, JsonOptions);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    }

    private void AcquireWriterLock()
    {
        if (_writerLock is not null)
        {
            return;
        }

        var directory = Path.GetDirectoryName(_persistencePath)
            ?? throw new InvalidOperationException("Persistence path must have a parent directory.");
        Directory.CreateDirectory(directory);
        try
        {
            _writerLock = new FileStream(_persistencePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException("Another bridge instance owns the configured persistence path.", exception);
        }
    }

    public void Dispose()
    {
        _writerLock?.Dispose();
        _writerLock = null;
        _gate.Dispose();
    }

    private static void TrimOldest<T>(List<T> items, int limit)
    {
        if (items.Count > limit)
        {
            items.RemoveRange(0, items.Count - limit);
        }
    }

    private static string MapSessionState(string eventKind, string? currentState) => eventKind switch
    {
        "started" or "progress" => "running",
        "completed" => "completed",
        "failed" => "failed",
        "message" => currentState ?? "idle",
        _ => throw new ArgumentOutOfRangeException(nameof(eventKind), eventKind, "Unsupported event kind."),
    };

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }

    private sealed class PersistedState
    {
        public Dictionary<string, SessionPayload> Sessions { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, AttentionPayload> Attentions { get; init; } = new(StringComparer.Ordinal);
        public List<BridgeHistoryItem> History { get; init; } = [];
        public List<Guid> ProcessedMessageIds { get; init; } = [];
        public List<string> ProcessedEventIds { get; init; } = [];
        public List<string> ProcessedAttentionIds { get; init; } = [];
        public List<PersistedFingerprint> MessageFingerprints { get; init; } = [];
        public List<PersistedFingerprint> IdentifierFingerprints { get; init; } = [];
        public Dictionary<string, Guid> AttentionMessageIds { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<Guid, ActionOutcome> ActionOutcomes { get; init; } = [];
        public List<Guid> ActionOutcomeOrder { get; init; } = [];
        public Dictionary<string, ulong> LastInboundSequences { get; init; } = new(StringComparer.Ordinal);
        public List<string> InboundConnectionOrder { get; init; } = [];
        public ulong LastServerSequence { get; set; }
    }

    private sealed record PersistedFingerprint(
        Guid? MessageId,
        string? Identifier,
        MessageKind Type,
        string Fingerprint,
        ulong ServerSequence);
}

public sealed record BridgeHistoryItem(
    ulong ServerSequence,
    MessageKind Type,
    Guid SourceMessageId,
    DateTimeOffset SentAt,
    JsonElement Payload);

public sealed record BridgeSnapshot(
    IReadOnlyList<SessionPayload> Sessions,
    IReadOnlyList<AttentionPayload> Attentions,
    IReadOnlyList<BridgeHistoryItem> History,
    ulong LastServerSequence);

public sealed record IngestResult(bool Duplicate, BridgeSnapshot Snapshot, ulong RecordedServerSequence);

public sealed record ProviderIngestResult(
    bool EventDuplicate,
    bool AttentionDuplicate,
    BridgeSnapshot Snapshot,
    IReadOnlyList<BridgeHistoryItem> CommittedItems);

public sealed record DeviceActionRequest(
    Guid MessageId,
    MessageKind Type,
    DateTimeOffset SentAt,
    Guid ActionId,
    string AttentionId,
    ulong ExpectedRevision,
    string DeviceId,
    bool Destructive,
    Confirmation? Confirmation,
    string? Reason,
    string? Note,
    string? Text)
{
    internal object FingerprintPayload => new
    {
        ActionId,
        AttentionId,
        ExpectedRevision,
        Destructive,
        Confirmation,
        Reason,
        Note,
        Text,
    };
}

public sealed record ActionOutcome(
    Guid ActionId,
    string AttentionId,
    string DeviceId,
    string Provider,
    string Action,
    string Status,
    DateTimeOffset RecordedAt,
    ulong ServerSequence);

public sealed record ActionProcessResult(
    bool Duplicate,
    ActionOutcome Outcome,
    BridgeSnapshot Snapshot,
    BridgeHistoryItem CommittedItem,
    DeviceActionRequest Request);

public static class ActionPolicy
{
    public static string GetAction(DeviceActionRequest request) => request.Type switch
    {
        MessageKind.Approval => "approve",
        MessageKind.Reply => "reply",
        MessageKind.Denial when request.Reason == "user_denied" => "deny",
        MessageKind.Denial when request.Reason == "user_cancelled" => "cancel",
        MessageKind.Denial when request.Reason == "acknowledged" => "acknowledge",
        MessageKind.Denial => throw new BridgeActionRejectedException("INVALID_DENIAL_REASON", "The device supplied an unsupported denial reason."),
        _ => throw new BridgeActionRejectedException("INVALID_ACTION", "The message kind is not a device action."),
    };

    public static string ComputePromptDigest(AttentionPayload attention)
    {
        var canonical = $"{attention.Title}\n{attention.Body}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public static bool FixedTimeDigestEquals(string expected, string supplied)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected),
                Convert.FromHexString(supplied));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed class BridgeActionRejectedException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

public sealed class BridgeStateConflictException(string message) : InvalidOperationException(message);
