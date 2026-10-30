"""Raw CCITT streams encoded by libtiff (via Pillow), independent of our decoder, covering /K, /EncodedByteAlign,
/EndOfLine and large widths. Each case writes <name>.bin (the encoded strip) and <name>.exp (packed rows,
black = 0, MSB first, rows padded to bytes) plus a manifest line: name k columns rows byteAlign.
Run: python tools/make_ccitt_option_fixtures.py"""
import io, os, random
from PIL import Image, ImageOps, TiffImagePlugin

out = os.path.join(os.path.dirname(__file__), "..", "tests", "fixtures", "ccitt-options")
os.makedirs(out, exist_ok=True)

def strip(b):
    im = Image.open(io.BytesIO(b)); o = im.tag_v2[273]; c = im.tag_v2[279]
    o = o if isinstance(o, int) else o[0]; c = c if isinstance(c, int) else c[0]
    return b[o:o + c]

def image(w, h, seed, density):
    rnd = random.Random(seed)
    im = Image.new("1", (w, h), 1)
    for y in range(h):
        x = 0
        # runs rather than noise so long make-up codes and 2D vertical modes are exercised
        while x < w:
            run = rnd.choice([1, 2, 3, 5, 8, 31, 64, 65, 200, 1000, 2000]) if rnd.random() > density else rnd.randint(1, 4)
            if rnd.random() < 0.5 and y > 0 and rnd.random() < 0.7:
                for xx in range(x, min(w, x + run)): im.putpixel((xx, y), im.getpixel((xx, y - 1)))
            else:
                v = rnd.randint(0, 1)
                for xx in range(x, min(w, x + run)): im.putpixel((xx, y), v)
            x += run
    return im

# (compression, T4Options, K, byteAlign): T4Options bit0 = 2D, bit2 = EOL fill bits.
modes = {
    "g4": ("group4", None, -1, False),
    "g3-eol": ("group3", 0, 0, False),
    "g3-eol-fill": ("group3", 4, 0, False),
    "g3-2d": ("group3", 1, 4, False),
    "g3-2d-fill": ("group3", 5, 4, False),
    "rle-align": ("tiff_ccitt", None, 0, True),
}
widths = [1, 7, 8, 9, 200, 1728, 2560, 5000]
manifest = []
for mode, (comp, opt, k, align) in modes.items():
    for w in widths:
        h = 24
        im = image(w, h, w * 31 + len(mode), 0.5)
        ifd = TiffImagePlugin.ImageFileDirectory_v2()
        if opt is not None: ifd[292] = opt
        buf = io.BytesIO()
        ImageOps.invert(im.convert("L")).convert("1").save(buf, format="TIFF", compression=comp, tiffinfo=ifd)
        name = f"{mode}-{w}"
        open(os.path.join(out, name + ".bin"), "wb").write(strip(buf.getvalue()))
        open(os.path.join(out, name + ".exp"), "wb").write(im.tobytes())
        manifest.append(f"{name} {k} {w} {h} {int(align)}")
open(os.path.join(out, "manifest.txt"), "w").write("\n".join(manifest) + "\n")
print("ok", len(manifest))
