"""Generates the huffman-refagg-*.pdf / .txt JBIG2 fixtures (T.88 6.5.8.2, Huffman symbol dictionary with refinement/aggregation).

Everything here is an independent encoder written from the spec: a Huffman bit writer, the Annex B standard tables
(transcribed from jbig2dec's jbig2_hufftab.c, not from this repository's decoder), and an MQ arithmetic encoder
(T.800 Annex C / T.88 Annex E) for the refinement bitmaps. The expected page bitmap (.txt, '1' = white) is built directly
from the target symbol bitmaps below, i.e. it is constructed, not produced by the C# decoder. The fixtures were additionally cross-checked against pdfium (pypdfium2 5.14): both render identically to the constructed bitmap.
MuPDF/jbig2dec cannot confirm them: jbig2dec never repositions its arithmetic decoder for Huffman refinement data (and pdf.js
reports "symbol refinement with Huffman is not supported"). pdfium (pypdfium2 5.14) does decode them, and the huffman-oddwidth-* fixtures
(symbol widths 5/6/7/8/13, i.e. not multiples of 8, and RDW = 1 / RDH = -1 refinements) render identically to the constructed bitmaps there.
The huffman-oddwidth-raw / -mmr (non-refinement, byte-padded collective bitmap rows and MMR collective bitmaps) and huffman-custom-tables
(segment type 53 with lower/upper/OOB lines in every table slot) fixtures are additionally confirmed by pdfium, MuPDF-or-pdf.js where noted in tools/jbig2_crosscheck.py output.

Usage: python make_huffman_refagg.py [output-directory]
"""
import struct
import sys
from pathlib import Path

# ---- Annex B standard tables: (prefix length, range length, range low); then lower, upper and OOB prefix lengths.
TABLES = {
    1: ([(1, 4, 0), (2, 8, 16), (3, 16, 272)], None, (3, 65808), None),
    2: ([(1, 0, 0), (2, 0, 1), (3, 0, 2), (4, 3, 3), (5, 6, 11)], None, (6, 75), 6),
    4: ([(1, 0, 1), (2, 0, 2), (3, 0, 3), (4, 3, 4), (5, 6, 12)], None, (5, 76), None),
    6: ([(5, 10, -2048), (4, 9, -1024), (4, 8, -512), (4, 7, -256), (5, 6, -128), (5, 5, -64), (4, 5, -32), (2, 7, 0), (3, 7, 128),
         (3, 8, 256), (4, 9, 512), (4, 10, 1024)], (6, -2049), (6, 2048), None),
    8: ([(8, 3, -15), (9, 1, -7), (8, 1, -5), (9, 0, -3), (7, 0, -2), (4, 0, -1), (2, 1, 0), (5, 0, 2), (6, 0, 3), (3, 4, 4), (6, 1, 20),
         (4, 4, 22), (4, 5, 38), (5, 6, 70), (5, 7, 134), (6, 7, 262), (7, 8, 390), (6, 10, 646)], (9, -16), (9, 1670), 2),
    11: ([(1, 0, 1), (2, 1, 2), (4, 0, 4), (4, 1, 5), (5, 1, 7), (5, 2, 9), (6, 2, 13), (7, 2, 17), (7, 3, 21), (7, 4, 29), (7, 5, 45),
          (7, 6, 77)], None, (7, 141), None),
    15: ([(7, 4, -24), (6, 2, -8), (5, 1, -4), (4, 0, -2), (3, 0, -1), (1, 0, 0), (3, 0, 1), (4, 0, 2), (5, 1, 3), (6, 2, 5), (7, 4, 9)],
          (7, -25), (7, 25), None),
}


class BitWriter:
    def __init__(self):
        self.bits = []

    def put(self, value, n):
        for i in range(n - 1, -1, -1):
            self.bits.append((value >> i) & 1)

    def align(self):
        while len(self.bits) % 8:
            self.bits.append(0)

    def bytes_(self, data):
        assert len(self.bits) % 8 == 0
        for b in data:
            self.put(b, 8)

    def to_bytes(self):
        self.align()
        return bytes(sum(self.bits[i + j] << (7 - j) for j in range(8)) for i in range(0, len(self.bits), 8))


