"""Independent validator/comparer for the sample PDFs (pikepdf/qpdf structure + pdfium rendering).

  python tools/validate_samples.py check  <dir>                 structural check of every PDF (pikepdf/qpdf); encrypted samples use the documented passwords
  python tools/validate_samples.py compare <old-dir> <new-dir> [name-fragment] [--dpi 40]
                                                                page count / boxes / structure counts / pixel difference between two sample sets

These tools only VALIDATE; the samples themselves are authored by tools/FreeDF.Samples (C#).
"""
import glob
import io
import logging
import os
import sys

import numpy as np
import pikepdf
import pypdfium2 as pdfium

PASSWORDS = {"17-": "user123", "18-": "owner123"}


def password_for(path):
    base = os.path.basename(path)
    return next((v for k, v in PASSWORDS.items() if base.startswith(k)), "")


def open_pdf(path):
    return pikepdf.open(path, password=password_for(path))


def summary(path):
    with open_pdf(path) as p:
        root = p.Root
        names = root.get("/Names", {})
        out = {
            "pages": len(p.pages),
            "boxes": [tuple(round(float(v), 1) for v in pg.MediaBox) for pg in p.pages],
            "rotate": [int(pg.get("/Rotate", 0)) for pg in p.pages],
            "annots": sum(len(pg.get("/Annots", [])) for pg in p.pages),
            "fields": len(root.AcroForm.get("/Fields", [])) if "/AcroForm" in root else 0,
            "outline": count_outline(root.Outlines) if "/Outlines" in root else 0,
            "labels": "/PageLabels" in root,
            "ocgs": len(root.OCProperties.get("/OCGs", [])) if "/OCProperties" in root else 0,
            "files": "/EmbeddedFiles" in names,
            "tagged": "/StructTreeRoot" in root,
            "encrypted": p.is_encrypted,
            "lang": str(root.get("/Lang", "")),
            "title": str(p.docinfo.get("/Title", "")),
        }
    return out


def count_outline(node):
    n, cur = 0, node.get("/First")
    while cur is not None:
        n += 1 + (count_outline(cur) if "/First" in cur else 0)
        cur = cur.get("/Next")
    return n


def render(path, dpi):
    pdf = pdfium.PdfDocument(path, password=password_for(path) or None)
    try:
        for page in pdf:
            yield np.asarray(page.render(scale=dpi / 72).to_pil().convert("RGB"), dtype=np.int16)
    finally:
        pdf.close()


def check(directory):
    bad = 0
    for f in sorted(glob.glob(os.path.join(directory, "*.pdf"))):
        name = os.path.basename(f)
        try:
            records = []
            h = logging.Handler()
            h.emit = lambda r: records.append(r.getMessage())
            logging.getLogger("pikepdf").addHandler(h)
            logging.getLogger("pikepdf").setLevel(logging.WARNING)
            with open_pdf(f) as p:
                p.save(io.BytesIO())
                pages = len(p.pages)
            for _ in render(f, 24):
                pass
            logging.getLogger("pikepdf").removeHandler(h)
            print(("OK   " if not records else "WARN ") + name, pages, "pages", f"{os.path.getsize(f) / 1024:.0f} KB")
            for r in records[:5]:
                print("      ", r)
            bad += bool(records)
        except Exception as e:
            print("FAIL", name, e)
            bad += 1
    return bad


def compare(old, new, frag, dpi):
    bad = 0
    for f in sorted(glob.glob(os.path.join(old, "*.pdf"))):
        name = os.path.basename(f)
        if frag not in name:
            continue
        g = os.path.join(new, name)
        if not os.path.exists(g):
            print("MISSING", name)
            bad += 1
            continue
        a, b = summary(f), summary(g)
        diffs = [k for k in a if a[k] != b[k] and k not in ("title",)]
        px = []
        for x, y in zip(render(f, dpi), render(g, dpi)):
            if x.shape != y.shape:
                px.append(1.0)
                continue
            px.append(float((np.abs(x - y).max(axis=2) > 48).mean()))
        worst = max(px) if px else 1.0
        mean = float(np.mean(px)) if px else 1.0
        status = "OK  " if not diffs and worst < 0.03 else "DIFF"
        bad += status == "DIFF"
        print(f"{status} {name}: pages {a['pages']}->{b['pages']} size {os.path.getsize(f) // 1024}->{os.path.getsize(g) // 1024} KB, "
              f"pixels>48 differing: mean {mean:.4f} worst {worst:.4f} (page {px.index(worst) + 1 if px else '-'})" + (f", structure diffs {diffs}" if diffs else ""))
        for k in diffs:
            print(f"      {k}: {a[k]} -> {b[k]}")
    return bad


if __name__ == "__main__":
    if len(sys.argv) >= 3 and sys.argv[1] == "check":
        sys.exit(1 if check(sys.argv[2]) else 0)
    if len(sys.argv) >= 4 and sys.argv[1] == "compare":
        dpi = float(sys.argv[sys.argv.index("--dpi") + 1]) if "--dpi" in sys.argv else 40
        frag = next((a for a in sys.argv[4:] if not a.startswith("--") and not a.replace(".", "").isdigit()), "")
        sys.exit(1 if compare(sys.argv[2], sys.argv[3], frag, dpi) else 0)
    print(__doc__)
    sys.exit(2)
