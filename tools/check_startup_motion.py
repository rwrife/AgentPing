"""Cold-boot hardware check for fall -> stand -> looping idle and host status."""
import argparse
import json
import re
import time
from pathlib import Path
import serial
from esptool.reset import HardReset

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--port',default='COM5')
args=parser.parse_args()
usb=serial.Serial(baudrate=115200,timeout=.05,write_timeout=2)
usb.dtr=False;usb.rts=False;usb.port=args.port
lines=[];rows=[];pending=b''
pattern=re.compile(r'STARTUP STATUS state=(\w+) frame=([\d.]+) waiting=(\d) blend=(\d)')

def collect(seconds):
    global pending
    until=time.monotonic()+seconds;next_query=0
    while time.monotonic()<until:
        if time.monotonic()>=next_query:
            usb.write(b'startupstatus\n');next_query=time.monotonic()+.25
        pending+=usb.read(1024)
        while b'\n' in pending:
            raw,pending=pending.split(b'\n',1);line=raw.decode(errors='replace').strip();lines.append(line)
            match=pattern.search(line)
            if match:rows.append((match[1],float(match[2]),int(match[3]),int(match[4])))
            elif 'STARTUP' in line or 'FPS' in line:print(line,flush=True)

with usb:
    HardReset(usb,uses_usb=True)()
    collect(29)
    order=[]
    for row in rows:
        if not order or order[-1]!=row[0]:order.append(row[0])
    assert order==['fall','stand','idle'],order
    first=re.search(r'STARTUP FIRST max_y=(-?\d+)', '\n'.join(lines))
    assert first and int(first[1])<0,'Character did not start entirely above display'
    assert all(row[2]==0 for row in rows if row[0]!='idle'),'Early connection caption'
    idle=[row for row in rows if row[0]=='idle']
    assert idle and all(row[2]==1 for row in idle),'Idle caption missing'
    assert any(b[1]<a[1] for a,b in zip(idle,idle[1:])),'Idle did not loop'
    assert any(row[3] for row in rows if row[0]=='stand'),'Stand transition not observed'
    grounded=re.findall(r'STARTUP STATUS state=(?:stand|idle).* bottom=(-?\d+)', '\n'.join(lines))
    resolution=re.search(r'LIVE3D RES (\d+)x(\d+) output=(\d+)x(\d+)', '\n'.join(lines))
    assert resolution, 'Missing startup render dimensions'
    height=int(resolution[2]); scale=int(resolution[4])/height
    floor=round((height*.88-25/scale)*scale)
    assert grounded and all(abs(int(y)-floor)<=1 for y in grounded),'Stand-up/idle ground level moved'
    print('PASS: off-screen boot, fall once, stand once, blended transitions, looping idle and waiting caption',flush=True)
    usb.write(b'host\nidle\n');collect(2)
    assert rows[-1][0]=='idle' and rows[-1][2]==0,'Host heartbeat did not clear waiting caption'
    collect(15)
    assert rows[-1][0]=='idle' and rows[-1][2]==0,'Waiting caption returned after the first desktop signal'
    print('PASS: first desktop signal clears waiting status permanently until restart',flush=True)
output=Path('test-results/startup-motion');output.mkdir(parents=True,exist_ok=True)
(output/'hardware.json').write_text(json.dumps({'lines':lines,'samples':rows},indent=2))
