"""Send a bounded character-demo command over the ESP32-C6 USB serial port."""
import argparse,time
import serial
parser=argparse.ArgumentParser()
parser.add_argument('command',choices=['idle','listening','wave','pause','resume','status'])
parser.add_argument('--port',default='COM5')
args=parser.parse_args()
connection = serial.Serial(baudrate=115200,timeout=.2,write_timeout=2)
connection.dtr = False
connection.rts = False
connection.port = args.port
with connection as port:
    port.reset_input_buffer()
    port.write((args.command+'\n').encode('ascii'))
    deadline=time.monotonic()+4
    while time.monotonic()<deadline:
        line=port.readline().decode('utf-8',errors='replace').strip()
        if line:print(line)
        if 'PAL OK ' in line:break
    else:raise SystemExit('No PAL acknowledgment received')
