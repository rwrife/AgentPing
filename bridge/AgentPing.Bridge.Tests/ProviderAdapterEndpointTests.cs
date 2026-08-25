using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentPing.Bridge.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentPing.Bridge.Tests;

public sealed class ProviderAdapterEndpointTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 19, 0, 0, TimeSpan.Zero);
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ProviderAdapterEndpointTests()
    {
        var stateDirectory = Path.Combine(Path.GetTempPath(), $"agentping-adapters-{Guid.NewGuid():N}");
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Bridge:PersistencePath", Path.Combine(stateDirectory, "state.json"));
            builder.UseSetting("Bridge:DeviceTokensPath", Path.Combine(stateDirectory, "tokens.json"));
            builder.UseSetting("Adapters:Manual:Enabled", "true");
            builder.UseSetting("Adapters:Codex:Enabled", "true");
            builder.UseSetting("Adapters:ClaudeCode:Enabled", "true");
            builder.UseSetting("Adapters:CopilotCli:Enabled", "true");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
            });
        });
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Status_reports_each_adapter_and_explicit_enablement()
    {
        using var status = JsonDocument.Parse(await _client.GetStringAsync("/api/status"));

        var adapters = status.RootElement.GetProperty("adapters").EnumerateArray().ToArray();
        Assert.Equal(4, adapters.Length);
        Assert.All(adapters, adapter => Assert.True(adapter.GetProperty("enabled").GetBoolean()));
        Assert.Contains(adapters, adapter => adapter.GetProperty("name").GetString() == "codex"
            && adapter.GetProperty("capabilities").GetString() == "completion_events");
    }

    [Fact]
    public async Task Codex_notify_ingests_completion_and_replay_is_idempotent()
    {
        const string json = """
            {
              "type":"agent-turn-complete",
              "thread-id":"thread-endpoint-1",
              "turn-id":"turn-endpoint-1",
              "cwd":"/workspace/agentping",
              "last-assistant-message":"Finished safely"
            }
            """;
        using var source = JsonDocument.Parse(json);

        using var first = await _client.PostAsJsonAsync("/api/adapters/codex", source.RootElement);
        using var second = await _client.PostAsJsonAsync("/api/adapters/codex", source.RootElement);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        var duplicate = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(duplicate.GetProperty("eventDuplicate").GetBoolean());
        using var status = JsonDocument.Parse(await _client.GetStringAsync("/api/status"));
        Assert.Equal(1, status.RootElement.GetProperty("sessionCount").GetInt32());
        Assert.Equal<ulong>(1, status.RootElement.GetProperty("lastServerSequence").GetUInt64());
    }

    [Fact]
    public async Task Claude_permission_request_creates_fail_closed_attention_and_replay_is_idempotent()
    {
        var payload = new
        {
            session_id = "claude-endpoint-session",
            hook_event_name = "PermissionRequest",
            tool_name = "Bash",
            tool_use_id = "claude-endpoint-tool",
            message = "Run the build",
        };

        using var first = await _client.PostAsJsonAsync("/api/adapters/claude_code", payload);
        using var second = await _client.PostAsJsonAsync("/api/adapters/claude_code", payload);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        var duplicate = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(duplicate.GetProperty("eventDuplicate").GetBoolean());
        Assert.True(duplicate.GetProperty("attentionDuplicate").GetBoolean());
        using var status = JsonDocument.Parse(await _client.GetStringAsync("/api/status"));
        Assert.Equal(1, status.RootElement.GetProperty("sessionCount").GetInt32());
        Assert.Equal(1, status.RootElement.GetProperty("attentionCount").GetInt32());
        Assert.Equal<ulong>(2, status.RootElement.GetProperty("lastServerSequence").GetUInt64());
    }

    [Fact]
    public async Task Disabled_unknown_and_malformed_integrations_fail_without_echoing_payloads()
    {
        using var unknown = await _client.PostAsJsonAsync("/api/adapters/not-real", new { secret = "do-not-echo" });
        using var malformed = await _client.PostAsJsonAsync("/api/adapters/codex", new { type = "wrong", secret = "do-not-echo" });

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, malformed.StatusCode);
        Assert.DoesNotContain("do-not-echo", await unknown.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain("do-not-echo", await malformed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Attention_identifier_reuse_with_changed_content_fails_closed()
    {
        var first = new
        {
            eventId = "manual-conflict-event",
            sessionId = "manual-conflict-session",
            kind = "message",
            summary = "Manual approval",
            attention = new
            {
                attentionId = "manual-conflict-attention",
                category = "approval",
                title = "Approve?",
                body = "Original bounded request",
                destructive = true,
                allowedActions = new[] { "approve", "deny" },
            },
        };
        var changed = new
        {
            eventId = "manual-conflict-event-2",
            first.sessionId,
            first.kind,
            summary = "Second event must roll back",
            attention = new
            {
                first.attention.attentionId,
                first.attention.category,
                first.attention.title,
                body = "Changed bounded request",
                first.attention.destructive,
                first.attention.allowedActions,
            },
        };

        Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsJsonAsync("/api/adapters/manual", first)).StatusCode);
        using var response = await _client.PostAsJsonAsync("/api/adapters/manual", changed);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var status = JsonDocument.Parse(await _client.GetStringAsync("/api/status"));
        Assert.Equal<ulong>(2, status.RootElement.GetProperty("lastServerSequence").GetUInt64());
        var snapshot = await _factory.Services.GetRequiredService<BridgeStateStore>().GetSnapshotAsync();
        Assert.Equal("Manual approval", Assert.Single(snapshot.Sessions).DisplayName);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
