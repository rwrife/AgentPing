from pathlib import Path
from PIL import Image
import json,hashlib
root=Path(__file__).resolve().parents[1]
frames=root/'simulator/test-results/firmware-linked'
out=root/'firmware/assets';out.mkdir(exist_ok=True)
data=[];offsets=[];stats=[];anchor=None
for clip in json.loads((frames/'clips.json').read_text()):
 start=len(offsets); hashes=[]
 for frame in range(clip['count']):
  path=frames/f"{clip['name']}-{frame:02d}.png"
  image=Image.open(path).convert('RGB').quantize(colors=64).convert('RGB')
  assert image.size==(240,384)
  pixels=[((r>>3)<<11)|((g>>2)<<5)|(b>>3) for r,g,b in image.getdata()]
  hashes.append(hashlib.sha256(bytes(v for pixel in pixels for v in (pixel&255,pixel>>8))).hexdigest())
  offsets.append(len(data));i=0;decoded=[]
  while i<len(pixels):
   j=i+1
   while j<len(pixels) and pixels[j]==pixels[i] and j-i<65535:j+=1
   data.extend([j-i,pixels[i]]);decoded.extend([pixels[i]]*(j-i));i=j
  assert decoded==pixels,str(path)
 if anchor is None:anchor=hashes[-1]
 assert hashes[-1]==anchor, f"{clip['name']}: exit differs from anchor"
 if clip['mode']!='once':assert hashes[0]==anchor,f"{clip['name']}: entry differs from anchor"
 stats.append({'name':clip['name'],'first':start,'count':clip['count'],'period_ms':round(clip['seconds']*1000/(clip['count']-1)),'mode':clip['mode'],'entry_sha256':hashes[0],'exit_sha256':hashes[-1]})
offsets.append(len(data))
text='#pragma once\n#include <cstdint>\nnamespace pal_assets {\ninline constexpr unsigned width=240,height=384;\n'
text+='struct Clip { unsigned first,count,period; bool pingpong; };\ninline constexpr Clip clips[]={\n'
text+='\n'.join('{%d,%d,%d,%s},'%(c['first'],c['count'],c['period_ms'],'true' if c['mode']=='pingpong' else 'false') for c in stats)+'\n};\n'
text+='inline constexpr std::uint32_t offsets[]={'+','.join(map(str,offsets))+'};\n'
text+='inline constexpr std::uint16_t runs[]={\n'
text+='\n'.join(','.join(map(str,data[i:i+24]))+',' for i in range(0,len(data),24))+'\n};\n}\n'
(out/'pal_frames.h').write_text(text)
(out/'manifest.json').write_text(json.dumps({'width':240,'height':384,'encoded_bytes':len(data)*2,'decoded_frame_bytes':240*384*2,'anchor_sha256':anchor,'clips':stats},indent=2))
print('Encoded bytes:',len(data)*2,'Frame RAM:',240*384*2,'All clip boundaries match anchor')
