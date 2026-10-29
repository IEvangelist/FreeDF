"""Minimal JBIG2 encoder used only to build test fixtures (T.88): MQ coder, generic regions (templates 0-3, TPGD, custom AT),
generic refinement regions, and arithmetic symbol dictionaries / text regions."""
import struct

QE = [0x5601, 0x3401, 0x1801, 0x0AC1, 0x0521, 0x0221, 0x5601, 0x5401, 0x4801, 0x3801, 0x3001, 0x2401, 0x1C01, 0x1601, 0x5601, 0x5401,
      0x5101, 0x4801, 0x3801, 0x3401, 0x3001, 0x2801, 0x2401, 0x2201, 0x1C01, 0x1801, 0x1601, 0x1401, 0x1201, 0x1101, 0x0AC1, 0x09C1,
      0x08A1, 0x0521, 0x0441, 0x02A1, 0x0221, 0x0141, 0x0111, 0x0085, 0x0049, 0x0025, 0x0015, 0x0009, 0x0005, 0x0001, 0x5601]
NMPS = [1, 2, 3, 4, 5, 38, 7, 8, 9, 10, 11, 12, 13, 29, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 45, 46]
NLPS = [1, 6, 9, 12, 29, 33, 6, 14, 14, 14, 17, 18, 20, 21, 14, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 46]
SW = {0, 6, 14}


class Cx:
    def __init__(self, n): self.i = [0] * n; self.m = [0] * n


class MQEnc:
    def __init__(self):
        self.a = 0x8000; self.c = 0; self.ct = 12; self.out = bytearray([0])

    def _byteout(self):
        o = self.out
        if o[-1] == 0xFF:
            o.append((self.c >> 20) & 0xFF); self.c &= 0xFFFFF; self.ct = 7
        elif self.c < 0x8000000:
            o.append((self.c >> 19) & 0xFF); self.c &= 0x7FFFF; self.ct = 8
        else:
            o[-1] += 1
            if o[-1] == 0xFF:
                self.c &= 0x7FFFFFF
                o.append((self.c >> 20) & 0xFF); self.c &= 0xFFFFF; self.ct = 7
            else:
                o.append((self.c >> 19) & 0xFF); self.c &= 0x7FFFF; self.ct = 8

    def _renorm(self):
        while True:
            self.a <<= 1; self.c <<= 1; self.ct -= 1
            if self.ct == 0: self._byteout()
            if self.a & 0x8000: break

    def encode(self, d, cx, k):
        i = cx.i[k]; qe = QE[i]
        if d == cx.m[k]:
            self.a -= qe
            if not self.a & 0x8000:
                if self.a < qe: self.a = qe
                else: self.c += qe
                cx.i[k] = NMPS[i]; self._renorm()
            else:
                self.c += qe
        else:
            self.a -= qe
            if self.a < qe: self.c += qe
            else: self.a = qe
            if i in SW: cx.m[k] = 1 - cx.m[k]
            cx.i[k] = NLPS[i]; self._renorm()

    def flush(self):
        t = self.c + self.a
        self.c |= 0xFFFF
        if self.c >= t: self.c -= 0x8000
        self.c <<= self.ct; self._byteout()
        self.c <<= self.ct; self._byteout()
        if self.out[-1] != 0xFF: self.out.append(0xFF)
        self.out.append(0xAC)
        return bytes(self.out[1:])


def px(b, x, y):
    return b[y][x] if 0 <= y < len(b) and 0 <= x < len(b[0]) else 0


def generic_ctx(b, x, y, t, at):
    g = lambda dx, dy: px(b, x + dx, y + dy)
    if t == 0:
        return (g(-1, 0) | g(-2, 0) << 1 | g(-3, 0) << 2 | g(-4, 0) << 3 | g(*at[0]) << 4 | g(2, -1) << 5 | g(1, -1) << 6 | g(0, -1) << 7 |
                g(-1, -1) << 8 | g(-2, -1) << 9 | g(*at[1]) << 10 | g(*at[2]) << 11 | g(1, -2) << 12 | g(0, -2) << 13 | g(-1, -2) << 14 | g(*at[3]) << 15)
    if t == 1:
        return (g(-1, 0) | g(-2, 0) << 1 | g(-3, 0) << 2 | g(*at[0]) << 3 | g(2, -1) << 4 | g(1, -1) << 5 | g(0, -1) << 6 | g(-1, -1) << 7 |
                g(-2, -1) << 8 | g(2, -2) << 9 | g(1, -2) << 10 | g(0, -2) << 11 | g(-1, -2) << 12)
    if t == 2:
        return (g(-1, 0) | g(-2, 0) << 1 | g(*at[0]) << 2 | g(1, -1) << 3 | g(0, -1) << 4 | g(-1, -1) << 5 | g(-2, -1) << 6 |
                g(1, -2) << 7 | g(0, -2) << 8 | g(-1, -2) << 9)
    return (g(-1, 0) | g(-2, 0) << 1 | g(-3, 0) << 2 | g(-4, 0) << 3 | g(*at[0]) << 4 | g(1, -1) << 5 | g(0, -1) << 6 | g(-1, -1) << 7 |
            g(-2, -1) << 8 | g(-3, -1) << 9)


