"""Profile the experimental USB renderer and capture its actual framebuffer."""
import argparse
import json
import time
from pathlib import Path

import serial
from PIL import Image


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--port', default='COM5')
    parser.add_argument('--seconds', type=float, default=12)
    parser.add_argument('--modes', nargs='+', default=['low', 'medium', 'high', 'max', 'native'])
    parser.add_argument('--output', type=Path, default=Path('test-results/live3d-device'))
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    connection = serial.Serial(baudrate=115200, timeout=0.3, write_timeout=5)
    connection.dtr = False
    connection.rts = False
    connection.port = args.port
    records = []
    with connection as usb:
        def line():
            message = usb.readline().decode('utf-8', errors='replace').strip()
            if message:
                print(message, flush=True)
                records.append(message)
            return message

        for mode in args.modes:
            usb.write((mode + '\n').encode())
            until = time.monotonic() + args.seconds
            while time.monotonic() < until:
                line()
        usb.write(b'pause\n')
        until = time.monotonic() + 5
        while time.monotonic() < until:
            if 'paused=1' in line():
                break
        else:
            raise RuntimeError('Renderer did not acknowledge pause')
        usb.write(b'frame\n')
        until = time.monotonic() + 5
        while time.monotonic() < until:
            message = line()
            if message.startswith('LIVE3D FRAME '):
                _, _, w, h = message.split()
                w, h = int(w), int(h)
                break
        else:
            raise RuntimeError('No framebuffer header')
        data = bytearray()
        until = time.monotonic() + 20
        while len(data) < w * h * 2 and time.monotonic() < until:
            data.extend(usb.read(w * h * 2 - len(data)))
        if len(data) != w * h * 2:
            raise RuntimeError(f'Incomplete framebuffer: {len(data)} bytes')
        colors = []
        for i in range(0, len(data), 2):
            pixel = data[i] | data[i + 1] << 8
            colors.append(((pixel >> 11) * 255 // 31, ((pixel >> 5) & 63) * 255 // 63, (pixel & 31) * 255 // 31))
        image = Image.new('RGB', (w, h))
        image.putdata(colors)
        image.save(args.output / 'frame.png')
        usb.write(b'resume\n')
        until = time.monotonic() + 2
        while time.monotonic() < until:
            line()
    (args.output / 'benchmark.json').write_text(json.dumps(records, indent=2))


if __name__ == '__main__':
    main()
