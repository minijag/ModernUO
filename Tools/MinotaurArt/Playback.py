"""Assemble decoded native frames for animation QA, without changing the assets."""
import json
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parent
meta = json.loads((root / 'adapted/frames.json').read_text(encoding='utf-8-sig'))
decoded = root / 'full-set/decoded'
for action in (0, 2, 3, 4, 5, 6, 10, 11, 15, 16, 17, 18):
    rows = [m for m in meta if m['Action'] == action]
    count = max(m['Frame'] for m in rows) + 1
    playback = []
    for frame in range(count):
        canvas = Image.new('RGB', (800, 160), (36, 39, 38))
        draw = ImageDraw.Draw(canvas)
        for d in range(5):
            m = next(m for m in rows if m['Direction'] == d and m['Frame'] == frame)
            sprite = Image.open(decoded / m['File']).convert('RGBA')
            canvas.paste(sprite, (d * 160 + 80 - m['X'], 126 - m['Y'] - m['Height']), sprite)
            draw.text((d * 160 + 8, 143), f'Facing {d}', fill=(200, 195, 180))
        playback.append(canvas)
    playback[0].save(root / f'full-set/action-{action}.gif', save_all=True,
                     append_images=playback[1:], duration=120, loop=0, disposal=2)
print('Created 12 decoded animation playback strips, each with all five facings.')
