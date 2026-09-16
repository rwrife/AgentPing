"""Hardware playback check. Stop the USB notification worker before running."""
import argparse
import re
import time
import serial
from esptool.reset import HardReset

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--port',default='COM5')
args=parser.parse_args()
port=serial.Serial(baudrate=115200,timeout=.05,write_timeout=1)
port.dtr=False;port.rts=False;port.port=args.port
pattern=re.compile(rb'PAL OK clip=([\w-]+) frame=(\d+) direction=(-?\d+) requested=([\w-]+)')

def command(value, expected=b'PAL OK'):
    port.write(value.encode()+b'\n')
    end=time.monotonic()+2;data=b''
    while time.monotonic()<end:
        data+=port.read(256)
        if expected in data and b'\n' in data[data.index(expected):]:
            return data
    raise AssertionError(f'No acknowledgment for {value}')

def sample(seconds):
    rows=[];end=time.monotonic()+seconds
    while time.monotonic()<end:
        match=pattern.search(command('status'))
        assert match, 'Missing playback status'
        rows.append((match[1].decode(),int(match[2]),int(match[3])))
        time.sleep(.05)
    return rows

with port:
    HardReset(port,uses_usb=True)()
    rows=sample(9)
    clips=[row[0] for row in rows]
    assert 'boot' in clips and 'listening' in clips,clips
    assert all(c=='listening' for c in clips[clips.index('listening'):]),'Boot replayed'
    listening=[r for r in rows if r[0]=='listening']
    assert {r[2] for r in listening}=={-1,1},'Listening did not reverse'
    assert all(0<=r[1]<16 for r in listening)
    print('PASS: boot once, then listening forward/backward',flush=True)
    command('host',b'PAL HOST OK')
    rows=sample(3)
    assert rows[-1][0]=='idle',rows[-1]
    print('PASS: host connection enters idle',flush=True)
    command('notify 0123456789abcdef claude attention',b'PAL EVENT')
    rows=sample(3)
    assert rows[-1][0]=='wave-claude',rows[-1]
    print('PASS: notification enters provider wave',flush=True)
    rows=sample(37)
    assert rows[-1][0]=='listening',rows[-1]
    assert all(r[0]!='boot' for r in rows),'Boot replayed after notification'
    print('PASS: expired notification returns to listening without replaying boot',flush=True)
