"""Scalable synthetic corpus generator (our own output; nothing third-party).
Usage: python gen_synthetic.py <outdir> [--scale 1.0]
Requires pymupdf, pikepdf, numpy. Deterministic (seeded).
"""
import argparse, glob, os, random, io
import numpy as np
import pymupdf, pikepdf

ap = argparse.ArgumentParser()
ap.add_argument("out"); ap.add_argument("--scale", type=float, default=1.0)
ap.add_argument("--samples", default=os.path.join(os.path.dirname(__file__), "..", "..", "samples"))
a = ap.parse_args()
os.makedirs(a.out, exist_ok=True)
rnd = random.Random(42)
S = lambda n: max(1, int(n * a.scale))
WORDS = "lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor incididunt ut labore et dolore magna aliqua portable document format".split()
FONTS = ["helv", "tiro", "cour", "heit", "tibo", "cobo"]


def para(n):
    return " ".join(rnd.choice(WORDS) for _ in range(n))


def text_doc(pages, cjk=False):
    d = pymupdf.open()
    for i in range(pages):
        pg = d.new_page()
        for j in range(6):
            r = pymupdf.Rect(50, 50 + j * 120, 560, 160 + j * 120)
            pg.insert_textbox(r, para(70), fontsize=9 + (j % 3), fontname=FONTS[(i + j) % len(FONTS)])
        if cjk:
            pg.insert_text((50, 790), "日本語のテキスト 中文文本 한국어 텍스트", fontname="china-s" if i % 2 else "japan", fontsize=11)
    return d


def vector_doc(pages):
    d = pymupdf.open()
    for i in range(pages):
        pg = d.new_page(); sh = pg.new_shape()
        for _ in range(400):
            p1 = pymupdf.Point(rnd.uniform(20, 580), rnd.uniform(20, 820))
            p2 = pymupdf.Point(rnd.uniform(20, 580), rnd.uniform(20, 820))
            p3 = pymupdf.Point(rnd.uniform(20, 580), rnd.uniform(20, 820))
            sh.draw_bezier(p1, p2, p3, pymupdf.Point(rnd.uniform(20, 580), rnd.uniform(20, 820)))
            sh.finish(color=(rnd.random(), rnd.random(), rnd.random()), fill=(rnd.random(), rnd.random(), rnd.random()) if _ % 3 == 0 else None,
                      fill_opacity=0.5 if _ % 5 == 0 else 1, width=rnd.uniform(0.2, 2), closePath=False)
        sh.commit()
    return d


def image_doc(pages, w, h):
    d = pymupdf.open()
    base = np.random.default_rng(1).integers(0, 255, (h // 8, w // 8, 3), dtype=np.uint8)
    for i in range(pages):
        arr = np.kron(np.roll(base, i * 7, axis=0), np.ones((8, 8, 1), dtype=np.uint8))
        pix = pymupdf.Pixmap(pymupdf.csRGB, w, h, arr.tobytes(), False)
        pg = d.new_page(); pg.insert_image(pg.rect, pixmap=pix)
    return d


def mixed_doc(target_pages):
    srcs = [f for f in sorted(glob.glob(os.path.join(a.samples, "*.pdf"))) if not os.path.basename(f).startswith(("17", "18"))]
    d = pymupdf.open(); seen = 0
    while seen < target_pages:
        for f in srcs:
            try:
                s = pymupdf.open(f); d.insert_pdf(s); seen += s.page_count
            except Exception:
                pass
            if seen >= target_pages: break
    return d


def save(d, name, **kw):
    p = os.path.join(a.out, name); d.save(p, **kw); d.close(); print(name, os.path.getsize(p) >> 10, "KiB"); return p


text = save(text_doc(S(2000)), "syn-text-2000p.pdf", garbage=0, deflate=True)
save(text_doc(S(1000), cjk=True), "syn-text-cjk-1000p.pdf", deflate=True)
save(vector_doc(S(300)), "syn-vector-300p.pdf", deflate=True)
save(image_doc(S(40), 3000, 2000), "syn-images-40p-3000x2000.pdf", deflate=True)
mixed = save(mixed_doc(S(1500)), "syn-mixed-1500p.pdf", deflate=True, garbage=0)
small = save(text_doc(S(300)), "syn-text-300p.pdf", deflate=True)

# Structure variants of the mixed doc.
with pikepdf.open(mixed) as pdf:
    pdf.save(os.path.join(a.out, "syn-mixed-objstm-xrefstream.pdf"), object_stream_mode=pikepdf.ObjectStreamMode.generate)
    pdf.save(os.path.join(a.out, "syn-mixed-classic-xref.pdf"), object_stream_mode=pikepdf.ObjectStreamMode.disable)
with pikepdf.open(text) as pdf:
    pdf.save(os.path.join(a.out, "syn-text-2000p-aes256.pdf"), encryption=pikepdf.Encryption(user="", owner="o", R=6))
    pdf.save(os.path.join(a.out, "syn-text-2000p-rc4-128.pdf"), encryption=pikepdf.Encryption(user="", owner="o", R=3, aes=False, metadata=False))

# Incremental-update chain: append N revisions of (new Info object + xref section + trailer).
def incremental_chain(src, dst, revisions):
    b = bytearray(open(src, "rb").read())
    with pikepdf.open(src) as pdf:
        root = pdf.trailer.Root.objgen; size = int(pdf.trailer.Size)
    import re
    prev = int(re.findall(rb"startxref\s+(\d+)", bytes(b))[-1])
    for r in range(revisions):
        off = len(b); num = size + r
        b += b"\n%d 0 obj\n<< /Title (rev %d) >>\nendobj\n" % (num, r)
        xoff = len(b)
        b += b"xref\n%d 1\n%010d 00000 n \ntrailer\n<< /Size %d /Root %d %d R /Info %d 0 R /Prev %d >>\nstartxref\n%d\n%%%%EOF\n" % (num, off + 1, num + 1, root[0], root[1], num, prev, xoff)
        prev = xoff
    open(dst, "wb").write(b); print(os.path.basename(dst), len(b) >> 10, "KiB")

incremental_chain(small, os.path.join(a.out, "syn-incremental-chain-500rev.pdf"), 500)

# Damaged files needing recovery.
raw = open(mixed, "rb").read()
open(os.path.join(a.out, "syn-damaged-truncated-tail.pdf"), "wb").write(raw[:-4096])
open(os.path.join(a.out, "syn-damaged-bad-startxref.pdf"), "wb").write(raw.replace(b"startxref", b"startxreg"))
print("done")
