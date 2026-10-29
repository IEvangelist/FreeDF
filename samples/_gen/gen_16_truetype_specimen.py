import io
import os
from fontTools import subset
from fontTools.ttLib import TTFont as FTFont
from theme import *

FONTS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fonts")


def simple_truetype(d, filename, prefix="AAAAAA"):
    """Subset a TrueType font as a *simple* font: /Subtype /TrueType, WinAnsiEncoding, /Widths, FontFile2."""
    path = os.path.join(FONTS, filename)
    ft = FTFont(path)
    upem = ft["head"].unitsPerEm
    cmap = ft.getBestCmap()
    hmtx = ft["hmtx"]
    widths = []
    for code in range(32, 256):
        g = cmap.get(code)
        widths.append(int(round(hmtx[g][0] * 1000 / upem)) if g else 0)
    opt = subset.Options()
    opt.layout_features = ["kern"]
    opt.hinting = True
    opt.notdef_outline = True
    opt.glyph_names = False
    opt.name_IDs = [1, 2, 3, 4, 6]
    s = subset.Subsetter(opt)
    s.populate(unicodes=list(range(32, 127)) + [0xA0, 0xA9, 0xB0, 0xB1, 0xD7, 0xE9, 0xFC, 0x2022, 0x2013, 0x2014])
    sub = FTFont(path)
    s.subset(sub)
    buf = io.BytesIO()
    sub.save(buf)
    data = buf.getvalue()
    ff = d.stream(f"/Length1 {len(data)}", data)
    head, hhea = ft["head"], ft["hhea"]
    sc = 1000 / upem
    psname = ft["name"].getDebugName(6)
    desc = d.add(f"<< /Type /FontDescriptor /FontName /{prefix}+{psname} /Flags 32 /FontBBox [{int(head.xMin * sc)} {int(head.yMin * sc)} {int(head.xMax * sc)} {int(head.yMax * sc)}] "
                 f"/ItalicAngle 0 /Ascent {int(hhea.ascent * sc)} /Descent {int(hhea.descent * sc)} /CapHeight 729 /StemV 80 /FontFile2 {ff} 0 R >>")
    return d.add(f"<< /Type /Font /Subtype /TrueType /BaseFont /{prefix}+{psname} /FirstChar 32 /LastChar 255 /Widths [{' '.join(map(str, widths))}] "
                 f"/Encoding /WinAnsiEncoding /FontDescriptor {desc} 0 R >>"), FTFont(path), widths


def build(path):
    d = Doc("TrueType specimen", subject="Bitstream Vera Sans: composite (Identity-H, CIDFontType2) and simple (/TrueType, WinAnsi) embedded subsets", lang="en-US")
    t = Type(d)
    vera = d.font("Vera.ttf")
    vera_b = d.font("VeraBd.ttf")
    sref, _, widths = simple_truetype(d, "Vera.ttf")
    pg = d.page(*A4)
    background(pg, P["paper"])
    page_header(pg, t, "TrueType specimen", "Typography  /  TrueType", 1, 1)
    pg.text(t.sans, 11, M, pg.h - 106, "Bitstream Vera Sans embedded as glyf-outline TrueType, in both PDF flavours.", color=P["slate"])

    # hero line
    box(pg, M, pg.h - 330, pg.w - 2 * M, 190, fill=P["ink"], r=14)
    pg.text(vera_b, 46, M + 28, pg.h - 220, "Hamburgefonts", color="#FFFFFF")
    pg.text(vera, 46, M + 28, pg.h - 280, "Quick brown fox", color="#A5B4FC")
    pg.text(vera, 10, M + 30, pg.h - 318, "Composite font: /Type0 + /CIDFontType2 + /CIDToGIDMap /Identity + FontFile2 subset", color="#94A3B8", kern=False)

    # size ladder
    y = pg.h - 380
    eyebrow(pg, t, M, y + 12, "Size ladder")
    for sz in (6, 8, 10, 12, 16, 20, 24):
        pg.text(vera, sz, M, y - sz * 0.2, "The five boxing wizards jump." if sz > 16 else "The five boxing wizards jump quickly.", color=P["ink"])
        pg.text(t.mono, 7, pg.w - M - 40, y - 2, f"{sz} pt", color=P["muted"], kern=False)
        y -= sz * 1.25 + 8

    # simple TrueType glyph table
    y -= 14
    eyebrow(pg, t, M, y, "Simple font (/TrueType, WinAnsiEncoding) - codes 32 to 126")
    fname = pg.reg("Font", f"{sref} 0 R")
    y -= 24
    codes = list(range(33, 127))
    per = 32
    cell = (pg.w - 2 * M) / per
    for r in range(3):
        for c in range(per):
            i = r * per + c
            if i >= len(codes):
                break
            ch = chr(codes[i])
            x = M + c * cell
            esc = ch.replace("\\", "\\\\").replace("(", "\\(").replace(")", "\\)")
            pg.raw(f"BT /{fname} 15 Tf {rgbs(P['ink'])} rg 1 0 0 1 {n(x + (cell - widths[codes[i] - 32] * 15 / 1000) / 2)} {n(y - r * 30)} Tm ({esc}) Tj ET")
    y -= 3 * 30 + 12
    pg.raw(f"BT /{fname} 11 Tf {rgbs(P['slate'])} rg 1 0 0 1 {n(M)} {n(y)} Tm (Simple fonts address glyphs through 8-bit codes; the viewer maps codes to glyphs via the cmap.) Tj ET")
    pg.raw(f"BT /{fname} 11 Tf {rgbs(P['slate'])} rg 1 0 0 1 {n(M)} {n(y - 16)} Tm (\\251 2026   \\260 \\261 \\327 \\351 \\374  \\225  \\226 \\227) Tj ET")

    # paragraph in Vera
    y -= 60
    paragraph(pg, vera, 10.5, M, y, 300,
              "Bitstream Vera is a free typeface with a permissive licence: it may be embedded, subset and redistributed. TrueType outlines are quadratic splines; this page renders them as filled paths in the page's own coordinate space.",
              color=P["ink"], leading=15.5, kern=False)
    d.save(path)


OUT = "16-truetype-specimen.pdf"
