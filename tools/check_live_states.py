"""Exercise live thinking/attention/error, camera easing, bubbles and USB notifications."""
import argparse
import json
import re
import time
from pathlib import Path
import serial

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--port',default='COM5')
args=parser.parse_args()
event_id=f'{time.time_ns() & ((1 << 64)-1):016x}'
output=Path('test-results/live-states')
output.mkdir(parents=True,exist_ok=True)
usb=serial.Serial(baudrate=115200,timeout=.05,write_timeout=3)
usb.dtr=False;usb.rts=False;usb.port=args.port
lines=[];pending=b''
pattern=re.compile(r'VIEW state=(\w+) zoom=([\d.]+) bubble=(\d) thought=(\d) heap=(\d+)')
def collect(seconds):
    global pending
    rows=[];until=time.monotonic()+seconds;query=0
    while time.monotonic()<until:
        if time.monotonic()>=query:
            usb.write(b'viewstatus\n');query=time.monotonic()+.15
        pending+=usb.read(2048)
        while b'\n' in pending:
            raw,pending=pending.split(b'\n',1)
            line=raw.decode(errors='replace').strip();lines.append(line)
            match=pattern.search(line)
            if match:rows.append((match[1],float(match[2]),int(match[3]),int(match[4]),int(match[5])))
            elif 'FPS' in line or 'PAL ' in line:print(line,flush=True)
    return rows
with usb:
    usb.write(b'resume\nidle\n');collect(2)
    usb.write(b'thinking\n');rows=collect(7)
    assert rows[-1][:4]==('thinking',1.0,1,1),rows[-1]
    assert any(0.05<r[1]<.95 for r in rows),'No intermediate camera positions'
    assert all(b[1]>=a[1] for a,b in zip(rows,rows[1:])), 'Camera did not smoothly approach target'
    usb.write(b'attention Codex is waiting for your input.\n');rows=collect(6)
    assert rows[-1][:4]==('attention',1.0,1,0),rows[-1]
    usb.write(f'notify {event_id} claude error\n'.encode());rows=collect(6)
    assert rows[-1][:4]==('error',1.0,1,0),rows[-1]
    assert any(f'PAL EVENT {event_id} OK' in line for line in lines)
    usb.write(f'notify {event_id} claude error\n'.encode())
    rows=collect(25)
    assert rows[-1][0]=='idle' and rows[-1][1]<.1 and rows[-1][2:4]==(0,0),'Expiry did not return to idle or duplicate refreshed expiry'
    assert any(r[0]=='idle' and .05<r[1]<.95 for r in rows),'No eased return after bubble expiry'
    usb.write(b'thinking A brief thought...\n');collect(2)
    usb.write(b'idle\n');rows=collect(3)
    assert rows[-1][:4]==('idle',0.0,0,0),rows[-1]
    assert any(.05<r[1]<.95 for r in rows),'No smooth camera return'
    assert min(r[4] for r in rows)>24000, 'Heap below renderer reserve'
(output/'hardware.json').write_text(json.dumps(lines,indent=2))
print('PASS: states, eased camera in/out, 30-second dismissal, deduplicated USB error and stable heap')
