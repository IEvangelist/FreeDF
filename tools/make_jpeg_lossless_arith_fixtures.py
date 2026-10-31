"""Lossless JPEG fixtures that no mainstream decoder can verify: SOF11 (arithmetic lossless, T.81 H.1.2.3) and
subsampled interleaved lossless scans (T.81 A.2.3 / H.1). Hand-derived from the specification: the encoders below are
independent Python implementations (the QM-coder is the Annex D port shared with make_jpeg_ext_fixtures.py, which is
cross-checked against libjpeg-turbo for SOF9/SOF10); expectations are the source samples. libjpeg-turbo 3.2.0
(imagecodecs) is attempted as an independent decoder for each file and its outcome is printed, never required.
Writes tests/fixtures/jpegx/<name>.{pdf,bin,meta} consumed by JpegExtendedTests. Run: python tools/make_jpeg_lossless_arith_fixtures.py
"""
import io
import os
import sys
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import make_jpeg_ext_fixtures as ext  # noqa: E402
from make_jpeg_ext_fixtures import Enc, Bits, seg, write  # noqa: E402
from make_jpx_fixtures import picture  # noqa: E402


def enc_diff(e, st, ca, cb, lo, hi, d):
    """Figures H.2/H.3 binary decision tree. Returns the difference category (0 zero, 1 +S, 2 -S, 3 +L, 4 -L)."""
    s0 = 4 * (5 * ca + cb)
    x1 = 129 if cb >= 3 else 100
    if d == 0:
        e.encode(st, s0, 0)
        return 0
    e.encode(st, s0, 1)
    sign = 1 if d < 0 else 0
    e.encode(st, s0 + 1, sign)
    s = s0 + 2 + sign
    v = abs(d) - 1
    m = 0
    if v == 0:
        e.encode(st, s, 0)
    else:
        e.encode(st, s, 1)
        m = 1
        s = x1
        t = v
        while True:
            t >>= 1
            if not t:
                break
            e.encode(st, s, 1)
            m <<= 1
            s += 1
        e.encode(st, s, 0)
    cat = 0 if m < ((1 << lo) >> 1) else (3 + sign if m > ((1 << hi) >> 1) else 1 + sign)
    s += 14
    mm = m
    while True:
        mm >>= 1
        if not mm:
            break
        e.encode(st, s, 1 if v & mm else 0)
    return cat


PRED = {1: lambda a, b, c: a, 2: lambda a, b, c: b, 3: lambda a, b, c: c, 4: lambda a, b, c: a + b - c,
        5: lambda a, b, c: a + ((b - c) >> 1), 6: lambda a, b, c: b + ((a - c) >> 1), 7: lambda a, b, c: (a + b) >> 1}


