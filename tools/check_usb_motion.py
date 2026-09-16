"""Hardware checks for bounded motion uploads; restore the flash wave afterward."""
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
    args=parser.parse_args()
    clip=json.loads(args.clip.read_text())
    frames=[struct.pack('<108h',*row) for row in clip['frames']]
    usb=serial.Serial(baudrate=115200,timeout=.2,write_timeout=5)
    usb.dtr=False;usb.rts=False;usb.port=args.port
    with usb:
        def expect(command,token):
            usb.write((command+'\n').encode())
            until=time.monotonic()+5
            while time.monotonic()<until:
                line=usb.readline().decode(errors='replace').strip()
                if token in line:
                    print(line,flush=True)
                    return line
            raise AssertionError(f'No {token} after {command[:40]}')
        try:
            expect('motion 1000 24 0','MOTION ERROR header')
            expect('motion 2 24 0','MOTION READY 2')
            expect('play','MOTION ERROR incomplete')
            expect('key 1 '+frames[0].hex(),'MOTION ERROR key')
            expect('key 0 '+'00'*216,'MOTION ERROR quaternion')
            expect('key 0 '+'zz'*216,'MOTION ERROR hex')
            expect(f'motion {len(frames)} 24 0',f'MOTION READY {len(frames)}')
            for i,frame in enumerate(frames):expect(f'key {i} '+frame.hex(),f'MOTION KEY {i}')
            expect('play','MOTION ERROR checksum')
        finally:
            expect('wave','MOTION STORED')
        expect('motionstatus','source=flash playing=1')
    print('PASS: invalid counts, incomplete clips, index order, quaternion norms, hex, checksum, flash restoration')


if __name__=='__main__':main()
