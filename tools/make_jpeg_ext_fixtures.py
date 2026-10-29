"""Generates DCTDecode fixtures Pillow cannot write: arithmetic-coded (SOF9/SOF10) and lossless Huffman (SOF3) JPEGs.

Writes tests/fixtures/jpegx/name.pdf (one /Im0 DCTDecode image), name.bin (8-bit reference pixels) and name.meta
("MODE width height tolerance"). The arithmetic encoder is a Python port of the QM-coder procedures of ITU-T T.81 Annex D/F/G
(the probability table is read from src/AdoBye.Content/JpegArith.cs). Arithmetic references come from Pillow (libjpeg-turbo)
when it can decode the file, and from a numpy float IDCT otherwise; lossless references are the source samples.
"""
import io
import os
import re
import sys
import numpy as np
from pikepdf import Pdf, Stream, Name, Dictionary
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from make_jpx_fixtures import picture  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "tests", "fixtures", "jpegx")
os.makedirs(OUT, exist_ok=True)

_raw = re.search(r"QmRaw = \[(.*?)\];", open(os.path.join(ROOT, "src", "AdoBye.Content", "JpegArith.cs")).read(), re.S).group(1)
_v = [int(x, 0) for x in _raw.replace("\n", " ").split(",")]
QM = [(_v[i] << 16) | (_v[i + 2] << 8) | (_v[i + 3] << 7) | _v[i + 1] for i in range(0, len(_v), 4)]

ZIG = [0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
       35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63]

QL = [16, 11, 10, 16, 24, 40, 51, 61, 12, 12, 14, 19, 26, 58, 60, 55, 14, 13, 16, 24, 40, 57, 69, 56, 14, 17, 22, 29, 51, 87, 80, 62,
      18, 22, 37, 56, 68, 109, 103, 77, 24, 35, 55, 64, 81, 104, 113, 92, 49, 64, 78, 87, 103, 121, 120, 101, 72, 92, 95, 98, 112, 100, 103, 99]
QC = [17, 18, 24, 47, 99, 99, 99, 99, 18, 21, 26, 66, 99, 99, 99, 99, 24, 26, 56, 99, 99, 99, 99, 99, 47, 66, 99, 99, 99, 99, 99, 99] + [99] * 32


class Enc:
    def __init__(s):
        s.out = bytearray()
        s.start()

    def start(s):
        s.c = 0
        s.a = 0x10000
        s.sc = 0
        s.zc = 0
        s.ct = 11
        s.buffer = -1

    def _zeros(s):
        s.out.extend(b"\0" * s.zc)
        s.zc = 0

    def _ff(s):
        for _ in range(s.sc):
            s.out.extend(b"\xff\0")
        s.sc = 0

    def encode(s, st, i, val):
        sv = st[i]
        t = QM[sv & 0x7F]
        qe = t >> 16
        nl = t & 0xFF
        nm = (t >> 8) & 0xFF
        s.a -= qe
        if val != (sv >> 7):
            if s.a >= qe:
                s.c += s.a
                s.a = qe
            st[i] = (sv & 0x80) ^ nl
        else:
            if s.a >= 0x8000:
                return
            if s.a < qe:
                s.c += s.a
                s.a = qe
            st[i] = (sv & 0x80) ^ nm
        while True:
            s.a <<= 1
            s.c <<= 1
            s.ct -= 1
            if s.ct == 0:
                temp = s.c >> 19
                if temp > 0xFF:
                    if s.buffer >= 0:
                        s._zeros()
                        s.out.append(s.buffer + 1)
                        if s.buffer + 1 == 0xFF:
                            s.out.append(0)
                    s.zc += s.sc
                    s.sc = 0
                    s.buffer = temp & 0xFF
                elif temp == 0xFF:
                    s.sc += 1
                else:
                    if s.buffer == 0:
                        s.zc += 1
                    elif s.buffer >= 0:
                        s._zeros()
                        s.out.append(s.buffer)
                    if s.sc:
                        s._zeros()
                        s._ff()
                    s.buffer = temp & 0xFF
                s.c &= 0x7FFFF
                s.ct += 8
            if s.a >= 0x8000:
                break

    def finish(s):
        temp = (s.a - 1 + s.c) & 0xFFFF0000
        s.c = temp + 0x8000 if temp < s.c else temp
        s.c <<= s.ct
        if s.c & 0xF8000000:
            if s.buffer >= 0:
                s._zeros()
                s.out.append(s.buffer + 1)
                if s.buffer + 1 == 0xFF:
                    s.out.append(0)
            s.zc += s.sc
            s.sc = 0
        else:
            if s.buffer == 0:
                s.zc += 1
            elif s.buffer >= 0:
                s._zeros()
                s.out.append(s.buffer)
            if s.sc:
                s._zeros()
                s._ff()
        if s.c & 0x7FFF800:
            s._zeros()
            b = (s.c >> 19) & 0xFF
            s.out.append(b)
            if b == 0xFF:
                s.out.append(0)
            if s.c & 0x7F800:
                b = (s.c >> 11) & 0xFF
                s.out.append(b)
                if b == 0xFF:
                    s.out.append(0)


