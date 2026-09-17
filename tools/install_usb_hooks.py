"""Install additive, notification-only user hooks. Existing handlers are preserved."""
from __future__ import annotations
import argparse
import copy
import json
from pathlib import Path
import shutil
import sys
import time

from agentping_usb_notifications import state_dir


def merge_hooks(existing: dict, additions: dict) -> dict:
    result = copy.deepcopy(existing)
    hooks = result.setdefault("hooks", {})
    for event, handlers in additions.items():
        current = hooks.setdefault(event, [])
        # Replace only this installer's handlers when its runtime/path changes.
        kept = []
        for item in current:
            if "hooks" in item:
                item["hooks"] = [h for h in item["hooks"] if "agentping_usb_notifications.py" not in json.dumps(h)]
                if item["hooks"]: kept.append(item)
            elif "agentping_usb_notifications.py" not in json.dumps(item):
                kept.append(item)
        hooks[event] = current = kept
        for handler in handlers:
            if handler not in current:
                current.append(handler)
    return result


def configs(python: Path, script: Path) -> dict:
    result = {}
    events = {
        "codex": ["UserPromptSubmit", "PostToolUse", "PermissionRequest", "Stop"],
        "claude": ["PermissionRequest", "Notification", "Stop", "StopFailure"],
        "copilot": ["userPromptSubmitted", "preToolUse", "permissionRequest", "postToolUse", "notification", "awaitingUserInput", "agentStop", "errorOccurred"],
    }
    for provider, names in events.items():
        hooks = {}
        for event in names:
            args = [str(script), "hook", "--provider", provider, "--event", event]
            if provider == "codex":
                # Use a Windows-specific override; no hook input is interpolated.
                command = ' '.join('"' + str(x).replace('"', '\\"') + '"' for x in [python.as_posix(), script.as_posix(), "hook", "--provider", provider, "--event", event])
                handler = {"type": "command", "command": command, "commandWindows": "& " + command, "timeout": 3}
                hooks[event] = [{"hooks": [handler]}]
            elif provider == "claude":
                hooks[event] = [{"hooks": [{"type": "command", "command": str(python), "args": args, "timeout": 3}]}]
            else:
                hooks[event] = [{"type": "command", "exec": str(python), "args": args, "timeoutSec": 3}]
        result[provider] = {"hooks": hooks}
        if provider == "copilot":
            result[provider]["version"] = 1
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()
    root = state_dir()
    script = root / "bin/agentping_usb_notifications.py"
    generated = configs(Path(sys.executable), script)
    paths = {"codex": Path.home()/".codex/hooks.json", "claude": Path.home()/".claude/settings.json",
             "copilot": Path.home()/".copilot/hooks/agentping-usb.json"}
    updates = {}
    for provider, path in paths.items():
        existing = json.loads(path.read_text(encoding="utf-8-sig")) if path.exists() else {}
        result = merge_hooks(existing, generated[provider]["hooks"])
        if provider == "copilot": result["version"] = 1
        updates[path] = json.dumps(result, indent=2, ensure_ascii=False)+"\n"
        print(f"{provider}: {path}")
    if not args.apply:
        print("Review-only. Pass --apply to install these additive hooks.")
        return
    backup = root / "backups" / str(time.time_ns())
    backup.mkdir(parents=True, exist_ok=True)
    script.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(Path(__file__).with_name(script.name), script)
    shutil.copy2(Path(__file__).with_name("robot_control.py"), script.with_name("robot_control.py"))
    for index, (path, content) in enumerate(updates.items()):
        if path.exists(): shutil.copy2(path, backup / f"{index}-{path.name}")
        path.parent.mkdir(parents=True, exist_ok=True)
        temporary = path.with_name(path.name + ".agentping.tmp")
        temporary.write_text(content, encoding="utf-8")
        temporary.replace(path)
    print("Installed. Existing Codex notify and other hooks preserved.")
    print("Codex requires review/trust of the new hooks via /hooks. Restart CLI sessions to load hooks.")
    print(f"Backups: {backup}")


if __name__ == "__main__":
    main()
