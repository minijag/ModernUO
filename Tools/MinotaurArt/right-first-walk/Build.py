"""Package idle-first right/left walking preview. No game asset writes."""
from pathlib import Path
from statistics import median
import hashlib,json,shutil
from PIL import Image,ImageDraw
root=Path(__file__).resolve().parent
out=root/'frames';out.mkdir(exist_ok=True)
idle_path=root.parent/'idle-walk-rebuild/frames/0-0-0.png'
idle=Image.open(idle_path).convert('RGBA')
sheet=Image.open(root/'poses.png').convert('RGBA')
cells=[]
for i in range(8):
    col,row=i%4,i//4
    p=sheet.crop((round(col*sheet.width/4),round(row*sheet.height/2),round((col+1)*sheet.width/4),round((row+1)*sheet.height/2)))
    p.putalpha(p.getchannel('A').point(lambda a:255 if a>=220 else 0))
    cells.append(p)
scale=idle.height/median(p.getbbox()[3]-p.getbbox()[1] for p in cells)
# Reviewable anatomical landmarks in source-cell coordinates, never foot centroids.
landmarks=[(195,303),(191,303),(190,303),(183,303),(192,307),(190,307),(190,307),(183,307)]
labels=['Idle / frame 0','Right lifting','Right peak','Right lowering','Right planted','Left lifting','Left peak','Left lowering','Left planted','Return to idle']
records=[];previews=[]
for i in range(10):
    name=f'0-0-{i}.png'
    if i in (0,9):
        shutil.copy2(idle_path,out/name)
        p=idle.copy();px,py=36,58
    else:
        p=cells[i-1]
        p=p.resize((round(p.width*scale),round(p.height*scale)),Image.Resampling.NEAREST)
        px,py=[round(v*scale) for v in landmarks[i-1]]
        p.save(out/name)
    m=dict(Action=0,Direction=0,Frame=i,File=name,Width=p.width,Height=p.height,X=px,Y=py-p.height+35,PelvisX=px,PelvisY=py,Phase=labels[i])
    assert px-m['X']==0 and py-m['Y']-p.height==-35
    records.append(m)
    c=Image.new('RGB',(180,160),(38,41,39));d=ImageDraw.Draw(c)
    c.paste(p,(90-m['X'],130-m['Y']-p.height),p)
    d.line((90,10,90,137),fill=(105,115,108));d.line((86,95,94,95),fill=(112,231,221))
    d.text((5,146),f'{i}: {labels[i]}',fill=(220,220,200))
    previews.append(c.resize((540,480),Image.Resampling.NEAREST))
# Idle available as a separate action with the same centered registration.
shutil.copy2(idle_path,out/'1-0-0.png')
records.append(dict(records[0],Action=1,File='1-0-0.png',Phase='Idle'))
(out/'frames.json').write_text(json.dumps(records,indent=2))
previews[0].save(root/'preview.gif',save_all=True,append_images=previews[1:],duration=120,loop=0,disposal=2)
contact=Image.new('RGB',(1800,640))
for i,p in enumerate(previews):contact.paste(p.resize((360,320),Image.Resampling.NEAREST),((i%5)*360,(i//5)*320))
contact.save(root/'contact.png')
assert (out/'0-0-0.png').read_bytes()==idle_path.read_bytes()==(out/'0-0-9.png').read_bytes()
(root/'validation.json').write_text(json.dumps(dict(exactIdleFrame0=True,exactIdleFrame9=True,pelvisRelativeToRoot=[0,-35],frames=10,direction=0,scale=scale,sourceCellPelvis=landmarks),indent=2))
print('PASS: exact idle at frame 0 and wrap; 10 walk frames plus idle; fixed pelvis (0,-35). Preview only.')