def enc_dc(e, st, lo, hi, blk, state, al):
    m = blk[0] >> al
    s = state["ctx"]
    v = m - state["last"]
    if v == 0:
        e.encode(st, s, 0)
        state["ctx"] = 0
        return
    state["last"] = m
    e.encode(st, s, 1)
    if v > 0:
        e.encode(st, s + 1, 0)
        s += 2
        ctx = 4
    else:
        v = -v
        e.encode(st, s + 1, 1)
        s += 3
        ctx = 8
    m = 0
    v -= 1
    if v:
        e.encode(st, s, 1)
        m = 1
        v2 = v
        s = 20
        while True:
            v2 >>= 1
            if not v2:
                break
            e.encode(st, s, 1)
            m <<= 1
            s += 1
    e.encode(st, s, 0)
    if m < ((1 << lo) >> 1):
        ctx = 0
    elif m > ((1 << hi) >> 1):
        ctx += 8
    state["ctx"] = ctx
    s += 14
    while True:
        m >>= 1
        if not m:
            break
        e.encode(st, s, 1 if m & v else 0)


def last_nonzero(blk, se, sh):
    ke = se
    while ke > 0:
        if abs(blk[ZIG[ke]]) >> sh:
            break
        ke -= 1
    return ke


def enc_ac_first(e, st, kx, fixed, blk, ss, se, al):
    ke = last_nonzero(blk, se, al)
    k = ss
    while k <= ke:
        s = 3 * (k - 1)
        e.encode(st, s, 0)
        while True:
            v = blk[ZIG[k]]
            neg = v < 0
            v = abs(v) >> al
            if v:
                e.encode(st, s + 1, 1)
                e.encode(fixed, 0, 1 if neg else 0)
                break
            e.encode(st, s + 1, 0)
            s += 3
            k += 1
        s += 2
        m = 0
        v -= 1
        if v:
            e.encode(st, s, 1)
            m = 1
            v2 = v >> 1
            if v2:
                e.encode(st, s, 1)
                m <<= 1
                s = 189 if k <= kx else 217
                while True:
                    v2 >>= 1
                    if not v2:
                        break
                    e.encode(st, s, 1)
                    m <<= 1
                    s += 1
        e.encode(st, s, 0)
        s += 14
        while True:
            m >>= 1
            if not m:
                break
            e.encode(st, s, 1 if m & v else 0)
        k += 1
    if k <= se:
        e.encode(st, 3 * (k - 1), 1)


def enc_ac_refine(e, st, fixed, blk, ss, se, ah, al):
    ke = last_nonzero(blk, se, al)
    kex = ke
    while kex > 0:
        if abs(blk[ZIG[kex]]) >> ah:
            break
        kex -= 1
    k = ss
    while k <= ke:
        s = 3 * (k - 1)
        if k > kex:
            e.encode(st, s, 0)
        while True:
            v = blk[ZIG[k]]
            neg = v < 0
            v = abs(v) >> al
            if v:
                if v >> 1:
                    e.encode(st, s + 2, v & 1)
                else:
                    e.encode(st, s + 1, 1)
                    e.encode(fixed, 0, 1 if neg else 0)
                break
            e.encode(st, s + 1, 0)
            s += 3
            k += 1
        k += 1
    if k <= se:
        e.encode(st, 3 * (k - 1), 1)


