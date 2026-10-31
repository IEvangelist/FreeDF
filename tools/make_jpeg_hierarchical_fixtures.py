"""Hierarchical JPEG fixtures (ITU-T T.81 Annex J): DHP, EXP, non-differential frames (SOF1/3/9/11) and differential frames
(SOF5/7/13/15) with bilinear resolution doubling. No mainstream decoder implements hierarchical JPEG, so every expectation
is hand-derived: this script is an independent spec-based encoder whose reference reconstruction (numpy float IDCT, J.1.1.2
upsampling) never touches the C# decoder; the final lossless differential frames make most files exactly reproduce the source.
Writes tests/fixtures/jpegx/hier-*.{pdf,bin,meta}. Run: python tools/make_jpeg_hierarchical_fixtures.py
"""
import os
import sys
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from make_jpeg_ext_fixtures import Enc, Bits, seg, write, enc_dc, enc_ac_first, ZIG, C  # noqa: E402
from make_jpx_fixtures import picture  # noqa: E402
from make_jpeg_lossless_arith_fixtures import enc_diff, PRED  # noqa: E402


def signed16(x):
    x = x & 0xFFFF
    return np.where(x >= 32768, x - 65536, x)


def upsample(a, eh, ev):
    """J.1.1.2: out[2i] = in[i], out[2i+1] = (in[i] + in[i+1]) / 2 (truncating), last sample replicated; horizontal first."""
    a = a.astype(np.int64)
    if eh:
        w = a.shape[1]
        o = np.empty((a.shape[0], 2 * w), dtype=np.int64)
        o[:, 0::2] = a
        o[:, 1::2] = (a + a[:, np.minimum(np.arange(w) + 1, w - 1)]) >> 1
        a = o
    if ev:
        h = a.shape[0]
        o = np.empty((2 * h, a.shape[1]), dtype=np.int64)
        o[0::2] = a
        o[1::2] = (a + a[np.minimum(np.arange(h) + 1, h - 1)]) >> 1
        a = o
    return a


def lossless_frame(vals, prec, pred, pt, arith, diff, ids, restart=0):
    """vals: int arrays that are coded as-is (source >> pt, or difference >> pt for differential frames)."""
    nc = len(vals)
    h, w = vals[0].shape
    marker = {(False, False): 0xC3, (False, True): 0xCB, (True, False): 0xC7, (True, True): 0xCF}[(diff, arith)]
    out = seg(marker, bytes([prec]) + h.to_bytes(2, "big") + w.to_bytes(2, "big") + bytes([nc])
              + b"".join(bytes([ids[i], 0x11, 0]) for i in range(nc)))
    out += seg(0xDA, bytes([nc]) + b"".join(bytes([ids[i], 0]) for i in range(nc)) + bytes([0 if diff else pred, 0, pt]))
    initial = 1 << (prec - pt - 1)
    p = [v.tolist() for v in vals]
    e = Enc() if arith else None
    bw = None if arith else Bits()
    stats = [bytearray(158) for _ in range(nc)]
    cats = [[[0] * w for _ in range(h)] for _ in range(nc)]
    first = [True] * nc
    start_row = 0
    rst = 0
    for i in range(w * h):
        y, x = divmod(i, w)
        if restart and i > 0 and i % restart == 0:
            if arith:
                e.finish()
                e.out.extend([0xFF, 0xD0 + rst])
                e.start()
                for s in stats:
                    s[:] = bytes(len(s))
            else:
                bw.flush()
                bw.out.extend([0xFF, 0xD0 + rst])
            rst = (rst + 1) & 7
            first = [True] * nc
            start_row = y
        for ci in range(nc):
            a = p[ci]
            if diff:
                pr = 0
            elif first[ci]:
                pr = initial
            elif y == start_row:
                pr = a[y][x - 1]
            elif x == 0:
                pr = a[y - 1][x]
            else:
                pr = PRED[pred](a[y][x - 1], a[y - 1][x], a[y - 1][x - 1])
            first[ci] = False
            d = signed16(np.int64(a[y][x] - pr))
            d = int(d)
            if arith:
                ca = 0 if x == 0 else cats[ci][y][x - 1]
                cb = 0 if y == start_row else cats[ci][y - 1][x]
                cats[ci][y][x] = enc_diff(e, stats[ci], ca, cb, 0, 1, d)
            elif d == -32768:
                bw.put(16, 5)
            else:
                cat = abs(d).bit_length()
                bw.put(cat, 5)
                if cat:
                    bw.put(d if d > 0 else d + (1 << cat) - 1, cat)
    if arith:
        e.finish()
        return out + bytes(e.out)
    bw.flush()
    return out + bytes(bw.out)


