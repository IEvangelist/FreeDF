"""Generates tests/fixtures/fonts/cff.pdf: simple fonts with an embedded bare CFF (Type1C) and an OpenType-CFF wrapper."""
import io, os
import pikepdf
from pikepdf import Pdf, Dictionary, Name, Array, Stream
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.t2CharStringPen import T2CharStringPen
from fontTools.ttLib import TTFont

out = os.path.join(os.path.dirname(__file__), "..", "tests", "fixtures", "fonts")
os.makedirs(out, exist_ok=True)

order = [".notdef", "A", "B", "space"]
fb = FontBuilder(1000, isTTF=False)
fb.setupGlyphOrder(order)
fb.setupCharacterMap({65: "A", 66: "B", 32: "space"})

def make(draw):
    pen = T2CharStringPen(700, None)
    draw(pen)
    return pen.getCharString()

def empty(p): pass
def square(p):  # 0..600 x 0..700
    p.moveTo((0, 0)); p.lineTo((600, 0)); p.lineTo((600, 700)); p.lineTo((0, 700)); p.closePath()
def blob(p):    # cubic "circle" within 0..600 x 0..700
    p.moveTo((300, 0))
    p.curveTo((466, 0), (600, 157), (600, 350))
    p.curveTo((600, 543), (466, 700), (300, 700))
    p.curveTo((134, 700), (0, 543), (0, 350))
    p.curveTo((0, 157), (134, 0), (300, 0))
    p.closePath()

cs = {".notdef": make(empty), "A": make(square), "B": make(blob), "space": make(empty)}
fb.setupCFF("TestCff", {"FullName": "TestCff"}, cs, {})
fb.setupHorizontalMetrics({g: (700, 0) for g in order})
fb.setupHorizontalHeader(ascent=800, descent=-200)
fb.setupNameTable({"familyName": "TestCff", "styleName": "Regular"})
fb.setupOS2(sTypoAscender=800, usWinAscent=800, usWinDescent=200)
fb.setupPost()
buf = io.BytesIO(); fb.save(buf)
otf = buf.getvalue()
bare = TTFont(io.BytesIO(otf)).getTableData("CFF ")

pdf = Pdf.new()

def font(data, subtype):
    ff = Stream(pdf, data)
    if subtype:
        ff.Subtype = Name("/" + subtype)
    desc = pdf.make_indirect(Dictionary(Type=Name.FontDescriptor, FontName=Name.TestCff, Flags=32,
                                        FontBBox=Array([0, 0, 600, 700]), ItalicAngle=0, Ascent=800, Descent=-200,
                                        CapHeight=700, StemV=80, FontFile3=ff))
    return pdf.make_indirect(Dictionary(Type=Name.Font, Subtype=Name.Type1, BaseFont=Name.TestCff,
                                        FirstChar=65, LastChar=66, Widths=Array([700, 700]),
                                        Encoding=Name.WinAnsiEncoding, FontDescriptor=desc))

f1 = font(bare, "Type1C")
f2 = font(otf, "OpenType")
content = b"BT /F1 50 Tf 10 20 Td (AB) Tj ET BT /F2 50 Tf 10 120 Td (AB) Tj ET"
page = pdf.make_indirect(Dictionary(Type=Name.Page, MediaBox=Array([0, 0, 200, 200]),
                                    Resources=Dictionary(Font=Dictionary(F1=f1, F2=f2)),
                                    Contents=Stream(pdf, content)))
pdf.Root.Pages = pdf.make_indirect(Dictionary(Type=Name.Pages, Kids=Array([page]), Count=1))
pdf.Root.Type = Name.Catalog
pdf.save(os.path.join(out, "cff.pdf"))
print("ok", len(bare), len(otf))
