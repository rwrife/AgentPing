"""Upload a compact converted animation into the live renderer's RAM over USB."""
import argparse
import json
import struct
import time
from pathlib import Path
import serial


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('clip',type=Path)
    parser.add_argument('--port',default='COM5')
    parser.add_argument('--observe',type=float,default=15)
    args=parser.parse_args()
    clip=json.loads(args.clip.read_text())
    assert clip['bones']==27 and 2<=len(clip['frames'])<=64
    assert all(len(frame)==108 for frame in clip['frames'])
    frames=[struct.pack('<108h',*frame) for frame in clip['frames']]
    checksum=2166136261
    for byte in b''.join(frames):
        checksum=((checksum^byte)*16777619)&0xffffffff
    usb=serial.Serial(baudrate=115200,timeout=.2,write_timeout=5)
    usb.dtr=False;usb.rts=False;usb.port=args.port
    with usb:
        def command(text,expected):
            usb.write((text+'\n').encode())
            until=time.monotonic()+5
            while time.monotonic()<until:
                line=usb.readline().decode(errors='replace').strip()
                if line: print(line,flush=True)
                if 'MOTION ERROR' in line:raise RuntimeError(line)
                if expected in line:return
            raise TimeoutError(expected)
        command(f'motion {len(frames)} {clip["rate"]} {checksum:08x}',f'MOTION READY {len(frames)}')
        for index,frame in enumerate(frames):command(f'key {index} {frame.hex()}',f'MOTION KEY {index}')
        command('play','MOTION PLAY')
        until=time.monotonic()+args.observe
        while time.monotonic()<until:
            line=usb.readline().decode(errors='replace').strip()
            if line:print(line,flush=True)


if __name__=='__main__':main()