class HuffTable:
    def __init__(self, n):
        normal, lower, upper, oob = TABLES[n] if isinstance(n, int) else n
        lines = [(p, r, lo, 'n') for p, r, lo in normal]
        if lower: lines.append((lower[0], 32, lower[1], 'l'))
        lines.append((upper[0], 32, upper[1], 'u'))
        if oob is not None: lines.append((oob, 0, 0, 'o'))
        self.lines = lines
        # B.3 canonical code assignment
        self.codes = {}
        maxlen = max(l[0] for l in lines)
        count = [0] * (maxlen + 2)
        for l in lines:
            count[l[0]] += 1
        count[0] = 0
        first = 0
        for ln in range(1, maxlen + 1):
            first = (first + count[ln - 1]) << 1
            code = first
            for i, l in enumerate(lines):
                if l[0] == ln:
                    self.codes[i] = (code, ln)
                    code += 1

    def put(self, bw, value):
        for i, (p, r, lo, kind) in enumerate(self.lines):
            if kind == 'n' and lo <= value < lo + (1 << r):
                code, ln = self.codes[i]
                bw.put(code, ln)
                bw.put(value - lo, r)
                return
        for i, (p, r, lo, kind) in enumerate(self.lines):
            if (kind == 'l' and value <= lo) or (kind == 'u' and value >= lo):
                code, ln = self.codes[i]
                bw.put(code, ln)
                bw.put(lo - value if kind == 'l' else value - lo, 32)
                return
        raise ValueError(f"value {value} not codable")

    def put_oob(self, bw):
        for i, l in enumerate(self.lines):
            if l[3] == 'o':
                bw.put(*self.codes[i])
                return
        raise ValueError("no OOB")


# ---- MQ encoder (T.800 Annex C)
QE = [0x5601, 0x3401, 0x1801, 0x0AC1, 0x0521, 0x0221, 0x5601, 0x5401, 0x4801, 0x3801, 0x3001, 0x2401, 0x1C01, 0x1601, 0x5601, 0x5401,
      0x5101, 0x4801, 0x3801, 0x3401, 0x3001, 0x2801, 0x2401, 0x2201, 0x1C01, 0x1801, 0x1601, 0x1401, 0x1201, 0x1101, 0x0AC1, 0x09C1,
      0x08A1, 0x0521, 0x0441, 0x02A1, 0x0221, 0x0141, 0x0111, 0x0085, 0x0049, 0x0025, 0x0015, 0x0009, 0x0005, 0x0001, 0x5601]
NMPS = [1, 2, 3, 4, 5, 38, 7, 8, 9, 10, 11, 12, 13, 29, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35,
        36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 45, 46]
NLPS = [1, 6, 9, 12, 29, 33, 6, 14, 14, 14, 17, 18, 20, 21, 14, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30,
        31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 46]
SWITCH = {0, 6, 14}


class MqEncoder:
    def __init__(self):
        self.a, self.c, self.ct = 0x8000, 0, 12
        self.out = bytearray([0])  # out[-1] is the byte at BP; the leading dummy is dropped at the end

    def encode(self, cx, i, d):
        idx, mps = cx[i]
        qe = QE[idx]
        self.a -= qe
        if d == mps:
            if (self.a & 0x8000) == 0:
                if self.a < qe: self.a = qe
                else: self.c += qe
                cx[i] = (NMPS[idx], mps)
                self._renorm()
            else:
                self.c += qe
        else:
            if self.a < qe: self.c += qe
            else: self.a = qe
            if idx in SWITCH: mps = 1 - mps
            cx[i] = (NLPS[idx], mps)
            self._renorm()

    def _renorm(self):
        while True:
            self.a = (self.a << 1) & 0xFFFF
            self.c <<= 1
            self.ct -= 1
            if self.ct == 0: self._byteout()
            if self.a & 0x8000: break

    def _byteout(self):
        if self.out[-1] == 0xFF:
            self.out.append((self.c >> 20) & 0xFF); self.c &= 0xFFFFF; self.ct = 7
        elif self.c < 0x8000000:
            self.out.append((self.c >> 19) & 0xFF); self.c &= 0x7FFFF; self.ct = 8
        else:
            self.out[-1] += 1
            if self.out[-1] == 0xFF:
                self.c &= 0x7FFFFFF
                self.out.append((self.c >> 20) & 0xFF); self.c &= 0xFFFFF; self.ct = 7
            else:
                self.out.append((self.c >> 19) & 0xFF); self.c &= 0x7FFFF; self.ct = 8

    def flush(self):
        tempc = self.c + self.a
        self.c |= 0xFFFF
        if self.c >= tempc: self.c -= 0x8000
        self.c <<= self.ct
        self._byteout()
        self.c <<= self.ct
        self._byteout()
        data = bytes(self.out[1:])
        if data.endswith(b'\xff'): data = data[:-1]
        return data + b'\xff\xac'


