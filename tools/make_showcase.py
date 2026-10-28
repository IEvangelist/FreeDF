"""Builds tests/fixtures/showcase.pdf: a colourful 2-page document exercising gradients, transparency, curves and text."""
import math
from reportlab.pdfgen import canvas
from reportlab.lib.colors import Color, HexColor

W, H = 612, 792
c = canvas.Canvas("tests/fixtures/showcase.pdf", pagesize=(W, H))
c.setTitle("Showcase")

# Page 1: gradient header, translucent circles, rose curve, text
c.saveState(); p0 = c.beginPath(); p0.rect(0, H - 160, W, 160); c.clipPath(p0, stroke=0, fill=0)
c.linearGradient(0, H - 160, W, H, (HexColor("#512BD4"), HexColor("#E91E8C")))
c.restoreState()
c.setFillColor(HexColor("#FFFFFF"))
c.setFont("Helvetica-Bold", 44)
c.drawString(48, H - 95, "C# PDF Toolkit")
c.setFont("Helvetica", 18)
c.drawString(50, H - 128, "Parser, renderer and editor - ISO 32000-2")

for i, col in enumerate(["#FF5252", "#40C4FF", "#FFD740"]):
    c.setFillColor(Color(*HexColor(col).rgb(), alpha=0.6))
    c.circle(210 + i * 95 + (i == 1) * 0, 520 + (i == 1) * 70, 100, stroke=0, fill=1)

c.setStrokeColor(HexColor("#512BD4")); c.setLineWidth(2)
p = c.beginPath()
for k in range(0, 721):
    t = math.radians(k * 0.5)
    r = 110 * math.cos(5 * t)
    x, y = 306 + r * math.cos(t), 250 + r * math.sin(t)
    p.moveTo(x, y) if k == 0 else p.lineTo(x, y)
c.drawPath(p, stroke=1, fill=0)

c.setFillColor(HexColor("#222222")); c.setFont("Times-Italic", 16)
c.drawCentredString(W / 2, 70, "Vector paths, alpha blending, shadings and fonts")

# Page 2: rounded cards, dashed lines, bars
c.showPage()
c.setFillColor(HexColor("#F4F1FF")); c.rect(0, 0, W, H, stroke=0, fill=1)
palette = ["#512BD4", "#E91E8C", "#00B8A9", "#FF9F1C", "#3A86FF"]
for i, col in enumerate(palette):
    x = 48 + i * 106
    h = 120 + i * 60
    c.setFillColor(HexColor(col)); c.roundRect(x, 400, 90, h, 12, stroke=0, fill=1)
c.setStrokeColor(HexColor("#555555")); c.setDash(6, 4); c.setLineWidth(1.5)
for y in range(120, 380, 40): c.line(48, y, W - 48, y)
c.setDash(); c.setFillColor(HexColor("#222222")); c.setFont("Helvetica-Bold", 26)
c.drawString(48, 720, "Quarterly results")
c.save()