AC_SYMS = [0x00, 0xF0] + [r * 16 + s for r in range(16) for s in range(1, 16)]
AC_IDX = {s: i for i, s in enumerate(AC_SYMS)}
DHT = (seg(0xC4, bytes([0x00]) + bytes([0, 0, 0, 0, 16] + [0] * 11) + bytes(range(16)))
       + seg(0xC4, bytes([0x10]) + bytes([0] * 7 + [len(AC_SYMS)] + [0] * 8) + bytes(AC_SYMS)))
LL_DHT = seg(0xC4, bytes([0]) + bytes([0, 0, 0, 0, 17] + [0] * 11) + bytes(range(17)))


def huff_block(bw, blk, last, diff):
    dcv = blk[0] if diff else blk[0] - last
    cat = abs(dcv).bit_length()
    bw.put(cat, 5)
    if cat:
        bw.put(dcv if dcv > 0 else dcv + (1 << cat) - 1, cat)
    run = 0
    for k in range(1, 64):
        v = blk[ZIG[k]]
        if v == 0:
            run += 1
            continue
        while run > 15:
            bw.put(AC_IDX[0xF0], 8)
            run -= 16
        s = abs(v).bit_length()
        assert s <= 15
        bw.put(AC_IDX[run * 16 + s], 8)
        bw.put(v if v > 0 else v + (1 << s) - 1, s)
        run = 0
    if run:
        bw.put(AC_IDX[0], 8)
    return blk[0]