def px(rows, x, y):
    return 1 if 0 <= y < len(rows) and 0 <= x < len(rows[0]) and rows[y][x] == '#' else 0


def encode_refinement(enc, cx, target, ref, dx, dy, at=((-1, -1), (-1, -1))):
    """Generic refinement region, template 0, TPGRON = 0 (6.3). `ref` pixel for target (x, y) is ref(x - dx, y - dy)."""
    for y in range(len(target)):
        for x in range(len(target[0])):
            rx, ry = x - dx, y - dy
            c = (px(target, x - 1, y) | px(target, x + 1, y - 1) << 1 | px(target, x, y - 1) << 2 | px(target, x + at[0][0], y + at[0][1]) << 3
                 | px(ref, rx + 1, ry + 1) << 4 | px(ref, rx, ry + 1) << 5 | px(ref, rx - 1, ry + 1) << 6
                 | px(ref, rx + 1, ry) << 7 | px(ref, rx, ry) << 8 | px(ref, rx - 1, ry) << 9
                 | px(ref, rx + 1, ry - 1) << 10 | px(ref, rx, ry - 1) << 11 | px(ref, rx + at[1][0], ry + at[1][1]) << 12)
            enc.encode(cx, c, px(target, x, y))


def refinement_bytes(cx, target, ref, dx, dy):
    enc = MqEncoder()
    encode_refinement(enc, cx, target, ref, dx, dy)
    return enc.flush()


# ---- symbol bitmaps (rows of '#'/'.')
# Widths are multiples of 8 so no padding bits sit next to any symbol in a collective bitmap or decoder row buffer.
S0 = ["..####..", ".#....#.", "#......#", "#......#", "########", "#......#", "#......#", "#......#"]
S1 = ["#######.", "#......#", "#......#", "#######.", "#......#", "#......#", "#......#", "#######."]
N0 = ["..####..", ".#....#.", "#......#", "#..##..#", "########", "#......#", "#......#", "##....##"]  # refines S0
N1 = ["." + r + "." for r in N0]
N1[0] = "...####..."; N1[6] = ".#......##."                                                # refines N0, dx = 1
R = ['.' + r[:7] for r in N0]
R[0] = "...####."; R[5] = "#......#"; R[6] = ".#.....#"                                      # 8x8, refines N0 with dx = 1


def compose(canvas, sym, x0, y0):
    for y, row in enumerate(sym):
        for x, ch in enumerate(row):
            if ch == '#' and 0 <= y0 + y < len(canvas) and 0 <= x0 + x < len(canvas[0]):
                canvas[y0 + y][x0 + x] = '#'


def blank(w, h):
    return [['.'] * w for _ in range(h)]


def to_rows(canvas):
    return [''.join(r) for r in canvas]


N2c = blank(16, 12)
compose(N2c, S0, 0, 0)
compose(N2c, R, 8, 0)
N2 = to_rows(N2c)  # aggregate of S0 @ (0,0) and refined N0 @ (8,0)

# ---- segments
def segment(num, type_, page, refs, payload):
    assert num < 256 and len(refs) < 5
    return struct.pack('>IBB', num, type_, len(refs) << 5) + bytes(refs) + bytes([page]) + struct.pack('>I', len(payload)) + payload


def page_info(w, h):
    return struct.pack('>IIIIBH', w, h, 0, 0, 0, 0)


def region_info(w, h, x, y, op=0):
    return struct.pack('>IIIIB', w, h, x, y, op)


