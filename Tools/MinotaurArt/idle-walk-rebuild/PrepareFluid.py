"""Stage an alternating step trial, no client writes or anatomy warping."""
from pathlib import Path
import json
from PIL import Image, ImageDraw

root=Path(__file__).resolve().parent
output=root/'fluid-frames'
output.mkdir(exist_ok=True)
sheet=Image.open(root/'fluid-step-sheet.png').convert('RGBA')
idle=Image.open(root/'frames/0-0-0.png').convert('RGBA')
# Generated sheet has the wrong leg lifted in cell 2. Do not use it.
# Reuse the lift poses on descent for symmetric easing and no overlapping steps.
sequence=[None,1,8,1,None,None,6,7,6,None]
# Cell-local pelvis landmarks, normalized to the generated 5x2 layout.
# Baseline is determined by the supporting (opposite) hoof, never both feet.
frames=[]
records=[]
scale=98/395
for i,cell in enumerate(sequence):
    if cell is None:
        p=idle.copy(); px=36; ground=98
    else:
        col,row=cell%5,cell//5
        p=sheet.crop((round(col*sheet.width/5),round(row*sheet.height/2),round((col+1)*sheet.width/5),round((row+1)*sheet.height/2)))
        p.putalpha(p.getchannel('A').point(lambda a:255 if a>=220 else 0))
        p=p.resize((round(p.width*scale),round(p.height*scale)),Image.Resampling.NEAREST)
        # Trunk center reviewed on the native-size contact sheet; not cell center.
        px={1:46,8:42,6:46,7:44}[cell]
        # Character-left step is viewer-right: supporting hoof is viewer-left.
        support=p.getchannel('A').crop((0 if i<5 else px, int(p.height*.80), px if i<5 else p.width, p.height)).getbbox()
        assert support is not None
        ground=int(p.height*.80)+support[3]
    name=f'0-0-{i}.png'; p.save(output/name)
    m=dict(Action=0,Direction=0,Frame=i,File=name,Width=p.width,Height=p.height,X=px,Y=ground-p.height-5)
    records.append(m)
    canvas=Image.new('RGB',(180,160),(38,41,39))
    canvas.paste(p,(90-m['X'],130-m['Y']-m['Height']),p)
    draw=ImageDraw.Draw(canvas)
    draw.line((90,10,90,140),fill=(90,100,93))
    draw.line((20,135,160,135),fill=(90,100,93))
    draw.text((5,146),f'{i}: '+('plant' if cell is None else 'left step' if i<5 else 'right step'),fill=(220,220,200))
    frames.append(canvas.resize((540,480),Image.Resampling.NEAREST))
(root/'fluid-draft.json').write_text(json.dumps(records,indent=2))
frames[0].save(root/'fluid-draft.gif',save_all=True,append_images=frames[1:],duration=120,loop=0,disposal=2)
contact=Image.new('RGB',(1800,640))
for i,frame in enumerate(frames):
    contact.paste(frame.resize((360,320),Image.Resampling.NEAREST),((i%5)*360,(i//5)*320))
contact.save(root/'fluid-contact.png')
assert all(records[i]['Y']==-5 for i in (0,4,5,9))
print('Staged 10 frames: left lift/peak/lower/plant, then right. Supporting hoof baseline fixed; exact idle at plants.')
