"""JBIG2 fixtures for T.88 amendments and the remaining segment-level features (stripes, combination operators, intermediate regions,
halftone skip/HCOMBOP, colour extension). Expected pixels are constructed in Python from the spec rules, never produced by the C# decoder.
Every fixture is also rendered by pdfium (via make_jbig2_fixtures.emit, which warns on mismatch) and tools/jbig2_crosscheck.py compares all
fixtures against pdfium, MuPDF/jbig2dec (PyMuPDF) and pdf.js. Fixtures named unverified-* (Amd.2 12-AT templates, Amd.3 colour) are hand-derived from the amendment text only: pdfium 5.14, MuPDF/jbig2dec
and pdf.js 3.11 do not implement either amendment (jbig2dec lists colour as NYI) and no public sample stream exists. Amd.2 leaves the TPGD SLTP context
for the 12-AT template unspecified (Figure 8 is not amended); this generator maps the standard pattern by pixel position, which is an assumption.
Run: python tools/make_jbig2_extension_fixtures.py"""
import os, struct, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import jbig2_enc as E
import make_jbig2_fixtures as M

emit = M.emit


def truth(b):
    return M.truth_str(b)


def gseg(num, b, x=0, y=0, op=0, t=0, tp=38, page=1, tpgd=False, refs=(), flags_extra=0, trailer=b""):
    enc = E.MQEnc(); E.encode_generic(enc, E.Cx(65536), b, t, tpgd, E.NOMINAL_AT[t])
    data = (E.region_info(len(b[0]), len(b), x, y, op | flags_extra) + bytes([t << 1 | (8 if tpgd else 0)]) + E.pack_at(E.NOMINAL_AT[t]) +
            enc.flush() + trailer)
    return E.seg(num, tp, page, data, refs)


# ---------------------------------------------------------------- Amd.2: 12 AT pixels
EXT_NOMINAL = [(-2, 0), (0, -2), (-2, -1), (-1, -2), (1, -2), (2, -1), (-3, 0), (-4, 0), (2, -2), (3, -1), (-2, -2), (-3, -1)]
EXT_FIXED = [(-1, 0), (-1, -1), (0, -1), (1, -1)]
# Figure 3(b) slots in reading order (row -2, row -1, row 0); context bit 15 is the first slot. This numbering deliberately differs from the decoder's.
EXT_SLOTS = [(x, -2) for x in range(-2, 3)] + [(x, -1) for x in range(-3, 4)] + [(x, 0) for x in range(-4, 0)]
STD_POS = [(-1, 0), (-2, 0), (-3, 0), (-4, 0), (3, -1), (2, -1), (1, -1), (0, -1), (-1, -1), (-2, -1), (-3, -1), (2, -2), (1, -2), (0, -2),
           (-1, -2), (-2, -2)]


def ext_ctx(b, x, y, at):
    c = 0
    for pos in EXT_SLOTS:
        if pos in EXT_NOMINAL:
            dx, dy = at[EXT_NOMINAL.index(pos)]
        else:
            assert pos in EXT_FIXED
            dx, dy = pos
        c = c << 1 | E.px(b, x + dx, y + dy)
    return c


def ext_sltp():
    c = 0
    for bit, pos in enumerate(STD_POS):
        if 0x9B25 >> bit & 1:
            c |= 1 << (15 - EXT_SLOTS.index(pos))
    return c


def ext_segment(num, b, at, tpgd=False, page=1):
    enc = E.MQEnc(); cx = E.Cx(65536)
    h = len(b); w = len(b[0]); ltp = 0; sltp = ext_sltp()
    for y in range(h):
        if tpgd:
            same = int(b[y] == (b[y - 1] if y else [0] * w))
            enc.encode(same ^ ltp, cx, sltp); ltp = same
            if same: continue
        for x in range(w):
            enc.encode(b[y][x], cx, ext_ctx(b, x, y, at))
    data = E.region_info(w, h) + bytes([0x10 | (8 if tpgd else 0)]) + E.pack_at(at) + enc.flush()
    return E.seg(num, 38, page, data)


def ext_cases(b, w, h):
    moved = [(-5, 0), (2, -4), (-6, -2), (-1, -5), (3, -3), (4, -1), (-8, 0), (-7, -1), (1, -6), (7, -2), (-3, -3), (-4, -6)]
    for name, at, tpgd in (("unverified-gen-t0-ext", EXT_NOMINAL, False), ("unverified-gen-t0-ext-at", moved, False), ("unverified-gen-t0-ext-tpgd", moved, True)):
        emit(name, w, h, E.seg(1, 48, 1, E.page_info(w, h)) + ext_segment(2, b, at, tpgd), None, truth(b))