def dict0():
    """Huffman symbol dictionary, no refinement: S0 and S1 (one height class, uncompressed collective bitmap)."""
    bw = BitWriter()
    t4, t2, t1 = HuffTable(4), HuffTable(2), HuffTable(1)
    t4.put(bw, 8)          # height class delta
    t2.put(bw, 8)          # S0 width
    t2.put(bw, 0)          # S1 width = 8
    t2.put_oob(bw)
    t1.put(bw, 0)          # BMSIZE 0 = uncompressed
    bw.align()
    for a, b in zip(S0, S1):
        row = a + b
        v = 0
        for i in range(16):
            v = v << 1 | (1 if row[i] == '#' else 0)
        bw.put(v, 16)
    t1.put(bw, 0); t1.put(bw, 2)   # export flags: 0 not exported, then 2 exported
    return struct.pack('>HII', 0x0001, 2, 2) + bw.to_bytes()


def dict1(aggregate, code_len):
    """Huffman symbol dictionary with SDREFAGG=1 referring to dict0 (2 input symbols, id bits = ceil(log2(2 + new)))."""
    bw = BitWriter()
    gr = [(0, 0)] * 8192
    t4, t2, t1, t15, t6, t8, t11 = (HuffTable(n) for n in (4, 2, 1, 15, 6, 8, 11))

    def single(ref_id, ref, target, dx, dy):
        t1.put(bw, 1)                         # REFAGGNINST = 1
        bw.put(ref_id, code_len)
        t15.put(bw, dx); t15.put(bw, dy)
        data = refinement_bytes(gr, target, ref, dx, dy)
        t1.put(bw, len(data))
        bw.align()
        bw.bytes_(data)

    t4.put(bw, 8)                             # height class 8
    t2.put(bw, 8); single(0, S0, N0, 0, 0)    # N0 (id 2): refinement of S0 (id 0)
    new = 2
    if not aggregate:
        t2.put(bw, 2); single(2, N0, N1, 1, 0)  # N1 (id 3): refinement of N0 (id 2), shifted one pixel right
        t2.put_oob(bw)
    else:
        t2.put_oob(bw)
        t4.put(bw, 4)                         # height class 12
        t2.put(bw, 16)
        t1.put(bw, 2)                         # REFAGGNINST = 2: text region (Table 17), Huffman, SBSYMCODELEN id codes
        t11.put(bw, 1)                        # initial STRIPT: -1 (DT >= 1 in Table B.11)
        t11.put(bw, 1)                        # strip DT -> T = 0
        t6.put(bw, 0)                         # first S = 0
        bw.put(0, code_len); bw.put(0, 1)     # instance 1: id 0 (S0), RI = 0, S = 0
        t8.put(bw, 1)                         # IDS: next S = (0 + 7) + 1 = 8
        bw.put(2, code_len); bw.put(1, 1)     # instance 2: id 2 (N0), RI = 1
        t15.put(bw, 0); t15.put(bw, 0)        # RDW = 0, RDH = 0
        t15.put(bw, 1); t15.put(bw, 0)        # RDX = 1, RDY = 0  -> dx = 1, dy = 0
        data = refinement_bytes(gr, R, N0, 1, 0)
        t1.put(bw, len(data))
        bw.align()
        bw.bytes_(data)
        t8.put_oob(bw)                        # end of strip
        t2.put_oob(bw)                        # end of height class
    t1.put(bw, 0); t1.put(bw, 2 + new)        # export everything
    flags = 0x0003                            # SDHUFF=1, SDREFAGG=1, B.4/B.2/B.1/B.1, SDRTEMPLATE=0
    return struct.pack('>H', flags) + bytes([0xFF, 0xFF, 0xFF, 0xFF]) + struct.pack('>II', 2 + new, new) + bw.to_bytes()


def text_region(symbols, w, h, tables=None, hf=0):
    """Huffman text region (no refinement) placing each symbol at the given (x, y) top-left; ids use ceil(log2(n))-bit codes."""
    n = len(symbols)
    bw = BitWriter()
    # symbol ID Huffman table: run-code lengths (35 x 4 bits) with only code 3 assigned length 1, then n lengths of 3
    idlen = max(1, (n - 1).bit_length())
    for i in range(35):
        bw.put(1 if i == idlen else 0, 4)
    for _ in range(n):
        bw.put(0, 1)
    bw.align()
    t6, t8, t11 = tables or (HuffTable(6), HuffTable(8), HuffTable(11))
    t11.put(bw, 1)
    y = symbols[0][2]
    t11.put(bw, y + 1)
    t6.put(bw, symbols[0][1])
    cur = symbols[0][1]
    for k, (sid, x, _, width) in enumerate(symbols):
        if k:
            t8.put(bw, x - cur)
        bw.put(sid, idlen)
        cur = x + width - 1
    t8.put_oob(bw)
    body = bw.to_bytes()
    flags = 0x0011  # SBHUFF=1, REFCORNER=TOPLEFT
    return region_info(w, h, 0, 0) + struct.pack('>HHI', flags, hf, n) + body


