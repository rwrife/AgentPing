import json
import shutil
import subprocess
from pathlib import Path
import sys
import tempfile
import unittest
from types import SimpleNamespace
from unittest.mock import patch, Mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from agentping_usb_notifications import DEVICE_PID, DEVICE_VID, discover_port, enqueue, normalize, wire, ThinkingCooldown, PortDiscoveryError, run
from install_usb_hooks import merge_hooks, configs


def port_info(device, vid=DEVICE_VID, pid=DEVICE_PID, serial_number=None):
    return SimpleNamespace(device=device, vid=vid, pid=pid, serial_number=serial_number)


class UsbNotificationsTests(unittest.TestCase):
    def test_worker_recovers_from_unplug_and_close_failure_on_new_port(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            unplugged = Mock()
            unplugged.read.side_effect = OSError("device removed")
            unplugged.close.side_effect = OSError("handle removed")
            reconnected = Mock()
            reconnected.read.return_value = b"PAL HOST OK\n"
            states = []
            def requests(state_root, send):
                states.append(json.loads((root / "status.json").read_text())["connected"]
                              if (root / "status.json").exists() else None)
                return len(states) == 3
            with patch.dict(sys.modules, {"msvcrt": Mock()}), \
                 patch("agentping_usb_notifications.discover_port",
                       side_effect=[PortDiscoveryError("No COM ports"), "COM7", "COM9"]) as discover, \
                 patch("serial.Serial", side_effect=[unplugged, reconnected]), \
                 patch("robot_control.process_requests", side_effect=requests), \
                 patch("agentping_usb_notifications.time.sleep"):
                run(root, None)
            self.assertEqual(3, discover.call_count)
            self.assertEqual("COM7", unplugged.port)
            self.assertEqual("COM9", reconnected.port)
            reconnected.write.assert_called_once_with(b"host\n")
            unplugged.close.assert_called_once()
            reconnected.close.assert_called_once()
            self.assertEqual([False, False], states[:2])
            self.assertFalse(json.loads((root / "status.json").read_text())["running"])

    def test_copilot_pretool_hook_does_not_import_worker_dependencies(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            script = root / "agentping_usb_notifications.py"
            shutil.copy2(Path(__file__).resolve().parents[1] / script.name, script)
            (root / "robot_control.py").write_text("raise ImportError('worker unavailable')\n")
            for tool in ("powershell", "view", "ask_user"):
                with self.subTest(tool=tool):
                    state = root / tool
                    result = subprocess.run(
                        [sys.executable, str(script), "--state-dir", str(state),
                         "hook", "--provider", "copilot", "--event", "preToolUse"],
                        input=json.dumps({"toolName": tool}), text=True,
                        capture_output=True, timeout=3)
                    self.assertEqual(0, result.returncode, result.stderr)
                    self.assertEqual("", result.stdout)
                    self.assertEqual("", result.stderr)
                    queued = list((state / "pending").glob("*.json"))
                    self.assertEqual(1 if tool == "ask_user" else 0, len(queued))
                    if queued:
                        self.assertEqual("attention", json.loads(queued[0].read_text())["kind"])

    def test_discover_port_matches_device_vid_pid_and_ignores_others(self):
        ports = [port_info("COM3", vid=0x1234, pid=0x1), port_info("COM7")]
        with patch("serial.tools.list_ports.comports", return_value=ports):
            self.assertEqual("COM7", discover_port())

    def test_discover_port_disambiguates_by_serial_number(self):
        ports = [port_info("COM7", serial_number="AAA"), port_info("COM9", serial_number="BBB")]
        with patch("serial.tools.list_ports.comports", return_value=ports):
            self.assertEqual("COM9", discover_port("BBB"))
            with self.assertRaises(OSError):
                discover_port()
            with self.assertRaises(OSError):
                discover_port("CCC")

    def test_discover_port_raises_when_none_found(self):
        with patch("serial.tools.list_ports.comports", return_value=[]):
            with self.assertRaisesRegex(OSError, "No COM ports detected"):
                discover_port()

    def test_discover_port_reports_unmatched_ports_without_selecting_one(self):
        with patch("serial.tools.list_ports.comports", return_value=[port_info("COM3", vid=0x1234)]):
            with self.assertRaisesRegex(OSError, "No matching Pixel Pal.*COM3"):
                discover_port()

    def test_copilot_question_requests_attention_before_tool_runs(self):
        hooks = configs(Path("python.exe"), Path("hook.py"))["copilot"]["hooks"]
        self.assertIn("preToolUse", hooks)
        self.assertEqual("attention", normalize("copilot", {"toolName": "ask_user"}, "preToolUse"))
        self.assertEqual("attention", normalize("copilot", {"tool_name": "AskUserQuestion"}, "PreToolUse"))
        for tool in ("bash", "view", "edit", "web_search", None):
            self.assertIsNone(normalize("copilot", {"toolName": tool}, "preToolUse"))
        self.assertIsNone(normalize("claude", {"toolName": "ask_user"}, "preToolUse"))

    def test_codex_lifecycle_without_windows_notifications(self):
        hooks = configs(Path("python.exe"), Path("hook.py"))["codex"]["hooks"]
        for event, expected in (("UserPromptSubmit", "thinking"),
                                ("PostToolUse", "thinking"),
                                ("PermissionRequest", "attention"),
                                ("Stop", "completed")):
            with self.subTest(event=event):
                self.assertIn(event, hooks)
                self.assertEqual(expected, normalize("codex", {"hook_event_name": event}))
                self.assertEqual(expected, normalize("codex", {}, event))
        for event in ("SubagentStop", "SessionEnd", "Interrupt"):
            self.assertIsNone(normalize("codex", {"hook_event_name": event}))

    def test_thinking_cooldown_drops_repeats_without_sliding_deadline(self):
        gate = ThinkingCooldown()
        self.assertTrue(gate.allows("thinking", 0))
        self.assertTrue(gate.allows("thinking", 1))  # No ACK yet.
        gate.delivered("thinking", 1)
        for now in (2, 30, 119, 120.999):
            self.assertFalse(gate.allows("thinking", now))
            for kind in ("attention", "error", "completed"):
                self.assertTrue(gate.allows(kind, now))
                gate.delivered(kind, now)
        self.assertTrue(gate.allows("thinking", 121))
        gate.delivered("thinking", 121)
        self.assertFalse(gate.allows("thinking", 240))
        self.assertTrue(gate.allows("thinking", 241))

    def test_copilot_work_does_not_request_attention(self):
        hooks = configs(Path("python.exe"), Path("hook.py"))["copilot"]["hooks"]
        for event in ("userPromptSubmitted", "permissionRequest", "postToolUse"):
            self.assertIn(event, hooks)
            self.assertEqual("thinking", normalize("copilot", {}, event))
        self.assertEqual("attention", normalize("copilot", {}, "awaitingUserInput"))
        self.assertEqual("attention", normalize("copilot", {"notification_type":"permission_prompt"}, "notification"))
        self.assertEqual("attention", normalize("claude", {}, "PermissionRequest"))

    def test_maps_real_hook_shapes_and_explicit_copilot_event(self):
        self.assertEqual("completed", normalize("codex", {"type":"agent-turn-complete"}))
        self.assertEqual("attention", normalize("codex", {"hook_event_name":"PermissionRequest"}))
        self.assertEqual("attention", normalize("claude", {"hook_event_name":"Notification", "notification_type":"idle_prompt"}))
        self.assertEqual("attention", normalize("copilot", {"hookEventName":"awaitingUserInput"}))
        self.assertEqual("thinking", normalize("copilot", {"notification_type":"agent_thinking"}, "notification"))
        self.assertEqual("completed", normalize("copilot", {"sessionId":"a", "stopReason":"end_turn"}, "agentStop"))
        self.assertEqual("attention", normalize("copilot", {"notification_type":"permission_prompt"}, "notification"))
        self.assertEqual("error", normalize("copilot", {}, "errorOccurred"))
        self.assertIsNone(normalize("claude", {"hook_event_name":"Notification", "notification_type":"auth_success"}))
        self.assertIsNone(normalize("codex", {"hook_event_name":"PreToolUse"}))

    def test_copilot_planning_with_background_work_only_completes_on_main_stop(self):
        events = [("userPromptSubmitted", {}),
                  ("notification", {"notification_type": "agent_completed"}),
                  ("notification", {"notification_type": "agent_idle"}),
                  ("notification", {"notification_type": "shell_completed"}),
                  ("notification", {"notification_type": "shell_detached_completed"}),
                  ("subagentStop", {"stopReason": "end_turn"}),
                  ("postToolUse", {}),
                  ("agentThinking", {}),
                  ("agentStop", {"stopReason": "end_turn"})]
        results = [normalize("copilot", payload, event) for event, payload in events]
        self.assertEqual(["thinking", None, None, None, None, None,
                          "thinking", "thinking", "completed"], results)

    def test_copilot_completion_requires_explicit_end_turn(self):
        for event, key in (("agentStop", "stopReason"), ("Stop", "stop_reason")):
            for reason in (None, "", "cancelled", "error", "tool_use", "max_tokens"):
                with self.subTest(event=event, reason=reason):
                    self.assertIsNone(normalize("copilot", {key: reason}, event))
            self.assertIsNone(normalize("copilot", {}, event))
            self.assertEqual("completed", normalize("copilot", {key: "end_turn"}, event))
        for event in ("sessionEnd", "SubagentStop", "agent-turn-complete"):
            self.assertIsNone(normalize("copilot", {"stopReason": "end_turn"}, event))

    def test_queue_never_stores_provider_payload_and_wire_is_bounded(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            kind = normalize("claude", {"hook_event_name":"Stop", "message":"secret", "tool_input":{"token":"secret"}})
            event_id = enqueue(root,"claude",kind,now=100)
            event = json.loads(next((root/"pending").glob("*.json")).read_text())
            self.assertNotIn("secret", json.dumps(event))
            self.assertEqual(f"notify {event_id} claude completed\n".encode(),wire(event,101))
            self.assertIsNone(wire(event,161))
            self.assertIsNone(wire(event,99))
            event["provider"]="codex\nwave"
            with self.assertRaises(ValueError): wire(event,101)

    def test_merge_preserves_existing_and_is_idempotent(self):
        existing = {"env":{"keep":"yes"},"hooks":{"Stop":[{"hooks":[{"command":"existing"}]}]}}
        additions = configs(Path("C:/Python/python.exe"), Path("C:/AgentPing/hook.py"))["claude"]["hooks"]
        merged = merge_hooks(existing,additions)
        self.assertEqual(existing["hooks"]["Stop"][0], merged["hooks"]["Stop"][0])
        self.assertEqual(merged, merge_hooks(merged,additions))
        self.assertEqual("yes", merged["env"]["keep"])
        self.assertEqual(1,len(existing["hooks"]["Stop"]))

    def test_invalid_payload_and_unknown_kind(self):
        with self.assertRaises(ValueError): normalize("codex",[])
        with self.assertRaises(ValueError): normalize("unknown",{})
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(ValueError): enqueue(Path(directory),"codex","approve")


if __name__ == "__main__": unittest.main()