def dct_frame(planes, prec, diff, arith, qv, ids, tq):
    """Returns (bytes, reconstructed planes before adding any reference). planes hold samples or differences."""
    nc = len(planes)
    h, w = planes[0].shape
    marker = {(False, False): 0xC1, (False, True): 0xC9, (True, False): 0xC5, (True, True): 0xCD}[(diff, arith)]
    q = np.full((8, 8), qv)
    out = seg(0xDB, bytes([tq]) + bytes(int(q.reshape(64)[ZIG[k]]) for k in range(64)))
    out += seg(marker, bytes([prec]) + h.to_bytes(2, "big") + w.to_bytes(2, "big") + bytes([nc])
               + b"".join(bytes([ids[i], 0x11, tq]) for i in range(nc)))
    out += seg(0xDA, bytes([nc]) + b"".join(bytes([ids[i], 0]) for i in range(nc)) + bytes([0, 63, 0]))
    bh, bw_ = -(-h // 8), -(-w // 8)
    shift = 0 if diff else 1 << (prec - 1)
    coefs, recon = [], []
    for pl in planes:
        pad = np.pad(pl.astype(float), ((0, bh * 8 - h), (0, bw_ * 8 - w)), mode="edge") - shift
        blocks = pad.reshape(bh, 8, bw_, 8).transpose(0, 2, 1, 3)
        qc = np.rint((C @ blocks @ C.T) / q).astype(int)
        coefs.append(qc.reshape(bh, bw_, 64))
        px = C.T @ (qc * q) @ C
        full = np.floor(px.transpose(0, 2, 1, 3).reshape(bh * 8, bw_ * 8)[:h, :w] + 0.5).astype(np.int64)
        recon.append(full if diff else np.clip(full + shift, 0, (1 << prec) - 1))
    if arith:
        e = Enc()
        dcs, acs, fixed = bytearray(64), bytearray(256), bytearray([113, 0, 0, 0])
        states = [{"last": 0, "ctx": 0} for _ in range(nc)]
        for by in range(bh):
            for bx in range(bw_):
                for ci in range(nc):
                    blk = coefs[ci][by][bx].tolist()
                    states[ci]["last"] = 0 if diff else states[ci]["last"]
                    enc_dc(e, dcs, 0, 1, blk, states[ci], 0)
                    enc_ac_first(e, acs, 5, fixed, blk, 1, 63, 0)
        e.finish()
        return out + bytes(e.out), recon
    bw = Bits()
    last = [0] * nc
    for by in range(bh):
        for bx in range(bw_):
            for ci in range(nc):
                last[ci] = huff_block(bw, coefs[ci][by][bx].tolist(), last[ci], diff)
    bw.flush()
    return out + bytes(bw.out), recon


def header(prec, h, w, ids):
    return bytes([prec]) + h.to_bytes(2, "big") + w.to_bytes(2, "big") + bytes([len(ids)]) + b"".join(bytes([i, 0x11, 0]) for i in ids)


def levels_for(src, steps):
    """steps: [(eh, ev), ...] from coarsest to finest. Returns arrays per level (coarsest first), each a list per component."""
    lv = [src]
    for eh, ev in reversed(steps):
        lv.insert(0, [a[:: 2 if ev else 1, :: 2 if eh else 1] for a in lv[0]])
    return lv


def reduce8(a, prec):
    mx = (1 << prec) - 1
    return (a >> (prec - 8)) if prec > 8 else (a * 255 + (mx >> 1)) // mx if prec < 8 else a


def emit(name, out, recon, prec, tol=0):
    ref = np.stack(recon, -1) if len(recon) > 1 else recon[0]
    w = ref.shape[1]
    h = ref.shape[0]
    write(name, w, h, bytes(out), reduce8(ref, prec).astype(np.uint8), tol)


def hier_lossless(name, src, prec, steps, arith, pred=1, pt_first=0, pt_diff=0, restart=0, adobe=True):
    lv = levels_for(src, steps)
    nc = len(src)
    ids = list(range(1, nc + 1))
    full_h, full_w = src[0].shape
    out = bytearray(b"\xff\xd8")
    if nc == 3 and adobe:
        out += seg(0xEE, b"Adobe" + bytes([0, 100, 0, 0, 0, 0, 0]))
    if arith is False:
        out += LL_DHT
    if restart:
        out += seg(0xDD, restart.to_bytes(2, "big"))
    out += seg(0xDE, header(prec, full_h, full_w, ids))
    mask = (1 << prec) - 1
    out += lossless_frame([(a >> pt_first) for a in lv[0]], prec, pred, pt_first, arith, False, ids, restart)
    recon = [((a >> pt_first) << pt_first) & mask for a in lv[0]]
    for k in range(1, len(lv)):
        eh, ev = steps[k - 1]
        refs = [upsample(r, eh, ev)[: lv[k][i].shape[0], : lv[k][i].shape[1]] for i, r in enumerate(recon)]
        if eh or ev:
            out += seg(0xDF, bytes([eh << 4 | ev]))
        vals = [signed16(lv[k][i] - refs[i]) >> pt_diff for i in range(nc)]
        out += lossless_frame(vals, prec, 0, pt_diff, arith, True, ids, restart)
        recon = [(refs[i] + (vals[i] << pt_diff)) & 0xFFFF for i in range(nc)]
    out += b"\xff\xd9"
    emit(name, out, [r & mask for r in recon], prec)


def hier_dct(name, src, prec, steps, arith, q0=8, q1=4, final_lossless=True, tol=0, share_table=True):
    """Gray/colour 1x1 components: DCT frame at the coarsest level, differential DCT frames up the pyramid, optional lossless fix-up."""
    lv = levels_for(src, steps)
    nc = len(src)
    ids = list(range(1, nc + 1))
    full_h, full_w = src[0].shape
    out = bytearray(b"\xff\xd8")
    if nc == 3:
        out += seg(0xEE, b"Adobe" + bytes([0, 100, 0, 0, 0, 0, 0]))
    out += seg(0xDE, header(prec, full_h, full_w, ids))
    if not arith:
        out += DHT
    mask = (1 << prec) - 1
    fb, recon = dct_frame(lv[0], prec, False, arith, q0, ids, 0)
    out += fb
    for k in range(1, len(lv)):
        eh, ev = steps[k - 1]
        refs = [upsample(r, eh, ev)[: lv[k][i].shape[0], : lv[k][i].shape[1]] for i, r in enumerate(recon)]
        if eh or ev:
            out += seg(0xDF, bytes([eh << 4 | ev]))
        diffs = [lv[k][i] - refs[i] for i in range(nc)]
        fb, drec = dct_frame(diffs, prec, True, arith, q1, ids, 0 if share_table else k)
        out += fb
        recon = [np.clip(refs[i] + drec[i], 0, mask) for i in range(nc)]
    if final_lossless:
        if arith is False:
            out += LL_DHT
        vals = [signed16(lv[-1][i] - recon[i]) for i in range(nc)]
        out += lossless_frame(vals, prec, 0, 0, arith, True, ids)
        recon = [lv[-1][i] for i in range(nc)]
    out += b"\xff\xd9"
    emit(name, out, recon, prec, tol)


def main():
    rgb = np.asarray(picture(96, 72)).astype(np.int64)
    odd = np.asarray(picture(37, 53, 7)).astype(np.int64)
    g = [rgb[..., 0] * 0 + (rgb[..., 0] * 299 + rgb[..., 1] * 587 + rgb[..., 2] * 114) // 1000]
    go = [(odd[..., 0] * 299 + odd[..., 1] * 587 + odd[..., 2] * 114) // 1000]
    rng = np.random.default_rng(5)
    both, hor, ver = (1, 1), (1, 0), (0, 1)
    for arith, tag in ((False, "huff"), (True, "arith")):
        hier_lossless(f"hier-lossless-{tag}-2lvl", g, 8, [both], arith)
        hier_lossless(f"hier-lossless-{tag}-3lvl-odd", go, 8, [both, both], arith, pred=4)
        hier_lossless(f"hier-lossless-{tag}-horizontal", g, 8, [hor], arith)
        hier_lossless(f"hier-lossless-{tag}-vertical", g, 8, [ver], arith)
        hier_lossless(f"hier-lossless-{tag}-mixed-steps", go, 8, [hor, ver, both], arith, pred=7)
        hier_lossless(f"hier-lossless-{tag}-rgb", [rgb[..., i] for i in range(3)], 8, [both], arith, pred=2)
        hier_lossless(f"hier-lossless-{tag}-pt2", g, 8, [both], arith, pt_diff=2)
        hier_lossless(f"hier-lossless-{tag}-refine-same-size", g, 8, [(0, 0)], arith, pt_first=3)
        hier_lossless(f"hier-lossless-{tag}-restart", g, 8, [both], arith, restart=96)
        g16 = [np.clip(g[0] * 257 + rng.integers(-5, 6, g[0].shape), 0, 65535)]
        hier_lossless(f"hier-lossless-{tag}-16bit", g16, 16, [both], arith)
        wrap = [np.where(np.indices(g[0].shape).sum(0) % 3 == 0, 0, 65535)]
        hier_lossless(f"hier-lossless-{tag}-16bit-wrap", wrap, 16, [both], arith, pred=3)
        hier_lossless(f"hier-lossless-{tag}-12bit", [g[0] * 16 + rng.integers(0, 16, g[0].shape)], 12, [both], arith)
        hier_dct(f"hier-dct-{tag}-2lvl-lossless", g, 8, [both], arith)
        hier_dct(f"hier-dct-{tag}-3lvl-odd-lossless", go, 8, [both, both], arith)
        hier_dct(f"hier-dct-{tag}-rgb-lossless", [rgb[..., i] for i in range(3)], 8, [both], arith)
        hier_dct(f"hier-dct-{tag}-horizontal-lossless", g, 8, [hor], arith)
        hier_dct(f"hier-dct-{tag}-2lvl-only", g, 8, [both], arith, final_lossless=False, tol=1)
        hier_dct(f"hier-dct-{tag}-new-table-per-frame", g, 8, [both], arith, share_table=False)
        hier_dct(f"hier-dct-{tag}-12bit-lossless", [g[0] * 16 + rng.integers(0, 16, g[0].shape)], 12, [both], arith, q0=64, q1=32)


if __name__ == "__main__":
    main()