def encode_scan(comps, scan, hmax, vmax, mcux, mcuy, restart, cond):
    e = Enc()
    dcs = [bytearray(64) for _ in range(4)]
    acs = [bytearray(256) for _ in range(4)]
    fixed = bytearray([113, 0, 0, 0])
    states = {ci: {"last": 0, "ctx": 0} for ci in scan["ids"]}
    prog, ss, se, ah, al = scan["prog"], scan["ss"], scan["se"], scan["ah"], scan["al"]

    def block(ci, bx, by):
        c = comps[ci]
        blk = c["blocks"][by][bx]
        td = c["td"]
        if not prog:
            enc_dc(e, dcs[td], cond["dcl"][td], cond["dcu"][td], blk, states[ci], 0)
            enc_ac_first(e, acs[td], cond["ack"][td], fixed, blk, 1, 63, 0)
        elif ss == 0:
            if ah == 0:
                enc_dc(e, dcs[td], cond["dcl"][td], cond["dcu"][td], blk, states[ci], al)
            else:
                e.encode(fixed, 0, (blk[0] >> al) & 1)
        elif ah == 0:
            enc_ac_first(e, acs[td], cond["ack"][td], fixed, blk, ss, se, al)
        else:
            enc_ac_refine(e, acs[td], fixed, blk, ss, se, ah, al)

    n = 0
    rst = 0

    def maybe_restart():
        nonlocal rst
        if restart and n > 0 and n % restart == 0:
            e.finish()
            e.out.extend([0xFF, 0xD0 + rst])
            rst = (rst + 1) & 7
            for a in dcs:
                a[:] = bytes(len(a))
            for a in acs:
                a[:] = bytes(len(a))
            for st in states.values():
                st["last"] = st["ctx"] = 0
            e.start()

    if len(scan["ids"]) == 1:
        ci = scan["ids"][0]
        for by in range(comps[ci]["rbh"]):
            for bx in range(comps[ci]["rbw"]):
                maybe_restart()
                block(ci, bx, by)
                n += 1
    else:
        for r in range(mcuy):
            for col in range(mcux):
                maybe_restart()
                for ci in scan["ids"]:
                    c = comps[ci]
                    for v in range(c["V"]):
                        for h in range(c["H"]):
                            block(ci, col * c["H"] + h, r * c["V"] + v)
                n += 1
    e.finish()
    return bytes(e.out)


def seg(marker, payload):
    return bytes([0xFF, marker]) + (len(payload) + 2).to_bytes(2, "big") + payload


C = np.zeros((8, 8))
for u in range(8):
    for x in range(8):
        C[u, x] = np.sqrt((1 if u == 0 else 2) / 8) * np.cos((2 * x + 1) * u * np.pi / 16)


