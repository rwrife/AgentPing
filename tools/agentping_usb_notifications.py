"""Notification-only provider hooks and a single-owner USB worker.

Only fixed provider/state identifiers leave the hook. Prompts, tool arguments,
transcripts, and provider messages are never persisted or sent to the display.
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import sys
import time
import uuid

from robot_control import process_requests

PROVIDERS = ("codex", "claude", "copilot")
KINDS = ("attention", "completed", "error", "thinking")
MAX_INPUT = 1024 * 1024
TTL = 60
MAX_PENDING = 64
THINKING_COOLDOWN = 120
# The ESP32-C6's built-in USB Serial/JTAG controller always enumerates with
# this Espressif VID/PID, regardless of which physical port it is plugged
# into, so the worker can find the Pixel Pal without a fixed COM port.
DEVICE_VID = 0x303A
DEVICE_PID = 0x1001


def discover_port(serial_number: str | None = None) -> str:
    """Find the Pixel Pal's COM port by USB VID/PID instead of a fixed port."""
    from serial.tools import list_ports
    matches = [p for p in list_ports.comports()
               if p.vid == DEVICE_VID and p.pid == DEVICE_PID
               and (serial_number is None or p.serial_number == serial_number)]
    if not matches:
        raise OSError("No Pixel Pal found on USB; check the cable/port")
    if len(matches) > 1:
        raise OSError("Multiple Pixel Pals found; pass --serial to pick one: "
                       + ", ".join(f"{m.device}={m.serial_number}" for m in matches))
    return matches[0].device


class ThinkingCooldown:
    """One thinking notification per display every two minutes, after ACK."""
    def __init__(self):
        self.last_sent = None

    def allows(self, kind, now):
        return kind != "thinking" or self.last_sent is None or now - self.last_sent >= THINKING_COOLDOWN

    def delivered(self, kind, now):
        if kind == "thinking":
            self.last_sent = now


def state_dir() -> Path:
    # Avoid Microsoft Store Python's transparent LocalAppData virtualization.
    return Path.home() / ".agentping/usb"


def normalize(provider: str, payload: dict, event: str | None = None) -> str | None:
    if provider not in PROVIDERS or not isinstance(payload, dict):
        raise ValueError("invalid provider or payload")
    event = event or payload.get("hook_event_name") or payload.get("hookEventName") or payload.get("type")
    # Interactive questions can open before awaitingUserInput is emitted.
    # Observe only the tool name; never inspect or forward its question/arguments.
    if provider == "copilot" and event in ("preToolUse", "PreToolUse"):
        return "attention" if payload.get("toolName", payload.get("tool_name")) in ("ask_user", "AskUserQuestion") else None
    # Copilot checks permissions even for already-approved tools. Only its
    # actual input/permission notifications should interrupt with a wave.
    if provider == "copilot" and event in ("userPromptSubmitted", "permissionRequest", "postToolUse"):
        return "thinking"
    if event in ("agentThinking", "agent_thinking", "thinking", "working", "agentWorking"):
        return "thinking"
    if event in ("PermissionRequest", "permissionRequest", "awaitingUserInput"):
        return "attention"
    if event in ("Stop", "agentStop", "agent-turn-complete"):
        return "completed"
    if event in ("StopFailure", "PostToolUseFailure", "errorOccurred"):
        return "error"
    if event in ("Notification", "notification"):
        kind = payload.get("notification_type")
        if kind in ("permission_prompt", "idle_prompt", "elicitation_dialog"):
            return "attention"
        if kind in ("agent_thinking", "agent_working", "thinking", "working"):
            return "thinking"
        if kind in ("agent_completed", "agent_idle", "shell_completed", "shell_detached_completed"):
            return "completed"
    return None


def enqueue(root: Path, provider: str, kind: str, now: float | None = None) -> str:
    if provider not in PROVIDERS or kind not in KINDS:
        raise ValueError("invalid notification")
    now = time.time() if now is None else now
    pending = root / "pending"
    pending.mkdir(parents=True, exist_ok=True)
    files = sorted(pending.glob("*.json"))
    for path in files:
        try:
            if now - path.stat().st_mtime > TTL:
                path.unlink(missing_ok=True)
        except FileNotFoundError:
            pass
    if len(list(pending.glob("*.json"))) >= MAX_PENDING:
        raise ValueError("notification queue full")
    event_id = uuid.uuid4().hex[:16]
    target = pending / f"{time.time_ns():020d}-{event_id}.json"
    temporary = target.with_suffix(".tmp")
    temporary.write_text(json.dumps({"id": event_id, "provider": provider, "kind": kind, "created": now}), encoding="utf-8")
    temporary.replace(target)
    return event_id


def wire(event: dict, now: float) -> bytes | None:
    if set(event) != {"id", "provider", "kind", "created"}:
        raise ValueError("invalid notification fields")
    if not isinstance(event["id"], str) or not re.fullmatch(r"[0-9a-f]{16}", event["id"]):
        raise ValueError("invalid notification id")
    if event["provider"] not in PROVIDERS or event["kind"] not in KINDS:
        raise ValueError("invalid notification kind")
    if not isinstance(event["created"], (int, float)) or not 0 <= now - event["created"] < TTL:
        return None
    return f"notify {event['id']} {event['provider']} {event['kind']}\n".encode("ascii")


