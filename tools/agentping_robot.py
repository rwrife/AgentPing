"""AgentPing desktop USB controls: CLI and local stdio MCP server."""
from __future__ import annotations
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time
from typing import Literal

from agentping_usb_notifications import state_dir
from robot_control import DANCES, JOINTS, MOTION_BONES, MOTION_MAX_FRAMES, STATES, request

REPO = Path(__file__).resolve().parents[1]
EXPORTED_CLIP = REPO / "simulator/test-results/live3d/custom_motion.json"
# The renderer refuses an upload that would leave it less than this much heap.
MOTION_HEAP_RESERVE = 24576
MOTION_HEAP_MARGIN = 512


def convert_fbx(source: Path, rate: int) -> Path:
    """Retarget a Mixamo FBX onto the Pixel Pal rig using the simulator exporter."""
    node = shutil.which("node")
    if not node:
        raise RuntimeError("Node.js is required to convert an FBX; pass an exported .json clip instead")
    exporter = REPO / "simulator/scripts/export-motion.mjs"
    finished = subprocess.run([node, str(exporter), str(source), "custom_motion", "--no-header", "--rate", str(rate)],
                              capture_output=True, text=True, cwd=REPO)
    if finished.returncode:
        raise RuntimeError("FBX conversion failed; run simulator/scripts/export-motion.mjs directly for details")
    return EXPORTED_CLIP


def sample_clip(clip: dict, fps: int, start: float, max_frames: int = MOTION_MAX_FRAMES) -> dict:
    """Pick an evenly spaced window that fits the renderer's RAM clip limit."""
    if clip.get("bones") != MOTION_BONES:
        raise ValueError(f"clip must target the {MOTION_BONES}-bone Pixel Pal rig")
    source_rate, frames = clip.get("rate"), clip.get("frames")
    if type(source_rate) is not int or source_rate < 1 or not isinstance(frames, list):
        raise ValueError("clip is missing a frame rate or keyframes")
    if not 1 <= fps <= 60:
        raise ValueError("fps must be between 1 and 60")
    stride = max(1, round(source_rate / fps))
    first = min(max(0, round(start * source_rate)), max(0, len(frames) - 1))
    window = frames[first::stride][:max(0, min(max_frames, MOTION_MAX_FRAMES))]
    if len(window) < 2:
        raise ValueError("clip window needs at least two keyframes; lower --start or raise --fps")
    return {"frames": window, "rate": max(1, min(60, round(source_rate / stride)))}


def load_motion(clip: Path, fps: int, start: float, max_frames: int = MOTION_MAX_FRAMES) -> dict:
    # Sampling the FBX at the playback rate avoids uploading frames the renderer
    # would immediately discard; exported .json clips are strided instead.
    source = convert_fbx(clip, fps) if clip.suffix.lower() == ".fbx" else clip
    return sample_clip(json.loads(source.read_text(encoding="utf-8")), fps, start, max_frames)


def frame_budget(root: Path) -> int:
    """Ask the device how many keyframes fit beside the renderer's fixed heap reserve."""
    reply = request(root, "status", {}).get("reply") or ""
    free = re.search(r"heap=(\d+)", reply)
    if not free:
        raise RuntimeError("robot did not report free memory")
    usable = int(free.group(1)) - MOTION_HEAP_RESERVE - MOTION_HEAP_MARGIN
    budget = min(MOTION_MAX_FRAMES, usable // (MOTION_BONES * 8))
    if budget < 2:
        raise RuntimeError("robot has too little free memory for a custom clip; lower the render resolution")
    return budget


def play_motion(root: Path, clip: Path, fps: int, start: float) -> dict:
    # Convert/validate before interrupting the current animation. Release the old
    # RAM clip before measuring capacity for its replacement.
    motion = load_motion(clip, fps, start)
    request(root, "reset", {})
    motion["frames"] = motion["frames"][:frame_budget(root)]
    result = request(root, "motion", motion, timeout=90)
    result["clip"] = {"frames": len(motion["frames"]), "rate": motion["rate"],
                      "seconds": round((len(motion["frames"]) - 1) / motion["rate"], 2)}
    return result


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
    def robot_dance(name: Literal["random", "twist", "chicken"] = "random") -> dict:
        """Play one dance, then return to idle."""
        return request(root, "dance", {"name": name})

    @mcp.tool()
    def robot_move_joint(joint: Literal["head", "torso", "left_shoulder", "right_shoulder", "left_elbow", "right_elbow", "left_hip", "right_hip", "left_knee", "right_knee"],
                         x: float = 0, y: float = 0, z: float = 0, duration_ms: int = 600) -> dict:
        """Freeze the current pose and smoothly offset a joint on its local XYZ axes, in degrees (-90..90). Offsets are relative to the pose captured on entering manual mode. Later calls retain other joints. Idle/reset exits manual mode."""
        return request(root, "joint", {"joint": joint, "x": x, "y": y, "z": z, "duration_ms": duration_ms})

    @mcp.tool()
    def robot_play_motion(path: str, fps: int = 10, start_seconds: float = 0) -> dict:
        """Retarget a Mixamo .fbx animation (or an already exported .json clip) onto the robot and loop it from RAM. Converting an FBX needs Node.js and the simulator dependencies. The renderer keeps clips in its spare heap, so a long clip may be trimmed: fps trades smoothness against how much of the clip fits, and start_seconds chooses where the window begins. Idle or reset stops playback."""
        return play_motion(root, Path(path), fps, start_seconds)

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
    motion = sub.add_parser("motion", help="Play a custom Mixamo animation from the renderer's RAM")
    motion.add_argument("clip", type=Path, help="Mixamo .fbx, or a .json clip already written by export-motion.mjs")
    motion.add_argument("--fps", type=int, default=10, help="Keyframes sampled per second (1-60)")
    motion.add_argument("--start", type=float, default=0, help="Seconds into the clip to begin the sampled window")
    args = parser.parse_args()
    try:
        if args.mode == "mcp":
            serve_mcp(args.state_dir); return 0
        if args.mode == "start": result = start_worker(args.state_dir, args.port)
        elif args.mode == "worker-status": result = worker_status(args.state_dir)
        elif args.mode == "motion":
            result = play_motion(args.state_dir, args.clip, args.fps, args.start)
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
