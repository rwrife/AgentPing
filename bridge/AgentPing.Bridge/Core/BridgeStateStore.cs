using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
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

public sealed class BridgeStateConflictException(string message) : InvalidOperationException(message);
