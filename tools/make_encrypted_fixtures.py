"""Generates encrypted fixtures with pikepdf (independent of our implementation). Run: python tools/make_encrypted_fixtures.py"""
import pikepdf
from pikepdf import Pdf, Name, Dictionary, Stream, Encryption, Permissions
import os

out = os.path.join(os.path.dirname(__file__), "..", "tests", "fixtures", "encrypted")
os.makedirs(out, exist_ok=True)

def base():
    pdf = Pdf.new()
    page = pdf.add_blank_page(page_size=(200, 200))
    page.Contents = pdf.make_stream(b"1 0 0 rg 0 0 100 100 re f 0 0 1 rg 100 100 100 100 re f")
    pdf.docinfo["/Title"] = "Secret Title"
    return pdf

cases = {
    "rc4-40": dict(R=2),
    "rc4-128": dict(R=3),
    "aes-128": dict(R=4),
    "aes-256-r5": dict(R=5),
    "aes-256-r6": dict(R=6),
}
for name, kw in cases.items():
    for pwd_name, user, owner in [("empty", "", "owner"), ("pw", "secret", "owner")]:
        pdf = base()
        pdf.save(os.path.join(out, f"{name}-{pwd_name}.pdf"),
                 encryption=Encryption(user=user, owner=owner, **kw, metadata=False, aes=False) if kw["R"] < 4 else Encryption(user=user, owner=owner, **kw),
                 object_stream_mode=pikepdf.ObjectStreamMode.disable)
# object streams + AES-256 (encrypted xref stream/objstm path)
pdf = base()
pdf.save(os.path.join(out, "aes-256-r6-objstm.pdf"), encryption=Encryption(user="", owner="o", R=6),
         object_stream_mode=pikepdf.ObjectStreamMode.generate)
print("ok")
