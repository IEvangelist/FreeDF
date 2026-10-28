"""Generates performance fixtures: text-heavy, vector/clip-heavy and image-heavy documents."""
import os, random
from reportlab.pdfgen import canvas
from reportlab.lib.pagesizes import letter
from PIL import Image, ImageDraw

out = os.path.join(os.path.dirname(__file__), "..", "bench", "fixtures")
os.makedirs(out, exist_ok=True)
random.seed(7)
words = "the quick brown fox jumps over a lazy dog while portable document formats render glyphs".split()

c = canvas.Canvas(os.path.join(out, "text.pdf"), pagesize=letter, pageCompression=1)
for p in range(5):
    c.setFont("Helvetica", 9)
    for line in range(70):
        c.drawString(40, 760 - line * 10.5, " ".join(random.choice(words) for _ in range(16)))
    c.showPage()
c.save()

c = canvas.Canvas(os.path.join(out, "vector.pdf"), pagesize=letter, pageCompression=1)
for p in range(3):
    for i in range(600):
        c.setStrokeColorRGB(random.random(), random.random(), random.random())
        c.setFillColorRGB(random.random(), random.random(), random.random())
        c.setLineWidth(random.random() * 3)
        if i % 7 == 0: c.setDash(3, 2)
        else: c.setDash()
        x, y = random.random() * 500, random.random() * 700
        pth = c.beginPath(); pth.moveTo(x, y); pth.curveTo(x + 40, y + 90, x + 90, y - 30, x + 100, y + 60); pth.lineTo(x + 10, y + 80)
        if i % 5 == 0:
            c.saveState(); c.clipPath(pth, stroke=0, fill=0); c.rect(x - 20, y - 20, 160, 160, fill=1, stroke=0); c.restoreState()
        else: c.drawPath(pth, stroke=1, fill=i % 2)
    c.showPage()
c.save()

im = Image.new("RGB", (1600, 1200))
d = ImageDraw.Draw(im)
for i in range(300):
    x0, y0 = random.randint(0, 1500), random.randint(0, 1100)
    d.ellipse([x0, y0, x0 + random.randint(5, 100), y0 + random.randint(5, 100)], outline=(random.randint(0, 255), random.randint(0, 255), random.randint(0, 255)), width=4)
im.save(os.path.join(out, "photo.jpg"), quality=85, progressive=False)
im.save(os.path.join(out, "photo-prog.jpg"), quality=85, progressive=True)
c = canvas.Canvas(os.path.join(out, "image.pdf"), pagesize=letter)
c.drawImage(os.path.join(out, "photo.jpg"), 20, 100, 570, 570)
c.showPage(); c.save()
print("ok")
