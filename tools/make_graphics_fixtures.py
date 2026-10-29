"""Generates graphics/colour fixtures (transparency groups, blend modes, soft masks, Cal*/Lab/ICC colour, mesh shadings 4-7)
into tests/fixtures/graphics. Requires pikepdf. Usage: python tools/make_graphics_fixtures.py"""
import struct
from pathlib import Path
import pikepdf
from pikepdf import Name, Dictionary, Array, Stream

OUT = Path(__file__).resolve().parent.parent / "tests" / "fixtures" / "graphics"
OUT.mkdir(parents=True, exist_ok=True)


def new_page(pdf, content, resources, size=(300, 300), page_extra=None):
    page = pdf.add_blank_page(page_size=size)
    page.Contents = pdf.make_stream(content.encode("latin1"))
    page.Resources = resources
    for k, v in (page_extra or {}).items():
        page[k] = v
    return page


def form(pdf, content, group=None, bbox=(0, 0, 300, 300), res=None):
    d = Dictionary(Type=Name.XObject, Subtype=Name.Form, BBox=Array(list(bbox)))
    if group is not None:
        d.Group = group
    if res is not None:
        d.Resources = res
    s = Stream(pdf, content.encode("latin1"))
    for k, v in d.items():
        s[k] = v
    return s


def transparency():
    pdf = pikepdf.Pdf.new()
    gs = Dictionary(
        A=Dictionary(ca=0.5, CA=0.5),
        Mul=Dictionary(BM=Name.Multiply),
        Scr=Dictionary(BM=Name.Screen),
        Hue=Dictionary(BM=Name.Hue), Sat=Dictionary(BM=Name.Saturation),
        Col=Dictionary(BM=Name.Color), Lum=Dictionary(BM=Name.Luminosity),
    )
    inner = Dictionary(ExtGState=Dictionary(M=Dictionary(BM=Name.Multiply), A=Dictionary(ca=0.5)))
    grp = lambda **kw: Dictionary(S=Name.Transparency, **kw)
    cy = "/M gs 0 1 1 rg 0 0 60 60 re f"
    forms = {
        "Fni": form(pdf, cy, grp(), (0, 0, 60, 60), inner),
        "Fi": form(pdf, cy, grp(I=True), (0, 0, 60, 60), inner),
        "Fko": form(pdf, "/A gs 1 0 0 rg 0 0 40 60 re f 0 0 1 rg 20 0 40 60 re f", grp(K=True), (0, 0, 60, 60), inner),
        "Fnk": form(pdf, "/A gs 1 0 0 rg 0 0 40 60 re f 0 0 1 rg 20 0 40 60 re f", grp(), (0, 0, 60, 60), inner),
    }
    mask_tr = form(pdf, "0.25 g 0 0 300 300 re f", grp(CS=Name.DeviceGray))
    mask_bc = form(pdf, "1 g 0 0 30 300 re f", grp(CS=Name.DeviceGray))
    inv = Dictionary(FunctionType=2, Domain=Array([0, 1]), C0=Array([1]), C1=Array([0]), N=1)
    gs["/SM0"] = Dictionary(SMask=Dictionary(Type=Name.Mask, S=Name.Luminosity, G=mask_tr))
    gs["/SM1"] = Dictionary(SMask=Dictionary(Type=Name.Mask, S=Name.Luminosity, G=mask_tr, TR=inv))
    gs["/SM2"] = Dictionary(SMask=Dictionary(Type=Name.Mask, S=Name.Luminosity, G=mask_bc, BC=Array([0.5])))
    gs["/SM3"] = Dictionary(SMask=Dictionary(Type=Name.Mask, S=Name.Luminosity, G=mask_bc, BC=Array([0.5]), TR=inv))
    gs["/G0"] = Dictionary(SMask=Name("/None"))
    xo = Dictionary(**forms)
    c = ["0.9 0.9 0.6 rg 0 0 300 300 re f 1 0 0 rg 0 150 300 20 re f"]
    for i, f in enumerate(["Fni", "Fi", "Fko", "Fnk"]):
        c.append(f"q 1 0 0 1 {10 + 70 * i} 230 cm /{f} Do Q")
        c.append(f"q /A gs 1 0 0 1 {10 + 70 * i} 160 cm /{f} Do Q")
    for i, m in enumerate(["Mul", "Scr", "Hue", "Sat", "Col", "Lum"]):
        c.append(f"0.4 0.6 0.2 rg {10 + 48 * i} 90 40 50 re f q /{m} gs 0.7 0.3 0.9 rg {10 + 48 * i + 10} 100 40 50 re f Q")
    for i, s in enumerate(["SM0", "SM1", "SM2", "SM3"]):
        c.append(f"q /{s} gs 0 0 1 rg {10 + 70 * i} 10 60 70 re f Q")
    new_page(pdf, "\n".join(c), Dictionary(ExtGState=gs, XObject=xo))
    pdf.save(OUT / "transparency.pdf")


