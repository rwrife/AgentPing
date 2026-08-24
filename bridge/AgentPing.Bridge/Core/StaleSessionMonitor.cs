using AgentPing.Bridge.Transport;

namespace AgentPing.Bridge.Core;

public sealed class StaleSessionMonitor(
    BridgeStateStore stateStore,
    DeviceConnectionHub connectionHub,
    TimeSpan interval) : BackgroundService
{
    private readonly TimeSpan _interval = interval > TimeSpan.Zero
        ? interval
        : throw new ArgumentOutOfRangeException(nameof(interval));

    public async Task<bool> SweepOnceAsync(CancellationToken cancellationToken = default)
    {
        var committedItems = await stateStore.MarkStaleSessionsAsync(cancellationToken);
        foreach (var item in committedItems)
        {
            connectionHub.Publish(item);
        }

        return committedItems.Count > 0;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await SweepOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal hosted-service shutdown.
        }
    }
}
