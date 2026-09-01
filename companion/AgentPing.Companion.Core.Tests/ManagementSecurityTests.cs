using System.Net;
using System.Text;
using AgentPing.Companion.Core;

namespace AgentPing.Companion.Core.Tests;

public sealed class ManagementSecurityTests
{
    [Theory]
    [InlineData("127.0.0.1", false, true, true)]
    [InlineData("192.168.1.20", true, true, true)]
    [InlineData("10.0.0.4", true, false, false)]
    [InlineData("8.8.8.8", true, true, false)]
    [InlineData("169.254.1.2", true, true, false)]
    public void Listener_policy_fails_closed(string address, bool lanEnabled, bool tls, bool expected) =>
        Assert.Equal(expected, ListenerPolicy.IsAllowed(IPAddress.Parse(address), lanEnabled, tls));

    [Fact]
    public async Task Logs_are_redacted_and_export_excludes_reply_and_pairing_text()
    {
        var log = new SafeLogBuffer(10);
        log.Add("paired token=secret pairing=material reply=do the dangerous thing", LogSensitivity.Secret);
        log.Add("Adapter healthy", LogSensitivity.Public);
        using var output = new MemoryStream();
        await log.ExportAsync(output);
        var text = Encoding.UTF8.GetString(output.ToArray());
        Assert.Contains("[REDACTED]", text);
        Assert.Contains("Adapter healthy", text);
        Assert.DoesNotContain("secret", text);
        Assert.DoesNotContain("dangerous", text);
    }

    [Fact]
    public async Task Startup_preference_is_opt_in_and_idempotent()
    {
        var startup = new StartupPreference(new MemoryStartupRegistration());
        Assert.False(await startup.IsEnabledAsync());
        await startup.SetEnabledAsync(true);
        await startup.SetEnabledAsync(true);
        Assert.True(await startup.IsEnabledAsync());
        await startup.SetEnabledAsync(false);
        Assert.False(await startup.IsEnabledAsync());

    }

    [Theory]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--background" }, true)]
    [InlineData(new[] { "--BACKGROUND" }, true)]
    [InlineData(new[] { "--other" }, false)]
    public void Startup_launch_mode_only_hides_the_window_for_the_explicit_background_switch(
        string[] arguments,
        bool expected) =>
        Assert.Equal(expected, StartupLaunchMode.IsBackground(arguments));
}
