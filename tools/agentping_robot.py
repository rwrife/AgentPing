"""AgentPing desktop USB controls: CLI and local stdio MCP server."""
from __future__ import annotations
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import time
from typing import Literal

from agentping_usb_notifications import state_dir
from robot_control import DANCES, JOINTS, STATES, request


def worker_status(root: Path) -> dict:
    try:
        value = json.loads((root / "status.json").read_text(encoding="utf-8"))
        value["running"] = value.get("running", True) and time.time() - value["updated"] < 7
        return value
    except (OSError, ValueError, KeyError):
        return {"running": False, "connected": False}


def start_worker(root: Path, port: str) -> dict:
    status = worker_status(root)
    if status["running"]:
        if status.get("control_version") != 1 or status.get("port") != port:
            raise RuntimeError("An older worker or different USB port is active; stop it before starting this worker")
        return status
    root.mkdir(parents=True, exist_ok=True)
    # The existing worker lock arbitrates simultaneous CLI/MCP starts.
    subprocess.Popen([sys.executable, str(Path(__file__).with_name("agentping_usb_notifications.py")),
                      "--state-dir", str(root), "run", "--port", port],
                     stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                     creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
    until = time.monotonic() + 6
    while time.monotonic() < until:
        status = worker_status(root)
        if status["running"] and status.get("control_version") == 1:
            return status
        time.sleep(.1)
    raise RuntimeError("Worker did not start; run agentping_usb_notifications.py run in a terminal for diagnostics")


def serve_mcp(root: Path) -> None:
    from mcp.server.fastmcp import FastMCP
    mcp = FastMCP("AgentPing Robot", instructions="Control the on-screen Pixel Pal over the desktop USB worker. Joint movement changes the rendered skeleton, not physical motors. Reset returns to idle.")

    @mcp.tool()
    def robot_status() -> dict:
        """Read the robot's live state, camera zoom and bubble visibility."""
        return request(root, "status", {})

    @mcp.tool()
    def robot_message(message: str, state: Literal["thinking", "attention", "error"] = "attention") -> dict:
        """Show up to 192 printable ASCII characters with an animation. Returns to idle after 30 seconds."""
        return request(root, "state", {"state": state, "message": message})

    @mcp.tool()
    def robot_icon(data_hex: str, color: str = "#50dfff", message: str = "", state: Literal["attention", "error"] = "attention") -> dict:
        """Show a custom 48x48 1-bit face icon: exactly 576 hex digits (288 bytes), row-major top-to-bottom, leftmost pixel in the least-significant bit. Color is #RRGGBB; set bits use that color, clear bits use the dark face background. Optional ASCII message. Error state always renders the icon red, overriding color. Returns to idle after 30 seconds."""
        return request(root, "icon", {"data_hex": data_hex, "color": color, "message": message, "state": state})

    @mcp.tool()
    def robot_animation(state: Literal["idle", "thinking", "attention", "error", "wave", "boot"]) -> dict:
        """Play a built-in robot state. Thinking selects a playful thought automatically."""
        return request(root, "state", {"state": state})

    @mcp.tool()
    def robot_dance(name: Literal["random", "hiphop", "twist", "chicken"] = "random") -> dict:
        """Play one dance, then return to idle."""
        return request(root, "dance", {"name": name})

    @mcp.tool()
    def robot_move_joint(joint: Literal["head", "torso", "left_shoulder", "right_shoulder", "left_elbow", "right_elbow", "left_hip", "right_hip", "left_knee", "right_knee"],
                         x: float = 0, y: float = 0, z: float = 0, duration_ms: int = 600) -> dict:
        """Freeze the current pose and smoothly offset a joint on its local XYZ axes, in degrees (-90..90). Offsets are relative to the pose captured on entering manual mode. Later calls retain other joints. Idle/reset exits manual mode."""
        return request(root, "joint", {"joint": joint, "x": x, "y": y, "z": z, "duration_ms": duration_ms})

    @mcp.tool()
    def robot_reset() -> dict:
        """Clear manual joint controls and bubbles, blending back to idle."""
        return request(root, "reset", {})

    mcp.run(transport="stdio")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--state-dir", type=Path, default=state_dir())
    parser.add_argument("--port", default="COM5")
    sub = parser.add_subparsers(dest="mode", required=True)
    sub.add_parser("start", help="Start the shared desktop worker in the background")
    sub.add_parser("worker-status")
    sub.add_parser("stop", help="Stop the worker and release USB for flashing")
    sub.add_parser("mcp", help="Serve MCP over stdio (start the worker first)")
    sub.add_parser("status")
    sub.add_parser("reset")
    state = sub.add_parser("state"); state.add_argument("state", choices=STATES)
    message = sub.add_parser("message"); message.add_argument("message")
    message.add_argument("--state", choices=("thinking", "attention", "error"), default="attention")
    dance = sub.add_parser("dance"); dance.add_argument("name", choices=DANCES, nargs="?", default="random")
    icon = sub.add_parser("icon", help="Show a 48x48 1-bit face icon with an RGB color")
    source = icon.add_mutually_exclusive_group(required=True)
    source.add_argument("--data-hex", help="576 hex digits, row-major, LSB first")
    source.add_argument("--file", type=Path, help="Raw 288-byte packed bitmap")
    icon.add_argument("--color", default="#50dfff")
    icon.add_argument("--message", default="")
    icon.add_argument("--state", choices=("attention", "error"), default="attention")
    joint = sub.add_parser("joint"); joint.add_argument("joint", choices=JOINTS)
    for axis in ("x", "y", "z"): joint.add_argument("--" + axis, type=float, default=0)
    joint.add_argument("--duration-ms", type=int, default=600)
    args = parser.parse_args()
    try:
        if args.mode == "mcp":
            serve_mcp(args.state_dir); return 0
        if args.mode == "start": result = start_worker(args.state_dir, args.port)
        elif args.mode == "worker-status": result = worker_status(args.state_dir)
        else:
            fields = {k: v for k, v in vars(args).items() if k not in ("mode", "state_dir", "port")}
            if args.mode == "icon":
                path = fields.pop("file")
                if path is not None:
                    with path.open("rb") as stream:
                        fields["data_hex"] = stream.read(289).hex()
            result = request(args.state_dir, "state" if args.mode == "message" else args.mode, fields)
        print(json.dumps(result))
        return 0
    except (ValueError, RuntimeError, OSError) as error:
        print(json.dumps({"ok": False, "error": str(error)}), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
