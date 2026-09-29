"""Package a draft with a shared coordinate system. Never installs artwork."""
from pathlib import Path
import json, hashlib
from statistics import median
from PIL import Image, ImageDraw

root=Path(__file__).resolve().parent
source=root.parent/'walk-idle-match'
meta=json.loads((source/'frames/frames.json').read_text())
records=[]
for d in range(5):
    m=next(m for m in meta if m['Action']==1 and m['Direction']==d)
    path=root/'frames'/f'0-{d}-0.png'
    assert path.read_bytes()==(source/'packed/decoded'/m['File']).read_bytes()
    records.append(dict(m,Action=0,Frame=0,File=path.name))
(root/'frame-zero.json').write_text(json.dumps(records,indent=2))
idle=Image.open(root/'frames/0-0-0.png').convert('RGBA')
im=Image.open(root/'front-successors-draft.png').convert('RGBA')
cells=[];heads=[];heights=[];tops=[]
for i in range(9):
    x,y=i%3,i//3
    p=im.crop((round(x*im.width/3),round(y*im.height/3),round((x+1)*im.width/3),round((y+1)*im.height/3)))
    p.putalpha(p.getchannel('A').point(lambda a:255 if a>=220 else 0))
    b=p.getbbox();heights.append(b[3]-b[1]);tops.append(b[1])
    h=p.getchannel('A').crop((0,b[1],p.width,b[1]+round((b[3]-b[1])*.15))).getbbox()
    heads.append((h[0]+h[2])/2)
    cells.append(p)
# One scale and root for the ENTIRE cycle, never a separate fit per pose.
scale=idle.height/median(heights)
ih=idle.getchannel('A').crop((0,0,idle.width,round(idle.height*.15))).getbbox()
idle_head=(ih[0]+ih[2])/2
base=records[0]
cx=round(median(heads)*scale-(idle_head-base['X']))
root_y=round(median(tops)*scale+idle.height+base['Y'])
front=[dict(base)]
for i,p in enumerate(cells,1):
    p=p.resize((round(p.width*scale),round(p.height*scale)),Image.Resampling.NEAREST)
    name=f'0-0-{i}.png';p.save(root/'frames'/name)
    front.append(dict(base,Frame=i,File=name,Width=p.width,Height=p.height,X=cx,Y=root_y-p.height))
# A common cell origin did not remove the generated artwork's drift inside cells.
# Register a homologous anatomical landmark, not the feet or overall bounds.
points=json.loads((root/'pelvis-landmarks.json').read_text())['points']
assert len(points)==len(front)
target_x=0  # Pelvis must be ON the viewer's vertical UO-root guide.
target_y=points[0][1]-base['Y']-base['Height']
audit=[]
for m,(px,py) in zip(front,points):
    old_x,old_y=m['X'],m['Y']
    m['X']=px-target_x
    m['Y']=py-m['Height']-target_y
    m['PelvisX'],m['PelvisY']=px,py
    assert (px-m['X'],py-m['Y']-m['Height'])==(target_x,target_y)
    audit.append(dict(frame=m['Frame'],translationX=old_x-m['X'],translationY=old_y-m['Y'],registeredPelvis=[target_x,target_y]))
assert front[0]['X']==points[0][0] and front[0]['Y']==base['Y']
for zoom in (1,2,3,4):
    for m in front:
        assert 300-m['X']*zoom+m['PelvisX']*zoom == 300
(root/'alignment-check.json').write_text(json.dumps(audit,indent=2))
(root/'front-draft.json').write_text(json.dumps(front,indent=2))
frames=[]
for m in front:
    canvas=Image.new('RGB',(180,160),(38,41,39));draw=ImageDraw.Draw(canvas)
    draw.line((90,8,90,135),fill=(65,69,65));draw.line((25,130,155,130),fill=(65,69,65))
    p=Image.open(root/'frames'/m['File']).convert('RGBA')
    canvas.paste(p,(90-m['X'],130-m['Y']-m['Height']),p)
    draw.line((90,8,90,135),fill=(105,115,108))
    py=130-m['Y']-m['Height']+m['PelvisY']
    draw.line((87,py,93,py),fill=(112,231,221))
    draw.text((8,146),f'Front draft frame {m["Frame"]}',fill=(210,205,190))
    frames.append(canvas.resize((540,480),Image.Resampling.NEAREST))
frames[0].save(root/'front-draft.gif',save_all=True,append_images=frames[1:],duration=120,loop=0,disposal=2)
sheet=Image.new('RGB',(1800,640),(38,41,39))
for i,frame in enumerate(frames):
    sheet.paste(frame.resize((360,320),Image.Resampling.NEAREST),((i%5)*360,(i//5)*320))
sheet.save(root/'front-contact-sheet.png')
print(f'Five frame-zero PNGs verified byte-for-byte against idle. All ten front pelvis landmarks register at ({target_x}, {target_y}); exact gray-line alignment verified at 1x through 4x zoom. Frame-zero X intentionally recentered; pixels and Y unchanged. Draft only.')
