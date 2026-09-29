"""Prepare one native-compatible palette shared by all walking directions."""
from pathlib import Path
import json
import numpy as np
from PIL import Image
root=Path(__file__).resolve().parent/'install-frames'
records=[m for m in json.loads((root/'frames.json').read_text()) if m['Action']==0]
pixels=[]
for m in records:
    data=np.array(Image.open(root/m['File']).convert('RGBA'))
    pixels.append(data[data[:,:,3]>=128,:3])
samples=np.concatenate(pixels)
palette=Image.fromarray(samples.reshape((-1,1,3))).quantize(colors=240,method=Image.Quantize.MEDIANCUT)
values=palette.getpalette()
values=[(v>>3)<<3 for v in values]
for i in range(0,len(values),3):
    if values[i:i+3]==[0,0,0]: values[i:i+3]=[8,8,8]
reference=Image.new('P',(1,1));reference.putpalette(values)
for m in records:
    p=Image.open(root/m['File']).convert('RGBA')
    q=p.convert('RGB').quantize(palette=reference,dither=Image.Dither.NONE).convert('RGBA')
    q.putalpha(p.getchannel('A'));q.save(root/m['File'])
print('50 staged walking frames converted to shared 240-color RGB555 palette; preview assets and idle unchanged.')
