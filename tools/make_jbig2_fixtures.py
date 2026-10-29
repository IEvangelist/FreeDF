"""JBIG2 fixtures. Expected pixels come from pdfium (via pypdfium2) or from the source bitmap, never from our decoder.
Inputs: the ITU-T T.88 Annex H sample file (path in $ANNEXH or scratch/annexh.jbig2) and a small built-in JBIG2 encoder
(MQ coder, generic regions templates 0-3 with TPGD, generic refinement, symbol dictionary + text region).
Run: python tools/make_jbig2_fixtures.py"""
import os, struct, sys
import pikepdf
from pikepdf import Pdf, Name, Dictionary, Stream
import pypdfium2 as pdfium

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
OUT = os.path.join(ROOT, "tests", "fixtures", "jbig2")
os.makedirs(OUT, exist_ok=True)


def parse_file(d):
    pos = 9
    if not d[8] & 2: pos += 4
    segs = []
    while pos < len(d):
        start = pos
        num, flags = struct.unpack(">IB", d[pos:pos + 5]); pos += 5
        t = flags & 63
        cnt = d[pos] >> 5; pos += 1
        sz = 1 if num <= 256 else 2 if num <= 65536 else 4
        pos += cnt * sz
        if flags & 0x40: pg = struct.unpack(">I", d[pos:pos + 4])[0]; pos += 4
        else: pg = d[pos]; pos += 1
        ln = struct.unpack(">I", d[pos:pos + 4])[0]; pos += 4
        pos += ln
        segs.append((t, pg, d[start:pos]))
    return segs


def make_pdf(path, w, h, data, globals_=None):
    pdf = Pdf.new(); page = pdf.add_blank_page(page_size=(w, h))
    img = Stream(pdf, data)
    img.Type = Name.XObject; img.Subtype = Name.Image; img.Width = w; img.Height = h
    img.ColorSpace = Name.DeviceGray; img.BitsPerComponent = 1
    img.Filter = Name.JBIG2Decode
    if globals_:
        g = Stream(pdf, globals_)
        img.DecodeParms = Dictionary(JBIG2Globals=g)
    page.Resources = Dictionary(XObject=Dictionary(Im0=img))
    page.Contents = pdf.make_stream(f"q {w} 0 0 {h} 0 0 cm /Im0 Do Q".encode())
    pdf.save(path)


def reference(path, w, h):
    doc = pdfium.PdfDocument(path)
    bm = doc[0].render(scale=1.0, grayscale=True, may_draw_forms=False)
    im = bm.to_pil().convert("L")
    assert im.size == (w, h), (im.size, w, h)
    return "".join("1" if im.getpixel((x, y)) > 127 else "0" for y in range(h) for x in range(w))


def emit(name, w, h, data, globals_=None, truth=None):
    p = os.path.join(OUT, name + ".pdf")
    make_pdf(p, w, h, data, globals_)
    ref = reference(p, w, h)
    if truth is not None and truth != ref:
        bad = sum(a != b for a, b in zip(truth, ref))
        print(f"  WARNING {name}: pdfium differs from source bitmap in {bad} px")
    with open(os.path.join(OUT, name + ".txt"), "w") as f:
        f.write(truth if truth is not None else ref)
    raw = os.path.join(ROOT, "scratch", "raw")
    if os.path.isdir(raw):
        open(os.path.join(raw, name + ".jb2"), "wb").write(data)
        open(os.path.join(raw, name + ".glob"), "wb").write(globals_ or b"")
        open(os.path.join(raw, name + ".dim"), "w").write(f"{w} {h}")
    print("wrote", name, w, h)


def annex_h():
    path = os.environ.get("ANNEXH") or os.path.join(ROOT, "scratch", "annexh.jbig2")
    if not os.path.exists(path): print("no annex H file; skipping"); return
    segs = parse_file(open(path, "rb").read())
    glob = b"".join(s[2] for s in segs if s[1] == 0 and s[0] != 51)
    for pg in (1, 2, 3):
        body = b"".join(s[2] for s in segs if s[1] == pg)
        info = next(s for s in segs if s[1] == pg and s[0] == 48)[2]
        i = info.index(b"") if False else len(info) - 19
        w, h = struct.unpack(">II", info[i:i + 8])
        emit(f"annexh-p{pg}", w, h, body, glob or None)


def bitmap_from_pil(im):
    im = im.convert("L")
    w, h = im.size
    return [[1 if im.getpixel((x, y)) < 128 else 0 for x in range(w)] for y in range(h)]


def truth_str(b):
    return "".join("0" if v else "1" for row in b for v in row)  # expected text uses 1 = white like the CCITT fixtures


def test_image():
    from PIL import Image, ImageDraw, ImageFont
    im = Image.new("L", (240, 160), 255)
    d = ImageDraw.Draw(im)
    d.text((6, 6), "JBIG2 Decode 2026", fill=0)
    d.text((6, 24), "The quick brown fox jumps", fill=0)
    d.rectangle([10, 50, 90, 100], outline=0, width=2)
    d.ellipse([110, 50, 200, 140], outline=0, width=3)
    d.line([0, 150, 239, 120], fill=0, width=1)
    d.polygon([(20, 110), (60, 150), (30, 155)], fill=0)
    for y in range(60, 95, 4):
        d.line([20, y, 80, y], fill=0)
    return bitmap_from_pil(im)


