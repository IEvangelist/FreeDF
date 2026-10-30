"""Generates deterministic bare and OpenType CFF Expert-encoding PDF fixtures."""
import io
import os

import pikepdf
from pikepdf import Array, Dictionary, Name, Pdf, Stream
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.t2CharStringPen import T2CharStringPen
from fontTools.ttLib import TTFont
from fontTools.cffLib import cffISOAdobeStrings, cffIExpertStrings, cffExpertSubsetStrings


def make_charstring(width, draw):
    pen = T2CharStringPen(width, None)
    draw(pen)
    return pen.getCharString()


def empty(_pen):
    pass


def exclam(pen):
    pen.moveTo((0, 0))
    pen.lineTo((180, 0))
    pen.lineTo((180, 500))
    pen.lineTo((0, 500))
    pen.closePath()


def hungarumlaut(pen):
    pen.moveTo((0, 0))
    pen.lineTo((500, 0))
    pen.lineTo((250, 300))
    pen.closePath()


def dollar_oldstyle(pen):
    pen.moveTo((0, 300))
    pen.lineTo((200, 600))
    pen.lineTo((400, 300))
    pen.lineTo((200, 0))
    pen.closePath()


def dollar_superior(pen):
    pen.moveTo((0, 0))
    pen.lineTo((300, 0))
    pen.lineTo((300, 400))
    pen.lineTo((0, 400))
    pen.closePath()


def quote(pen):
    pen.moveTo((0, 300))
    pen.lineTo((80, 300))
    pen.lineTo((80, 500))
    pen.lineTo((0, 500))
    pen.closePath()
    pen.moveTo((180, 300))
    pen.lineTo((260, 300))
    pen.lineTo((260, 500))
    pen.lineTo((180, 500))
    pen.closePath()


def build_otf(glyph_order, shapes, ps_name, encoding="ExpertEncoding"):
    fb = FontBuilder(1000, isTTF=False)
    fb.setupGlyphOrder(glyph_order)
    fb.setupCharacterMap({})
    charstrings = {
        name: make_charstring(width, draw)
        for name, (width, draw) in shapes.items()
    }
    fb.setupCFF(ps_name, {"FullName": ps_name, "Encoding": encoding}, charstrings, {})
    fb.setupHorizontalMetrics({name: (shapes[name][0], 0) for name in glyph_order})
    fb.setupHorizontalHeader(ascent=800, descent=-200)
    fb.setupNameTable({"familyName": ps_name, "styleName": "Regular"})
    fb.setupOS2(sTypoAscender=800, usWinAscent=800, usWinDescent=200)
    fb.setupPost()
    out = io.BytesIO()
    fb.save(out)
    return out.getvalue()


def add_pdf_font(pdf, font_data, subtype, base_font, encoding=None, to_unicode=None):
    font_stream = Stream(pdf, font_data)
    font_stream.Subtype = Name("/" + subtype)
    descriptor = pdf.make_indirect(Dictionary(
        Type=Name.FontDescriptor,
        FontName=Name("/" + base_font),
        Flags=4,
        FontBBox=Array([0, 0, 600, 700]),
        ItalicAngle=0,
        Ascent=800,
        Descent=-200,
        CapHeight=700,
        StemV=80,
        FontFile3=font_stream,
    ))
    entries = dict(
        Type=Name.Font,
        Subtype=Name.Type1,
        BaseFont=Name("/" + base_font),
        FontDescriptor=descriptor,
    )
    if encoding is not None:
        entries["Encoding"] = encoding
    if to_unicode is not None:
        entries["ToUnicode"] = to_unicode
    return pdf.make_indirect(Dictionary(**entries))


expert_shapes = {
    ".notdef": (900, empty),
    "space": (300, empty),
    "exclamsmall": (510, exclam),
    "Hungarumlautsmall": (620, hungarumlaut),
    "dollaroldstyle": (740, dollar_oldstyle),
}
subset_shapes = {
    ".notdef": (900, empty),
    "space": (300, empty),
    "dollaroldstyle": (740, dollar_oldstyle),
    "dollarsuperior": (380, dollar_superior),
}
iso_shapes = {
    ".notdef": (900, empty),
    "space": (300, empty),
    "exclam": (480, exclam),
    "quotedbl": (520, quote),
}

expert_order = list(cffIExpertStrings[:len(expert_shapes)])
subset_order = list(cffExpertSubsetStrings[:len(subset_shapes)])
iso_order = list(cffISOAdobeStrings[:len(iso_shapes)])
assert set(expert_order) == set(expert_shapes)
assert set(subset_order) == set(subset_shapes)
assert set(iso_order) == set(iso_shapes)

expert_otf = build_otf(expert_order, expert_shapes, "SyntheticExpert")
subset_otf = build_otf(subset_order, subset_shapes, "SyntheticExpertSubset")
iso_otf = build_otf(iso_order, iso_shapes, "SyntheticIsoAdobe", "StandardEncoding")
expert_bare = TTFont(io.BytesIO(expert_otf)).getTableData("CFF ")
subset_bare = TTFont(io.BytesIO(subset_otf)).getTableData("CFF ")
iso_bare = TTFont(io.BytesIO(iso_otf)).getTableData("CFF ")

pdf = Pdf.new()
unicode_cmap = Stream(pdf, b"1 beginbfchar <21> <0058> endbfchar")
differences = Dictionary(Differences=Array([33, Name.dollaroldstyle]))
standard_differences = Dictionary(
    BaseEncoding=Name.StandardEncoding,
    Differences=Array([33, Name.dollaroldstyle]),
)

fonts = Dictionary(
    ExpertBare=add_pdf_font(pdf, expert_bare, "Type1C", "SyntheticExpert"),
    ExpertOpenType=add_pdf_font(pdf, expert_otf, "OpenType", "SyntheticExpert"),
    SubsetBare=add_pdf_font(pdf, subset_bare, "Type1C", "SyntheticExpertSubset"),
    SubsetOpenType=add_pdf_font(pdf, subset_otf, "OpenType", "SyntheticExpertSubset"),
    IsoBare=add_pdf_font(pdf, iso_bare, "Type1C", "SyntheticIsoAdobe"),
    IsoOpenType=add_pdf_font(pdf, iso_otf, "OpenType", "SyntheticIsoAdobe"),
    ExpertDifferences=add_pdf_font(pdf, expert_bare, "Type1C", "SyntheticExpert", differences),
    ExpertStandard=add_pdf_font(pdf, expert_bare, "Type1C", "SyntheticExpert", standard_differences),
    ExpertMac=add_pdf_font(pdf, expert_bare, "Type1C", "SyntheticExpert", Name.MacExpertEncoding),
    ExpertToUnicode=add_pdf_font(pdf, expert_bare, "Type1C", "SyntheticExpert", to_unicode=unicode_cmap),
)

content = b"BT /ExpertBare 12 Tf <212223> Tj ET"
page = pdf.make_indirect(Dictionary(
    Type=Name.Page,
    MediaBox=Array([0, 0, 200, 200]),
    Resources=Dictionary(Font=fonts),
    Contents=Stream(pdf, content),
))
pdf.Root.Pages = pdf.make_indirect(Dictionary(Type=Name.Pages, Kids=Array([page]), Count=1))
pdf.Root.Type = Name.Catalog

out = os.path.join(os.path.dirname(__file__), "..", "tests", "fixtures", "fonts", "cff-expert.pdf")
pdf.save(out, deterministic_id=True)
print("ok", len(expert_bare), len(expert_otf), len(subset_bare), len(subset_otf), len(iso_bare), len(iso_otf))
