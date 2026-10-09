"""Package the generated cloth banner as native UO RGB555/RLE gump 64040.

Stages copies only. Never use the live data directory as output.
"""
import argparse
import hashlib
import shutil
import struct
from pathlib import Path
from PIL import Image

parser = argparse.ArgumentParser()
parser.add_argument('client', type=Path)
parser.add_argument('output', type=Path)
parser.add_argument('--replace-existing', action='store_true', help='Replace the installed 300x45 or 300x32 title banner only')
args = parser.parse_args()
assert args.client.resolve() != args.output.resolve(), 'Output must not be live data'
args.output.mkdir(parents=True, exist_ok=True)
source = Image.open(Path(__file__).parent / 'banner-source.png').convert('RGBA')
# MUL has binary transparency. Crop using the same threshold used by its encoder;
# do not key out dark cloth colors (which must remain opaque).
bounds = source.getchannel('A').point(lambda a: 255 if a >= 128 else 0).getbbox()
banner = source.crop(bounds)
banner.thumbnail((300, 32), Image.Resampling.LANCZOS)
canvas = Image.new('RGBA', (300, 32))
canvas.alpha_composite(banner, ((300-banner.width)//2, (32-banner.height)//2))
canvas.save(args.output / 'banner.png')
rows = bytearray(32 * 4)
for y in range(32):
    struct.pack_into('<I', rows, y * 4, len(rows)//4)
    colors = []
    for x in range(300):
        r, g, b, a = canvas.getpixel((x, y))
        colors.append(0 if a < 128 else max(1, (r>>3)<<10 | (g>>3)<<5 | b>>3))
    x = 0
    while x < 300:
        end = x + 1
        while end < 300 and colors[end] == colors[x]:
            end += 1
        rows.extend(struct.pack('<HH', colors[x], end-x))
        x = end

for name in ('gumpart.mul', 'gumpidx.mul'):
    shutil.copy2(args.client / name, args.output / name)
index = args.output / 'gumpidx.mul'
before = index.read_bytes()
offset = 64040 * 12
assert len(before) >= offset + 12
old_offset, old_length, old_size = struct.unpack_from('<iii', before, offset)
assert old_offset == -1 or (args.replace_existing and old_length > 0 and old_size in ((300<<16)|45, (300<<16)|32)), 'Gump 64040 occupied; explicitly approve replacing a known title banner'
with (args.output / 'gumpart.mul').open('ab') as art, index.open('r+b') as idx:
    idx.seek(offset)
    idx.write(struct.pack('<iii', art.tell(), len(rows), (300<<16)|32))
    art.write(rows)
after = index.read_bytes()
assert before[:offset] == after[:offset] and before[offset+12:] == after[offset+12:]
# Decode every staged row to verify native dimensions and palette runs.
for y in range(32):
    pos = struct.unpack_from('<I', rows, y*4)[0]*4
    width = 0
    while width < 300:
        color, count = struct.unpack_from('<HH', rows, pos)
        assert 0 < count <= 300-width and color <= 32767
        width += count
        pos += 4
print('Verified gump 64040: 300 x 32; all other index records unchanged.')
for name in ('gumpart.mul', 'gumpidx.mul'):
    print(name, hashlib.sha256((args.output/name).read_bytes()).hexdigest())
