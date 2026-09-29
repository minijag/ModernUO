"""Package generated artwork for review; does not modify client files."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parent
sheet = Image.open(root / 'attack-4-1-candidate.png').convert('RGBA')
# Generated sprites are not centered in mathematical quarters. Boundaries are
# chosen from the visible transparent gutters, avoiding the extended axe.
cuts = [0, 460, 1080, 1640, sheet.width]
parts = []
for left, right in zip(cuts, cuts[1:]):
    part = sheet.crop((left, 0, right, sheet.height))
    alpha = part.getchannel('A').point(lambda a: 255 if a >= 220 else 0)
    if alpha.getextrema() != (0, 255):
        raise ValueError('Expected a transparent sprite')
    part.putalpha(alpha)
    parts.append(part.crop(alpha.getbbox()))
# Recovery pose is comparable to the 98px standing test. One uniform scale for
# all frames preserves the extended overhead weapon instead of shrinking it.
scale = 98 / parts[3].height
frames = []
contact = Image.new('RGB', (640, 160), (40, 42, 41))
for i, part in enumerate(parts):
    sprite = part.resize((round(part.width * scale), round(part.height * scale)), Image.Resampling.LANCZOS)
    sprite.putalpha(sprite.getchannel('A').point(lambda a: 255 if a >= 128 else 0))
    sprite.save(root / f'attack-4-1-frame-{i}.png')
    canvas = Image.new('RGB', (160, 160), (40, 42, 41))
    canvas.paste(sprite, (80 - sprite.width // 2, 135 - sprite.height), sprite)
    ImageDraw.Draw(canvas).text((8, 145), f'Attack draft {i + 1}/4', fill=(210, 205, 190))
    contact.paste(canvas, (i * 160, 0))
    frames.append(canvas)
contact.save(root / 'attack-4-1-review.png')
frames[0].save(root / 'attack-4-1-review.gif', save_all=True, append_images=frames[1:], duration=[220, 120, 180, 400], loop=0, disposal=2)
large = [f.resize((480, 480), Image.Resampling.NEAREST) for f in frames]
large[0].save(root / 'attack-4-1-review-3x.gif', save_all=True, append_images=large[1:], duration=[220, 120, 180, 400], loop=0, disposal=2)
print('Packaged four attack draft frames. No client files changed.')
