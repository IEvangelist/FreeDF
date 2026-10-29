"""Contact sheet of rendered pages: python sheet.py <name-fragment> -> out/sheet.png (pages side by side, scaled)."""
import glob
import os
import sys
from PIL import Image

here = os.path.dirname(os.path.abspath(__file__))
frag = sys.argv[1]
scale = float(sys.argv[2]) if len(sys.argv) > 2 else 0.75
files = sorted(glob.glob(os.path.join(here, "out", f"*{frag}*.png")))
files = [f for f in files if not f.endswith("sheet.png")]
ims = [Image.open(f).convert("RGB") for f in files]
ims = [im.resize((int(im.width * scale), int(im.height * scale)), Image.LANCZOS) for im in ims]
W = sum(i.width for i in ims) + 8 * (len(ims) - 1)
H = max(i.height for i in ims)
sheet = Image.new("RGB", (W, H), (120, 120, 120))
x = 0
for im in ims:
    sheet.paste(im, (x, 0))
    x += im.width + 8
sheet.save(os.path.join(here, "out", "sheet.png"))
print(len(ims), sheet.size)