def srgb_gamma_profile(gamma):
    def xyz(x, y, z):
        s = lambda v: int(round(v * 65536))
        return b"XYZ \0\0\0\0" + struct.pack(">iii", s(x), s(y), s(z))
    curv = b"curv\0\0\0\0" + struct.pack(">IH", 1, int(round(gamma * 256))) + b"\0\0"
    desc = b"desc\0\0\0\0" + struct.pack(">I", 5) + b"test\0" + b"\0" * (4 + 4 + 2 + 1 + 67)
    tags = [(b"desc", desc), (b"cprt", b"text\0\0\0\0test\0"), (b"wtpt", xyz(0.9642, 1.0, 0.8249)),
            (b"rXYZ", xyz(0.4360747, 0.2225045, 0.0139322)), (b"gXYZ", xyz(0.3850649, 0.7168786, 0.0971045)),
            (b"bXYZ", xyz(0.1430804, 0.0606169, 0.7141733)), (b"rTRC", curv), (b"gTRC", curv), (b"bTRC", curv)]
    table = struct.pack(">I", len(tags)); body = b""; off = 128 + 4 + 12 * len(tags)
    for sig, d in tags:
        pad = (-len(body)) % 4; body += b"\0" * pad
        table += sig + struct.pack(">II", off + len(body), len(d)); body += d
    hdr = bytearray(128); hdr[8:10] = b"\x02\x40"; hdr[12:16] = b"mntr"; hdr[16:20] = b"RGB "; hdr[20:24] = b"XYZ "; hdr[36:40] = b"acsp"
    allb = bytes(hdr) + table + body
    return struct.pack(">I", len(allb)) + allb[4:]


def colorspaces():
    pdf = pikepdf.Pdf.new()
    d65 = Array([0.9505, 1, 1.089]); d50 = Array([0.9642, 1, 0.8249])
    m = Array([0.4124, 0.2126, 0.0193, 0.3576, 0.7152, 0.1192, 0.1805, 0.0722, 0.9505])
    cs = Dictionary(
        CG1=Array([Name.CalGray, Dictionary(WhitePoint=d65, Gamma=1.0)]),
        CG22=Array([Name.CalGray, Dictionary(WhitePoint=d65, Gamma=2.2)]),
        CR1=Array([Name.CalRGB, Dictionary(WhitePoint=d65, Gamma=Array([1, 1, 1]), Matrix=m)]),
        CR22=Array([Name.CalRGB, Dictionary(WhitePoint=d65, Gamma=Array([2.2, 2.2, 2.2]), Matrix=m)]),
        LAB=Array([Name.Lab, Dictionary(WhitePoint=d50, Range=Array([-128, 127, -128, 127]))]),
    )  # hand-built non-sRGB ICC swatches omitted: PDFium renders them white, so Edge cannot be a reference
    rows = [("CG1", "0.5"), ("CG22", "0.5"), ("CR1", "0.5 0.5 0.5"), ("CR22", "0.5 0.5 0.5"), ("CR22", "1 0.5 0"),
            ("LAB", "50 0 0"), ("LAB", "54 80 70"), ("LAB", "80 -60 60")]
    c = []
    for i, (n, col) in enumerate(rows):
        c.append(f"/{n} cs {col} sc {10 + 45 * (i % 6)} {200 - 90 * (i // 6)} 40 80 re f")
    new_page(pdf, "\n".join(c), Dictionary(ColorSpace=cs), (290, 300))
    pdf.save(OUT / "colorspaces.pdf")


def mesh():
    pdf = pikepdf.Pdf.new()
    dec = Array([0, 255, 0, 255, 0, 1, 0, 1, 0, 1])

    def sh(t, data, **kw):
        s = Stream(pdf, data)
        s.ShadingType = t; s.ColorSpace = Name.DeviceRGB; s.BitsPerCoordinate = 8; s.BitsPerComponent = 8; s.Decode = dec
        if t != 5:
            s.BitsPerFlag = 8
        for k, v in kw.items():
            s["/" + k] = v
        return s

    R, G, B, Y = b"\xff\0\0", b"\0\xff\0", b"\0\0\xff", b"\xff\xff\0"
    v = lambda x, y, c: bytes([x, y]) + c
    t4 = b"\0" + v(0, 0, R) + b"\0" + v(255, 0, G) + b"\0" + v(0, 255, B) + b"\1" + v(255, 255, Y)
    t5 = v(0, 0, R) + v(128, 40, G) + v(255, 0, B) + v(0, 255, Y) + v(128, 215, R) + v(255, 255, G)
    order = [(0, 0), (0, 1), (0, 2), (0, 3), (1, 3), (2, 3), (3, 3), (3, 2), (3, 1), (3, 0), (2, 0), (1, 0), (1, 1), (1, 2), (2, 2), (2, 1)]
    pts = lambda n: b"".join(bytes([j * 85, i * 85]) for i, j in order[:n])
    t6 = b"\0" + pts(12) + R + G + B + Y
    t7 = b"\0" + pts(16) + R + G + B + Y
    shs = Dictionary(S4=sh(4, t4), S5=sh(5, t5, VerticesPerRow=3), S6=sh(6, t6), S7=sh(7, t7))
    c = ["q 0.4 0 0 0.4 10 160 cm /S4 sh Q", "q 0.4 0 0 0.4 160 160 cm /S5 sh Q", "q 0.4 0 0 0.4 10 20 cm /S6 sh Q", "q 0.4 0 0 0.4 160 20 cm /S7 sh Q"]
    new_page(pdf, "\n".join(c), Dictionary(Shading=shs), (300, 300))
    pdf.save(OUT / "mesh.pdf")


if __name__ == "__main__":
    transparency(); colorspaces(); mesh()
    print("wrote", sorted(p.name for p in OUT.glob("*.pdf")))
