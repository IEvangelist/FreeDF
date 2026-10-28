"""CCITT fixtures encoded by libtiff (via PIL), independent of our decoder. Run: python tools/make_ccitt_fixtures.py"""
import io, os, struct
from PIL import Image, ImageDraw, ImageOps
import pikepdf
from pikepdf import Pdf, Name, Dictionary, Stream

out = os.path.join(os.path.dirname(__file__), "..", "tests", "fixtures", "ccitt")
os.makedirs(out, exist_ok=True)
W, H = 101, 60  # odd width exercises padding

def make_image():
    im = Image.new("1", (W, H), 1)
    d = ImageDraw.Draw(im)
    d.rectangle([5, 5, 40, 20], fill=0)
    d.ellipse([50, 10, 95, 50], outline=0, width=3)
    d.line([0, 0, W, H], fill=0, width=1)
    d.rectangle([10, 30, 12, 58], fill=0)
    for x in range(0, W, 2): d.point((x, 55), fill=0)
    return im

def strip(tiff_bytes):
    im = Image.open(io.BytesIO(tiff_bytes))
    off = im.tag_v2[273]; cnt = im.tag_v2[279]
    off = off if isinstance(off, int) else off[0]; cnt = cnt if isinstance(cnt, int) else cnt[0]
    return tiff_bytes[off:off + cnt]

im = make_image()
with open(os.path.join(out, "expected.txt"), "w") as f:
    f.write("".join("1" if im.getpixel((x, y)) else "0" for y in range(H) for x in range(W)))

cases = {"g4": ("group4", -1), "g3-1d": ("group3", 0)}
for name, (comp, k) in cases.items():
    buf = io.BytesIO(); ImageOps.invert(im.convert("L")).convert("1").save(buf, format="TIFF", compression=comp)
    data = strip(buf.getvalue())
    pdf = Pdf.new(); page = pdf.add_blank_page(page_size=(W, H))
    img = Stream(pdf, data)
    img.Type = Name.XObject; img.Subtype = Name.Image; img.Width = W; img.Height = H
    img.ColorSpace = Name.DeviceGray; img.BitsPerComponent = 1
    img.Filter = Name.CCITTFaxDecode
    img.DecodeParms = Dictionary(K=k, Columns=W, Rows=H)
    page.Resources = Dictionary(XObject=Dictionary(Im0=img))
    page.Contents = pdf.make_stream(f"q {W} 0 0 {H} 0 0 cm /Im0 Do Q".encode())
    pdf.save(os.path.join(out, f"{name}.pdf"))
print("ok")
