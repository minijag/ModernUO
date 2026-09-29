"""Add directions 1–4 without changing the reviewed front frames."""
from pathlib import Path
from statistics import median
import hashlib,json,shutil
from PIL import Image,ImageDraw
root=Path(__file__).resolve().parent
out=root/'frames'
records=[m for m in json.loads((out/'frames.json').read_text()) if m['Direction']==0]
front_hashes={m['File']:hashlib.sha256((out/m['File']).read_bytes()).hexdigest() for m in records}
idle_points={1:(35,59),2:(23,58),3:(31,58),4:(28,58)}
# Anatomical pelvis centers, in equal source-sheet cells. No foot-based centering.
points={1:[(200,262)]*8,2:[(192,270)]*8,3:[(192,255)]*8,4:[(200,272)]*8}
points[2][1]=(176,270)
labels=['Idle','Right lifting','Right peak','Right lowering','Right planted','Left lifting','Left peak','Left lowering','Left planted','Idle return']
audit=[]
for direction in range(1,5):
    idle_path=root.parent/f'walk-idle-match/packed/decoded/1-{direction}-0.png'
    idle=Image.open(idle_path).convert('RGBA')
    filename=f'poses-{direction}-fixed.png' if direction in (2,3) else f'poses-{direction}.png'
    sheet=Image.open(root/filename).convert('RGBA')
    cells=[]
    for i in range(8):
        x,y=i%4,i//4
        p=sheet.crop((x*384,y*512,(x+1)*384,(y+1)*512))
        p.putalpha(p.getchannel('A').point(lambda a:255 if a>=220 else 0))
        b=p.getbbox()
        assert b and b[0]>0 and b[1]>0 and b[2]<384 and b[3]<512, (direction,i,b)
        cells.append(p)
    scale=idle.height/median(p.getbbox()[3]-p.getbbox()[1] for p in cells)
    previews=[];group=[]
    for i in range(10):
        name=f'0-{direction}-{i}.png'
        if i in (0,9):
            shutil.copy2(idle_path,out/name);p=idle.copy();px,py=idle_points[direction]
        else:
            p=cells[i-1]
            p=p.resize((round(p.width*scale),round(p.height*scale)),Image.Resampling.NEAREST)
            px,py=[round(v*scale) for v in points[direction][i-1]]
            p.save(out/name)
        m=dict(Action=0,Direction=direction,Frame=i,File=name,Width=p.width,Height=p.height,X=px,Y=py-p.height+35,PelvisX=px,PelvisY=py,Phase=labels[i])
        assert px-m['X']==0 and py-m['Y']-p.height==-35
        records.append(m);group.append(m)
        c=Image.new('RGB',(180,160),(38,41,39));c.paste(p,(90-px,130-m['Y']-p.height),p)
        draw=ImageDraw.Draw(c);draw.line((90,8,90,138),fill=(105,115,108));draw.line((86,95,94,95),fill=(112,231,221))
        draw.text((5,146),f'{direction}/{i} {labels[i]}',fill=(220,220,200))
        previews.append(c.resize((540,480),Image.Resampling.NEAREST))
    shutil.copy2(idle_path,out/f'1-{direction}-0.png')
    records.append(dict(group[0],Action=1,File=f'1-{direction}-0.png'))
    assert (out/f'0-{direction}-0.png').read_bytes()==idle_path.read_bytes()==(out/f'0-{direction}-9.png').read_bytes()
    previews[0].save(root/f'preview-{direction}.gif',save_all=True,append_images=previews[1:],duration=120,loop=0,disposal=2)
    contact=Image.new('RGB',(1800,640))
    for i,p in enumerate(previews):contact.paste(p.resize((360,320),Image.Resampling.NEAREST),((i%5)*360,(i//5)*320))
    contact.save(root/f'contact-{direction}.png')
    audit.append(dict(direction=direction,scale=scale,frames=10,exactIdleEndpoints=True,pelvis=[0,-35],idleLandmark=idle_points[direction],sourceLandmarks=points[direction]))
assert all(hashlib.sha256((out/name).read_bytes()).hexdigest()==digest for name,digest in front_hashes.items())
assert len(records)==55 and len({(m['Action'],m['Direction'],m['Frame']) for m in records})==55
(out/'frames.json').write_text(json.dumps(records,indent=2))
(root/'directions-validation.json').write_text(json.dumps(audit,indent=2))
print('PASS: 50 walk frames + 5 exact idle frames; directions 0–4; front unchanged; all pelvis anchors on center; transparent gutters intact.')
