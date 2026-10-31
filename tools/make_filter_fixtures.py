"""TIFF predictor 2 fixtures (16-bit and 4-bit gray) for /Predictor 2 with /FlateDecode.
The differenced stream is built here (hand-derived), wrapped in a big-endian Adobe-Deflate TIFF (Compression 8, Predictor 2)
and decoded by libtiff via Pillow; the script asserts libtiff reproduces the source samples, so libtiff is the independent
decoder of the stream. Writes <name>.bin (zlib stream) and <name>.exp (undifferenced packed rows).
Run: python tools/make_filter_fixtures.py"""
import io, os, struct, zlib
import numpy as np
from PIL import Image

out = os.path.join(os.path.dirname(__file__), "..", "tests", "fixtures", "filters")
os.makedirs(out, exist_ok=True)

def tiff(w, h, bps, photometric, payload):
    # big-endian single-strip TIFF with Compression=8 (Deflate) and Predictor=2
    entries = [(256, 3, w), (257, 3, h), (258, 3, bps), (259, 3, 8), (262, 3, photometric), (273, 4, 0), (277, 3, 1),
               (278, 3, h), (279, 4, len(payload)), (317, 3, 2)]
    ifd_off = 8; data_off = ifd_off + 2 + 12 * len(entries) + 4
    b = b"MM\x00*" + struct.pack(">I", ifd_off) + struct.pack(">H", len(entries))
    for tag, typ, val in entries:
        if tag == 273: val = data_off
        b += struct.pack(">HHI", tag, typ, 1) + (struct.pack(">HH", val, 0) if typ == 3 else struct.pack(">I", val))
    return b + struct.pack(">I", 0) + payload

rng = np.random.default_rng(5)
# 16-bit gray
w, h = 9, 6
src = rng.integers(0, 65536, (h, w), dtype=np.uint32)
diff = src.copy(); diff[:, 1:] = (src[:, 1:] - src[:, :-1]) & 0xFFFF
raw = diff.astype(">u2").tobytes(); z = zlib.compress(raw)
img = Image.open(io.BytesIO(tiff(w, h, 16, 1, z)))
lt = np.array(img).astype(np.uint32)
assert (lt == src).all(), "libtiff disagrees with 16-bit source"
open(os.path.join(out, "pred2-16.bin"), "wb").write(z)
open(os.path.join(out, "pred2-16.exp"), "wb").write(src.astype(">u2").tobytes())

# 4-bit gray (rows padded to bytes; odd width so a pad nibble exists)
w, h = 7, 5
src = rng.integers(0, 16, (h, w), dtype=np.uint32)
diff = src.copy(); diff[:, 1:] = (src[:, 1:] - src[:, :-1]) & 0xF
def pack(a):
    rows = []
    for r in a:
        v = list(r) + [0] * (-len(r) % 2)
        rows.append(bytes((v[i] << 4) | v[i + 1] for i in range(0, len(v), 2)))
    return b"".join(rows)
z = zlib.compress(pack(diff))  # libtiff rejects Predictor 2 below 8 bits, so this fixture is hand-derived (T.2/ISO 32000 §7.4.4.4 semantics)
open(os.path.join(out, "pred2-4.bin"), "wb").write(z)
open(os.path.join(out, "pred2-4.exp"), "wb").write(pack(src))
print("ok")

# LZW strip from libtiff (EarlyChange 1, table growth to 12 bits and clear-code resets); expectation = source pixels.
from PIL import TiffImagePlugin
rng2 = np.random.default_rng(11)
pix = (rng2.integers(0, 4, (200, 150), dtype=np.uint8) * 60 + (np.arange(150) // 7).astype(np.uint8)[None, :]).astype(np.uint8)
buf = io.BytesIO(); Image.fromarray(pix).save(buf, format="TIFF", compression="tiff_lzw")
t = Image.open(io.BytesIO(buf.getvalue())); o = t.tag_v2[273]; c = t.tag_v2[279]
o = o if isinstance(o, int) else o[0]; c = c if isinstance(c, int) else c[0]
open(os.path.join(out, "lzw-libtiff.bin"), "wb").write(buf.getvalue()[o:o + c])
open(os.path.join(out, "lzw-libtiff.exp"), "wb").write(pix.tobytes())
