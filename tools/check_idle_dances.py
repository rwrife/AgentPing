"""Hardware check of quiet-idle scheduling, dance completion and interruption."""
import argparse
import json
import re
import time
from pathlib import Path
import serial

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--port',default='COM5')
args=parser.parse_args()
usb=serial.Serial(baudrate=115200,timeout=.05,write_timeout=3)
usb.dtr=False;usb.rts=False;usb.port=args.port
lines=[];pending=b''
pattern=re.compile(r'STARTUP STATUS state=(\w+) frame=([\d.]+) waiting=(\d) blend=(\d)')
def collect(seconds,stop_dance=False):
    global pending
    rows=[];until=time.monotonic()+seconds;query=0
    while time.monotonic()<until:
        if time.monotonic()>=query:
            usb.write(b'startupstatus\n');query=time.monotonic()+.2
        pending+=usb.read(2048)
        while b'\n' in pending:
            raw,pending=pending.split(b'\n',1)
            line=raw.decode(errors='replace').strip();lines.append(line)
            match=pattern.search(line)
            if match:
                rows.append((match[1],float(match[2]),int(match[3]),int(match[4])))
                if stop_dance and rows[-1][0] in ('hiphop','twist','chicken'):return rows
            elif 'FPS' in line or 'STARTUP ENTER' in line: print(line,flush=True)
    return rows
with usb:
    usb.write(b'resume\nidle\n');collect(1)
    for name in ('hiphop','twist','chicken'):
        usb.write(f'dance {name}\n'.encode());rows=collect(1)
        assert rows[-1][0]==name,rows[-1]
        assert any(r[3] for r in rows),'Missing pose transition'
    rows=collect(6)
    assert rows[-1][0]=='idle','Chicken did not finish and return to idle'
    last='chicken'
    for _ in range(5):
        usb.write(b'dance\n');rows=collect(.7)
        assert rows[-1][0]!=last,'Dance repeated immediately'
        last=rows[-1][0]
    usb.write(b'attention Testing dance interruption.\n');rows=collect(1)
    assert rows[-1][0]=='attention','Notification failed to interrupt dance'
    usb.write(b'idle\n');began=time.monotonic()
    rows=collect(29)
    assert all(r[0]=='idle' for r in rows),'Dance started before 30 seconds'
    rows=collect(33,stop_dance=True)
    assert rows[-1][0] in ('hiphop','twist','chicken'),'No automatic dance after 60 seconds'
    assert 29<time.monotonic()-began<63,'Idle delay outside expected range'
    assert rows[-1][0]!=last,'Automatic selection repeated last dance'
    rows=collect(19)
    assert rows[-1][0]=='idle','Automatic dance did not return to idle'
    usb.write(b'viewstatus\n');collect(.5)
output=Path('test-results/idle-dances');output.mkdir(parents=True,exist_ok=True)
(output/'hardware.json').write_text(json.dumps(lines,indent=2))
print('PASS: all dances, pose blends, no consecutive repeat, notification interrupt, 30-60s idle delay and automatic return')
