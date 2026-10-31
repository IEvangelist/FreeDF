"""Independent renderer cross-check (pdfium via pypdfium2, MuPDF via PyMuPDF) for the behaviours fixed in this change."""
import io
import pypdfium2 as pdfium
import pymupdf


def build(content, resources="", extra=(), box="[0 0 200 200]"):
    objs = [
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        f"<< /Type /Page /Parent 2 0 R /MediaBox {box} /Contents 4 0 R /Resources << {resources} >> >>",
        f"<< /Length {len(content)} >>\nstream\n{content}\nendstream",
    ] + list(extra)
    out = "%PDF-1.7\n"
    offs = []
    for i, o in enumerate(objs, 1):
        offs.append(len(out.encode("latin1")))
        out += f"{i} 0 obj\n{o}\nendobj\n"
    x = len(out.encode("latin1"))
    out += f"xref\n0 {len(objs)+1}\n0000000000 65535 f \n" + "".join(f"{o:010d} 00000 n \n" for o in offs)
    out += f"trailer\n<< /Size {len(objs)+1} /Root 1 0 R >>\nstartxref\n{x}\n%%EOF\n"
    return out.encode("latin1")


def stream(n, dict_, data):
    return f"<< {dict_} /Length {len(data)} >>\nstream\n{data}\nendstream"


def px(pdf, x, y):
    img = pdfium.PdfDocument(pdf)[0].render(scale=1).to_pil().convert("RGB")
    a = img.getpixel((x, y))
    pix = pymupdf.open(stream=pdf, filetype="pdf")[0].get_pixmap(dpi=72)
    m = pix.pixel(x, y)
    return a, tuple(m[:3])


FONT = "/Font << /F0 << /Type /Font /Subtype /Type3 /FontBBox [0 0 1000 1000] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs << /A 5 0 R >> /Encoding << /Type /Encoding /Differences [65 /A] >> /FirstChar 65 /LastChar 65 /Widths [1000] >> >>"


def type3(glyph, pre):
    return build(f"{pre} BT /F0 100 Tf 50 50 Td (A) Tj ET", FONT, [stream(5, "", glyph)])


print("d1 ignores rg (ours expects blue)   pdfium/mupdf:", px(type3("1000 0 0 0 1000 1000 d1 1 0 0 rg 0 0 1000 1000 re f", "0 0 1 rg"), 100, 100))
print("d0 own colour (ours expects red)    pdfium/mupdf:", px(type3("1000 0 d0 1 0 0 rg 0 0 1000 1000 re f", "0 0 1 rg"), 100, 100))
print("Tr 3 type3 (ours expects white)     pdfium/mupdf:", px(type3("1000 0 0 0 1000 1000 d1 0 0 1000 1000 re f", "0 0 1 rg 3 Tr"), 100, 100))
print("Tr 7 type3 (ours expects white)     pdfium/mupdf:", px(type3("1000 0 0 0 1000 1000 d1 0 0 1000 1000 re f", "0 0 1 rg 7 Tr"), 100, 100))


def matte(m):
    img = "<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask 6 0 R /Filter /ASCIIHexDecode /Length 7 >>\nstream\n404040>\nendstream"
    sm = f"<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 {m} /Filter /ASCIIHexDecode /Length 3 >>\nstream\n80>\nendstream"
    return build("200 0 0 200 0 0 cm /Im0 Do", "/XObject << /Im0 5 0 R >>", [img, sm])


print("no matte  (ours ~159)               pdfium/mupdf:", px(matte(""), 100, 100))
print("black matte (ours ~191)             pdfium/mupdf:", px(matte("/Matte [0 0 0]"), 100, 100))
inline = "q 90 0 0 10 0 0 cm BI /W 9 /H 1 /BPC 8 /CS /G /F /RL /L 10 ID \x08 EI Q 0  \nEI Q"
print("inline /L sample 6 (ours 48)        pdfium/mupdf:", px(build(inline), 65, 195))