# ---------------------------------------------------------------- Amd.3: colour extension
def colour_cases(b, w, h):
    page = struct.pack(">IIIIBH", w, h, 0, 0, 0x80, 0)  # bit 7: page might contain coloured segments
    palette = bytes([0]) + bytes([3, 1]) + struct.pack(">I", 1) + bytes([10, 20, 30])  # flags, CPNCOMP=3, CPCOMPLEN=1, CPNVALS=1, one RGB value
    gen = gseg(3, b, op=4, flags_extra=8, refs=[2], trailer=struct.pack(">I", 32))  # COLEXTFLAG, REPLACE, GBFGCOLID = default colour 32 (palette entry 0)
    emit("unverified-colour-generic", w, h, E.seg(1, 48, 1, page) + E.seg(2, 54, 1, palette) + gen, None, truth(b))

    order, syms = M.glyph_set()
    index = {c: i for i, c in enumerate(order)}
    W, H = 200, 40
    pl, x = [], 5
    for ch in "JBIG2 Decode":
        if ch == " ": x += 6; continue
        pl.append((index[ch], x, 10)); x += len(syms[index[ch]][0]) + 2
    canvas = [[0] * W for _ in range(H)]
    for sid, px_, py_ in pl:
        for j, row in enumerate(syms[sid]):
            for i, v in enumerate(row):
                if v: canvas[py_ + j][px_ + i] = 1
    sd = E.symbol_dict_segment(1, syms, 0, page=1)
    txt = E.text_segment(2, W, H, syms, pl, [1], refcorner=1, page=1)
    hl = 11 + 1  # header length for one referred segment
    body = bytearray(txt[hl:]); body[16] |= 8 | 4  # COLEXTFLAG; external combination operator REPLACE
    section = bytes(range(len(pl))) + struct.pack(">I", len(pl) + 4)  # opaque T.45 colour palette IDs + section length (field included)
    textseg = E.seg(2, 6, 1, bytes(body) + section, [1])
    emit("unverified-colour-text", W, H, E.seg(0, 48, 1, struct.pack(">IIIIBH", W, H, 0, 0, 0x80, 0)) + sd + textseg, None, truth(canvas))


# ---------------------------------------------------------------- stripes of unknown height
def stripe_case(b, w, h):
    sh = h // 2
    top, bottom = b[:sh], b[sh:sh * 2]
    page = struct.pack(">IIIIBH", w, 0xFFFFFFFF, 0, 0, 0, 0x8000 | sh)
    stream = (E.seg(1, 48, 1, page) + gseg(2, top, 0, 0, 0) + E.seg(3, 50, 1, struct.pack(">I", sh - 1)) +
              gseg(4, bottom, 0, sh, 0) + E.seg(5, 50, 1, struct.pack(">I", 2 * sh - 1)) + E.seg(6, 49, 1, b""))
    emit("stripes-unknown-height", w, 2 * sh, stream, None, truth(top + bottom))


