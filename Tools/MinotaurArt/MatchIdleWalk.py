"""Match walking silhouette width to the approved idle, direction by direction."""
from pathlib import Path
from statistics import median
import json, shutil
from PIL import Image,ImageDraw

root=Path(__file__).resolve().parent
original=root/'redesign-v2'
previous=root/'redesign-slim'
out=root/'walk-idle-match'
dest=out/'frames';dest.mkdir(parents=True,exist_ok=True)
meta=json.loads((previous/'frames/frames.json').read_text())
orig=json.loads((original/'frames/frames.json').read_text())
lookup={m['File']:m for m in orig}
factors={}
for d in range(5):
    idle=next(m for m in orig if m['Action']==1 and m['Direction']==d)
    widths=[m['Width'] for m in orig if m['Action']==0 and m['Direction']==d]
    factors[d]=idle['Width']/median(widths)
    print(f'Facing {d}: idle {idle["Width"]}px; uncompressed walk median {median(widths)}px; envelope ratio {factors[d]:.4f}')
# The profile's stride and forward-held axe enlarge its envelope independently
# of body thickness. Do not shrink that body to the tucked-in idle envelope.
# Use the median of the other facings as its conservative proportion correction.
factors[2]=median(factors[d] for d in (0,1,3,4))
print('Final factors:',factors)
records=[]
for m in meta:
    if m['Action']!=0:
        shutil.copyfile(previous/'packed/decoded'/m['File'],dest/m['File'])
        records.append(m);continue
    src=lookup[m['File']]
    p=Image.open(original/'packed/decoded'/m['File']).convert('RGBA')
    w=round(p.width*factors[m['Direction']])
    p.resize((w,p.height),Image.Resampling.NEAREST).save(dest/m['File'])
    records.append(dict(src,Width=w,X=round(src['X']*w/p.width)))
(dest/'frames.json').write_text(json.dumps(records,indent=2))
(out/'factors.json').write_text(json.dumps(factors,indent=2))
sheet=Image.new('RGB',(5*300,190),(38,41,39));draw=ImageDraw.Draw(sheet)
for d in range(5):
    for col,(a,folder,metadata,label) in enumerate([
        (1,previous/'packed/decoded',meta,'Idle'),
        (0,previous/'packed/decoded',meta,'Old walk'),
        (0,dest,records,'New walk')]):
        m=next(m for m in metadata if m['Action']==a and m['Direction']==d and m['Frame']==0)
        p=Image.open(folder/m['File']).convert('RGBA')
        sheet.paste(p,(d*300+col*100+50-m['X'],145-m['Y']-m['Height']),p)
        draw.text((d*300+col*100+8,162),label,fill=(210,205,185))
sheet.save(out/'comparison.png')
print('50 walking frames adjusted from uncompressed originals; all other frames preserved.')
