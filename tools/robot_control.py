"""Validated demo commands shared by the desktop worker, CLI and MCP server."""
from __future__ import annotations
import json
import math
from pathlib import Path
import time
import uuid

STATES = ("idle", "thinking", "attention", "error", "wave", "boot")
DANCES = ("random", "hiphop", "twist", "chicken")
JOINTS = ("head", "torso", "left_shoulder", "right_shoulder", "left_elbow", "right_elbow",
          "left_hip", "right_hip", "left_knee", "right_knee")


def command(action: str, args: dict) -> tuple[bytes, bytes]:
    if not isinstance(args, dict):
        raise ValueError("arguments must be an object")
    allowed = {"status": set(), "state": {"state", "message"}, "dance": {"name"},
               "joint": {"joint", "x", "y", "z", "duration_ms"}, "reset": set(), "stop": set()}
    if action not in allowed or set(args) - allowed[action]:
        raise ValueError("unknown command or argument")
    if action == "stop":
        return b"", b""
    if action == "status":
        return b"viewstatus\n", b"VIEW "
    if action == "reset":
        return b"idle\n", b"STARTUP ENTER state=idle "
    if action == "state":
        state, message = args.get("state"), args.get("message", "")
        if state not in STATES or not isinstance(message, str):
            raise ValueError("invalid state or message")
        # Current display font and line protocol support printable ASCII.
        if len(message) > 192 or any(ord(c) < 32 or ord(c) > 126 for c in message):
            raise ValueError("message must be at most 192 printable ASCII characters")
        if message and state not in ("thinking", "attention", "error"):
            raise ValueError("messages require thinking, attention or error")
        line = state + (" " + message if message else "")
        expected = {"idle": "STARTUP ENTER state=idle ", "boot": "STARTUP ENTER state=fall ",
                    "wave": "PAL STATE attention OK"}.get(state, f"PAL STATE {state} OK")
        return (line + "\n").encode(), expected.encode()
    if action == "dance":
        name = args.get("name", "random")
        if name not in DANCES:
            raise ValueError("unknown dance")
        return ("dance" + (" " + name if name != "random" else "") + "\n").encode(), b"STARTUP ENTER state="
    joint = args.get("joint")
    if joint not in JOINTS:
        raise ValueError("unknown joint")
    angles = [args.get(axis, 0) for axis in ("x", "y", "z")]
    if any(type(v) not in (int, float) or not math.isfinite(v) or abs(v) > 90 for v in angles):
        raise ValueError("joint angles must be finite numbers between -90 and 90 degrees")
    duration = args.get("duration_ms", 600)
    if type(duration) is not int or not 100 <= duration <= 5000:
        raise ValueError("duration_ms must be an integer between 100 and 5000")
    return (f"joint {joint} {angles[0]:.3f} {angles[1]:.3f} {angles[2]:.3f} {duration}\n".encode(),
            f"PAL JOINT {joint} OK".encode())


def file_io(operation):
    # Windows can briefly retain a rename/delete handle after publishing a file.
    # Retry only filesystem sharing failures, never a USB send.
    for attempt in range(6):
        try:
            return operation()
        except PermissionError:
            if attempt == 5:
                raise
            time.sleep(.02)


def atomic_json(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(".tmp")
    temp.write_text(json.dumps(value, allow_nan=False), encoding="utf-8")
    file_io(lambda: temp.replace(path))


def request(root: Path, action: str, args: dict, timeout: float = 8) -> dict:
    command(action, args)
    request_id = uuid.uuid4().hex
    target = root / "commands" / (request_id + ".json")
    result = root / "results" / (request_id + ".json")
    if len(list(target.parent.glob("*.json"))) >= 32:
        raise RuntimeError("robot command queue is full")
    atomic_json(target, {"action": action, "args": args, "expires": time.time() + 5})
    deadline = time.monotonic() + timeout
    try:
        while time.monotonic() < deadline:
            if result.exists():
                value = json.loads(file_io(lambda: result.read_text(encoding="utf-8")))
                file_io(lambda: result.unlink(missing_ok=True))
                if not value.get("ok"):
                    raise RuntimeError(value.get("error", "robot command failed"))
                return value
            time.sleep(.05)
        raise RuntimeError("robot worker did not acknowledge; run 'start' and check USB")
    finally:
        file_io(lambda: target.unlink(missing_ok=True))


def process_requests(root: Path, send) -> bool:
    """Consume each request once. An uncertain USB send is never replayed."""
    now = time.time()
    for path in sorted((root / "commands").glob("*.json"))[:32]:
        if len(path.stem) != 32 or any(c not in "0123456789abcdef" for c in path.stem):
            file_io(lambda: path.unlink(missing_ok=True))
            continue
        try:
            if path.stat().st_size > 2048:
                raise ValueError("oversize command")
            event = json.loads(file_io(lambda: path.read_text(encoding="utf-8")))
            if set(event) != {"action", "args", "expires"}:
                raise ValueError("invalid request")
            now = time.time()
            expires = event["expires"]
            if type(expires) not in (int, float) or not math.isfinite(expires) or not now < expires <= now + 6:
                raise ValueError("command expired")
            wire, ack = command(event["action"], event["args"])
            file_io(lambda: path.unlink(missing_ok=True))
            reply = "Desktop USB worker stopped" if event["action"] == "stop" else send(wire, ack)
            value = {"ok": True, "reply": reply}
        except (OSError, ValueError, TypeError, KeyError) as error:
            # Do not persist user text or transport diagnostics in error logs.
            value = {"ok": False, "error": f"Invalid, expired or unacknowledged robot command ({type(error).__name__}); not retried"}
        finally:
            file_io(lambda: path.unlink(missing_ok=True))
        atomic_json(root / "results" / path.name, value)
        if value["ok"] and event["action"] == "stop":
            return True
    for path in (root / "results").glob("*.json"):
        try:
            if now - path.stat().st_mtime > 60:
                file_io(lambda: path.unlink(missing_ok=True))
        except FileNotFoundError:
            pass
    return False
