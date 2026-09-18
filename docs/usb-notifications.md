# USB provider notifications

This notification-only path connects local Codex, Claude Code, and GitHub Copilot
CLI hooks to the `live3d_usb` (C6) or `live3d_usb_s3` (S3) robot firmware (also supported by the older baked
`character_usb` profile). It is separate from the existing
network bridge and approval-return protocol. It never approves, denies, or
continues an agent task, and its hook commands emit no stdout.

## Setup on Windows

From the repository root, after running `companion/setup-robot.ps1`:

```powershell
.\.venv-robot\Scripts\python.exe tools/install_usb_hooks.py --apply
.\companion\robot.cmd start
```

The installer copies the hook/worker script into `~/.agentping/usb/bin`, backs up
existing settings under `~/.agentping/usb/backups`, and merges only its own hooks:

- Codex: `~/.codex/hooks.json`, `UserPromptSubmit`, `PostToolUse`,
  `PermissionRequest`, and `Stop`.
- Claude Code: `~/.claude/settings.json`, `PermissionRequest`, `Notification`,
  `Stop`, and `StopFailure`.
- Copilot CLI: `~/.copilot/hooks/agentping-usb.json`, `userPromptSubmitted`, `permissionRequest`, `postToolUse`,
  `notification`, `awaitingUserInput`, `agentStop`, and `errorOccurred`.

Review/trust the Codex entries in `/hooks`; untrusted hooks are skipped by Codex.
The Codex lifecycle path does not depend on Windows notifications: submitting a
prompt or finishing a tool emits thinking, permissions emit attention, and
`Stop` emits completion. Having a hooks file is not enough: all four AgentPing
definitions must be trusted. In a terminal, run `codex` and open `/hooks` to
review the handlers pointing to `~/.agentping/usb/bin/agentping_usb_notifications.py`.
Reload the desktop app after reviewing hooks so existing sessions pick them up.
Windows toast forwarding is separate and can work while these hooks are skipped.
Restart the provider sessions to load updated settings. Existing Codex `notify`
configuration and unrelated handlers are preserved. Rerunning the installer
updates AgentPing's handlers without adding duplicates. Remove only the
`agentping_usb_notifications.py` handlers to uninstall; do not overwrite later
user settings with an old backup.

One worker owns the Pixel Pal. Stop it before flashing firmware or using the
manual serial helper. The worker finds the device by its USB VID/PID
(`303A:1001`) rather than a fixed COM port, so unplugging it and reconnecting
it to a different port does not require a restart; pass `--port` to pin an
explicit port instead, or `--serial <id>` to disambiguate several connected
Pixel Pals. The process retries disconnected USB every two seconds and checks
a host heartbeat every five seconds. No network listener or Wi-Fi is needed.
Unplugging the device does not block provider tools: hooks only write to the
local notification queue and never open USB. The worker remains available for
stop commands while disconnected, tolerates errors closing unplugged handles,
and discovers the device again on reconnection. Queued notices older than
60 seconds are discarded rather than shown when you return.
Launching the worker does not install an auto-start service.

## Detection and display

Copilot's interactive `ask_user` questions trigger attention through a targeted
`preToolUse` hook, before the question opens. Other tools are ignored by this
hook, and it emits no permission decision. This covers questions that do not
emit `awaitingUserInput` while waiting. Restart existing Copilot sessions after
reinstalling hooks to load this handler. Attention bypasses the thinking cooldown.

