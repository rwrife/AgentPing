using System.Text.Json;

namespace AgentPing.Bridge.Providers;

public sealed class ManualProviderAdapter : IProviderAdapter
{
    public string Name => "manual";
    public string DisplayName => "Manual/test events";
    public string Integration => "stable AgentPing manual fixture contract";

    public ProviderMappedMessage Map(JsonElement source)
    {
        var eventId = ProviderPayload.SourceIdentifier(source, "manual-event", "eventId");
        var sessionId = ProviderPayload.SourceIdentifier(source, "manual-session", "sessionId");
        var kind = ProviderPayload.OptionalString(source, "kind") ?? "message";
        var summary = ProviderPayload.Text(ProviderPayload.OptionalString(source, "summary"), 512, "Manual AgentPing event");
        var detail = ProviderPayload.OptionalString(source, "detail") is { } rawDetail
            ? ProviderPayload.Text(rawDetail, 2048, string.Empty)
            : null;
        var severity = ProviderPayload.OptionalString(source, "severity") ?? "info";
        var mappedEvent = new ProviderMappedEvent(eventId, sessionId, kind, summary, detail, severity);

        if (!source.TryGetProperty("attention", out var attention) || attention.ValueKind != JsonValueKind.Object)
        {
            return new ProviderMappedMessage(mappedEvent);
        }

        var attentionId = ProviderPayload.SourceIdentifier(attention, "manual-attention", "attentionId");
        var category = ProviderPayload.OptionalString(attention, "category") ?? "notification";
        var title = ProviderPayload.Text(ProviderPayload.OptionalString(attention, "title"), 120, "AgentPing attention");
        var body = ProviderPayload.Text(ProviderPayload.OptionalString(attention, "body"), 1024, summary);
        var destructive = attention.TryGetProperty("destructive", out var destructiveElement)
            && destructiveElement.ValueKind == JsonValueKind.True;
        var actions = ReadActions(attention);
        return new ProviderMappedMessage(
            mappedEvent,
            new ProviderMappedAttention(attentionId, category, title, body, destructive, actions));
    }

    private static IReadOnlyList<string> ReadActions(JsonElement attention)
    {
        if (!attention.TryGetProperty("allowedActions", out var actions) || actions.ValueKind != JsonValueKind.Array)
        {
            return ["acknowledge"];
        }

        return actions.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .Take(3)
            .ToArray();
    }
}

public sealed class CodexCliProviderAdapter : IProviderAdapter
{
    public string Name => "codex";
    public string DisplayName => "OpenAI Codex CLI";
    public string Integration => "Codex CLI notify agent-turn-complete JSON";

    public ProviderMappedMessage Map(JsonElement source)
    {
        var hook = ProviderPayload.OptionalString(source, "hook_event_name", "hookEventName");
        if (hook == "PermissionRequest")
        {
            var sessionId = ProviderPayload.RequiredString(source, "session_id", "sessionId");
            var requestTurnId = ProviderPayload.RequiredString(source, "turn_id", "turnId");
            var toolName = ProviderPayload.RequiredString(source, "tool_name", "toolName");
            var requestId = ProviderPayload.SourceIdentifier(
                source, "codex-permission", "tool_use_id", "toolUseId");
            string? description = null;
            if (source.TryGetProperty("tool_input", out var toolInput)
                && toolInput.ValueKind == JsonValueKind.Object)
            {
                description = ProviderPayload.OptionalString(toolInput, "description");
            }
            else if (source.TryGetProperty("toolInput", out toolInput)
                && toolInput.ValueKind == JsonValueKind.Object)
            {
                description = ProviderPayload.OptionalString(toolInput, "description");
            }

            var actionSummary = ProviderPayload.Text(description, 512, $"Codex requested permission to use {toolName}");
            return new ProviderMappedMessage(
                new ProviderMappedEvent(
                    ProviderPayload.Identifier(requestId, "codex-event"),
                    ProviderPayload.Identifier(sessionId, "codex-session"),
                    "message",
                    actionSummary,
                    null,
                    "warning",
                    new Dictionary<string, string>
                    {
                        ["tool_name"] = ProviderPayload.Text(toolName, 256, "tool"),
                        ["turn_id"] = ProviderPayload.Text(requestTurnId, 256, "turn"),
                    }),
                new ProviderMappedAttention(
                    ProviderPayload.Identifier(requestId, "codex-attention"),
                    "approval",
                    "Codex permission request",
                    ProviderPayload.Text(description, 1024, $"Codex requested permission to use {toolName}. Provider execution remains on the PC."),
                    true,
                    ["approve", "deny"]));
        }

        var type = ProviderPayload.RequiredString(source, "type");
        if (!string.Equals(type, "agent-turn-complete", StringComparison.Ordinal))
        {
            throw new ProviderPayloadException($"Unsupported Codex notify type '{type}'.");
        }

        var threadId = ProviderPayload.RequiredString(source, "thread-id", "thread_id");
        var turnId = ProviderPayload.RequiredString(source, "turn-id", "turn_id");
        var summary = ProviderPayload.Text(
            ProviderPayload.OptionalString(source, "last-assistant-message", "last_assistant_message"),
            512,
            "Codex turn completed");
        var cwd = ProviderPayload.OptionalString(source, "cwd");
        return new ProviderMappedMessage(new ProviderMappedEvent(
            ProviderPayload.Identifier(turnId, "codex-event"),
            ProviderPayload.Identifier(threadId, "codex-session"),
            "completed",
            summary,
            null,
            "success",
            cwd is null ? null : new Dictionary<string, string> { ["cwd_name"] = ProviderPayload.Text(Path.GetFileName(cwd), 256, "workspace") }));
    }
}