NOMINAL_AT = {0: [(3, -1), (-3, -1), (2, -2), (-2, -2)], 1: [(3, -1)], 2: [(2, -1)], 3: [(2, -1)]}
SLTP = {0: 0x9B25, 1: 0x0795, 2: 0x00E5, 3: 0x0195}


def encode_generic(enc, cx, b, t, tpgd=False, at=None):
    at = at or NOMINAL_AT[t]
    ltp = 0
    h = len(b); w = len(b[0])
    for y in range(h):
        if tpgd:
            same = int(b[y] == (b[y - 1] if y else [0] * w))
            enc.encode(same ^ ltp, cx, SLTP[t]); ltp = same
            if same: continue
        for x in range(w):
            enc.encode(b[y][x], cx, generic_ctx(b, x, y, t, at))


def refine_ctx(b, rf, x, y, dx, dy, t, at):
    g = lambda xx, yy: px(b, xx, yy)
    r = lambda xx, yy: px(rf, xx - dx, yy - dy)
    if t == 0:
        return (g(x - 1, y) | g(x + 1, y - 1) << 1 | g(x, y - 1) << 2 | g(x + at[0][0], y + at[0][1]) << 3 |
                r(x + 1, y + 1) << 4 | r(x, y + 1) << 5 | r(x - 1, y + 1) << 6 | r(x + 1, y) << 7 | r(x, y) << 8 | r(x - 1, y) << 9 |
                r(x + 1, y - 1) << 10 | r(x, y - 1) << 11 | r(x + at[1][0], y + at[1][1]) << 12)
    return (g(x - 1, y) | g(x + 1, y - 1) << 1 | g(x, y - 1) << 2 | g(x - 1, y - 1) << 3 | r(x + 1, y + 1) << 4 | r(x, y + 1) << 5 |
            r(x + 1, y) << 6 | r(x, y) << 7 | r(x - 1, y) << 8 | r(x, y - 1) << 9)


def encode_refine(enc, cx, b, rf, dx, dy, t, tpgr=False, at=((-1, -1), (-1, -1))):
    h = len(b); w = len(b[0]); ltp = 0
    sltp = 0x100 if t == 0 else 0x80
    for y in range(h):
        typical = False
        if tpgr:
            # row is typical if every pixel whose 3x3 reference neighbourhood is uniform equals that value
            typical = True
            for x in range(w):
                v = px(rf, x - dx, y - dy)
                if all(px(rf, x - dx + i, y - dy + j) == v for i in (-1, 0, 1) for j in (-1, 0, 1)) and b[y][x] != v:
                    typical = False; break
            enc.encode(int(typical) ^ ltp, cx, sltp); ltp = int(typical)
        for x in range(w):
            if tpgr and ltp:
                v = px(rf, x - dx, y - dy)
                if all(px(rf, x - dx + i, y - dy + j) == v for i in (-1, 0, 1) for j in (-1, 0, 1)): continue
            enc.encode(b[y][x], cx, refine_ctx(b, rf, x, y, dx, dy, t, at))


class IntEnc:
    def __init__(self): self.cx = Cx(512)

    def encode(self, enc, v):  # v is None for OOB
        prev = [1]

        def bit(d):
            enc.encode(d, self.cx, prev[0])
            p = prev[0]
            prev[0] = (p << 1 | d) if p < 256 else (((p << 1 | d) & 511) | 256)
        if v is None: s, mag = 1, 0
        else: s, mag = (1 if v < 0 else 0), abs(v)
        ranges = [(2, 0), (4, 4), (6, 20), (8, 84), (12, 340), (32, 4436)]
        bit(s)
        for k, (n, off) in enumerate(ranges):
            nxt = ranges[k + 1][1] if k + 1 < len(ranges) else None
            if nxt is None or mag < nxt:
                for _ in range(k): bit(1)
                if k < 5: bit(0)
                val = mag - off
                for i in range(n - 1, -1, -1): bit(val >> i & 1)
                return


class IdEnc:
    def __init__(self, n): self.cx = Cx(1 << (n + 1)); self.n = n

    def encode(self, enc, v):
        prev = 1
        for i in range(self.n - 1, -1, -1):
            d = v >> i & 1
            enc.encode(d, self.cx, prev); prev = prev << 1 | d


def seg(num, t, page, data, refs=()):
    sz = 1 if num <= 256 else 2
    h = struct.pack(">IB", num, t) + bytes([len(refs) << 5]) + b"".join(r.to_bytes(sz, "big") for r in refs) + bytes([page]) + struct.pack(">I", len(data))
    return h + data