def build(aggregate):
    code_len = 2
    syms = [(0, S0), (1, S1), (2, N0), (3, N2 if aggregate else N1)]
    pw, ph = 64, 20
    canvas = blank(pw, ph)
    placed, x = [], 2
    for sid, bmp in syms:
        placed.append((sid, x, 2, len(bmp[0])))
        compose(canvas, bmp, x, 2)
        x += len(bmp[0]) + 2
    assert x - 2 <= pw
    stream = (segment(0, 48, 1, [], page_info(pw, ph)) + segment(1, 0, 1, [], dict0()) + segment(2, 0, 1, [1], dict1(aggregate, code_len))
              + segment(3, 6, 1, [2], text_region(placed, pw, ph)) + segment(4, 49, 1, [], b''))
    return stream, to_rows(canvas), pw, ph


def make_pdf(stream, w, h):
    objs = [
        b"<< /Pages 2 0 R /Type /Catalog >>",
        b"<< /Count 1 /Kids [ 3 0 R ] /Type /Pages >>",
        f"<< /Contents 4 0 R /MediaBox [ 0 0 {w} {h} ] /Parent 2 0 R /Resources << /XObject << /Im0 5 0 R >> >> /Type /Page >>".encode(),
        None,
        None,
    ]
    content = f"q {w} 0 0 {h} 0 0 cm /Im0 Do Q".encode()
    objs[3] = f"<< /Length {len(content)} >>\nstream\n".encode() + content + b"\nendstream"
    objs[4] = (f"<< /BitsPerComponent 1 /ColorSpace /DeviceGray /Filter /JBIG2Decode /Height {h} /Subtype /Image /Type /XObject /Width {w} "
               f"/Length {len(stream)} >>\nstream\n").encode() + stream + b"\nendstream"
    out = bytearray(b"%PDF-1.3\n")
    offsets = []
    for i, o in enumerate(objs, 1):
        offsets.append(len(out))
        out += f"{i} 0 obj\n".encode() + o + b"\nendobj\n"
    xref = len(out)
    out += f"xref\n0 {len(objs) + 1}\n0000000000 65535 f \n".encode()
    for o in offsets:
        out += f"{o:010d} 00000 n \n".encode()
    out += f"trailer << /Root 1 0 R /Size {len(objs) + 1} >>\nstartxref\n{xref}\n%%EOF\n".encode()
    return bytes(out)


def layout(syms, start=2, y=2, pw=64, ph=20):
    canvas = blank(pw, ph)
    placed, x = [], start
    for sid, bmp in syms:
        placed.append((sid, x, y, len(bmp[0])))
        compose(canvas, bmp, x, y)
        x += len(bmp[0]) + 2
    return placed, canvas, pw, ph


# ---- symbol widths that are not multiples of 8, non-zero RDW/RDH, MMR collective bitmaps, custom Huffman tables (segment type 53)
A5 = [r[:5] for r in S0]
B7 = [r[:7] for r in S1]
N0o = [r[:6] for r in N0]
N1o = ['.' + r + '.' for r in N0o]
N1o[0] = "...###.."; N1o[6] = ".#.....#"
Rp = [N0o[y + 1] + '.' for y in range(7)]   # 7x7: N0o shifted up one row (RDW = 1, RDH = -1 -> dx = 0, dy = -1)
Rp[2] = "#..##.#"; Rp[5] = ".#....#"
N2oc = blank(13, 8)
compose(N2oc, A5, 0, 0)
compose(N2oc, Rp, 6, 0)
N2o = to_rows(N2oc)


