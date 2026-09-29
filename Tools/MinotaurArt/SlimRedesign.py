"""User-requested mechanical horizontal slimming; no generated replacement art."""
from pathlib import Path
import json
import shutil
from PIL import Image,ImageDraw

root=Path(__file__).resolve().parent
src=root/'redesign-v2'
out=root/'redesign-slim'
dest=out/'frames'
dest.mkdir(parents=True,exist_ok=True)
meta=json.loads((src/'frames/frames.json').read_text())
# Walking was 23–26px through the central torso vs standing's 20–21px.
# Preserve heights and use a single factor through each action to avoid pumping.
# Attack/death silhouettes include extended weapons, so use a milder correction.
# The pointing and shoulder-rest idles already match the standing waist width.
factors={0:.82,1:1.,2:.85,3:.85,4:.85,5:.85,6:.85,
         10:.90,11:.90,15:.90,16:.90,17:1.,18:1.}
records=[]
for m in meta:
    p=Image.open(src/'packed/decoded'/m['File']).convert('RGBA')
    factor=factors[m['Action']]
    w=max(1,round(p.width*factor))
    if factor==1:
        shutil.copyfile(src/'packed/decoded'/m['File'],dest/m['File'])
        q=p
    else:
        # Nearest-neighbor keeps the exact native palette and binary transparency.
        # Scale the world-space horizontal anchor by the actual rounded ratio.
        q=p.resize((w,p.height),Image.Resampling.NEAREST)
        q.save(dest/m['File'])
    records.append(dict(m,Width=w,X=round(m['X']*w/p.width)))
    assert q.height==m['Height']
    if factor==1:assert q.tobytes()==p.tobytes()
(dest/'frames.json').write_text(json.dumps(records,indent=2))
(out/'width-factors.json').write_text(json.dumps(factors,indent=2))
examples=[(1,0,0),(0,0,0),(0,1,4),(4,0,0),(5,1,2),(6,1,2),(10,0,3),(17,0,0)]
canvas=Image.new('RGB',(8*160,200),(38,41,39))
draw=ImageDraw.Draw(canvas)
for i,key in enumerate(examples):
    m=next(m for m in meta if (m['Action'],m['Direction'],m['Frame'])==key)
    n=next(n for n in records if n['File']==m['File'])
    for j,(folder,r) in enumerate([(src/'packed/decoded',m),(dest,n)]):
        p=Image.open(folder/m['File']).convert('RGBA')
        canvas.paste(p,(i*160+j*80+40-r['X'],155-r['Y']-r['Height']),p)
    draw.text((i*160+4,175),f'{key[0]}: before / after',fill=(210,205,185))
canvas.save(out/'comparison.png')
print('Prepared 225 narrowed frames; 55 standing/consistent idle frames unchanged. Heights preserved in all 280 frames.')
