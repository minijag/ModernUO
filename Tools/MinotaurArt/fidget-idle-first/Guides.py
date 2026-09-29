from pathlib import Path
import json
from PIL import Image
root=Path(__file__).resolve().parent
source=root.parent/'original'
meta=json.loads((source/'frames.json').read_text())
for d in range(5):
    sheet=Image.new('RGBA',(640,320),(0,0,0,0))
    for row,a in enumerate((17,18)):
        for f in range(1,5):
            m=next(m for m in meta if m['Action']==a and m['Direction']==d and m['Frame']==f)
            p=Image.open(source/m['File']).convert('RGBA')
            sheet.alpha_composite(p,((f-1)*160+80-m['X'],row*160+130-m['Y']-p.height))
    sheet.resize((1920,960),Image.Resampling.NEAREST).save(root/f'guide-{d}.png')
