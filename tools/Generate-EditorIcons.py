"""Regenerate FreeDF's PNG/ICO icons using Pillow and the sample suite's local font."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parent.parent
output = root / "tools" / "FreeDF.EditorWeb" / "wwwroot"
size = 1024
image = Image.new("RGBA", (size, size))
draw = ImageDraw.Draw(image)
for y in range(size):
    t = y / (size - 1)
    color = tuple(round(a + (b - a) * t) for a, b in zip((190, 24, 93), (157, 23, 77)))
    draw.line((0, y, size, y), fill=color + (255,))
mask = Image.new("L", image.size)
ImageDraw.Draw(mask).rounded_rectangle((0, 0, size - 1, size - 1), radius=224, fill=255)
font = ImageFont.truetype(str(root / "samples" / "_gen" / "fonts" / "SourceSans3-Semibold.otf"), 672)
left, top, right, bottom = draw.textbbox((0, 0), "F", font=font)
draw.text(((size - right + left) / 2 - left, (size - bottom + top) / 2 - top), "F", font=font, fill="white")
square = image.copy()
image.putalpha(mask)
for name, pixels in [("icon-192.png", 192), ("icon-512.png", 512), ("apple-touch-icon.png", 180), ("favicon-32.png", 32)]:
    (square if name == "icon-512.png" else image).resize((pixels, pixels), Image.Resampling.LANCZOS).save(output / name)
image.save(output / "favicon.ico", sizes=[(16, 16), (32, 32), (48, 48)])
