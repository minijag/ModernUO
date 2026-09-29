"""Extract generated animation sheets and prepare native-sized UO frames.

Packaging only: all character artwork comes from the image-generation sheets.
Does not install or modify live client files.
"""
from pathlib import Path
import json
from statistics import median
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent
SRC = ROOT / 'redesign-v2'
OUT = SRC / 'frames'
OUT.mkdir(exist_ok=True)
meta = json.loads((ROOT / 'original/frames.json').read_text())
actions = sorted({m['Action'] for m in meta})
reference = {0:(None,98),1:(0,98),2:(0,112),3:(0,103),4:(0,128),
             5:(2,128),6:(0,128),10:(3,98),11:(2,100),15:(2,98),
             16:(2,98),17:(0,98),18:(None,98)}

def cuts(projection, count):
    """Find transparent gutters near equal-cell boundaries, avoiding clipping."""
    size = len(projection)
    result = [0]
    for k in range(1, count):
        target = size*k/count
        lo, hi = round(target-size/count*.22), round(target+size/count*.22)
        candidates = list(range(max(lo,result[-1]+1),min(hi,size-1)))
        boundary = min(candidates, key=lambda p:(int(projection[p]),abs(p-target)))
        if projection[boundary]:
            raise ValueError(f'No transparent gutter near {target}; needs manual review')
        result.append(boundary)
    return result+[size]

raw = {}
audit = []
for action in actions:
    records = [m for m in meta if m['Action']==action]
    n = sum(m['Direction']==0 for m in records)
    sheet = Image.open(SRC/f'action-{action}.png').convert('RGBA')
    data = np.array(sheet)
    # UO uses binary alpha. Discard faint invisible colored fringe before filtering.
    mask = data[:,:,3] >= 220
    data[:,:,3] = mask.astype(np.uint8)*255
    data[~mask,:3] = 0
    sheet = Image.fromarray(data)
    cols, rows = (5,1) if n==1 else (n,5)
    xc = cuts(mask.sum(axis=0),cols)
    for col in range(cols):
        left,right=xc[col:col+2]
        yc=cuts(mask[:,left:right].sum(axis=1),rows)
        for row in range(rows):
            top,bottom=yc[row:row+2]
            cell=sheet.crop((left,top,right,bottom))
            bounds=cell.getchannel('A').getbbox()
            if not bounds: raise ValueError('Empty frame')
            d,f=(col,0) if n==1 else (row,col)
            raw[action,d,f]=cell.crop(bounds)
            audit.append({'action':action,'direction':d,'frame':f,
                          'sheetBounds':[left+bounds[0],top+bounds[1],left+bounds[2],top+bounds[3]],
                          'touchesSheetEdge':left+bounds[0]==0 or top+bounds[1]==0 or left+bounds[2]==sheet.width or top+bounds[3]==sheet.height})

prepared={}
result=[]
for action in actions:
    for d in range(5):
        records=sorted([m for m in meta if m['Action']==action and m['Direction']==d],key=lambda m:m['Frame'])
        ref,target=reference[action]
        height=median(raw[action,d,m['Frame']].height for m in records) if ref is None else raw[action,d,ref].height
        scale=target/height
        for m in records:
            p=raw[action,d,m['Frame']]
            p=p.resize((round(p.width*scale),round(p.height*scale)),Image.Resampling.LANCZOS)
            p.putalpha(p.getchannel('A').point(lambda a:255 if a>=128 else 0))
            a=np.array(p.getchannel('A'))>0
            # Ground anchor comes from feet rather than the weapon/arm bounding box.
            if action not in (2,3):
                yy,xx=np.nonzero(a[round(p.height*.86):])
                x=round(float(np.median(xx)))
                y=-5
            else:
                x=round(m['X']/m['Width']*p.width)
                y=round(m['Y']*1.1)
            prepared[m['File']]=p
            result.append(dict(m,Width=p.width,Height=p.height,X=x,Y=y))

# Shared palette across the entire creature prevents independent frame palettes
# from producing skin/fur flicker. Store native RGB555-compatible samples.
pixels=[]
for p in prepared.values():
    a=np.array(p);pixels.append(a[a[:,:,3]>0,:3])
samples=np.concatenate(pixels)
palette_source=Image.fromarray(samples.reshape((-1,1,3))).quantize(colors=240,method=Image.Quantize.MEDIANCUT)
palette=palette_source.getpalette()
palette=[max(8,(v>>3)<<3) for v in palette]
palette_image=Image.new('P',(1,1));palette_image.putpalette(palette)
for name,p in prepared.items():
    q=p.convert('RGB').quantize(palette=palette_image,dither=Image.Dither.NONE).convert('RGBA')
    q.putalpha(p.getchannel('A'));q.save(OUT/name)
    prepared[name]=q

(OUT/'frames.json').write_text(json.dumps(result,indent=2))
(SRC/'extraction-audit.json').write_text(json.dumps(audit,indent=2))
for action in actions:
    records=[m for m in result if m['Action']==action]
    count=sum(m['Direction']==0 for m in records)
    contact=Image.new('RGB',(count*160,5*180),(38,41,39))
    play=[]
    for f in range(count):
        canvas=Image.new('RGB',(5*160,180),(38,41,39))
        for m in records:
            if m['Frame']!=f:continue
            p=prepared[m['File']];d=m['Direction']
            x=80-m['X'];y=148-m['Y']-p.height
            canvas.paste(p,(d*160+x,y),p)
            contact.paste(p,(f*160+x,d*180+y),p)
            ImageDraw.Draw(contact).text((f*160+4,d*180+165),f'{action}/{d}/{f}',fill=(205,200,185))
        play.append(canvas)
    contact.save(SRC/f'contact-{action}.png')
    play[0].save(SRC/f'preview-{action}.gif',save_all=True,append_images=play[1:],duration=120 if action==0 else 180,loop=0,disposal=2)
print(f'Extracted {len(result)} frames. Edge contacts: {sum(a["touchesSheetEdge"] for a in audit)}. No live files changed.')