def page_info(w, h, default=0):
    return struct.pack(">IIIIBH", w, h, 0, 0, default << 2, 0)


def region_info(w, h, x=0, y=0, op=0):
    return struct.pack(">IIIIB", w, h, x, y, op)


def pack_at(at):
    return bytes(v & 0xFF for p in at for v in p)


def generic_segment(num, b, t, tpgd=False, at=None, page=1, op=0, tp=38):
    at = at or NOMINAL_AT[t]
    enc = MQEnc(); encode_generic(enc, Cx(65536), b, t, tpgd, at)
    data = region_info(len(b[0]), len(b), 0, 0, op) + bytes([t << 1 | (8 if tpgd else 0)]) + pack_at(at) + enc.flush()
    return seg(num, tp, page, data)


def refine_segment(num, b, rf_page_aligned, t, tpgr, at=((-1, -1), (-1, -1)), page=1, op=4):
    enc = MQEnc(); encode_refine(enc, Cx(8192), b, rf_page_aligned, 0, 0, t, tpgr, at)
    data = region_info(len(b[0]), len(b), 0, 0, op) + bytes([t | (2 if tpgr else 0)]) + (pack_at(at) if t == 0 else b"") + enc.flush()
    return seg(num, 42, page, data)


def symbol_dict_segment(num, syms, tmpl=0, page=1):
    """syms: list of bitmaps sorted by height (then any width order); all exported."""
    enc = MQEnc(); gb = Cx(65536)
    iadh, iadw, iaex = IntEnc(), IntEnc(), IntEnc()
    h = 0; i = 0
    while i < len(syms):
        hh = len(syms[i]); iadh.encode(enc, hh - h); h = hh; w = 0
        while i < len(syms) and len(syms[i]) == hh:
            sw = len(syms[i][0]); iadw.encode(enc, sw - w); w = sw
            encode_generic(enc, gb, syms[i], tmpl, False, NOMINAL_AT[tmpl]); i += 1
        iadw.encode(enc, None)
    iaex.encode(enc, 0); iaex.encode(enc, len(syms))
    flags = tmpl << 10
    data = struct.pack(">H", flags) + pack_at(NOMINAL_AT[tmpl]) + struct.pack(">II", len(syms), len(syms)) + enc.flush()
    return seg(num, 0, page, data)


def text_segment(num, w, h, syms, placements, refs, refcorner=1, transposed=False, strips_log=0, combop=0, page=1, op=0):
    """placements: list of (symbol index, x, y) of the reference corner position, sorted by strip then x. Uses S along x unless transposed."""
    n = len(syms); codelen = max(0, (n - 1).bit_length())
    enc = MQEnc()
    iadt, iafs, iads, iait = IntEnc(), IntEnc(), IntEnc(), IntEnc()
    iaid = IdEnc(codelen)
    strips = 1 << strips_log
    inst = []
    for (sid, x, y) in placements:
        sw, sh = len(syms[sid][0]), len(syms[sid])
        cxr = x + (sw - 1 if refcorner in (2, 3) else 0)
        cyr = y + (sh - 1 if refcorner in (0, 2) else 0)
        s, t = (cyr, cxr) if transposed else (cxr, cyr)
        inst.append((t, s, sid))
    inst.sort(key=lambda a: (a[0] // strips, a[1]))
    iadt.encode(enc, 0)  # initial STRIPT = 0
    stript = 0; firsts = 0; i = 0
    while i < len(inst):
        st = inst[i][0] // strips * strips
        iadt.encode(enc, (st - stript) // strips); stript = st
        group = [a for a in inst[i:] if a[0] // strips * strips == st]
        curs = None
        for k, (t, s, sid) in enumerate(group):
            sw, sh = len(syms[sid][0]), len(syms[sid])
            # CURS before placement adjustment, derived from desired reference-corner S coordinate
            if not transposed: adj_in = (sw - 1) if refcorner in (2, 3) else 0
            else: adj_in = (sh - 1) if refcorner in (0, 2) else 0
            want = s - adj_in  # position from which `curs += adj_in` yields s... decoder: curs += adj; si = curs
            # decoder: CURS(before) + adj_in = SI  => CURS(before) = s - adj_in
            if k == 0: iafs.encode(enc, want - firsts); firsts = want
            else: iads.encode(enc, want - curs)
            if strips > 1: iait.encode(enc, t - st)
            iaid.encode(enc, sid)
            if not transposed: adj_out = (sw - 1) if refcorner in (0, 1) else 0
            else: adj_out = (sh - 1) if refcorner in (1, 3) else 0
            curs = s + adj_out
        iads.encode(enc, None)
        i += len(group)
    flags = strips_log << 2 | refcorner << 4 | (0x40 if transposed else 0) | combop << 7
    data = region_info(w, h, 0, 0, op) + struct.pack(">H", flags) + struct.pack(">I", len(inst)) + enc.flush()
    return seg(num, 6, page, data, refs)
