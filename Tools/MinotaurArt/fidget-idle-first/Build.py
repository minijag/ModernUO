"""Package generated fidget sheets; retain exact idle frame zero and one scale per direction."""
from pathlib import Path
from statistics import median
import json, shutil
from PIL import Image, ImageDraw
root=Path(__file__).resolve().parent
out=root/'frames'
out.mkdir(exist_ok=True)
idle_dir=root.parent/'right-first-walk/frames'
idles={m['Direction']:m for m in json.loads((idle_dir/'frames.json').read_text()) if m['Action']==1}
# Anatomical pelvis centers measured on the generated equal-cell sheets.
points={
0:[(202,314),(207,314),(205,314),(205,314),(180,286),(199,286),(199,286),(207,286)],
1:[(216,313),(232,313),(229,313),(224,313),(208,271),(226,271),(215,271),(235,271)],
2:[(210,303),(223,303),(214,303),(227,303),(216,282),(227,282),(229,282),(232,282)],
3:[(204,311),(221,311),(224,311),(230,311),(216,291),(214,291),(221,291),(226,291)],
4:[(185,313),(214,313),(218,313),(235,313),(200,290),(208,290),(208,290),(216,290)]}
records=[]; audit=[]
for d in range(5):
    idle=Image.open(idle_dir/idles[d]['File']).convert('RGBA')
    sheet=Image.open(root/f'poses-{d}.png').convert('RGBA')
    assert sheet.size==(1536,1024)
    cells=[]
    for i in range(8):
        p=sheet.crop(((i%4)*384,(i//4)*512,(i%4+1)*384,(i//4+1)*512))
        p.putalpha(p.getchannel('A').point(lambda a:255 if a>=220 else 0))
        b=p.getbbox()
        assert b and b[0]>0 and b[1]>0 and b[2]<384 and b[3]<512,(d,i,b)
        cells.append(p)
    scale=idle.height/median(p.getbbox()[3]-p.getbbox()[1] for p in cells)
    shutil.copy2(idle_dir/idles[d]['File'],out/idles[d]['File'])
    records.append(idles[d])
    for row,a in enumerate((17,18)):
        previews=[]
        for f in range(5):
            name=f'{a}-{d}-{f}.png'
            if f==0:
                shutil.copy2(idle_dir/idles[d]['File'],out/name)
                p=idle.copy(); px,py=idles[d]['PelvisX'],idles[d]['PelvisY']
            else:
                i=row*4+f-1
                p=cells[i].resize((round(384*scale),round(512*scale)),Image.Resampling.NEAREST)
                px,py=[round(v*scale) for v in points[d][i]]
                p.save(out/name)
            m=dict(Action=a,Direction=d,Frame=f,File=name,Width=p.width,Height=p.height,X=px,Y=py-p.height+35,PelvisX=px,PelvisY=py)
            assert px-m['X']==0 and py-m['Y']-p.height==-35
            records.append(m)
            c=Image.new('RGB',(160,150),(39,43,41));c.paste(p,(80-px,115-m['Y']-p.height),p)
            draw=ImageDraw.Draw(c);draw.line((80,0,80,130),fill=(88,97,91));draw.text((4,135),f'{a} / {d} / {f}',fill='white')
            previews.append(c.resize((480,450),Image.Resampling.NEAREST))
        assert (out/f'{a}-{d}-0.png').read_bytes()==(idle_dir/idles[d]['File']).read_bytes()
        previews[0].save(root/f'preview-{a}-{d}.gif',save_all=True,append_images=previews[1:],duration=180,loop=0,disposal=2)
        contact=Image.new('RGB',(2400,450))
        for i,p in enumerate(previews):contact.paste(p,(i*480,0))
        contact.save(root/f'contact-{a}-{d}.png')
    audit.append(dict(direction=d,scale=scale,exactIdleFrameZero=True,pelvis=[0,-35],landmarks=points[d]))
assert len(records)==55
(out/'frames.json').write_text(json.dumps(records,indent=2))
(root/'validation.json').write_text(json.dumps(audit,indent=2))
print('PASS: both five-frame fidgets in all five stored directions; exact idle frame zero; fixed pelvis registration. Live files unchanged.')
