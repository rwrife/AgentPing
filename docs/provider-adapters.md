# Provider adapters

Status: **local hook ingestion implemented and automated-test validated; provider action execution is not implemented**.

AgentPing adapters run on the PC and convert bounded provider hook notifications into protocol-v1 session and attention state. Provider credentials never belong in hook payloads or on the ESP32. All adapters are disabled by default, accept requests only through the bridge's loopback listener, and are independently enabled:

```text
Adapters__Manual__Enabled=true
Adapters__Codex__Enabled=true
Adapters__ClaudeCode__Enabled=true
Adapters__CopilotCli__Enabled=true
```

Restart the bridge after changing configuration. `GET /api/status` lists each adapter's enabled state, integration surface, and current capability level. Unknown adapters return 404, disabled adapters return 503, and unsupported source events return 422 without echoing the rejected payload.

## Security and privacy boundary

`tools/agentping_provider_relay.py` accepts one JSON object of at most 16,384 UTF-8 bytes and will send it only to a literal IPv4 or IPv6 loopback address over HTTP. It rejects hostnames, non-loopback addresses, URL credentials, HTTPS (the checked-in bridge is local HTTP only), paths, queries, and fragments. The bridge extracts only identifiers, lifecycle kind, bounded display text, and non-secret tool/workspace names.

The adapters deliberately do **not** forward provider API keys, authorization data, environment variables, full prompts, tool arguments, tool output, transcript paths, or raw rejected payloads. Display text is bounded and redacts credential assignments, bearer values, OpenAI/Anthropic key shapes, GitHub token shapes, and common secret fields. This is defense in depth, not permission to put secrets in prompts or hook messages.

Provider request/response execution remains issue #6 scope. `PermissionRequest` and `preToolUse` are represented as destructive, 30-second attention items for display/testing, but an AgentPing tap cannot yet approve a provider operation. Timeouts and unsupported source events fail closed.

## Supported-version policy

No live provider CLI binary or account was available in this implementation run, so this project does not invent a tested executable-version floor. Compatibility is explicitly tied to the public hook contract and fixtures below:

| Adapter | Supported contract/version | Live version tested | Drift behavior |
|---|---|---|---|
| Codex CLI | External `notify` contract with `type=agent-turn-complete`, documented 2026-08-25 | None | Other notify types return 422 |
| Claude Code | Command-hook JSON fields/events documented 2026-08-25 | None | Unknown hook events or missing required IDs return 422 |
| Copilot CLI | Hook configuration schema `version: 1` and the six documented CLI lifecycle events, documented 2026-08-25 | None | Unknown hook events or missing session IDs return 422 |
| Manual/test | AgentPing contract in this document and checked-in fixtures | Repository revision | Invalid protocol values return 422 |

This contract-based policy is deliberate: the upstream pages do not publish a reliable minimum CLI release for every hook field. A future release matrix may add exact binaries only after they are exercised in CI or a recorded live-provider compatibility run. The checked-in synthetic fixtures are regression evidence for payload shape, not a claim that every upstream release was tested.

## Relay usage

Run the bridge first, enable only the adapter(s) needed, and verify status:

```bash
curl http://127.0.0.1:8742/api/status
python3 tools/agentping_provider_relay.py --provider manual <<'JSON'
{"eventId":"demo-1","sessionId":"demo","kind":"started","summary":"Manual AgentPing event"}
JSON
```

A successful relay exits 0 and intentionally prints no payload or response body. A failed relay emits a payload-free diagnostic and exits 1.

## OpenAI Codex CLI

**Supported surface:** the documented Codex CLI `notify` command contract, currently `agent-turn-complete` only. The adapter uses `thread-id`, `turn-id`, optional `cwd`, and bounded `last-assistant-message`; it does not copy `input-messages`. Configure the absolute relay path in `~/.codex/config.toml`:

```toml
notify = ["python3", "/absolute/path/to/AgentPing/tools/agentping_provider_relay.py", "--provider", "codex"]
```

Codex appends one JSON argument to the command. AgentPing maps it to a completed session event. Codex approval notifications are **not** available through this external `notify` surface and are not claimed supported. Completion mapping is fixture-tested against the contract documented at <https://developers.openai.com/codex/config-advanced> (retrieved 2026-08-25).

## Anthropic Claude Code

**Supported hook events:** `SessionStart`, `UserPromptSubmit`, `PreToolUse`, `PostToolUse`, `PostToolUseFailure`, `PermissionRequest`, `Notification`, `PreCompact`, `Stop`, `SubagentStop`, and `SessionEnd` command-hook JSON. Configure command handlers in Claude Code settings for the events you want, with the relay reading JSON from stdin:

```json
{
  "hooks": {
    "SessionStart": [{"hooks":[{"type":"command","command":"python3 /absolute/path/to/AgentPing/tools/agentping_provider_relay.py --provider claude_code"}]}],
    "PermissionRequest": [{"hooks":[{"type":"command","command":"python3 /absolute/path/to/AgentPing/tools/agentping_provider_relay.py --provider claude_code"}]}],
    "Stop": [{"hooks":[{"type":"command","command":"python3 /absolute/path/to/AgentPing/tools/agentping_provider_relay.py --provider claude_code"}]}]
  }
}
```

Add equivalent handlers for progress/failure events as needed. `PermissionRequest` becomes a destructive display-only attention. The hook command's exit status does not authorize or deny Claude Code; provider decision control remains outside AgentPing until issue #6. Mapping is fixture-tested against <https://docs.anthropic.com/en/docs/claude-code/hooks> (retrieved 2026-08-25).

## GitHub Copilot CLI

**Supported hook events:** `sessionStart`, `sessionEnd`, `userPromptSubmitted`, `preToolUse`, `postToolUse`, and `errorOccurred`. Copilot CLI reads repository hooks from `.github/hooks/*.json` and personal hooks from `~/.copilot/hooks/*.json`. Configure the documented event keys to invoke:

```text
python3 /absolute/path/to/AgentPing/tools/agentping_provider_relay.py --provider copilot_cli
```

Include both the documented `bash` and `powershell` command forms when sharing configuration across operating systems. `preToolUse` becomes a destructive display-only attention; the relay does not return a policy decision and therefore cannot authorize a tool. Mapping is fixture-tested against <https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/use-hooks> (retrieved 2026-08-25).

## Fixtures and tests

Synthetic/recorded-shape payloads in `bridge/provider-fixtures/` cover session start, progress, waiting-for-user/approval, completion, failure, and reply handoff. They contain no live provider credentials or user transcripts. Run:

```bash
python3 -m unittest discover -s tools/tests -v
dotnet test AgentPing.sln --configuration Release
```

The .NET tests verify mapping, default-off feature switches, clean unsupported-event handling, secret redaction, omission of transcript/tool arguments, fail-closed attention mapping, state ingestion, and replay idempotency. These are local synthetic integration tests. They are not evidence of live provider accounts, provider approval execution, a physical display, or bench validation.

## Troubleshooting

- **503 Provider adapter disabled:** set only the relevant `Adapters__...__Enabled=true` variable and restart.
- **422 Unsupported provider payload:** confirm the hook event is in the supported list and compare its shape with `bridge/provider-fixtures/`; rejected payloads are intentionally not logged.
- **Relay rejects URL:** use the default `http://127.0.0.1:8742`. LAN ingestion is intentionally forbidden.
- **Codex completion appears but approvals do not:** expected; Codex external `notify` currently exposes completion only.
- **Attention appears but a tap does nothing:** expected until safe action dispatch in issue #6.