def g4(rows):
    import io
    from PIL import Image
    w, h = len(rows[0]), len(rows)
    im = Image.new('1', (w, h), 0)
    im.putdata([1 if ch == '#' else 0 for r in rows for ch in r])
    buf = io.BytesIO(); im.save(buf, format='TIFF', compression='group4')
    t = Image.open(io.BytesIO(buf.getvalue()))
    off, cnt = t.tag_v2[273], t.tag_v2[279]
    off = off if isinstance(off, int) else off[0]; cnt = cnt if isinstance(cnt, int) else cnt[0]
    return buf.getvalue()[off:off + cnt]


def dict0_widths(a, b, mmr=False, flags=0x0001, tables=None):
    """Huffman symbol dictionary without refinement: a then b in one height class, any widths (rows of the collective bitmap are byte padded)."""
    bw = BitWriter()
    t4, t2, t1 = tables or (HuffTable(4), HuffTable(2), HuffTable(1))
    t4.put(bw, len(a))
    t2.put(bw, len(a[0])); t2.put(bw, len(b[0]) - len(a[0])); t2.put_oob(bw)
    rows = [x + y for x, y in zip(a, b)]
    if mmr:
        data = g4(rows)
        t1.put(bw, len(data)); bw.align(); bw.bytes_(data)
    else:
        t1.put(bw, 0); bw.align()
        for row in rows:
            for ch in row: bw.put(1 if ch == '#' else 0, 1)
            bw.align()
    HuffTable(1).put(bw, 0); HuffTable(1).put(bw, 2)
    return struct.pack('>HII', flags, 2, 2) + bw.to_bytes()


def dict1_odd(aggregate, code_len=2):
    """Huffman symbol dictionary with SDREFAGG=1 over dict0_widths(A5, B7): 6-wide refinement of the 5-wide A5 and an 8-wide/13-wide second symbol."""
    bw = BitWriter()
    gr = [(0, 0)] * 8192
    t4, t2, t1, t15, t6, t8, t11 = (HuffTable(n) for n in (4, 2, 1, 15, 6, 8, 11))

    def single(ref_id, ref, target, dx, dy):
        t1.put(bw, 1)
        bw.put(ref_id, code_len)
        t15.put(bw, dx); t15.put(bw, dy)
        data = refinement_bytes(gr, target, ref, dx, dy)
        t1.put(bw, len(data))
        bw.align()
        bw.bytes_(data)

    t4.put(bw, 8)
    t2.put(bw, 6); single(0, A5, N0o, 0, 0)       # N0o (id 2, 6x8) refines A5 (id 0, 5x8)
    if not aggregate:
        t2.put(bw, 2); single(2, N0o, N1o, 1, 0)  # N1o (id 3, 8x8) refines N0o with dx = 1
    else:
        t2.put(bw, 7)                             # N2o (id 3, 13x8)
        t1.put(bw, 2)
        t11.put(bw, 1); t11.put(bw, 1)
        t6.put(bw, 0)
        bw.put(0, code_len); bw.put(0, 1)         # instance 1: A5 at S = 0
        t8.put(bw, 2)                             # next S = (0 + 4) + 2 = 6
        bw.put(2, code_len); bw.put(1, 1)         # instance 2: refined N0o, RI = 1
        t15.put(bw, 1); t15.put(bw, -1)           # RDW = 1, RDH = -1 -> 7x7
        t15.put(bw, 0); t15.put(bw, 0)            # RDX = 0, RDY = 0 -> dx = floor(1/2) = 0, dy = floor(-1/2) = -1
        data = refinement_bytes(gr, Rp, N0o, 0, -1)
        t1.put(bw, len(data))
        bw.align()
        bw.bytes_(data)
        t8.put_oob(bw)
    t2.put_oob(bw)
    t1.put(bw, 0); t1.put(bw, 4)
    return struct.pack('>H', 0x0003) + bytes([0xFF, 0xFF, 0xFF, 0xFF]) + struct.pack('>II', 4, 2) + bw.to_bytes()


def table_payload(spec):
    normal, lower, upper, oob = spec
    ps = max([p for p, _, _ in normal] + [lower[0], upper[0]] + ([oob] if oob is not None else [])).bit_length()
    rs = max(max(r for _, r, _ in normal).bit_length(), 1)
    bw = BitWriter()
    for p, r, _ in normal:
        bw.put(p, ps); bw.put(r, rs)
    bw.put(lower[0], ps); bw.put(upper[0], ps)
    if oob is not None: bw.put(oob, ps)
    flags = (1 if oob is not None else 0) | (ps - 1) << 1 | (rs - 1) << 4
    return bytes([flags]) + struct.pack('>ii', normal[0][2], upper[1]) + bw.to_bytes()


