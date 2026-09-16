# USB provider notifications

This notification-only path connects local Codex, Claude Code, and GitHub Copilot
CLI hooks to the `character_usb` firmware. It is separate from the existing
network bridge and approval-return protocol. It never approves, denies, or
continues an agent task, and its hook commands emit no stdout.

## Setup on Windows

From the repository root, using the firmware Python environment (includes pyserial):

```powershell
.\.venv-firmware\Scripts\python.exe tools/install_usb_hooks.py --apply
.\.venv-firmware\Scripts\python.exe tools/agentping_usb_notifications.py run --port COM5
```

The installer copies the hook/worker script into `~/.agentping/usb/bin`, backs up
existing settings under `~/.agentping/usb/backups`, and merges only its own hooks:

- Codex: `~/.codex/hooks.json`, `PermissionRequest` and `Stop`.
- Claude Code: `~/.claude/settings.json`, `PermissionRequest`, `Notification`,
  `Stop`, and `StopFailure`.
- Copilot CLI: `~/.copilot/hooks/agentping-usb.json`, `permissionRequest`,
  `notification`, `agentStop`, and `errorOccurred`.

Review/trust the Codex entries in `/hooks`; untrusted hooks are skipped by Codex.
Restart the provider sessions to load updated settings. Existing Codex `notify`
configuration and unrelated handlers are preserved. Rerunning the installer
updates AgentPing's handlers without adding duplicates. Remove only the
`agentping_usb_notifications.py` handlers to uninstall; do not overwrite later
user settings with an old backup.

One worker owns COM5. Stop it before flashing firmware or using the manual serial
helper. The process retries disconnected USB every two seconds and checks a host
heartbeat every five seconds. No network listener or Wi-Fi is needed. Launching
the worker does not install an auto-start service.

## Detection and display

Permission requests and requests for input trigger attention. Turn completion
triggers completion, and supported failure hooks trigger an error notice. Raw
provider payloads are read only in the short-lived hook; only a random event ID,
provider, fixed kind, and timestamp are queued. Prompts, notification text, tool
arguments, transcripts, credentials, and session IDs are never persisted by this
path. The firmware supplies fixed messages and the provider-specific face logo.

Notifications expire from the queue after 60 seconds. The queue is bounded, and
the worker coalesces repeated provider/kind events within three seconds. The
firmware acknowledges each event ID and ignores a retry of its most recent ID.
Messages dismiss after 30 seconds and return to idle. Multiple simultaneous
notifications currently replace the visible bubble; there is no on-device inbox.

USB commands added by this path:

```text
host
notify 0123456789abcdef codex attention
notify fedcba9876543210 claude completed
notify 1234567890abcdef copilot error
```

Acknowledgments are `PAL HOST OK` and `PAL EVENT <id> OK`. Unknown providers,
kinds, extra fields, and malformed IDs are rejected. The host connection caption
returns after 15 seconds without a heartbeat. Notifications do not expose any
device-side approval command.

Inspect the worker status without opening COM5:

```powershell
.\.venv-firmware\Scripts\python.exe tools/agentping_usb_notifications.py status
```

Check `updated` as well as `connected`: a stopped/killed process can leave stale
status on disk. Delivery counters reflect this worker run and successful device
acknowledgments, not proof that a human read the message.

## Verification and source contracts

```powershell
.\.venv-firmware\Scripts\python.exe -m unittest discover -s tools/tests -v
```

Installed binaries used during bring-up: Codex `0.154.0-alpha.6.2`, Claude Code
`2.1.197`, and Copilot CLI `1.0.84-8`. Keep live-provider results separate from
injected fixture results when reporting validation.

Bench results on 2026-09-16:

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
