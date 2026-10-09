"""Stage reusable menu button states as native UO RGB555/RLE gumps.

The source sheet contains the normal state above the pressed state. Only staged
copies are modified; the live client data directory is never an output target.
"""

import argparse
import hashlib
import shutil
import struct
from pathlib import Path

from PIL import Image


WIDTH = 56
HEIGHT = 56
GUMP_IDS = (64042, 64043)
STATE_NAMES = ("normal", "pressed")


def prepare_states(source_path):
    with Image.open(source_path) as source_file:
        assert "A" in source_file.getbands(), "Source must contain genuine alpha transparency"
        source = source_file.convert("RGBA")
    assert source.width > 0 and source.height >= 2, "Source sprite sheet is empty"
    split = source.height // 2
    states = []
    aspect_ratios = []
    for name, top, bottom in (
        ("normal", 0, split),
        ("pressed", split, source.height),
    ):
        state = source.crop((0, top, source.width, bottom))
        alpha = state.getchannel("A")
        assert alpha.getextrema()[0] < 128, f"{name}: transparent margins are missing"
        bounds = alpha.point(lambda value: 255 if value >= 128 else 0).getbbox()
        assert bounds is not None, f"{name}: no opaque artwork"
        left, upper, right, lower = bounds
        assert left > 0 and upper > 0 and right < state.width and lower < state.height, (
            f"{name}: artwork must have transparent margins on all sides"
        )
        cropped = state.crop(bounds)
        aspect_ratios.append(cropped.width / cropped.height)
        cropped.thumbnail((WIDTH, HEIGHT), Image.Resampling.LANCZOS)
        canvas = Image.new("RGBA", (WIDTH, HEIGHT))
        canvas.alpha_composite(cropped, ((WIDTH - cropped.width) // 2, (HEIGHT - cropped.height) // 2))
        states.append(canvas)
    assert max(aspect_ratios) / min(aspect_ratios) <= 1.12, (
        "Normal and pressed artwork have inconsistent proportions"
    )
    return states


def encode(image):
    rows = bytearray(HEIGHT * 4)
    for y in range(HEIGHT):
        struct.pack_into("<I", rows, y * 4, len(rows) // 4)
        colors = []
        for x in range(WIDTH):
            red, green, blue, alpha = image.getpixel((x, y))
            colors.append(0 if alpha < 128 else max(1, (red >> 3) << 10 | (green >> 3) << 5 | blue >> 3))
        x = 0
        while x < WIDTH:
            end = x + 1
            while end < WIDTH and colors[end] == colors[x]:
                end += 1
            rows.extend(struct.pack("<HH", colors[x], end - x))
            x = end
    return bytes(rows)


def decode(data, width, height):
    assert width == WIDTH and height == HEIGHT, "Unexpected staged dimensions"
    assert len(data) >= height * 4, "Truncated native row table"
    decoded = Image.new("RGBA", (width, height))
    previous_end = height * 4
    for y in range(height):
        position = struct.unpack_from("<I", data, y * 4)[0] * 4
        assert position == previous_end, "Native rows are not contiguous"
        x = 0
        while x < width:
            assert position + 4 <= len(data), "Truncated native run"
            color, count = struct.unpack_from("<HH", data, position)
            assert 0 < count <= width - x and color <= 32767, "Invalid native color/run"
            rgba = (0, 0, 0, 0) if color == 0 else (
                ((color >> 10) & 31) << 3,
                ((color >> 5) & 31) << 3,
                (color & 31) << 3,
                255,
            )
            for pixel_x in range(x, x + count):
                decoded.putpixel((pixel_x, y), rgba)
            x += count
            position += 4
        previous_end = position
    assert previous_end == len(data), "Unconsumed native data"
    assert decoded.getchannel("A").getbbox() is not None, "Decoded button is empty"
    assert decoded.getchannel("A").getextrema()[0] == 0, "Decoded button lacks transparency"
    return decoded


def file_hash(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("client", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument(
        "--replace-existing",
        action="store_true",
        help=f"Replace existing {WIDTH}x{HEIGHT} menu button states only",
    )
    args = parser.parse_args()
    assert args.client.resolve() != args.output.resolve(), "Output must not be the live client directory"
    for name in ("gumpart.mul", "gumpidx.mul"):
        live_file = args.client / name
        staged_file = args.output / name
        assert live_file.resolve() != staged_file.resolve() and (
            not staged_file.exists() or not live_file.samefile(staged_file)
        ), (
            f"Staged {name} must not resolve to the live client file"
        )
    source_path = Path(__file__).parent / "button-states.png"
    states = prepare_states(source_path)
    payloads = [encode(state) for state in states]
    index_before = (args.client / "gumpidx.mul").read_bytes()
    art_length = (args.client / "gumpart.mul").stat().st_size
    for gump_id in GUMP_IDS:
        offset = gump_id * 12
        assert len(index_before) >= offset + 12, f"Gump {gump_id} index record is missing"
        old_offset, old_length, old_size = struct.unpack_from("<iii", index_before, offset)
        assert old_offset == -1 or (
            args.replace_existing
            and old_offset >= 0
            and old_length > 0
            and old_offset + old_length <= art_length
            and old_size == ((WIDTH << 16) | HEIGHT)
        ), f"Gump {gump_id} is occupied; only an explicitly approved {WIDTH}x{HEIGHT} button can be replaced"

    args.output.mkdir(parents=True, exist_ok=True)
    for name in ("gumpart.mul", "gumpidx.mul"):
        shutil.copy2(args.client / name, args.output / name)
    index_path = args.output / "gumpidx.mul"
    art_path = args.output / "gumpart.mul"
    with art_path.open("ab") as art, index_path.open("r+b") as index:
        for gump_id, payload in zip(GUMP_IDS, payloads):
            index.seek(gump_id * 12)
            index.write(struct.pack("<iii", art.tell(), len(payload), (WIDTH << 16) | HEIGHT))
            art.write(payload)

    index_after = index_path.read_bytes()
    assert len(index_before) == len(index_after), "Native index length changed"
    cursor = 0
    for gump_id in GUMP_IDS:
        offset = gump_id * 12
        assert index_before[cursor:offset] == index_after[cursor:offset], "An unrelated index record changed"
        cursor = offset + 12
    assert index_before[cursor:] == index_after[cursor:], "An unrelated index record changed"

    with art_path.open("rb") as art:
        for gump_id, name, expected_payload in zip(GUMP_IDS, STATE_NAMES, payloads):
            offset, length, size = struct.unpack_from("<iii", index_after, gump_id * 12)
            assert offset >= art_length and length == len(expected_payload), "Invalid appended artwork location"
            art.seek(offset)
            staged_payload = art.read(length)
            assert staged_payload == expected_payload, "Staged native data differs from the encoded source"
            decoded = decode(staged_payload, size >> 16, size & 65535)
            decoded.save(args.output / f"{name}.png")
            print(f"Verified gump {gump_id} ({name}): {WIDTH} x {HEIGHT}; native palette/runs decoded.")
    print("All other index bytes are unchanged, including removed shield slots and the title banner.")
    for name in ("gumpart.mul", "gumpidx.mul", "normal.png", "pressed.png"):
        print(name, file_hash(args.output / name))


if __name__ == "__main__":
    main()