# (normal lines (prefix length, range length, low), lower (prefix length, HTLOW - 1), upper (prefix length, HTHIGH), OOB prefix length)
CUSTOM_DH = ([(2, 1, -2), (2, 2, 0), (2, 1, 4)], (3, -3), (3, 6), None)
CUSTOM_DW = ([(2, 1, 0), (2, 2, 2), (3, 1, 6)], (3, -1), (3, 8), 3)
CUSTOM_BM = ([(1, 0, 0)], (2, -1), (2, 1), None)
CUSTOM_FS = ([(2, 2, 0)], (2, -1), (2, 4), None)
CUSTOM_DS = ([(1, 1, 0)], (3, -1), (2, 2), 3)
CUSTOM_DT = ([(1, 1, 1)], (2, 0), (2, 3), None)


def build_custom():
    tdh, tdw, tbm, tfs, tds, tdt = (HuffTable(s) for s in (CUSTOM_DH, CUSTOM_DW, CUSTOM_BM, CUSTOM_FS, CUSTOM_DS, CUSTOM_DT))
    dict_ = dict0_widths(S0, S1, flags=0x007D, tables=(tdh, tdw, tbm))
    syms = [(0, S0), (1, S1)]
    placed, canvas, pw, ph = layout(syms, start=-6)
    stream = (segment(1, 53, 1, [], table_payload(CUSTOM_DH)) + segment(2, 53, 1, [], table_payload(CUSTOM_DW)) +
              segment(3, 53, 1, [], table_payload(CUSTOM_BM)) + segment(4, 53, 1, [], table_payload(CUSTOM_FS)) +
              segment(5, 53, 1, [], table_payload(CUSTOM_DS)) + segment(6, 53, 1, [], table_payload(CUSTOM_DT)) +
              segment(7, 48, 1, [], page_info(pw, ph)) + segment(8, 0, 1, [1, 2, 3], dict_) +
              segment(9, 6, 1, [8, 4, 5, 6], text_region(placed, pw, ph, (tfs, tds, tdt), 0x3F)) + segment(10, 49, 1, [], b''))
    return stream, to_rows(canvas), pw, ph


def build_widths(a, b, mmr=False):
    placed, canvas, pw, ph = layout([(0, a), (1, b)])
    stream = (segment(0, 48, 1, [], page_info(pw, ph)) + segment(1, 0, 1, [], dict0_widths(a, b, mmr)) +
              segment(2, 6, 1, [1], text_region(placed, pw, ph)) + segment(3, 49, 1, [], b''))
    return stream, to_rows(canvas), pw, ph


def build_odd(aggregate):
    syms = [(0, A5), (1, B7), (2, N0o), (3, N2o if aggregate else N1o)]
    placed, canvas, pw, ph = layout(syms)
    stream = (segment(0, 48, 1, [], page_info(pw, ph)) + segment(1, 0, 1, [], dict0_widths(A5, B7)) + segment(2, 0, 1, [1], dict1_odd(aggregate)) +
              segment(3, 6, 1, [2], text_region(placed, pw, ph)) + segment(4, 49, 1, [], b''))
    return stream, to_rows(canvas), pw, ph


if __name__ == '__main__':
    outdir = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).parent
    jobs = (('huffman-refagg-single', lambda: build(False)), ('huffman-refagg-aggregate', lambda: build(True)),
            ('huffman-oddwidth-refagg-single', lambda: build_odd(False)), ('huffman-oddwidth-refagg-aggregate-rdwrdh', lambda: build_odd(True)),
            ('huffman-oddwidth-raw', lambda: build_widths(A5, B7)), ('huffman-oddwidth-mmr', lambda: build_widths(A5, B7, True)),
            ('huffman-custom-tables', build_custom))
    for name, job in jobs:
        stream, rows, w, h = job()
        (outdir / f'{name}.pdf').write_bytes(make_pdf(stream, w, h))
        (outdir / f'{name}.txt').write_text(''.join(('0' if ch == '#' else '1') for r in rows for ch in r), newline='')
        print(name, len(stream), 'bytes')