def generic_cases():
    import jbig2_enc as E
    b = test_image(); h, w = len(b), len(b[0])
    cases = {
        "gen-t0": (0, False, None), "gen-t1": (1, False, None), "gen-t2": (2, False, None), "gen-t3": (3, False, None),
        "gen-t0-tpgd": (0, True, None), "gen-t1-tpgd": (1, True, None), "gen-t2-tpgd": (2, True, None), "gen-t3-tpgd": (3, True, None),
        "gen-t0-at": (0, False, [(4, -2), (-4, -1), (1, -3), (-6, -2)]), "gen-t1-at": (1, True, [(-4, -2)]),
    }
    for name, (t, tpgd, at) in cases.items():
        body = E.seg(1, 48, 1, E.page_info(w, h)) + E.generic_segment(2, b, t, tpgd, at)
        emit(name, w, h, body, None, truth_str(b))
    # MMR via libtiff
    import io
    from PIL import Image
    pil = Image.new("1", (w, h), 1)
    pil.putdata([1 if v else 0 for row in b for v in row])
    buf = io.BytesIO(); pil.save(buf, format="TIFF", compression="group4")
    t = Image.open(io.BytesIO(buf.getvalue()))
    off = t.tag_v2[273]; cnt = t.tag_v2[279]
    off = off if isinstance(off, int) else off[0]; cnt = cnt if isinstance(cnt, int) else cnt[0]
    mmr = buf.getvalue()[off:off + cnt]
    data = E.region_info(w, h) + bytes([1]) + mmr
    emit("gen-mmr", w, h, E.seg(1, 48, 1, E.page_info(w, h)) + E.seg(2, 38, 1, data), None, truth_str(b))
    # refinement of the page content
    b2 = [row[:] for row in b]
    for y in range(40, 120, 3):
        for x in range(100, 220, 5): b2[y][x] ^= 1
    for t_ in (0, 1):
        for tpgr in (False, True):
            body = (E.seg(1, 48, 1, E.page_info(w, h)) + E.generic_segment(2, b, 0) +
                    E.refine_segment(3, b2, b, t_, tpgr))
            emit(f"refine-t{t_}{'-tpgr' if tpgr else ''}", w, h, body, None, truth_str(b2))


GLYPH_TEXT = ["JBIG2 Decode 2026", "The quick brown fox", "jumps over lazy dogs", "0123456789 abcxyz"]


def glyph_set():
    from PIL import Image, ImageDraw
    chars = sorted(set("".join(GLYPH_TEXT)) - {" "})
    syms = {}
    for ch in chars:
        im = Image.new("L", (16, 16), 255); ImageDraw.Draw(im).text((2, 2), ch, fill=0)
        b = bitmap_from_pil(im)
        ys = [y for y, r in enumerate(b) if any(r)]; xs = [x for x in range(16) if any(r[x] for r in b)]
        if not ys: continue
        syms[ch] = [r[min(xs):max(xs) + 1] for r in b[min(ys):max(ys) + 1]]
    order = sorted(syms, key=lambda c: (len(syms[c]), c))
    return order, [syms[c] for c in order]


def text_cases():
    import jbig2_enc as E
    order, syms = glyph_set()
    index = {c: i for i, c in enumerate(order)}
    W, H = 200, 120

    def layout(transposed_pad=0):
        pl = []
        for r, line in enumerate(GLYPH_TEXT):
            x = 5
            for ch in line:
                if ch == " ": x += 6; continue
                s = syms[index[ch]]
                pl.append((index[ch], x, 10 + r * 22 + (len(s) and 0)))
                x += len(s[0]) + 2
        return pl

    def render(pl):
        page = [[0] * W for _ in range(H)]
        for sid, x, y in pl:
            for j, row in enumerate(syms[sid]):
                for i, v in enumerate(row):
                    if v and 0 <= y + j < H and 0 <= x + i < W: page[y + j][x + i] = 1
        return page
    pl = layout()
    page = render(pl)
    sd = E.symbol_dict_segment(1, syms, 0, page=1)
    for name, kw in {"text-topleft": dict(refcorner=1), "text-bottomleft": dict(refcorner=0), "text-topright": dict(refcorner=3),
                     "text-bottomright": dict(refcorner=2), "text-strips": dict(refcorner=1, strips_log=2)}.items():
        body = E.seg(0, 48, 1, E.page_info(W, H)) + sd + E.text_segment(2, W, H, syms, pl, [1], page=1, **kw)
        emit(name, W, H, body, None, truth_str(page))
    # transposed: positions swapped roles
    body = E.seg(0, 48, 1, E.page_info(W, H)) + sd + E.text_segment(2, W, H, syms, pl, [1], refcorner=1, transposed=True, page=1)
    emit("text-transposed", W, H, body, None, truth_str(page))
    # symbol dictionary delivered through /JBIG2Globals
    gsd = E.symbol_dict_segment(1, syms, 0, page=0)
    body = E.seg(2, 48, 1, E.page_info(W, H)) + E.text_segment(3, W, H, syms, pl, [1], refcorner=1, page=1)
    emit("text-globals", W, H, body, gsd, truth_str(page))


if __name__ == "__main__":
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    annex_h()
    generic_cases()
    text_cases()
