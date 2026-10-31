"""Restart-marker resynchronisation fixtures: libjpeg-turbo (Pillow) writes grayscale baseline JPEGs with a restart interval,
one interval is deliberately corrupted, and libjpeg-turbo's decode of the damaged file is stored as the independent reference.
Only MCUs outside the damaged interval are compared (error concealment inside it is decoder specific).
Writes tests/fixtures/jpeg-restart/<name>.jpg, <name>.ref (raw 8-bit gray) and <name>.meta ("w h mcusPerRow firstBad endBad").
Run: python tools/make_jpeg_restart_fixtures.py
"""
import io
import os
import numpy as np
from PIL import Image

import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from make_jpx_fixtures import picture  # noqa: E402

out = os.path.join(os.path.dirname(__file__), "..", "tests", "fixtures", "jpeg-restart")
os.makedirs(out, exist_ok=True)

W, H = 64, 48
gray = picture(W, H).convert("L")


def encode(blocks, **kw):
    b = io.BytesIO()
    gray.save(b, format="JPEG", quality=88, restart_marker_blocks=blocks, subsampling=0, **kw)
    return b.getvalue()


def corrupt(data, interval, blocks, seed):
    """Flips bytes inside the entropy-coded segment between RST(interval-1) and RST(interval), never creating markers."""
    sos = data.index(b"\xff\xda")
    start = sos + 2 + (data[sos + 2] << 8 | data[sos + 3]) - 0
    pos, n = start, 0
    marks = [start]
    while True:
        i = data.find(b"\xff", pos)
        if i < 0 or data[i + 1] == 0xD9:
            marks.append(i)
            break
        if 0xD0 <= data[i + 1] <= 0xD7:
            marks.append(i + 2)
        pos = i + 2
    lo, hi = marks[interval], marks[interval + 1] - 2
    rnd = np.random.default_rng(seed)
    d = bytearray(data)
    for k in rnd.integers(lo + 1, hi, 4):
        if d[k] != 0xFF and d[k - 1] != 0xFF:  # leave markers and byte stuffing intact
            v = int(rnd.integers(0, 255))  # 0..254, never 0xFF
            d[k] = v
    return bytes(d)


cases = [("rst4-interval3", 4, 3, 1), ("rst4-interval0", 4, 0, 2), ("rst2-interval7", 2, 7, 3), ("rst8-lastinterval", 8, 5, 4)]
for name, blocks, interval, seed in cases:
    data = corrupt(encode(blocks), interval, blocks, seed)
    ref = np.asarray(Image.open(io.BytesIO(data)).convert("L"))
    mpr = W // 8
    open(os.path.join(out, name + ".jpg"), "wb").write(data)
    open(os.path.join(out, name + ".ref"), "wb").write(ref.tobytes())
    open(os.path.join(out, name + ".meta"), "w").write(f"{W} {H} {mpr} {interval * blocks} {(interval + 1) * blocks}")
    print("wrote", name, len(data))
