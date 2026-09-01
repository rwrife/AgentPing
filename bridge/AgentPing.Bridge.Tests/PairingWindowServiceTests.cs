using AgentPing.Bridge.Security;

namespace AgentPing.Bridge.Tests;

public sealed class PairingWindowServiceTests
{
    [Fact]
    public async Task Lifetime_is_capped_at_five_minutes_and_expired_secret_is_rejected()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
        var pairing = new PairingWindowService(clock);
        var opened = await pairing.OpenAsync(TimeSpan.FromHours(1));
        Assert.Equal(clock.GetUtcNow().AddMinutes(5), opened.ExpiresUtc);
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.False((await pairing.GetStatusAsync()).Open);
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan amount) => now += amount;
    }
}