def lossless(name, planes, prec, pred, pt, samp, arith, restart=0, interleaved=True, cond=((0, 1), (0, 1)), adobe=True):
    """planes[i] has the component's own size ceil(dim * sampling / max sampling); samples are prec-bit unsigned."""
    nc = len(planes)
    hmax = max(s[0] for s in samp)
    vmax = max(s[1] for s in samp)
    ch0, cw0 = planes[0].shape
    # full-resolution size is recovered from the first component at maximum sampling
    w = cw0 * hmax // samp[0][0] if samp[0][0] == hmax else None
    assert w is not None
    h = ch0 * vmax // samp[0][1]
    mcux = -(-w // hmax)
    mcuy = -(-h // vmax)
    initial = 1 << (prec - pt - 1)
    out = bytearray(b"\xff\xd8")
    if nc == 3 and adobe:
        out += seg(0xEE, b"Adobe" + bytes([0, 100, 0, 0, 0, 0, 0]))
    out += seg(0xCB if arith else 0xC3, bytes([prec]) + h.to_bytes(2, "big") + w.to_bytes(2, "big") + bytes([nc])
               + b"".join(bytes([i + 1, samp[i][0] << 4 | samp[i][1], 0]) for i in range(nc)))
    if arith:
        if cond != ((0, 1), (0, 1)):
            out += seg(0xCC, b"".join(bytes([t, cond[t][0] | cond[t][1] << 4]) for t in range(2)))
    else:
        out += seg(0xC4, bytes([0]) + bytes([0, 0, 0, 0, 17] + [0] * 11) + bytes(range(17)))
    if restart:
        out += seg(0xDD, restart.to_bytes(2, "big"))
    src = [(p >> pt).astype(int) for p in planes]
    groups = [list(range(nc))] if interleaved or nc == 1 else [[i] for i in range(nc)]
    for g in groups:
        single = len(g) == 1
        td = {ci: (min(ci, 1) if arith else 0) for ci in g}
        out += seg(0xDA, bytes([len(g)]) + b"".join(bytes([i + 1, td[i] << 4]) for i in g) + bytes([pred, 0, pt]))
        if single:
            ci = g[0]
            arrs = {ci: src[ci].tolist()}
            gw, gh = src[ci].shape[1], src[ci].shape[0]
            units_x, units_y = gw, gh
        else:
            arrs = {}
            for ci in g:
                ph, pw = mcuy * samp[ci][1], mcux * samp[ci][0]
                arrs[ci] = np.pad(src[ci], ((0, ph - src[ci].shape[0]), (0, pw - src[ci].shape[1])), mode="edge").tolist()
            units_x, units_y = mcux, mcuy
        e = Enc() if arith else None
        bw = None if arith else Bits()
        stats = {ci: bytearray(158) for ci in g}
        cats = {ci: [[0] * len(arrs[ci][0]) for _ in arrs[ci]] for ci in g}
        first = {ci: True for ci in g}
        start_row = {ci: 0 for ci in g}
        rst = 0

        def sample(ci, x, y):
            p = arrs[ci]
            if first[ci]:
                pr = initial
                first[ci] = False
            elif y == start_row[ci]:
                pr = p[y][x - 1]
            elif x == 0:
                pr = p[y - 1][x]
            else:
                pr = PRED[pred](p[y][x - 1], p[y - 1][x], p[y - 1][x - 1])
            d = (p[y][x] - pr) & 0xFFFF
            if d >= 32768:
                d -= 65536
            if arith:
                ca = 0 if x == 0 else cats[ci][y][x - 1]
                cb = 0 if y == start_row[ci] else cats[ci][y - 1][x]
                cats[ci][y][x] = enc_diff(e, stats[ci], ca, cb, cond[td[ci]][0], cond[td[ci]][1], d)
            elif d == -32768:
                bw.put(16, 5)
            else:
                cat = abs(d).bit_length()
                bw.put(cat, 5)
                if cat:
                    bw.put(d if d > 0 else d + (1 << cat) - 1, cat)

        n = 0
        for r in range(units_y):
            for col in range(units_x):
                if restart and n > 0 and n % restart == 0:
                    if arith:
                        e.finish()
                        e.out.extend([0xFF, 0xD0 + rst])
                        e.start()
                        for s in stats.values():
                            s[:] = bytes(len(s))
                    else:
                        bw.flush()
                        bw.out.extend([0xFF, 0xD0 + rst])
                    rst = (rst + 1) & 7
                    for ci in g:
                        first[ci] = True
                        start_row[ci] = r if single else r * samp[ci][1]
                n += 1
                if single:
                    sample(g[0], col, r)
                else:
                    for ci in g:
                        for v in range(samp[ci][1]):
                            for hh in range(samp[ci][0]):
                                sample(ci, col * samp[ci][0] + hh, r * samp[ci][1] + v)
        if arith:
            e.finish()
            out += e.out
        else:
            bw.flush()
            out += bw.out
    out += b"\xff\xd9"
    # expected 8-bit pixels: decoder replicates subsampled samples, then reduces precision to 8 bits
    full = []
    for ci in range(nc):
        ys = (np.arange(h) * samp[ci][1]) // vmax
        xs = (np.arange(w) * samp[ci][0]) // hmax
        pl = (src[ci] << pt) if True else None
        full.append(pl[np.ix_(ys, xs)])
    ref = np.stack(full, -1) if nc > 1 else full[0]
    if prec > 8:
        ref = ref >> (prec - 8)
    elif prec < 8:
        mx = (1 << prec) - 1
        ref = (ref * 255 + (mx >> 1)) // mx
    data = bytes(out)
    try:
        import imagecodecs
        dec = imagecodecs.jpeg8_decode(data) if prec == 8 else None
        status = "n/a" if dec is None else ("match" if np.array_equal(np.squeeze(dec), ref) else "DIFFERS")
    except Exception as ex:  # libjpeg-turbo lacks SOF11 and subsampled lossless
        status = "rejected: " + str(ex).splitlines()[0][:60]
    print("  libjpeg-turbo:", status)
    write(name, w, h, data, ref.astype(np.uint8), 0)


def down(a, sx, sy):
    """Component plane for sampling factor ratio (nearest sample, matching the decoder's replication)."""
    return a[::sy, ::sx][: -(-a.shape[0] // sy), : -(-a.shape[1] // sx)]


def main():
    rgb = picture(96, 72)
    arr = np.asarray(rgb).astype(int)
    g = np.asarray(rgb.convert("L")).astype(int)
    rng = np.random.default_rng(11)
    odd = np.asarray(picture(37, 53, 7)).astype(int)
    one = [(1, 1)]
    three = [(1, 1)] * 3
    for p in range(1, 8):
        lossless(f"lossless-arith-gray8-pred{p}", [g], 8, p, 0, one, True)
    lossless("lossless-arith-rgb8-pred4", [arr[..., i] for i in range(3)], 8, 4, 0, three, True)
    lossless("lossless-arith-rgb8-noninterleaved", [arr[..., i] for i in range(3)], 8, 7, 0, three, True, interleaved=False)
    lossless("lossless-arith-gray8-pt2", [g], 8, 6, 2, one, True)
    lossless("lossless-arith-gray8-restart", [g], 8, 4, 0, one, True, restart=96)
    lossless("lossless-arith-rgb8-restart", [arr[..., i] for i in range(3)], 8, 5, 0, three, True, restart=96 * 2)
    lossless("lossless-arith-gray12", [g * 16 + rng.integers(0, 16, g.shape)], 12, 1, 0, one, True)
    lossless("lossless-arith-gray16", [np.clip(g * 257 + rng.integers(-3, 4, g.shape), 0, 65535)], 16, 4, 0, one, True)
    lossless("lossless-arith-gray16-extreme", [np.where(rng.random(g.shape) < 0.5, 0, 65535)], 16, 1, 0, one, True)
    lossless("lossless-arith-gray16-wrap", [np.where(np.indices(g.shape).sum(0) % 2 == 0, 0, 65535)], 16, 3, 0, one, True)
    lossless("lossless-arith-gray2", [g >> 6], 2, 1, 0, one, True)
    lossless("lossless-arith-odd", [odd[..., 0]], 8, 7, 0, one, True)
    lossless("lossless-arith-conditioning", [arr[..., i] for i in range(3)], 8, 4, 0, three, True, cond=((1, 3), (2, 6)))
    lossless("lossless-arith-conditioning-hi", [g], 8, 7, 0, one, True, cond=((3, 8), (0, 1)))

    # Subsampled interleaved scans (Huffman SOF3 and arithmetic SOF11), 4:2:0, 4:2:2, 4:4:0 and odd sizes.
    for tag, arith in (("huff", False), ("arith", True)):
        for sname, samp in (("420", [(2, 2), (1, 1), (1, 1)]), ("422", [(2, 1), (1, 1), (1, 1)]), ("440", [(1, 2), (1, 1), (1, 1)]),
                            ("mixed", [(2, 2), (2, 1), (1, 2)])):
            hmax = max(s[0] for s in samp)
            vmax = max(s[1] for s in samp)
            for dims, label in ((arr, "even"), (odd, "odd")):
                hh, ww = dims.shape[:2]
                planes = []
                for i, (sh, sv) in enumerate(samp):
                    ys = (np.arange(-(-hh * sv // vmax)) * vmax) // sv
                    xs = (np.arange(-(-ww * sh // hmax)) * hmax) // sh
                    planes.append(dims[..., i][np.ix_(np.minimum(ys, hh - 1), np.minimum(xs, ww - 1))])
                lossless(f"lossless-sub-{tag}-{sname}-{label}", planes, 8, 4, 0, samp, arith)
    cs = [(2, 2), (1, 1), (1, 1)]
    planes = [arr[..., 0], arr[::2, ::2, 1], arr[::2, ::2, 2]]
    lossless("lossless-sub-arith-420-restart", planes, 8, 5, 0, cs, True, restart=48)
    lossless("lossless-sub-huff-420-restart", planes, 8, 5, 0, cs, False, restart=48)
    lossless("lossless-sub-huff-420-noninterleaved", planes, 8, 1, 0, cs, False, interleaved=False)
    lossless("lossless-sub-arith-420-noninterleaved", planes, 8, 1, 0, cs, True, interleaved=False)


if __name__ == "__main__":
    main()
