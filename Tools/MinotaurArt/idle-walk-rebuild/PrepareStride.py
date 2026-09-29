"""Package generated pose-guided sprites; original order and no idle holds."""
from pathlib import Path
from statistics import median
import json
import sys
from PIL import Image, ImageDraw
root=Path(__file__).resolve().parent
variant='stride-blackfur' if '--blackfur' in sys.argv else 'stride'
out=root/f'{variant}-frames';out.mkdir(exist_ok=True)
source=json.loads((root.parent/'original/frames.json').read_text())
original=sorted([m for m in source if m['Action']==0 and m['Direction']==0],key=lambda m:m['Frame'])
im=Image.open(root/f'uo-{variant}-sheet.png').convert('RGBA')
cells=[]
for i in range(10):
    x,y=i%5,i//5
    p=im.crop((round(x*im.width/5),round(y*im.height/2),round((x+1)*im.width/5),round((y+1)*im.height/2)))
    p.putalpha(p.getchannel('A').point(lambda a:255 if a>=220 else 0))
    cells.append(p)
scale=98/median(p.getbbox()[3]-p.getbbox()[1] for p in cells)
original_top=[-m['Y']-m['Height'] for m in original]
records=[];previews=[]
for i,p in enumerate(cells):
    p=p.resize((round(p.width*scale),round(p.height*scale)),Image.Resampling.NEAREST)
    bbox=p.getbbox()
    # Horizontal head/horns center, independent of swinging feet and weapon.
    head=p.getchannel('A').crop((0,bbox[1],p.width,bbox[1]+18)).getbbox()
    cx=round((head[0]+head[2])/2)
    # Keep the original cycle's small vertical bob; do not fix each support foot.
    top=-93+original_top[i]-original_top[0]
    cy=bbox[1]-top-p.height
    name=f'0-0-{i}.png';p.save(out/name)
    records.append(dict(Action=0,Direction=0,Frame=i,File=name,Width=p.width,Height=p.height,X=cx,Y=cy))
    canvas=Image.new('RGB',(180,160),(38,41,39))
    canvas.paste(p,(90-cx,130-cy-p.height),p)
    d=ImageDraw.Draw(canvas);d.line((90,8,90,140),fill=(85,95,88))
    d.text((8,145),f'UO pose guide: {i}',fill=(220,220,200))
    previews.append(canvas.resize((540,480),Image.Resampling.NEAREST))
(root/f'{variant}-draft.json').write_text(json.dumps(records,indent=2))
previews[0].save(root/f'{variant}-draft.gif',save_all=True,append_images=previews[1:],duration=120,loop=0,disposal=2)
sheet=Image.new('RGB',(1800,640))
for i,p in enumerate(previews):sheet.paste(p.resize((360,320),Image.Resampling.NEAREST),((i%5)*360,(i//5)*320))
sheet.save(root/f'{variant}-contact.png')
print('10 consecutive pose-guided frames; no idle inserts or repeated/reversed frames. Preview only.')
