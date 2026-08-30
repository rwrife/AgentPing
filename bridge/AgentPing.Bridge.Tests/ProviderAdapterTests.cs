using System.Text.Json;
using AgentPing.Bridge.Providers;

namespace AgentPing.Bridge.Tests;

public sealed class ProviderAdapterTests
{
    public static TheoryData<IProviderAdapter, string, string> Fixtures => new()
    {
        { new CodexCliProviderAdapter(), "codex-turn-complete.json", "completed" },
        { new ClaudeCodeProviderAdapter(), "claude-session-start.json", "started" },
        { new ClaudeCodeProviderAdapter(), "claude-permission-request.json", "message" },
        { new ClaudeCodeProviderAdapter(), "claude-failure.json", "failed" },
        { new ClaudeCodeProviderAdapter(), "claude-reply-handoff.json", "progress" },
        { new CopilotCliProviderAdapter(), "copilot-session-start.json", "started" },
        { new CopilotCliProviderAdapter(), "copilot-progress.json", "progress" },
        { new CopilotCliProviderAdapter(), "copilot-failure.json", "failed" },
        { new CopilotCliProviderAdapter(), "copilot-completion.json", "completed" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task Recorded_and_synthetic_provider_fixtures_map_to_bounded_events(
        IProviderAdapter adapter,
        string fixtureName,
        string expectedKind)
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "provider-fixtures", fixtureName));
        using var document = await JsonDocument.ParseAsync(stream);

        var mapped = adapter.Map(document.RootElement);

        Assert.Equal(expectedKind, mapped.Event.Kind);
        Assert.InRange(mapped.Event.Summary.Length, 1, 512);
        Assert.InRange(mapped.Event.SessionId.Length, 1, 128);
        Assert.InRange(mapped.Event.EventId.Length, 1, 128);
    }

    [Fact]
    public void Claude_permission_request_is_fail_closed_and_does_not_forward_transcript_path()
    {
        using var source = JsonDocument.Parse("""
            {
              "session_id":"claude-session",
              "transcript_path":"/private/transcript.jsonl",
              "hook_event_name":"PermissionRequest",
              "tool_name":"Bash",
              "tool_use_id":"tool-1",
              "message":"Run command with token=top-secret-value"
            }
            """);

        var mapped = new ClaudeCodeProviderAdapter().Map(source.RootElement);

        Assert.NotNull(mapped.Attention);
        Assert.True(mapped.Attention.Destructive);
        Assert.Equal(new[] { "approve", "deny" }, mapped.Attention.AllowedActions);
        Assert.Contains("token=[REDACTED]", mapped.Event.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("top-secret-value", mapped.Event.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("transcript", JsonSerializer.Serialize(mapped), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Codex_rejects_unsupported_notify_events_cleanly()
    {
        using var source = JsonDocument.Parse("""{"type":"approval-requested","thread-id":"t","turn-id":"u"}""");

        var exception = Assert.Throws<ProviderPayloadException>(
            () => new CodexCliProviderAdapter().Map(source.RootElement));

        Assert.Contains("Unsupported Codex notify type", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Codex_permission_request_hook_maps_to_destructive_attention_without_tool_arguments()
    {
        using var source = JsonDocument.Parse("""
            {
              "hook_event_name":"PermissionRequest",
              "session_id":"codex-session",
              "turn_id":"codex-turn",
              "tool_name":"Bash",
              "tool_input":{"command":"rm -rf private","description":"Remove generated cache"}
            }
            """);

        var mapped = new CodexCliProviderAdapter().Map(source.RootElement);

        Assert.NotNull(mapped.Attention);
        Assert.True(mapped.Attention.Destructive);
        Assert.Equal(new[] { "approve", "deny" }, mapped.Attention.AllowedActions);
        var serialized = JsonSerializer.Serialize(mapped);
        Assert.DoesNotContain("rm -rf private", serialized, StringComparison.Ordinal);
        Assert.Contains("Remove generated cache", mapped.Attention.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Copilot_pre_tool_use_becomes_destructive_attention_without_forwarding_tool_arguments()
    {
        using var source = JsonDocument.Parse("""
            {
              "hookEventName":"preToolUse",
              "sessionId":"copilot-session",
              "toolName":"shell",
              "toolCallId":"call-1",
              "toolArgs":{"command":"curl -H 'Authorization: Bearer private-value'"}
            }
            """);

        var mapped = new CopilotCliProviderAdapter().Map(source.RootElement);

        Assert.NotNull(mapped.Attention);
        Assert.True(mapped.Attention.Destructive);
        Assert.DoesNotContain("private-value", JsonSerializer.Serialize(mapped), StringComparison.Ordinal);
    }

    [Fact]
    public void Display_text_redacts_complete_authorization_and_bearer_values()
    {
        using var source = JsonDocument.Parse("""
            {
              "session_id":"claude-redaction-session",
              "hook_event_name":"Notification",
              "notification_type":"redaction-test",
              "message":"Authorization: Bearer abcdefghijklmnop and Bearer standalone-secret-value"
            }
            """);

        var mapped = new ClaudeCodeProviderAdapter().Map(source.RootElement);

        Assert.DoesNotContain("abcdefghijklmnop", mapped.Event.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("standalone-secret-value", mapped.Event.Summary, StringComparison.Ordinal);
        Assert.Contains("authorization=[REDACTED]", mapped.Event.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Copilot_user_prompt_content_is_not_forwarded()
    {
        using var source = JsonDocument.Parse("""
            {
              "hookEventName":"userPromptSubmitted",
              "sessionId":"copilot-private-prompt-session",
              "timestamp":"2026-08-25T19:00:00Z",
              "prompt":"private prompt content that must remain on the PC"
            }
            """);

        var mapped = new CopilotCliProviderAdapter().Map(source.RootElement);

        Assert.Equal("Copilot CLI userPromptSubmitted", mapped.Event.Summary);
        Assert.DoesNotContain("private prompt content", JsonSerializer.Serialize(mapped), StringComparison.Ordinal);
    }
}
