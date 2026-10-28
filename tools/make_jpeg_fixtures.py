"""Generates progressive/baseline JPEG fixtures and expected decoded pixels (via PIL/libjpeg)."""
import os
from PIL import Image, ImageDraw

out = os.path.join(os.path.dirname(__file__), "..", "tests", "fixtures", "jpeg")
os.makedirs(out, exist_ok=True)
im = Image.new("RGB", (67, 45), (255, 255, 255))
d = ImageDraw.Draw(im)
d.rectangle([5, 5, 40, 30], fill=(200, 30, 30))
d.ellipse([25, 15, 62, 42], fill=(20, 60, 220))
d.line([0, 0, 66, 44], fill=(0, 160, 0), width=3)
for name, kw in {"baseline": {}, "progressive": {"progressive": True}, "gray-progressive": {"progressive": True}}.items():
    src = im.convert("L") if name.startswith("gray") else im
    path = os.path.join(out, name + ".jpg")
    src.save(path, "JPEG", quality=90, subsampling=2 if not name.startswith("gray") else 0, **kw)
    ref = Image.open(path)
    ref.load()
    with open(os.path.join(out, name + ".raw"), "wb") as f:
        f.write(ref.tobytes())
print("ok")
