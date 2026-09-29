from pathlib import Path
import json
from PIL import Image, ImageDraw
root=Path(__file__).resolve().parent
source=root.parent/'original'
meta=json.loads((source/'frames.json').read_text())
frames=sorted((m for m in meta if m['Action']==0 and m['Direction']==0),key=lambda m:m['Frame'])
sheet=Image.new('RGBA',(500,240),(0,0,0,0))
for i,m in enumerate(frames):
    p=Image.open(source/m['File']).convert('RGBA')
    sheet.alpha_composite(p,((i%5)*100+50-m['X'],(i//5)*120+104-m['Y']-m['Height']))
sheet.resize((2000,960),Image.Resampling.NEAREST).save(root/'original-walk-guide.png')