public sealed class ClaudeCodeProviderAdapter : IProviderAdapter
{
    public string Name => "claude_code";
    public string DisplayName => "Anthropic Claude Code";
    public string Integration => "Claude Code command-hook stdin JSON";

    public ProviderMappedMessage Map(JsonElement source)
    {
        var hook = ProviderPayload.RequiredString(source, "hook_event_name");
        var sessionId = ProviderPayload.RequiredString(source, "session_id");
        var sourceId = ProviderPayload.OptionalString(source, "tool_use_id", "notification_type")
            ?? ProviderPayload.Hash(source.GetRawText())[..24];
        var toolName = ProviderPayload.OptionalString(source, "tool_name");
        var message = ProviderPayload.OptionalString(source, "message", "reason");
        var kind = hook switch
        {
            "SessionStart" => "started",
            "Stop" or "SubagentStop" or "SessionEnd" => "completed",
            "PostToolUseFailure" => "failed",
            "UserPromptSubmit" or "PreToolUse" or "PostToolUse" or "PreCompact" => "progress",
            "PermissionRequest" or "Notification" => "message",
            _ => throw new ProviderPayloadException($"Unsupported Claude Code hook '{hook}'."),
        };
        var severity = kind switch
        {
            "failed" => "error",
            "completed" => "success",
            _ => "info",
        };
        var summary = ProviderPayload.Text(message, 512, toolName is null ? $"Claude Code {hook}" : $"Claude Code {hook}: {toolName}");
        var mappedEvent = new ProviderMappedEvent(
            ProviderPayload.Identifier(sourceId, "claude-event"),
            ProviderPayload.Identifier(sessionId, "claude-session"),
            kind,
            summary,
            null,
            severity,
            toolName is null ? null : new Dictionary<string, string> { ["tool_name"] = ProviderPayload.Text(toolName, 256, "tool") });

        if (hook != "PermissionRequest")
        {
            return new ProviderMappedMessage(mappedEvent);
        }

        var body = ProviderPayload.Text(message, 1024, toolName is null
            ? "Claude Code requested permission. Provider execution remains on the PC."
            : $"Claude Code requested permission to use {toolName}. Provider execution remains on the PC.");
        return new ProviderMappedMessage(
            mappedEvent,
            new ProviderMappedAttention(
                ProviderPayload.Identifier(sourceId, "claude-attention"),
                "approval",
                "Claude Code permission request",
                body,
                true,
                ["approve", "deny"]));
    }
}

public sealed class CopilotCliProviderAdapter : IProviderAdapter
{
    public string Name => "copilot_cli";
    public string DisplayName => "GitHub Copilot CLI";
    public string Integration => "Copilot CLI repository/personal hook stdin JSON";

    public ProviderMappedMessage Map(JsonElement source)
    {
        var hook = ProviderPayload.RequiredString(source, "hookEventName", "hook_event_name");
        var sessionId = ProviderPayload.RequiredString(source, "sessionId", "session_id");
        var toolName = ProviderPayload.OptionalString(source, "toolName", "tool_name");
        var sourceId = ProviderPayload.OptionalString(source, "toolCallId", "tool_call_id", "timestamp")
            ?? ProviderPayload.Hash(source.GetRawText())[..24];
        var kind = hook switch
        {
            "sessionStart" => "started",
            "sessionEnd" => "completed",
            "errorOccurred" => "failed",
            "userPromptSubmitted" or "preToolUse" or "postToolUse" => "progress",
            _ => throw new ProviderPayloadException($"Unsupported Copilot CLI hook '{hook}'."),
        };
        var rawSummary = ProviderPayload.OptionalString(source, "message", "errorMessage");
        var summary = ProviderPayload.Text(rawSummary, 512, toolName is null ? $"Copilot CLI {hook}" : $"Copilot CLI {hook}: {toolName}");
        var mappedEvent = new ProviderMappedEvent(
            ProviderPayload.Identifier(sourceId, "copilot-event"),
            ProviderPayload.Identifier(sessionId, "copilot-session"),
            kind,
            summary,
            null,
            kind == "failed" ? "error" : kind == "completed" ? "success" : "info",
            toolName is null ? null : new Dictionary<string, string> { ["tool_name"] = ProviderPayload.Text(toolName, 256, "tool") });

        if (hook != "preToolUse")
        {
            return new ProviderMappedMessage(mappedEvent);
        }

        return new ProviderMappedMessage(
            mappedEvent,
            new ProviderMappedAttention(
                ProviderPayload.Identifier(sourceId, "copilot-attention"),
                "approval",
                "Copilot CLI tool request",
                ProviderPayload.Text(rawSummary, 1024, toolName is null
                    ? "Copilot CLI is preparing to use a tool. Provider execution remains on the PC."
                    : $"Copilot CLI is preparing to use {toolName}. Provider execution remains on the PC."),
                true,
                ["approve", "deny"]));
    }
}
