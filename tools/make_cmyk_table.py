"""Regenerates src/PortableDocument.Content/Resources/cmyk-srgb-9x9x9x9.bin from PDFium and reports its error.

The table holds PDFium's DeviceCMYK -> sRGB result at the 9x9x9x9 grid nodes {0,32,...,224,255} per ink (c,m,y,k row-major, k fastest,
RGB bytes). It is measured by rendering DeviceCMYK `k` fills with pypdfium2 (PDFium, BSD-3-Clause; see THIRD-PARTY-NOTICES.md), then
validated by multilinear interpolation against PDFium on random off-grid colours (the interpolation CmykConversion.cs performs).

  pip install pypdfium2
  python tools/make_cmyk_table.py --out src/PortableDocument.Content/Resources/cmyk-srgb-9x9x9x9.bin   # regenerate + validate
  python tools/make_cmyk_table.py --check src/PortableDocument.Content/Resources/cmyk-srgb-9x9x9x9.bin # compare a committed table with PDFium

Expected (pypdfium2 PDFium builds checked so far): off-grid mean ~0.75, p99 4, max 9 levels. --check fails when the max exceeds --max-error (default 12).
"""
import argparse, itertools, os, random, sys, tempfile
import pypdfium2 as pdfium

NODES = [0, 32, 64, 96, 128, 160, 192, 224, 255]
SWATCH, COLS = 4, 80


def render(colours):
    rows = (len(colours) + COLS - 1) // COLS
    ops = [f"{c / 255:.6f} {m / 255:.6f} {y / 255:.6f} {k / 255:.6f} k {(i % COLS) * SWATCH} {(i // COLS) * SWATCH} {SWATCH} {SWATCH} re f"
           for i, (c, m, y, k) in enumerate(colours)]
    content = "\n".join(ops).encode()
    w, h = COLS * SWATCH, rows * SWATCH
    pdf = b"%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n"
    pdf += f"3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 {w} {h}]/Contents 4 0 R>>endobj\n".encode()
    pdf += f"4 0 obj<</Length {len(content)}>>stream\n".encode() + content + b"\nendstream endobj\ntrailer<</Root 1 0 R/Size 5>>\n%%EOF"
    with tempfile.TemporaryDirectory() as d:
        path = os.path.join(d, "swatches.pdf")
        open(path, "wb").write(pdf)
        doc = pdfium.PdfDocument(path)
        img = doc[0].render(scale=1, no_smoothtext=True, no_smoothpath=True).to_pil().convert("RGB")
        doc.close()
    return [img.getpixel(((i % COLS) * SWATCH + SWATCH // 2, h - ((i // COLS) * SWATCH + SWATCH // 2) - 1)) for i in range(len(colours))]


def interpolate(table, c, m, y, k):
    pos = [v / 255 * 8 for v in (c, m, y, k)]
    base = [min(int(p), 7) for p in pos]
    frac = [p - b for p, b in zip(pos, base)]
    out = [0.0, 0.0, 0.0]
    for corner in itertools.product((0, 1), repeat=4):
        w = 1.0
        for a in range(4):
            w *= frac[a] if corner[a] else 1 - frac[a]
        if w == 0:
            continue
        off = ((((base[0] + corner[0]) * 9 + base[1] + corner[1]) * 9 + base[2] + corner[2]) * 9 + base[3] + corner[3]) * 3
        for ch in range(3):
            out[ch] += w * table[off + ch]
    return [round(v) for v in out]


def validate(table, samples=3000, seed=7):
    random.seed(seed)
    pts = [tuple(random.randint(0, 255) for _ in range(4)) for _ in range(samples)]
    ref = render(pts)
    ds = sorted(max(abs(a - b) for a, b in zip(interpolate(table, *p), r)) for p, r in zip(pts, ref))
    stats = dict(mean=sum(ds) / len(ds), p95=ds[int(.95 * len(ds))], p99=ds[int(.99 * len(ds))], max=ds[-1])
    print(f"off-grid n={len(ds)} mean={stats['mean']:.2f} p95={stats['p95']} p99={stats['p99']} max={stats['max']} over4={sum(d > 4 for d in ds) / len(ds):.4f}")
    return stats


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out")
    ap.add_argument("--check")
    ap.add_argument("--max-error", type=int, default=12)
    a = ap.parse_args()
    if bool(a.out) == bool(a.check):
        ap.error("pass exactly one of --out or --check")
    if a.out:
        table = bytearray()
        for rgb in render([tuple(NODES[i] for i in idx) for idx in itertools.product(range(9), repeat=4)]):
            table += bytes(rgb)
        open(a.out, "wb").write(bytes(table))
        print(f"wrote {len(table)} bytes to {a.out}")
    else:
        table = open(a.check, "rb").read()
        assert len(table) == 9 ** 4 * 3, "unexpected table size"
    stats = validate(table)
    if a.check and stats["max"] > a.max_error:
        sys.exit(1)


main()