def run(root: Path, port_name: str | None, serial_number: str | None = None) -> None:
    import serial
    root.mkdir(parents=True, exist_ok=True)
    (root / "pending").mkdir(exist_ok=True)
    # Windows releases the byte-range lock even if the process crashes.
    import msvcrt
    lock = (root / "worker.lock").open("a+b")
    lock.seek(0); lock.write(b"0"); lock.flush(); lock.seek(0)
    try:
        msvcrt.locking(lock.fileno(), msvcrt.LK_NBLCK, 1)
    except OSError:
        raise SystemExit("AgentPing USB worker is already running")
    connection = None
    active_port = None  # The COM port actually opened; re-resolved by VID/PID when port_name is None.
    counters = {p: 0 for p in PROVIDERS}
    recent = {}
    thinking_cooldown = ThinkingCooldown()
    next_heartbeat = 0
    last_error = None
    last_event = None
    def status(connected, running=True):
        p = root / "status.tmp"
        p.write_text(json.dumps({"pid": os.getpid(), "updated": time.time(), "connected": connected, "running": running,
                                "port": active_port or port_name or "auto", "requested_port": port_name,
                                "control_version": 1, "delivered": counters, "last_event": last_event,
                                "error": last_error}), encoding="utf-8")
        p.replace(root / "status.json")
    def send(command, expected):
        if connection is None:
            raise OSError("USB unavailable")
        connection.reset_input_buffer()
        connection.write(command)
        deadline = time.monotonic() + 2
        buffer = b""
        while time.monotonic() < deadline:
            buffer = (buffer + connection.read(256))[-4096:]
            while b"\n" in buffer:
                line, buffer = buffer.split(b"\n", 1)
                if line.startswith(b"PAL ERROR"):
                    raise OSError("Device rejected command")
                if line.startswith(expected):
                    return line.decode("utf-8", errors="replace")
        raise OSError("USB acknowledgment timeout")
    try:
        while True:
            try:
                if connection is None:
                    # Re-resolve by VID/PID on every (re)connect so unplugging the
                    # device and plugging it into a different port still finds it.
                    active_port = discover_port(serial_number) if port_name is None else port_name
                    connection = serial.Serial(baudrate=115200, timeout=.1, write_timeout=1)
                    connection.dtr = False; connection.rts = False; connection.port = active_port
                    connection.open(); connection.reset_input_buffer()
                    next_heartbeat = 0
                if time.monotonic() >= next_heartbeat:
                    send(b"host\n", b"PAL HOST OK")
                    next_heartbeat = time.monotonic() + 5
                if process_requests(root, send):
                    break
                for path in sorted((root / "pending").glob("*.json")):
                    try:
                        if path.stat().st_size > 512:
                            raise ValueError("oversize event")
                        event = json.loads(path.read_text(encoding="utf-8"))
                        command = wire(event, time.time())
                    except (ValueError, TypeError, KeyError):
                        path.unlink(missing_ok=True)
                        continue
                    key = (event["provider"], event["kind"])
                    same_visible_event = last_event and (last_event["provider"], last_event["kind"]) == key
                    if command is None or not thinking_cooldown.allows(event["kind"], time.monotonic()) or (same_visible_event and time.monotonic() - recent.get(key, -100) < 3):
                        path.unlink(missing_ok=True)
                        continue
                    send(command, f"PAL EVENT {event['id']} OK".encode())
                    thinking_cooldown.delivered(event["kind"], time.monotonic())
                    recent[key] = time.monotonic()
                    counters[event["provider"]] += 1
                    last_event = {"provider": event["provider"], "kind": event["kind"], "at": time.time()}
                    path.unlink(missing_ok=True)
                last_error = None
                status(True)
                time.sleep(.25)
            except (OSError, serial.SerialException):
                if connection:
                    connection.close()
                connection = None
                active_port = None
                last_error = "USB unavailable; retrying"
                status(False)
                if process_requests(root, send):
                    break
                time.sleep(2)
    finally:
        if connection:
            connection.close()
        status(False, running=False)
        lock.close()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--state-dir", type=Path, default=state_dir())
    sub = parser.add_subparsers(dest="mode", required=True)
    hook = sub.add_parser("hook")
    hook.add_argument("--provider", choices=PROVIDERS, required=True)
    hook.add_argument("--event")
    hook.add_argument("payload", nargs="?")
    worker = sub.add_parser("run")
    worker.add_argument("--port", default=None, help="Explicit COM port; omit to auto-detect by USB VID/PID")
    worker.add_argument("--serial", default=None, help="USB serial number to pick one of several Pixel Pals")
    sub.add_parser("status")
    args = parser.parse_args()
    if args.mode == "run":
        run(args.state_dir, args.port, args.serial)
    elif args.mode == "status":
        path = args.state_dir / "status.json"
        print(path.read_text() if path.exists() else '{"connected": false}')
    else:
        try:
            raw = args.payload.encode() if args.payload is not None else sys.stdin.buffer.read(MAX_INPUT + 1)
            if len(raw) > MAX_INPUT:
                raise ValueError("oversize hook input")
            payload = json.loads(raw)
            kind = normalize(args.provider, payload, args.event)
            if kind:
                enqueue(args.state_dir, args.provider, kind)
        except (OSError, ValueError, TypeError):
            # Notification hooks never control provider permissions or task completion.
            print("AgentPing notification skipped; payload not logged", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
