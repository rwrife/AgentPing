import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from agentping_usb_notifications import enqueue, normalize, wire
from install_usb_hooks import merge_hooks, configs


class UsbNotificationsTests(unittest.TestCase):
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
