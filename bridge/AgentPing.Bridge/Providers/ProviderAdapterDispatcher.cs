using System.Text.Json;
using AgentPing.Bridge.Core;
using AgentPing.Bridge.Protocol;
using AgentPing.Bridge.Transport;
using Microsoft.Extensions.Options;

namespace AgentPing.Bridge.Providers;

public sealed class ProviderAdapterDispatcher
{
    internal const int ProviderActionTimeoutSeconds = 15;

    private readonly IReadOnlyDictionary<string, IProviderAdapter> _adapters;
    private readonly ProviderAdapterOptions _options;
    private readonly BridgeStateStore _stateStore;
    private readonly DeviceConnectionHub _connectionHub;
    private readonly ProviderActionBroker _actionBroker;
    private readonly TimeProvider _timeProvider;

    public ProviderAdapterDispatcher(
        IEnumerable<IProviderAdapter> adapters,
        IOptions<ProviderAdapterOptions> options,
        BridgeStateStore stateStore,
        DeviceConnectionHub connectionHub,
        ProviderActionBroker actionBroker,
        TimeProvider timeProvider)
    {
        _adapters = adapters.ToDictionary(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase);
        _options = options.Value;
        _stateStore = stateStore;
        _connectionHub = connectionHub;
        _actionBroker = actionBroker;
        _timeProvider = timeProvider;
    }

    public IReadOnlyList<ProviderAdapterStatus> GetStatuses() => _adapters.Values
        .OrderBy(adapter => adapter.Name, StringComparer.Ordinal)
        .Select(adapter => new ProviderAdapterStatus(
            adapter.Name,
            adapter.DisplayName,
            IsEnabled(adapter.Name),
            adapter.Integration,
            adapter.Name is "codex" ? "completion_events" : "lifecycle_and_attention_events"))
        .ToArray();

    public async Task<ProviderDispatchResult> DispatchAsync(
        string provider,
        JsonElement source,
        CancellationToken cancellationToken = default)
    {
        if (!_adapters.TryGetValue(provider, out var adapter))
        {
            throw new ProviderAdapterNotFoundException(provider);
        }

        if (!IsEnabled(adapter.Name))
        {
            throw new ProviderAdapterDisabledException(adapter.Name);
        }

        var mapped = adapter.Map(source);
        var now = _timeProvider.GetUtcNow();
        var connectionId = $"adapter-{adapter.Name}-{Guid.NewGuid():N}";
        var eventEnvelope = new ProtocolEnvelope<EventPayload>
        {
            ProtocolVersion = ProtocolV1.Version,
            MessageId = Guid.NewGuid(),
            Type = MessageKind.Event,
            SentAt = now,
            ConnectionId = connectionId,
            Sequence = 1,
            Payload = new EventPayload
            {
                EventId = mapped.Event.EventId,
                SessionId = mapped.Event.SessionId,
                Provider = adapter.Name,
                EventKind = mapped.Event.Kind,
                Summary = mapped.Event.Summary,
                Detail = mapped.Event.Detail,
                Severity = mapped.Event.Severity,
                Metadata = mapped.Event.Metadata,
            },
        };
        EnsureValid(ProtocolValidation.ValidateEvent(eventEnvelope, _timeProvider));
        Func<SessionPayload, ProtocolEnvelope<AttentionPayload>>? createAttention = null;
        if (mapped.Attention is { } mappedAttention)
        {
            createAttention = session =>
            {
                var attentionEnvelope = new ProtocolEnvelope<AttentionPayload>
                {
                    ProtocolVersion = ProtocolV1.Version,
                    MessageId = Guid.NewGuid(),
                    Type = MessageKind.Attention,
                    SentAt = now,
                    ConnectionId = connectionId,
                    Sequence = 2,
                    Payload = new AttentionPayload
                    {
                        AttentionId = mappedAttention.AttentionId,
                        SessionId = mapped.Event.SessionId,
                        Revision = checked(session.Revision + 1),
                        Category = mappedAttention.Category,
                        Title = mappedAttention.Title,
                        Body = mappedAttention.Body,
                        ResponseDeadlineAt = now.AddSeconds(ProviderActionTimeoutSeconds),
                        Destructive = mappedAttention.Destructive,
                        AllowedActions = mappedAttention.AllowedActions,
                    },
                };
                EnsureValid(ProtocolValidation.ValidateAttention(attentionEnvelope, _timeProvider));
                return attentionEnvelope;
            };
        }

        var result = await _stateStore.IngestProviderBatchAsync(
            eventEnvelope,
            createAttention,
            cancellationToken);
        foreach (var item in result.CommittedItems)
        {
            _connectionHub.Publish(item);
        }

        return new ProviderDispatchResult(
            adapter.Name,
            result.EventDuplicate,
            result.AttentionDuplicate,
            result.Snapshot.LastServerSequence,
            mapped.Attention?.AttentionId,
            mapped.Attention is null ? null : now.AddSeconds(ProviderActionTimeoutSeconds));
    }

    public Task<ProviderActionResponse> WaitForActionAsync(
        ProviderDispatchResult dispatch,
        CancellationToken cancellationToken = default)
    {
        if (dispatch.AttentionId is null || dispatch.ResponseDeadlineAt is null)
        {
            throw new ProviderActionNotAvailableException();
        }

        return _actionBroker.WaitAsync(dispatch.AttentionId, dispatch.ResponseDeadlineAt.Value, cancellationToken);
    }

    private bool IsEnabled(string name) => name switch
    {
        "manual" => _options.Manual.Enabled,
        "codex" => _options.Codex.Enabled,
        "claude_code" => _options.ClaudeCode.Enabled,
        "copilot_cli" => _options.CopilotCli.Enabled,
        _ => false,
    };

    private static void EnsureValid(IReadOnlyDictionary<string, string[]> errors)
    {
        if (errors.Count > 0)
        {
            throw new ProviderPayloadException("Provider payload could not be normalized into a valid protocol-v1 message.");
        }
    }
}

public sealed record ProviderAdapterStatus(
    string Name,
    string DisplayName,
    bool Enabled,
    string Integration,
    string Capabilities);

public sealed record ProviderDispatchResult(
    string Provider,
    bool EventDuplicate,
    bool AttentionDuplicate,
    ulong LastServerSequence,
    string? AttentionId,
    DateTimeOffset? ResponseDeadlineAt);

public sealed class ProviderActionNotAvailableException()
    : InvalidOperationException("The provider event did not create an actionable attention item.");

public sealed class ProviderAdapterNotFoundException(string provider)
    : InvalidOperationException($"Provider adapter '{provider}' is not supported.");

public sealed class ProviderAdapterDisabledException(string provider)
    : InvalidOperationException($"Provider adapter '{provider}' is disabled.");
