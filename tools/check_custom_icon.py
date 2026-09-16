"""Validate custom icon publication and malformed-upload handling on COM5.

Stop the desktop worker before running this hardware check.
"""
import json
from pathlib import Path
import time
import serial
from robot_control import command

usb = serial.Serial(baudrate=115200, timeout=.1, write_timeout=3)
usb.dtr = False; usb.rts = False; usb.port = "COM5"
lines = []


def send(wire, expected):
    usb.reset_input_buffer()
    usb.write(wire)
    until = time.monotonic() + 3
    while time.monotonic() < until:
        line = usb.readline().decode(errors="replace").strip()
        lines.append(line)
        if line.startswith(expected):
            print(line, flush=True)
            return line
    raise AssertionError(f"Missing acknowledgment: {expected}")


with usb:
    data = (Path(__file__).resolve().parents[1] / "assets/icons/heart-48.bin").read_bytes().hex()
    wire, _ = command("icon", {"data_hex": data, "color": "#ff8800", "message": "Custom icon test"})
    upload = wire.splitlines()[0]
    send(wire, "PAL ICON OK")
    for malformed in (b"icondata bad", upload[:9] + b"zzzzzz" + upload[15:], upload[:-1] + b"!"):
        send(malformed + b"\niconshow Bad upload\n", "PAL ERROR")
        status = send(b"viewstatus\n", "VIEW ")
        assert "icon=1" in status and "color=fc40" in status, "Malformed upload changed the active icon"
    send(upload + b"\n", "PAL ICON READY")
    time.sleep(5.2)
    send(b"iconshow Expired upload\n", "PAL ERROR")
    status = send(b"viewstatus\n", "VIEW ")
    assert "icon=1" in status and "color=fc40" in status
    send(b"idle\n", "STARTUP ENTER state=idle")
    assert "icon=0" in send(b"viewstatus\n", "VIEW ")
output = Path("test-results/custom-icon")
output.mkdir(parents=True, exist_ok=True)
(output / "validation.json").write_text(json.dumps(lines, indent=2))
print("PASS: custom bitmap, RGB565 color, rejected malformed/expired uploads and idle reset")
