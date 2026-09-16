from pathlib import Path
from PIL import Image
import struct,json
root=Path(__file__).resolve().parents[1]
frames=root/'simulator/test-results/firmware-frames'
out=root/'firmware/assets';out.mkdir(exist_ok=True)
data=[];offsets=[];stats=[]
for name in ['idle','wave']:
 paths=sorted(frames.glob(name+'-*.png'));assert paths
 start=len(offsets)
 for path in paths:
  image=Image.open(path).convert('RGB').quantize(colors=64).convert('RGB')
  assert image.size==(240,384)
  pixels=[((r>>3)<<11)|((g>>2)<<5)|(b>>3) for r,g,b in image.getdata()]
  offsets.append(len(data));i=0;decoded=[]
  while i<len(pixels):
   j=i+1
   while j<len(pixels) and pixels[j]==pixels[i] and j-i<65535:j+=1
   data.extend([j-i,pixels[i]]);decoded.extend([pixels[i]]*(j-i));i=j
  assert decoded==pixels, str(path)
 stats.append({'name':name,'first':start,'count':len(paths)})
offsets.append(len(data))
text='#pragma once\n#include <cstdint>\nnamespace pal_assets {\ninline constexpr unsigned width=240,height=384;\n'
text+='inline constexpr std::uint32_t offsets[]={'+','.join(map(str,offsets))+'};\n'
text+='inline constexpr std::uint16_t runs[]={\n'
text+='\n'.join(','.join(map(str,data[i:i+24]))+',' for i in range(0,len(data),24))+'\n};\n}\n'
(out/'pal_frames.h').write_text(text)
(out/'manifest.json').write_text(json.dumps({'width':240,'height':384,'encoded_bytes':len(data)*2,'decoded_frame_bytes':240*384*2,'clips':stats},indent=2))
print('Encoded bytes:',len(data)*2,'Frame RAM:',240*384*2)