def prepare(im, sub, quality=85):
    """Returns (comps, w, h, hmax, vmax, mcux, mcuy, qtables) with quantised coefficient blocks."""
    w, h = im.size
    if im.mode == "L":
        planes = [(np.asarray(im).astype(float), 1, 1)]
    else:
        y, cb, cr = [np.asarray(c).astype(float) for c in im.convert("YCbCr").split()]
        s = 2 if sub else 1
        if sub:
            def down(p):
                ph, pw = (h + 1) // 2 * 2, (w + 1) // 2 * 2
                p = np.pad(p, ((0, ph - h), (0, pw - w)), mode="edge")
                return p.reshape(ph // 2, 2, pw // 2, 2).mean(axis=(1, 3))
            cb, cr = down(cb), down(cr)
        planes = [(y, s, s), (cb, 1, 1), (cr, 1, 1)]
    hmax = max(p[1] for p in planes)
    vmax = max(p[2] for p in planes)
    mcux = (w + 8 * hmax - 1) // (8 * hmax)
    mcuy = (h + 8 * vmax - 1) // (8 * vmax)
    scale = 5000 // quality if quality < 50 else 200 - 2 * quality
    qt = [np.clip((np.array(t) * scale + 50) // 100, 1, 255).astype(int) for t in (QL, QC)]
    comps = []
    for i, (p, H, V) in enumerate(planes):
        bw, bh = mcux * H, mcuy * V
        padded = np.pad(p, ((0, bh * 8 - p.shape[0]), (0, bw * 8 - p.shape[1])), mode="edge") - 128
        blocks = padded.reshape(bh, 8, bw, 8).transpose(0, 2, 1, 3)
        coef = C @ blocks @ C.T
        q = qt[0 if i == 0 else 1].reshape(8, 8)
        qc = np.rint(coef / q).astype(int).reshape(bh, bw, 64)
        cw = (w * H + hmax - 1) // hmax
        ch = (h * V + vmax - 1) // vmax
        comps.append(dict(id=i + 1, H=H, V=V, bw=bw, bh=bh, rbw=(cw + 7) // 8, rbh=(ch + 7) // 8, cw=cw, ch=ch,
                          blocks=qc.tolist(), td=0 if i == 0 else 1, tq=0 if i == 0 else 1, q=q, raw=qc))
    return comps, w, h, hmax, vmax, mcux, mcuy, qt


def numpy_reference(comps, w, h, hmax, vmax):
    planes = []
    for c in comps:
        co = c["raw"].reshape(c["bh"], c["bw"], 8, 8) * c["q"]
        px = C.T @ co @ C
        full = px.transpose(0, 2, 1, 3).reshape(c["bh"] * 8, c["bw"] * 8) + 128
        ys = (np.arange(h) * c["V"]) // vmax
        xs = (np.arange(w) * c["H"]) // hmax
        planes.append(full[np.ix_(ys, xs)])
    if len(planes) == 1:
        return np.clip(np.rint(planes[0]), 0, 255).astype(np.uint8)
    y, cb, cr = planes
    r = y + 1.402 * (cr - 128)
    g = y - 0.344136 * (cb - 128) - 0.714136 * (cr - 128)
    b = y + 1.772 * (cb - 128)
    return np.clip(np.rint(np.stack([r, g, b], -1)), 0, 255).astype(np.uint8)


def build_dct(comps, w, h, hmax, vmax, mcux, mcuy, qt, progressive, scans, restart=0, cond=None):
    cond = cond or dict(dcl=[0] * 4, dcu=[1] * 4, ack=[5] * 4)
    out = bytearray(b"\xff\xd8")
    for i, t in enumerate(qt[: 1 if len(comps) == 1 else 2]):
        out += seg(0xDB, bytes([i]) + bytes(int(t.reshape(64)[ZIG[k]]) for k in range(64)))
    sof = bytes([8]) + h.to_bytes(2, "big") + w.to_bytes(2, "big") + bytes([len(comps)])
    for c in comps:
        sof += bytes([c["id"], c["H"] << 4 | c["V"], c["tq"]])
    out += seg(0xCA if progressive else 0xC9, sof)
    if cond["dcl"] != [0] * 4 or cond["dcu"] != [1] * 4 or cond["ack"] != [5] * 4:
        dac = b""
        for t in range(2):
            dac += bytes([t, cond["dcl"][t] | cond["dcu"][t] << 4, 0x10 | t, cond["ack"][t]])
        out += seg(0xCC, dac)
    if restart:
        out += seg(0xDD, restart.to_bytes(2, "big"))
    for sc in scans:
        sc = dict(sc, prog=progressive)
        sos = bytes([len(sc["ids"])])
        for ci in sc["ids"]:
            sos += bytes([comps[ci]["id"], comps[ci]["td"] << 4 | comps[ci]["td"]])
        sos += bytes([sc["ss"], sc["se"], sc["ah"] << 4 | sc["al"]])
        out += seg(0xDA, sos)
        out += encode_scan(comps, sc, hmax, vmax, mcux, mcuy, restart, cond)
    out += b"\xff\xd9"
    return bytes(out)


def seq_scans(n, interleaved=True):
    full = dict(ss=0, se=63, ah=0, al=0)
    return [dict(full, ids=list(range(n)))] if interleaved or n == 1 else [dict(full, ids=[i]) for i in range(n)]


def prog_scans(n):
    s = [dict(ids=list(range(n)), ss=0, se=0, ah=0, al=1)]
    if n == 1:
        s += [dict(ids=[0], ss=1, se=5, ah=0, al=2), dict(ids=[0], ss=6, se=63, ah=0, al=2),
              dict(ids=[0], ss=1, se=63, ah=2, al=1), dict(ids=[0], ss=0, se=0, ah=1, al=0), dict(ids=[0], ss=1, se=63, ah=1, al=0)]
    else:
        s += [dict(ids=[0], ss=1, se=5, ah=0, al=2), dict(ids=[1], ss=1, se=63, ah=0, al=1), dict(ids=[2], ss=1, se=63, ah=0, al=1),
              dict(ids=[0], ss=6, se=63, ah=0, al=2), dict(ids=[0], ss=1, se=63, ah=2, al=1),
              dict(ids=list(range(n)), ss=0, se=0, ah=1, al=0),
              dict(ids=[1], ss=1, se=63, ah=1, al=0), dict(ids=[2], ss=1, se=63, ah=1, al=0), dict(ids=[0], ss=1, se=63, ah=1, al=0)]
    return s


def make_pdf(path, w, h, data, cs):
    pdf = Pdf.new()
    page = pdf.add_blank_page(page_size=(max(w, 3), max(h, 3)))
    img = Stream(pdf, data)
    img.Type = Name.XObject
    img.Subtype = Name.Image
    img.Width = w
    img.Height = h
    img.ColorSpace = Name(cs)
    img.BitsPerComponent = 8
    img.Filter = Name.DCTDecode
    page.Resources = Dictionary(XObject=Dictionary(Im0=img))
    page.Contents = pdf.make_stream(f"q {w} 0 0 {h} 0 0 cm /Im0 Do Q".encode())
    pdf.save(path)


def write(name, w, h, data, ref, tol):
    mode = "L" if ref.ndim == 2 else "RGB"
    make_pdf(os.path.join(OUT, name + ".pdf"), w, h, data, "/DeviceGray" if mode == "L" else "/DeviceRGB")
    open(os.path.join(OUT, name + ".bin"), "wb").write(np.ascontiguousarray(ref).tobytes())
    open(os.path.join(OUT, name + ".meta"), "w").write(f"{mode} {w} {h} {tol}")
    print("wrote", name, mode, w, h, len(data), "bytes tol", tol)


def arith_case(name, im, progressive, sub=False, restart=0, cond=None, interleaved=True, quality=85):
    comps, w, h, hmax, vmax, mcux, mcuy, qt = prepare(im, sub, quality)
    n = len(comps)
    scans = prog_scans(n) if progressive else seq_scans(n, interleaved)
    data = build_dct(comps, w, h, hmax, vmax, mcux, mcuy, qt, progressive, scans, restart, cond)
    ref = numpy_reference(comps, w, h, hmax, vmax)
    tol = 12 if sub else 3  # numpy reference upsamples chroma differently at edges
    try:
        px = Image.open(io.BytesIO(data))
        px.load()
        pil = np.asarray(px)
        if not sub:
            diff = int(np.abs(pil.astype(int) - ref.astype(int)).max())
            print("  pillow decodes it; max diff vs numpy reference", diff)
            ref, tol = pil, 2
    except Exception as ex:
        print("  pillow cannot decode:", ex)
    write(name, w, h, data, ref, tol)


class Bits:
    def __init__(s):
        s.out = bytearray()
        s.acc = 0
        s.n = 0

    def put(s, v, n):
        s.acc = s.acc << n | v
        s.n += n
        while s.n >= 8:
            b = (s.acc >> (s.n - 8)) & 0xFF
            s.out.append(b)
            if b == 0xFF:
                s.out.append(0)
            s.n -= 8
        s.acc &= (1 << s.n) - 1

    def flush(s):
        if s.n:
            s.put((1 << (8 - s.n)) - 1, 8 - s.n)


def lossless_case(name, planes, prec, pred, pt, restart=0, interleaved=True, adobe=True):
    """planes: list of (h, w) int arrays holding prec-bit samples."""
    nc = len(planes)
    h, w = planes[0].shape
    initial = 1 << (prec - pt - 1)
    out = bytearray(b"\xff\xd8")
    if nc == 3 and adobe:
        out += seg(0xEE, b"Adobe" + bytes([0, 100, 0, 0, 0, 0, 0]))
    out += seg(0xC3, bytes([prec]) + h.to_bytes(2, "big") + w.to_bytes(2, "big") + bytes([nc]) + b"".join(bytes([i + 1, 0x11, 0]) for i in range(nc)))
    out += seg(0xC4, bytes([0]) + bytes([0, 0, 0, 0, 17] + [0] * 11) + bytes(range(17)))
    if restart:
        out += seg(0xDD, restart.to_bytes(2, "big"))
    src = [(p >> pt).astype(int).tolist() for p in planes]
    groups = [list(range(nc))] if interleaved or nc == 1 else [[i] for i in range(nc)]
    for g in groups:
        out += seg(0xDA, bytes([len(g)]) + b"".join(bytes([i + 1, 0]) for i in g) + bytes([pred, 0, pt]))
        bw = Bits()
        start = 0
        start_row = 0
        rst = 0
        for i in range(w * h):
            y, x = divmod(i, w)
            if restart and i > 0 and i % restart == 0:
                bw.flush()
                bw.out.extend([0xFF, 0xD0 + rst])
                rst = (rst + 1) & 7
                start, start_row = i, y
            for ci in g:
                p = src[ci]
                if i == start:
                    pr = initial
                elif y == start_row:
                    pr = p[y][x - 1]
                elif x == 0:
                    pr = p[y - 1][x]
                else:
                    ra, rb, rc = p[y][x - 1], p[y - 1][x], p[y - 1][x - 1]
                    pr = {1: ra, 2: rb, 3: rc, 4: ra + rb - rc, 5: ra + ((rb - rc) >> 1), 6: rb + ((ra - rc) >> 1), 7: (ra + rb) >> 1}[pred]
                d = (p[y][x] - pr) & 0xFFFF
                if d >= 32768:
                    d -= 65536
                if d == -32768:
                    bw.put(16, 5)
                    continue
                cat = abs(d).bit_length()
                bw.put(cat, 5)
                if cat:
                    bw.put(d if d > 0 else d + (1 << cat) - 1, cat)
        bw.flush()
        out += bw.out
    out += b"\xff\xd9"
    ref = np.stack([((p >> pt) << pt) for p in planes], -1) if nc > 1 else (planes[0] >> pt) << pt
    if prec > 8:
        ref = ref >> (prec - 8)
    elif prec < 8:
        mx = (1 << prec) - 1
        ref = (ref * 255 + (mx >> 1)) // mx
    write(name, w, h, bytes(out), ref.astype(np.uint8), 0)


def main():
    rgb = picture(96, 72)
    gray = rgb.convert("L")
    odd = picture(37, 53, 7)
    arith_case("arith-seq-gray", gray, False)
    arith_case("arith-seq-rgb", rgb, False)
    arith_case("arith-seq-rgb-420", rgb, False, sub=True)
    arith_case("arith-seq-rgb-noninterleaved", rgb, False, interleaved=False)
    arith_case("arith-seq-restart", rgb, False, restart=5)
    arith_case("arith-seq-conditioning", rgb, False, cond=dict(dcl=[1, 2, 0, 0], dcu=[3, 4, 1, 1], ack=[10, 2, 5, 5]))
    arith_case("arith-seq-odd", odd.convert("L"), False)
    arith_case("arith-seq-hq", rgb, False, quality=98)
    arith_case("arith-prog-gray", gray, True)
    arith_case("arith-prog-rgb", rgb, True)
    arith_case("arith-prog-rgb-420", rgb, True, sub=True)
    arith_case("arith-prog-restart", rgb, True, restart=7)
    arith_case("arith-prog-odd", odd, True)

    g16 = np.asarray(gray).astype(int)
    rng = np.random.default_rng(3)
    arr = np.asarray(rgb).astype(int)
    for p in range(1, 8):
        lossless_case(f"lossless-gray8-pred{p}", [g16], 8, p, 0)
    lossless_case("lossless-rgb8-pred1", [arr[..., i] for i in range(3)], 8, 1, 0)
    lossless_case("lossless-rgb8-pred4", [arr[..., i] for i in range(3)], 8, 4, 0)
    lossless_case("lossless-rgb8-pred7-noninterleaved", [arr[..., i] for i in range(3)], 8, 7, 0, interleaved=False)
    lossless_case("lossless-gray8-pt2", [g16], 8, 6, 2)
    lossless_case("lossless-gray8-restart", [g16], 8, 4, 0, restart=96)
    lossless_case("lossless-gray8-restart-odd", [g16], 8, 5, 0, restart=100)
    lossless_case("lossless-gray12", [g16 * 16 + rng.integers(0, 16, g16.shape)], 12, 1, 0)
    lossless_case("lossless-gray16", [np.clip(g16 * 257 + rng.integers(-3, 4, g16.shape), 0, 65535)], 16, 4, 0)
    lossless_case("lossless-gray16-extreme", [np.where(rng.random(g16.shape) < 0.5, 0, 65535)], 16, 1, 0)
    lossless_case("lossless-gray4", [g16 >> 4], 4, 1, 0)
    og = np.asarray(odd.convert("L")).astype(int)
    lossless_case("lossless-odd", [og], 8, 7, 0)
    lossless_case("lossless-odd-rgb", [np.asarray(odd).astype(int)[..., i] for i in range(3)], 8, 3, 0)


if __name__ == "__main__":
    main()
