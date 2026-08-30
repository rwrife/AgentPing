# Provider adapters

Status: **local hook ingestion and fail-closed action return implemented and synthetic-test validated; live provider accounts are not validated**.

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

`PermissionRequest` and `preToolUse` are represented as destructive, 15-second provider-hook attention items (protocol v1 still permits up to 30 seconds for other clients). With `--wait-for-action`, the relay waits for a device decision and prints only the provider's documented decision object. The bridge commits the action before waking the relay; stale, conflicting, malformed, timed-out, or disconnected requests deny/no-op. The relay uses a 20-second local timeout and emits an explicit deny decision on bridge/network/validation failure. Reply text is returned only to the manual/test adapter and is never placed in bridge persistent state or logs.

## Supported-version policy

No live provider CLI binary or account was available in this implementation run, so this project does not invent a tested executable-version floor. Compatibility is explicitly tied to the public hook contract and fixtures below:

| Adapter | Supported contract/version | Live version tested | Drift behavior |
|---|---|---|---|
| Codex CLI | External `notify` completion contract plus command-hook `PermissionRequest`, documented 2026-08-28 | None | Unknown hook/notify types return 422 |
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

A notification-only relay exits 0 and intentionally prints no payload or response body. For a documented permission event, add `--wait-for-action`; success prints one compact provider decision JSON object on stdout. A failed relay emits a payload-free diagnostic and exits 1. Do not use `--wait-for-action` for lifecycle notifications: the bridge returns 409 because no actionable attention was created.

## OpenAI Codex CLI

**Supported surfaces:** the documented Codex CLI `notify` completion contract (`agent-turn-complete`) and command-hook `PermissionRequest`. Completion mapping uses `thread-id`, `turn-id`, optional `cwd`, and bounded `last-assistant-message`; it does not copy `input-messages`. Configure completion notification in `~/.codex/config.toml`:

```toml
notify = ["python3", "/absolute/path/to/AgentPing/tools/agentping_provider_relay.py", "--provider", "codex"]
```

Codex appends one JSON argument to the command and AgentPing maps it to a completed session event. Separately, configure the documented `PermissionRequest` command hook to read JSON from stdin and invoke:

```text
python3 /absolute/path/to/AgentPing/tools/agentping_provider_relay.py --provider codex --wait-for-action
```

The adapter hashes the full source payload only for collision-safe correlation, but stores/displays only session/turn identifiers, tool name, and bounded redacted `tool_input.description`; raw commands and tool arguments are excluded. The relay emits Codex's documented `hookSpecificOutput.hookEventName=PermissionRequest` decision with `behavior=allow|deny`. This is fixture-tested against <https://developers.openai.com/codex/hooks> and the completion contract at <https://developers.openai.com/codex/config-advanced> (retrieved 2026-08-28).

## Anthropic Claude Code

**Supported hook events:** `SessionStart`, `UserPromptSubmit`, `PreToolUse`, `PostToolUse`, `PostToolUseFailure`, `PermissionRequest`, `Notification`, `PreCompact`, `Stop`, `SubagentStop`, and `SessionEnd` command-hook JSON. Configure command handlers in Claude Code settings for the events you want, with the relay reading JSON from stdin:

```json
{
  "hooks": {
    "SessionStart": [{"hooks":[{"type":"command","command":"python3 /absolute/path/to/AgentPing/tools/agentping_provider_relay.py --provider claude_code"}]}],
    "PermissionRequest": [{"hooks":[{"type":"command","command":"python3 /absolute/path/to/AgentPing/tools/agentping_provider_relay.py --provider claude_code --wait-for-action"}]}],
    "Stop": [{"hooks":[{"type":"command","command":"python3 /absolute/path/to/AgentPing/tools/agentping_provider_relay.py --provider claude_code"}]}]
  }
}
```

Add equivalent notification-only handlers for progress/failure events as needed. A `PermissionRequest` device approval emits Claude Code's documented `hookSpecificOutput` decision with `behavior=allow`; every non-approval emits `behavior=deny`. Mapping and decision rendering are fixture-tested against <https://docs.anthropic.com/en/docs/claude-code/hooks> (retrieved 2026-08-28).

## GitHub Copilot CLI

**Supported hook events:** `sessionStart`, `sessionEnd`, `userPromptSubmitted`, `preToolUse`, `postToolUse`, and `errorOccurred`. Copilot CLI reads repository hooks from `.github/hooks/*.json` and personal hooks from `~/.copilot/hooks/*.json`. Configure the documented event keys to invoke:

```text
python3 /absolute/path/to/AgentPing/tools/agentping_provider_relay.py --provider copilot_cli --wait-for-action
```

Use `--wait-for-action` only for `preToolUse`; omit it for lifecycle events. Include both documented `bash` and `powershell` command forms when sharing configuration across operating systems. Device approval emits `{"permissionDecision":"allow"}`; every non-approval emits `deny` with a fixed non-secret reason. Mapping and decision rendering are fixture-tested against <https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/use-hooks> (retrieved 2026-08-28).

## Fixtures and tests

Synthetic/recorded-shape payloads in `bridge/provider-fixtures/` cover session start, progress, waiting-for-user/approval, completion, failure, and reply handoff. They contain no live provider credentials or user transcripts. Run:

```bash
python3 -m unittest discover -s tools/tests -v
dotnet test AgentPing.sln --configuration Release
```

The .NET/Python tests verify mapping, default-off feature switches, clean unsupported-event handling, secret redaction, omission of transcript/tool arguments, fail-closed attention mapping, commit-before-notify action brokering, restart idempotency, deadline/conflict rejection, and provider-specific decision JSON. These are local synthetic integration tests. They are not evidence of live provider accounts, a physical display, or bench validation.

## Troubleshooting

- **503 Provider adapter disabled:** set only the relevant `Adapters__...__Enabled=true` variable and restart.
- **422 Unsupported provider payload:** confirm the hook event is in the supported list and compare its shape with `bridge/provider-fixtures/`; rejected payloads are intentionally not logged.
- **Relay rejects URL:** use the default `http://127.0.0.1:8742`. LAN ingestion is intentionally forbidden.
- **Codex completion appears but approvals do not:** completion uses external `notify`; configure the separate `PermissionRequest` command hook with `--wait-for-action`.
- **Permission hook returns 409:** that source event did not create an actionable attention, usually because `--wait-for-action` was attached to a lifecycle notification.
- **Permission hook times out:** the relay returns a deny decision after its 20-second local timeout; verify a paired display is connected and the 15-second provider-action deadline has not expired.
- **Copilot timeout safety:** Copilot CLI documents command-hook timeouts as fail-open. Keep its `timeoutSec` above 20 seconds (the default is 30), use the command-hook form rather than HTTP hooks, and do not treat the hook as the sole policy boundary. AgentPing returns an explicit deny on expected bridge/network failures before that provider timeout; an externally killed or hung relay process remains a provider limitation.
