using System.Collections.Concurrent;
using AgentPing.Bridge.Core;

namespace AgentPing.Bridge.Providers;

public sealed class ProviderActionBroker(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ProviderActionResponse>> _waiters =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ProviderActionResponse> _completed =
        new(StringComparer.Ordinal);
    private readonly Queue<string> _completedOrder = new();
    private readonly object _completedGate = new();

    public async Task<ProviderActionResponse> WaitAsync(
        string attentionId,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attentionId);
        if (_completed.TryRemove(attentionId, out var completed))
        {
            return completed;
        }

        var remaining = deadline - timeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
        {
            return ProviderActionResponse.Expired(attentionId);
        }

        var waiter = _waiters.GetOrAdd(
            attentionId,
            static _ => new TaskCompletionSource<ProviderActionResponse>(TaskCreationOptions.RunContinuationsAsynchronously));
        if (_completed.TryRemove(attentionId, out completed))
        {
            waiter.TrySetResult(completed);
        }

        try
        {
            return await waiter.Task.WaitAsync(remaining, timeProvider, cancellationToken);
        }
        catch (TimeoutException)
        {
            return ProviderActionResponse.Expired(attentionId);
        }
        finally
        {
            _waiters.TryRemove(new KeyValuePair<string, TaskCompletionSource<ProviderActionResponse>>(attentionId, waiter));
        }
    }

    public void Complete(ActionProcessResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var response = new ProviderActionResponse(
            result.Outcome.AttentionId,
            result.Outcome.Action,
            result.Outcome.Status,
            result.Request.Type == Protocol.MessageKind.Reply ? result.Request.Text : null);
        if (_waiters.TryRemove(result.Outcome.AttentionId, out var waiter))
        {
            waiter.TrySetResult(response);
            return;
        }

        _completed[result.Outcome.AttentionId] = response;
        lock (_completedGate)
        {
            _completedOrder.Enqueue(result.Outcome.AttentionId);
            while (_completedOrder.Count > Protocol.ProtocolV1.MaxReplayWindowMessages)
            {
                _completed.TryRemove(_completedOrder.Dequeue(), out _);
            }
        }
    }
}

public sealed record ProviderActionResponse(
    string AttentionId,
    string Action,
    string Status,
    string? Text)
{
    public static ProviderActionResponse Expired(string attentionId) =>
        new(attentionId, "deny", "expired", null);
}
