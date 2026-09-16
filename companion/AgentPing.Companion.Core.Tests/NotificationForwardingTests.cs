using AgentPing.Companion.Core;

namespace AgentPing.Companion.Core.Tests;

public sealed class NotificationForwardingTests
{
    [Fact]
    public void Baselines_history_filters_apps_and_deduplicates()
    {
        var policy = new NotificationForwardingPolicy();
        var allowed = new HashSet<string> { "teams" };
        var old = new DesktopNotice("1", "teams", "Teams", "Old");
        var fresh = old with { Key = "2", Text = "New" };
        var other = old with { Key = "3", AppId = "mail" };
        Assert.Empty(policy.TakeNew([old], allowed));
        Assert.Equal([fresh], policy.TakeNew([old, fresh, fresh, other], allowed));
        allowed.Add("mail");
        Assert.Empty(policy.TakeNew([old, fresh, other], allowed));
        policy.Reset();
        Assert.Empty(policy.TakeNew([old, fresh, other], allowed));
    }

    [Fact]
    public void Text_is_bounded_single_line_ascii()
    {
        Assert.Equal("Teams: Jose says \"hello\"...", NotificationForwardingPolicy.RobotText("Teams", "José\nsays “hello”… 🤖"));
        var text = NotificationForwardingPolicy.RobotText("App", new string('x', 300));
        Assert.Equal(192, text.Length);
        Assert.EndsWith("...", text);
        Assert.All(text, c => Assert.InRange((int)c, 32, 126));
    }
}
