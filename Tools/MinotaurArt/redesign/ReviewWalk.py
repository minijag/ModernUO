from pathlib import Path
from PIL import Image, ImageDraw
from statistics import median

root = Path(__file__).resolve().parent
sheet = Image.open(root / 'walk-1-candidate.png').convert('RGBA')
parts = []
for i in range(10):
    col, row = i % 5, i // 5
    cell = sheet.crop((round(col*sheet.width/5), round(row*sheet.height/2),
                       round((col+1)*sheet.width/5), round((row+1)*sheet.height/2)))
    alpha = cell.getchannel('A').point(lambda a: 255 if a >= 220 else 0)
    bounds = alpha.getbbox()
    if not bounds:
        raise RuntimeError(f'Empty frame {i}')
    cell.putalpha(alpha)
    parts.append(cell.crop(bounds))
scale = 98 / median(p.height for p in parts)
frames = []
for i, part in enumerate(parts):
    sprite = part.resize((round(part.width*scale), round(part.height*scale)), Image.Resampling.LANCZOS)
    sprite.putalpha(sprite.getchannel('A').point(lambda a: 255 if a >= 128 else 0))
    sprite.save(root / f'walk-1-frame-{i}.png')
    canvas = Image.new('RGB', (160, 150), (40, 42, 41))
    canvas.paste(sprite, (80-sprite.width//2, 123-sprite.height), sprite)
    ImageDraw.Draw(canvas).text((6,135), f'Walk draft {i+1}/10', fill=(210,205,190))
    frames.append(canvas)
frames[0].save(root/'walk-1-review.gif', save_all=True, append_images=frames[1:], duration=110, loop=0, disposal=2)
large = [f.resize((480,450), Image.Resampling.NEAREST) for f in frames]
large[0].save(root/'walk-1-review-3x.gif', save_all=True, append_images=large[1:], duration=110, loop=0, disposal=2)
print(f'Sheet {sheet.size}; ten frames extracted, median character height scaled to 98px. Review only, not installed.')