Permission requests and requests for input trigger attention. Thinking/working
signals trigger the thinking animation instead of the amber “Ready for your
input” attention state. Turn completion triggers completion, and supported
failure hooks trigger an error notice. Copilot completion requires a main-agent
`agentStop`/`Stop` event with `stopReason`/`stop_reason` equal to `end_turn`.
Background `agent_completed`, `agent_idle`, `shell_completed`, and
`shell_detached_completed` notifications never mean the main task is done and
are ignored. Missing or unknown stop reasons are ignored as well. This detects
the provider's end of a response turn, not independent verification that every
requested goal is satisfied; another stop hook can still force continuation.
See the [GitHub hook reference](https://docs.github.com/en/copilot/reference/hooks-reference).
Raw provider payloads are read only in
the short-lived hook; only a random event ID,
provider, fixed kind, and timestamp are queued. Prompts, notification text, tool
arguments, transcripts, credentials, and session IDs are never persisted by this
path. The firmware supplies fixed messages and the provider-specific face logo.

Notifications expire from the queue after 60 seconds. The queue is bounded, and
automatic thinking notices have a **120-second cooldown across all providers**.
The cooldown starts after a successful device acknowledgment. Repeats during
that interval are discarded, not delayed, and do not extend the current bubble's
20-second lifetime. Attention, error, and completion notices remain eligible
immediately. The cooldown resets when the desktop worker restarts; explicit
CLI/MCP demo commands bypass it.

For other repeated notices,
the worker coalesces repeated provider/kind events within three seconds. The
firmware acknowledges each event ID and ignores a retry of its most recent ID.
In both live3d profiles, thinking dismisses after 20 seconds; other messages dismiss after
30 seconds. Both blend back to idle. The
initial connection caption disappears after the first desktop signal. The older
baked profile uses forward/reverse listening while disconnected. Multiple simultaneous
notifications currently replace the visible bubble; there is no on-device inbox.

USB commands added by this path:

```text
host
notify 0123456789abcdef codex attention
notify fedcba9876543210 claude completed
notify 1234567890abcdef copilot error
```

Acknowledgments are `PAL HOST OK` and `PAL EVENT <id> OK`. Unknown providers,
kinds, extra fields, and malformed IDs are rejected. Only the older baked profile
restores the connection caption after 15 seconds without a heartbeat; live 3D
shows it only before the first desktop signal. Notifications do not expose any
device-side approval command.

Inspect the worker status without opening COM5:

```powershell
.\companion\robot.cmd worker-status
```

Check `updated` as well as `connected`: a stopped/killed process can leave stale
status on disk. Delivery counters reflect this worker run and successful device
acknowledgments, not proof that a human read the message.

## Verification and source contracts

```powershell
.\.venv-robot\Scripts\python.exe -m unittest discover -s tools/tests -v
```

Installed binaries used during bring-up: Codex `0.154.0-alpha.6.2`, Claude Code
`2.1.197`, and Copilot CLI `1.0.84-8`. Keep live-provider results separate from
injected fixture results when reporting validation.

Earlier baked-firmware bench results on 2026-09-16 (not live-renderer memory figures):

- Claude Code and Copilot CLI: real no-tool completion runs emitted hooks, and
  their queued events were acknowledged by the physical device on COM5.
- Codex: an injected permission event traversed hook, queue, worker, and device.
  A real CLI test completed but did not emit the installed hook; user hook
  review/activation is still pending. Do not count the injected event as a live
  Codex notification.
- The device rejected malformed IDs/providers, accepted a duplicate event ID,
  and returned to idle after the 30-second notification interval. Free heap
  remained 154,756 bytes during the short check.
- Eleven Python tests passed, including mapping, queue privacy/expiry, wire
  validation, and additive/idempotent installation. All 132 baked frames passed
  round-trip encoding and image-boundary checks.

Official hook references consulted 2026-09-16:

- [Codex lifecycle hooks and trust](https://learn.chatgpt.com/docs/hooks)
- [Claude Code hooks](https://code.claude.com/docs/en/hooks)
- [Copilot CLI hooks](https://docs.github.com/en/copilot/reference/hooks-reference)

This integration targets the CLIs and Codex surfaces that honor these lifecycle
hooks. It does not scrape Windows toast notifications or VS Code UI state.

Copilot prompt submission, routine permission checks, and successful tool completion
map to thinking. Permission checks can run for pre-approved tools; attention is
reserved for `awaitingUserInput` and permission/input notifications. Reinstall
hooks and restart Copilot sessions after updating. The live firmware must include
the thinking notification kind; pulling host code alone does not update the board.
