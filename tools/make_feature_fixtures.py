"""Generates tests/fixtures/features/features.pdf: outlines, links, form fields, page labels, XMP, embedded file."""
import os
import pikepdf
from pikepdf import Pdf, Dictionary, Name, Array, String
from reportlab.pdfgen import canvas

out = os.path.join(os.path.dirname(__file__), "..", "tests", "fixtures", "features")
os.makedirs(out, exist_ok=True)
tmp = os.path.join(out, "_base.pdf")

c = canvas.Canvas(tmp, pagesize=(300, 300))
c.setTitle("Feature Doc")
for i in range(3):
    c.drawString(50, 250, f"Page {i + 1}")
    c.bookmarkPage(f"p{i + 1}")
    c.addOutlineEntry(f"Chapter {i + 1}", f"p{i + 1}", level=0)
    if i == 0:
        c.linkURL("https://example.com", (10, 10, 100, 40))
        c.acroForm.textfield(name="firstName", value="Ada", x=50, y=100, width=120, height=20)
        c.acroForm.checkbox(name="agree", x=50, y=150, checked=True)
    c.showPage()
c.save()

pdf = Pdf.open(tmp)
labels = Dictionary(Nums=Array([
    0, Dictionary(S=Name.r),
    1, Dictionary(S=Name.D, P=String("A-"), St=5),
]))
pdf.Root.PageLabels = pdf.make_indirect(labels)
xmp = pdf.make_stream(b"<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf/></x:xmpmeta>")
xmp.Type = Name.Metadata
xmp.Subtype = Name.XML
pdf.Root.Metadata = xmp
pdf.attachments["hello.txt"] = b"hello attachment"
pdf.save(os.path.join(out, "features.pdf"))
pdf.close()
os.remove(tmp)
print("ok")
