"""Cross-checks every tests/fixtures/jbig2/*.pdf against independent JBIG2 decoders and prints a matrix.
Decoders: pdfium (pypdfium2), MuPDF/jbig2dec (PyMuPDF), pdf.js (Node, `pdfjs-dist` installed in --pdfjs DIR). Expected pixels are the
fixture .txt files ('1' = white), which are constructed from source bitmaps and never produced by the C# decoder.
Usage: python tools/jbig2_crosscheck.py [--pdfjs DIR]    (DIR contains node_modules/pdfjs-dist)"""
import glob, json, os, subprocess, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
FIX = os.path.join(ROOT, "tests", "fixtures", "jbig2")


def to_bits(gray, w, h):
    return "".join("1" if gray[y * w + x] > 127 else "0" for y in range(h) for x in range(w))


def run_pdfium(path):
    import pypdfium2 as pdfium
    page = pdfium.PdfDocument(path)[0]
    im = page.render(scale=1.0, grayscale=True, may_draw_forms=False).to_pil().convert("L")
    w, h = im.size
    return to_bits(list(im.getdata()), w, h)


def run_mupdf(path):
    import fitz
    page = fitz.open(path)[0]
    pix = page.get_pixmap(dpi=72, colorspace=fitz.csGRAY, alpha=False)
    return to_bits(list(pix.samples), pix.width, pix.height)


def run_pdfjs(files, pdfjs_dir):
    script = os.path.join(os.path.dirname(os.path.abspath(__file__)), "jbig2_pdfjs_dump.mjs")
    out = subprocess.run(["node", script, *files], cwd=pdfjs_dir, capture_output=True, text=True, timeout=600)
    if out.returncode != 0: raise RuntimeError(out.stderr[-400:])
    return json.loads(out.stdout.split("@@JSON@@", 1)[1])


def verdict(exp, got):
    if got is None: return "ERR"
    if got.startswith("ERR"): return got[:12]
    if len(got) != len(exp): return f"SIZE({len(got)})"
    bad = sum(a != b for a, b in zip(exp, got))
    return "ok" if bad == 0 else f"DIFF({bad})"


def main():
    pdfjs_dir = sys.argv[sys.argv.index("--pdfjs") + 1] if "--pdfjs" in sys.argv else None
    files = sorted(glob.glob(os.path.join(FIX, "*.pdf")))
    pj = run_pdfjs(files, pdfjs_dir) if pdfjs_dir else {}
    print(f"{'fixture':44}{'pdfium':>14}{'mupdf':>14}{'pdf.js':>14}")
    for f in files:
        name = os.path.splitext(os.path.basename(f))[0]
        exp = open(f[:-4] + ".txt").read()
        row = []
        for fn in (run_pdfium, run_mupdf):
            try: row.append(verdict(exp, fn(f)))
            except Exception as e: row.append("ERR " + type(e).__name__[:8])
        row.append(verdict(exp, pj.get(os.path.basename(f), "ERR missing")) if pdfjs_dir else "n/a")
        print(f"{name:44}" + "".join(f"{r:>14}" for r in row))


if __name__ == "__main__":
    main()
