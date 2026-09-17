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
from robot_control import MOTION_BONES, UsbDisconnectedError, atomic_json, command, motion_steps, process_requests, request


class RobotControlTests(unittest.TestCase):
    def test_disconnected_command_reports_not_sent_and_is_not_replayed(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            path = root / "commands" / ("a" * 32 + ".json")
            atomic_json(path, {"action": "reset", "args": {}, "expires": time.time() + 5})
            def disconnected(*args):
                raise UsbDisconnectedError()
            process_requests(root, disconnected)
            result = json.loads((root / "results" / path.name).read_text())
            self.assertFalse(result["ok"])
            self.assertIn("command not sent", result["error"])
            process_requests(root, lambda *args: self.fail("Must not replay"))

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


class MotionStepsTests(unittest.TestCase):
    @staticmethod
    def clip(frames=3):
        return {"frames": [[0, 0, 0, 32767] * MOTION_BONES for _ in range(frames)], "rate": 12}

    def test_upload_sequence_declares_header_then_keys_then_play(self):
        steps = motion_steps(self.clip())
        wires = [w.decode() if isinstance(w, bytes) else w for w, _ in steps]
        self.assertEqual(wires[0].strip(), "idle")
        self.assertTrue(wires[1].startswith("motion 3 12 "))
        self.assertEqual([w.split(" ", 2)[0:2] for w in wires[2:5]],
                         [["key", "0"], ["key", "1"], ["key", "2"]])
        self.assertEqual(wires[5].strip(), "play")
        # Every bone contributes four signed 16-bit components.
        self.assertEqual(len(wires[2].split(" ", 2)[2].strip()), MOTION_BONES * 8 * 2)

    def test_checksum_follows_the_uploaded_bytes(self):
        first = motion_steps(self.clip())[1][0]
        moved = self.clip()
        moved["frames"][1][2] = 16384
        self.assertNotEqual(first, motion_steps(moved)[1][0])

    def test_rejects_clips_the_firmware_cannot_accept(self):
        for bad in ({"frames": [], "rate": 12},
                    {"frames": self.clip()["frames"], "rate": 0},
                    {"frames": [[0, 0, 0, 32767] * (MOTION_BONES - 1)] * 3, "rate": 12}):
            with self.assertRaises((ValueError, TypeError)):
                motion_steps(bad)


if __name__ == "__main__": unittest.main()
