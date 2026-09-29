"""Build non-clipping playback previews from the actual decoded MUL frames."""
from pathlib import Path
import json
import sys
from PIL import Image,ImageDraw

root=Path(__file__).resolve().parent/(sys.argv[1] if len(sys.argv)>1 else 'redesign-v2')
meta=json.loads((root/'frames/frames.json').read_text())
out=root/'packed'
montage=[]
for action in sorted({m['Action'] for m in meta}):
    rows=[m for m in meta if m['Action']==action]
    n=max(m['Frame'] for m in rows)+1
    frames=[]
    for f in range(n):
        canvas=Image.new('RGB',(1120,240),(36,39,38))
        draw=ImageDraw.Draw(canvas)
        for d in range(5):
            m=next(m for m in rows if m['Direction']==d and m['Frame']==f)
            p=Image.open(out/'decoded'/m['File']).convert('RGBA')
            x=d*224+112-m['X'];y=164-m['Y']-m['Height']
            assert d*224<=x and x+p.width<=(d+1)*224 and 0<=y and y+p.height<220,(m,x,y)
            canvas.paste(p,(x,y),p)
            draw.text((d*224+8,224),f'Action {action} / facing {d}',fill=(210,200,180))
        frames.append(canvas)
    durations=[120]*n if action==0 else [180]*n
    if action in (2,3):durations[-1]=800
    frames[0].save(out/f'preview-{action}.gif',save_all=True,append_images=frames[1:],duration=durations,loop=0,disposal=2)
    # One illustrative native decoded frame per action for a compact overview.
    f=2 if n>2 else 0
    m=next(m for m in rows if m['Direction']==1 and m['Frame']==f)
    p=Image.open(out/'decoded'/m['File']).convert('RGBA')
    tile=Image.new('RGB',(224,240),(36,39,38))
    tile.paste(p,(112-m['X'],164-m['Y']-m['Height']),p)
    ImageDraw.Draw(tile).text((8,224),f'Action {action}',fill=(210,200,180))
    montage.append(tile)
overview=Image.new('RGB',(224*5,240*3),(36,39,38))
for i,p in enumerate(montage):overview.paste(p,((i%5)*224,(i//5)*240))
overview.save(out/'overview.png')
print('Created 13 decoded playback previews. All displayed sprites fit their canvases.')
