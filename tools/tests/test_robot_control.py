import json
import base64
from pathlib import Path
import sys
import tempfile
import threading
import time
import unittest
from unittest.mock import patch
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from robot_control import atomic_json, command, process_requests, request, suppressed, write_suppression


class RobotControlTests(unittest.TestCase):
    def test_icon_roundtrip_color_and_line_bounds(self):
        pixels = bytes(range(256)) + bytes(range(32))
        wire, ack = command("icon", {"data_hex": pixels.hex(), "color": "#ff8800", "message": "Custom icon"})
        upload, show = wire.decode().splitlines()
        self.assertEqual(ack, b"PAL ICON OK")
        self.assertEqual(upload.split()[1], "ff8800")
        self.assertEqual(base64.b64decode(upload.split()[2], validate=True), pixels)
        self.assertEqual(show, "iconshow Custom icon")
        self.assertLess(len(upload), 512)
        self.assertLess(len(show), 512)

    def test_error_icon_preserves_uploaded_color_but_selects_error_rendering(self):
        wire, ack = command("icon", {"data_hex": "ff" * 288, "color": "#00ff00", "state": "error", "message": "Oops"})
        self.assertTrue(wire.startswith(b"icondata 00ff00 "))
        self.assertTrue(wire.endswith(b"\niconerror Oops\n"))
        self.assertEqual(ack, b"PAL ICON OK")
        with self.assertRaises(ValueError):
            command("icon", {"data_hex": "ff" * 288, "state": "idle"})

    def test_icon_rejects_bad_sizes_colors_and_message_injection(self):
        valid = {"data_hex": "00" * 288, "color": "#abcdef"}
        for field, value in (("data_hex", "ff" * 287), ("data_hex", "ff" * 289),
                             ("data_hex", "gg" * 288), ("color", "#abc"),
                             ("color", "ffffff\nboot"), ("message", "hi\nidle")):
            with self.assertRaises(ValueError): command("icon", {**valid, field: value})

    def test_commands_and_joint_limits(self):
        self.assertEqual(command("state", {"state": "attention", "message": "Hello!"})[0], b"attention Hello!\n")
        self.assertEqual(command("joint", {"joint": "head", "y": 30})[0], b"joint head 0.000 30.000 0.000 600\n")
        for args in ({"joint": "head", "x": float("nan")}, {"joint": "head", "z": 91},
                     {"joint": "head", "duration_ms": True}, {"joint": "motor"}):
            with self.assertRaises(ValueError): command("joint", args)

    def test_no_wire_injection_or_silent_truncation(self):
        for message in ("hello\nboot", "hello\rwave", "x" * 193, "thinking\u2026", "\x00"):
            with self.assertRaises(ValueError): command("state", {"state": "attention", "message": message})
        with self.assertRaises(ValueError): command("state", {"state": "idle", "message": "hidden"})
        with self.assertRaises(ValueError): command("status", {"raw": "boot"})

    def test_expired_and_failed_requests_are_not_replayed(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            path = root / "commands" / ("a" * 32 + ".json")
            atomic_json(path, {"action": "reset", "args": {}, "expires": time.time() - 1})
            calls = []
            process_requests(root, lambda *a: calls.append(a))
            self.assertFalse(calls)
            self.assertFalse(json.loads((root / "results" / path.name).read_text())["ok"])
            atomic_json(path, {"action": "reset", "args": {}, "expires": time.time() + 5})
            def fail(*args):
                calls.append(args)
                raise OSError("private detail")
            process_requests(root, fail)
            process_requests(root, fail)
            self.assertEqual(len(calls), 1)
            self.assertNotIn("private detail", (root / "results" / path.name).read_text())

    def test_concurrent_clients_receive_their_own_acknowledgments(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            stop = threading.Event()
            def worker():
                while not stop.is_set():
                    process_requests(root, lambda wire, ack: wire.decode().strip())
                    time.sleep(.01)
            thread = threading.Thread(target=worker); thread.start()
            try:
                with ThreadPoolExecutor(max_workers=4) as pool:
                    results = list(pool.map(lambda i: request(root, "state", {"state": "attention", "message": str(i)}), range(4)))
                self.assertEqual([r["reply"] for r in results], [f"attention {i}" for i in range(4)])
                self.assertFalse(list((root / "commands").glob("*.json")))
            finally:
                stop.set(); thread.join()

    def test_worker_stop_does_not_require_usb(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            atomic_json(root / "commands" / ("a" * 32 + ".json"),
                        {"action": "stop", "args": {}, "expires": time.time() + 5})
            self.assertTrue(process_requests(root, lambda *a: self.fail("Stop must not open USB")))

    def test_display_affecting_commands_suppress_agent_notifications_but_status_does_not(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.assertFalse(suppressed(root))
            atomic_json(root / "commands" / ("a" * 32 + ".json"),
                        {"action": "status", "args": {}, "expires": time.time() + 5})
            process_requests(root, lambda *a: "VIEW state=idle")
            self.assertFalse(suppressed(root))
            atomic_json(root / "commands" / ("b" * 32 + ".json"),
                        {"action": "joint", "args": {"joint": "head", "y": 20}, "expires": time.time() + 5})
            process_requests(root, lambda *a: "PAL JOINT head OK")
            self.assertTrue(suppressed(root))
            self.assertFalse(suppressed(root, time.time() + 10))

    def test_write_suppression_and_suppressed_read_the_same_window(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            write_suppression(root, time.time() + 2)
            self.assertTrue(suppressed(root))
            self.assertFalse(suppressed(root, time.time() + 3))
            self.assertFalse(suppressed(Path(directory) / "missing"))

    def test_later_commands_expire_while_an_earlier_send_is_slow(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for key in ("a", "b"):
                atomic_json(root / "commands" / (key * 32 + ".json"),
                            {"action": "reset", "args": {}, "expires": 105})
            clock = [100]
            calls = []
            def send(*args):
                calls.append(args); clock[0] = 107
                return "OK"
            with patch("robot_control.time.time", side_effect=lambda: clock[0]):
                process_requests(root, send)
            self.assertEqual(len(calls), 1)


if __name__ == "__main__": unittest.main()