# ---------------------------------------------------------------- combination operators
def shape(w, h, kind):
    if kind == 0: return [[1 if (x // 4 + y // 4) % 2 == 0 else 0 for x in range(w)] for y in range(h)]
    if kind == 1: return [[1 if (x + 2 * y) % 5 < 2 else 0 for x in range(w)] for y in range(h)]
    if kind == 2: return [[1 if (x - w / 2) ** 2 + (y - h / 2) ** 2 < (min(w, h) / 2) ** 2 else 0 for x in range(w)] for y in range(h)]
    if kind == 3: return [[1 if x % 3 == 0 or y % 7 == 0 else 0 for x in range(w)] for y in range(h)]
    return [[1 if x > y else 0 for x in range(w)] for y in range(h)]


def combine(dst, src, x0, y0, op):
    for y, row in enumerate(src):
        for x, v in enumerate(row):
            yy, xx = y0 + y, x0 + x
            if not (0 <= yy < len(dst) and 0 <= xx < len(dst[0])): continue
            d = dst[yy][xx]
            dst[yy][xx] = (d | v, d & v, d ^ v, 1 - (d ^ v), v)[op]


def comb_cases():
    W, H = 72, 56
    placements = [(0, 4, 4, 40, 30), (1, 20, 10, 40, 30), (2, 10, 22, 36, 30), (3, 30, 18, 30, 30), (4, 40, 30, 36, 24)]
    for default in (0, 1):
        for opsel in ((0, 2, 1, 3, 4), (4, 3, 2, 1, 0)):
            page = [[default] * W for _ in range(H)]
            stream = E.seg(0, 48, 1, E.page_info(W, H, default))
            for k, ((kind, x, y, w, h), op) in enumerate(zip(placements, opsel)):
                bm = shape(w, h, kind)
                combine(page, bm, x, y, op)
                stream += gseg(1 + k, bm, x, y, op, t=k % 4)
            emit(f"comb-def{default}-{''.join(map(str, opsel))}", W, H, stream, None, truth(page))


# ---------------------------------------------------------------- intermediate regions and refinement references
def refine_cases(b, w, h):
    b2 = [row[:] for row in b]
    for y in range(40, 120, 3):
        for x in range(100, 220, 5): b2[y][x] ^= 1
    enc = E.MQEnc(); E.encode_refine(enc, E.Cx(8192), b2, b, 0, 0, 0, False)
    data = E.region_info(w, h, 0, 0, 4) + bytes([0]) + E.pack_at(((-1, -1), (-1, -1))) + enc.flush()
    stream = (E.seg(1, 48, 1, E.page_info(w, h)) + gseg(2, b, tp=36) + E.seg(3, 42, 1, data, [2]))
    emit("intermediate-refine", w, h, stream, None, truth(b2))

    rx, ry, rw, rh = 20, 10, 100, 60
    ref = [row[rx:rx + rw] for row in b[ry:ry + rh]]
    tgt = [row[:] for row in ref]
    for y in range(0, rh, 4):
        for x in range(1, rw, 6): tgt[y][x] ^= 1
    page = [row[:] for row in b]
    combine(page, tgt, rx, ry, 4)
    for name, t_, tpgr in (("refine-page-offset", 1, True), ("refine-page-offset-t0", 0, False), ("refine-page-offset-t1", 1, False),
                           ("refine-page-offset-t0-tpgr", 0, True)):
        enc = E.MQEnc(); E.encode_refine(enc, E.Cx(8192), tgt, ref, 0, 0, t_, tpgr)
        data = E.region_info(rw, rh, rx, ry, 4) + bytes([t_ | (2 if tpgr else 0)]) + (E.pack_at(((-1, -1), (-1, -1))) if t_ == 0 else b"") + enc.flush()
        emit(name, w, h, E.seg(1, 48, 1, E.page_info(w, h)) + gseg(2, b) + E.seg(3, 42, 1, data), None, truth(page))


# ---------------------------------------------------------------- halftone (arithmetic) with skip, HCOMBOP, rotated grid
def pattern_set(pw, ph, n):
    pats = []
    for g in range(n):
        pats.append([[1 if (x * (g + 1) + y * g) % (g + 2) == 0 or (g == n - 1) else 0 for x in range(pw)] for y in range(ph)])
    return pats


def encode_planes(g, gw, gh, bpp, skip, tmpl):
    enc = E.MQEnc(); cx = E.Cx(65536)
    at = [(3 if tmpl <= 1 else 2, -1), (-3, -1), (2, -2), (-2, -2)]
    prev = None
    for j in range(bpp - 1, -1, -1):
        plane = [[(g[m][n] >> j & 1) ^ (g[m][n] >> (j + 1) & 1 if j < bpp - 1 else 0) for n in range(gw)] for m in range(gh)]
        for m in range(gh):
            for n in range(gw):
                if skip and skip[m][n]: continue
                enc.encode(plane[m][n], cx, E.generic_ctx(plane, n, m, tmpl, at))
    return enc.flush()


def halftone_case(name, tmpl, hcombop, defpixel, enable_skip, rx, ry, gx, gy, gw, gh, W, H, pw=6, ph=6, levels=4, mmr=False):
    pats = pattern_set(pw, ph, levels)
    coll = [sum((pats[i][y] for i in range(levels)), []) for y in range(ph)]
    enc = E.MQEnc()
    pat_at = [(-pw, 0), (-3, -1), (2, -2), (-2, -2)]
    E.encode_generic(enc, E.Cx(65536), coll, 0, False, pat_at)
    pdict = bytes([0]) + bytes([pw, ph]) + struct.pack(">I", levels - 1) + enc.flush()
    bpp = (levels - 1).bit_length()
    g = [[(m * 3 + n * 5 + m * n) % levels for n in range(gw)] for m in range(gh)]
    skip = [[0] * gw for _ in range(gh)]
    for m in range(gh):
        for n in range(gw):
            x = (gx + m * ry + n * rx) >> 8; y = (gy + m * rx - n * ry) >> 8
            if x + pw <= 0 or x >= W or y + ph <= 0 or y >= H: skip[m][n] = 1
    if enable_skip:
        for m in range(gh):
            for n in range(gw):
                if skip[m][n]: g[m][n] = 0
    region = [[defpixel] * W for _ in range(H)]
    for m in range(gh):
        for n in range(gw):
            x = (gx + m * ry + n * rx) >> 8; y = (gy + m * rx - n * ry) >> 8
            combine(region, pats[g[m][n]], x, y, hcombop)
    if mmr:
        planes = b"".join(g4([[(g[m][n] >> j & 1) ^ (g[m][n] >> (j + 1) & 1 if j < bpp - 1 else 0) for n in range(gw)] for m in range(gh)])
                          for j in range(bpp - 1, -1, -1))
        pdict = bytes([1]) + bytes([pw, ph]) + struct.pack(">I", levels - 1) + g4(coll)
        flags = 1 | hcombop << 4 | defpixel << 7
    else:
        planes = encode_planes(g, gw, gh, bpp, skip if enable_skip else None, tmpl)
        flags = tmpl << 1 | (8 if enable_skip else 0) | hcombop << 4 | defpixel << 7
    hdata = E.region_info(W, H, 0, 0, 0) + bytes([flags]) + struct.pack(">IIiiHH", gw, gh, gx, gy, rx, ry) + planes
    stream = E.seg(1, 48, 1, E.page_info(W, H)) + E.seg(2, 16, 1, pdict) + E.seg(3, 22, 1, hdata, [2]) + E.seg(4, 49, 1, b"")
    emit(name, W, H, stream, None, truth(region))


def g4(bitmap):
    """Group 4 (MMR) data for a 0/1 bitmap (1 = black), including EOFB, taken from libtiff via Pillow."""
    import io
    from PIL import Image
    w, h = len(bitmap[0]), len(bitmap)
    im = Image.new("1", (w, h), 0)
    im.putdata([v for row in bitmap for v in row])
    buf = io.BytesIO(); im.save(buf, format="TIFF", compression="group4")
    t = Image.open(io.BytesIO(buf.getvalue()))
    off, cnt = t.tag_v2[273], t.tag_v2[279]
    off = off if isinstance(off, int) else off[0]; cnt = cnt if isinstance(cnt, int) else cnt[0]
    return buf.getvalue()[off:off + cnt]


def halftone_cases():
    halftone_case("halftone-skip-or", 0, 0, 0, True, 5 << 8, 2 << 8, -8 << 8, 6 << 8, 20, 14, 64, 48)
    halftone_case("halftone-skip-xor", 1, 2, 0, True, 4 << 8, 1 << 8, -3 << 8, 10 << 8, 22, 16, 64, 48)
    halftone_case("halftone-noskip-and-def1", 2, 1, 1, False, 6 << 8, 0, 0, 0, 12, 9, 72, 54)
    halftone_case("halftone-skip-xnor", 3, 3, 0, True, 5 << 8, 3 << 8, -10 << 8, 20 << 8, 24, 18, 64, 48)
    halftone_case("halftone-skip-replace", 0, 4, 1, True, 3 << 8, 1 << 8, 2 << 8, -2 << 8, 24, 18, 64, 48)
    halftone_case("halftone-mmr-or", 0, 0, 0, False, 5 << 8, 2 << 8, -8 << 8, 6 << 8, 20, 14, 64, 48, mmr=True)
    halftone_case("halftone-mmr-xor-rot", 0, 2, 0, False, 4 << 8, 1 << 8, 0, 8 << 8, 22, 16, 64, 48, mmr=True, levels=8)


# ---------------------------------------------------------------- unknown-length generic region
def unknown_length_case(b, w, h):
    enc = E.MQEnc(); E.encode_generic(enc, E.Cx(65536), b, 0, False, E.NOMINAL_AT[0])
    data = (E.region_info(w, h + 7, 0, 0, 0) + bytes([0]) + E.pack_at(E.NOMINAL_AT[0]) + enc.flush() + struct.pack(">I", h))
    seg = struct.pack(">IB", 2, 38) + bytes([0, 1]) + struct.pack(">I", 0xFFFFFFFF) + data  # data length unknown: ends with FF AC + row count
    emit("generic-unknown-length", w, h, E.seg(1, 48, 1, E.page_info(w, h)) + seg + E.seg(3, 49, 1, b""), None, truth(b))


# ---------------------------------------------------------------- retained/used symbol dictionary contexts and partial exports; transposed corners
def symbol_dict_chain(page=1):
    order, syms = M.glyph_set()
    a_syms, b_syms = syms[:3], syms[3:6]
    ctx = E.Cx(65536)

    def dictionary(num, new, n_in, runs, retain, used, refs):
        enc = E.MQEnc()
        iadh, iadw, iaex = E.IntEnc(), E.IntEnc(), E.IntEnc()
        hh = 0; i = 0
        while i < len(new):
            height = len(new[i]); iadh.encode(enc, height - hh); hh = height; ww = 0
            while i < len(new) and len(new[i]) == height:
                sw = len(new[i][0]); iadw.encode(enc, sw - ww); ww = sw
                E.encode_generic(enc, ctx, new[i], 0, False, E.NOMINAL_AT[0]); i += 1
            iadw.encode(enc, None)
        for r in runs: iaex.encode(enc, r)
        exported = sum(runs[1::2])
        flags = (0x100 if used else 0) | (0x200 if retain else 0)
        data = struct.pack(">H", flags) + E.pack_at(E.NOMINAL_AT[0]) + struct.pack(">II", exported, len(new)) + enc.flush()
        return E.seg(num, 0, page, data, refs)

    d1 = dictionary(1, a_syms, 0, [0, 3], True, False, [])
    # dictionary 2 reuses dictionary 1's contexts, imports its three symbols and exports only the second one plus all three new ones
    d2 = dictionary(2, b_syms, 3, [1, 1, 1, 3], False, True, [1])
    return d1 + d2, [a_syms[1]] + b_syms


def text_extra():
    order, syms = M.glyph_set()
    W, H = 200, 120
    chain, exported = symbol_dict_chain()
    placements = []
    for k, line in enumerate(("abcd", "bcad", "dcba")):
        x = 6
        for ch in line:
            sid = "abcd".index(ch)
            placements.append((sid, x, 8 + k * 24))
            x += len(exported[sid][0]) + 3
    canvas = [[0] * W for _ in range(H)]
    for sid, x, y in placements:
        for j, row in enumerate(exported[sid]):
            for i, v in enumerate(row):
                if v and 0 <= y + j < H and 0 <= x + i < W: canvas[y + j][x + i] = 1
    body = E.seg(0, 48, 1, E.page_info(W, H)) + chain + E.text_segment(3, W, H, exported, placements, [2], refcorner=1, page=1)
    emit("symdict-ctx-retained-partial-export", W, H, body, None, truth(canvas))

    # transposed text regions with every reference corner (SBCOMBOP = OR)
    order, syms = M.glyph_set()
    index = {c: i for i, c in enumerate(order)}
    pl = []
    for r, line in enumerate(M.GLYPH_TEXT):
        x = 5
        for ch in line:
            if ch == " ": x += 6; continue
            pl.append((index[ch], x, 10 + r * 22)); x += len(syms[index[ch]][0]) + 2
    page = [[0] * W for _ in range(H)]
    for sid, x, y in pl:
        for j, row in enumerate(syms[sid]):
            for i, v in enumerate(row):
                if v and 0 <= y + j < H and 0 <= x + i < W: page[y + j][x + i] = 1
    sd = E.symbol_dict_segment(1, syms, 0, page=1)
    for name, corner in (("text-transposed-bottomleft", 0), ("text-transposed-topright", 3), ("text-transposed-bottomright", 2)):
        body = E.seg(0, 48, 1, E.page_info(W, H)) + sd + E.text_segment(2, W, H, syms, pl, [1], refcorner=corner, transposed=True, page=1)
        emit(name, W, H, body, None, truth(page))


if __name__ == "__main__":
    b = M.test_image(); h, w = len(b), len(b[0])
    ext_cases(b, w, h)
    colour_cases(b, w, h)
    stripe_case(b, w, h)
    comb_cases()
    refine_cases(b, w, h)
    halftone_cases()
    unknown_length_case(b, w, h)
    text_extra()
