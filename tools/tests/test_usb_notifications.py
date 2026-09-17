import json
from pathlib import Path
import sys
import tempfile
import unittest
from types import SimpleNamespace
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from agentping_usb_notifications import DEVICE_PID, DEVICE_VID, discover_port, enqueue, normalize, wire, ThinkingCooldown
from install_usb_hooks import merge_hooks, configs


def port_info(device, vid=DEVICE_VID, pid=DEVICE_PID, serial_number=None):
    return SimpleNamespace(device=device, vid=vid, pid=pid, serial_number=serial_number)


class UsbNotificationsTests(unittest.TestCase):
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
            with self.assertRaises(OSError):
                discover_port()

    def test_copilot_question_requests_attention_before_tool_runs(self):
        hooks = configs(Path("python.exe"), Path("hook.py"))["copilot"]["hooks"]
        self.assertIn("preToolUse", hooks)
        self.assertEqual("attention", normalize("copilot", {"toolName": "ask_user"}, "preToolUse"))
        self.assertEqual("attention", normalize("copilot", {"tool_name": "AskUserQuestion"}, "PreToolUse"))
        for tool in ("bash", "view", "edit", "web_search", None):
            self.assertIsNone(normalize("copilot", {"toolName": tool}, "preToolUse"))
        self.assertIsNone(normalize("claude", {"toolName": "ask_user"}, "preToolUse"))

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
        self.assertEqual("completed", normalize("copilot", {"sessionId":"a"}, "agentStop"))
        self.assertEqual("attention", normalize("copilot", {"notification_type":"permission_prompt"}, "notification"))
        self.assertEqual("error", normalize("copilot", {}, "errorOccurred"))
        self.assertIsNone(normalize("claude", {"hook_event_name":"Notification", "notification_type":"auth_success"}))
        self.assertIsNone(normalize("codex", {"hook_event_name":"PreToolUse"}))

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
