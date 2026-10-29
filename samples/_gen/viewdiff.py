import sys
from PIL import Image

name = sys.argv[1]
w = int(sys.argv[2]) if len(sys.argv) > 2 else 420
ims = [Image.open(f"cmp/{name}.{k}.png").convert("RGB") for k in ("ours", "ref", "diff")]
ims = [i.resize((w, int(i.height * w / i.width))) for i in ims]
c = Image.new("RGB", (w * 3 + 20, ims[0].height), "white")
for k, i in enumerate(ims):
    c.paste(i, (k * (w + 10), 0))
c.save(f"cmp/v_{name}.png")
