"""Deterministic 12-bit Huffman DCT fixtures, without an image encoder dependency.

The encoded coefficients are the source of truth. Reference samples use the
separable floating-point inverse DCT from T.81 A.3.3, independently of the
decoder's integer butterfly. Every fixture is checked against imagecodecs'
12-bit libjpeg-turbo decoder, with fancy upsampling disabled, before writing.
Raw reference samples are retained in JSON, before the decoder API's >>4 mapping.
Coverage is SOF1 sequential and SOF2 progressive, including refinement, restart
markers, separate scans, 4:2:0 and Gray/RGB/YCbCr/CMYK/YCCK. SOF11, hierarchical
JPEG and subsampled interleaved lossless remain outside this fixture set.
Requires imagecodecs. Run: python tools/make_jpeg12_fixtures.py
"""
import base64
import json
import math
from pathlib import Path

import imagecodecs


ROOT = Path(__file__).resolve().parents[1]
ZIG = [0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
       12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
       35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
       58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63]
DC = list(range(16))
AC = [0, 0xF0] + [r * 16 + s for r in range(16) for s in range(1, 15)]


def segment(marker, data):
    return bytes([255, marker]) + (len(data) + 2).to_bytes(2, "big") + bytes(data)


class Bits:
    def __init__(self):
        self.bits = ""

    def put(self, value, size):
        self.bits += format(value & ((1 << size) - 1), f"0{size}b") if size else ""

    def symbol(self, value, dc=False):
        self.put((DC if dc else AC).index(value), 5 if dc else 8)

    def amplitude(self, value):
        size = abs(value).bit_length()
        self.put(value if value >= 0 else value + (1 << size) - 1, size)
        return size

    def finish(self):
        self.bits += "1" * (-len(self.bits) % 8)
        data = bytes(int(self.bits[i:i + 8], 2) for i in range(0, len(self.bits), 8))
        return data.replace(b"\xff", b"\xff\0")


def dc_value(bits, difference):
    bits.symbol(abs(difference).bit_length(), True)
    bits.amplitude(difference)


def ac_first(bits, block, al):
    run = 0
    for k in range(1, 64):
        v = block[ZIG[k]]
        v = (abs(v) >> al) * (-1 if v < 0 else 1)
        if not v:
            run += 1
            continue
        while run >= 16:
            bits.symbol(0xF0)
            run -= 16
        bits.symbol(run * 16 + abs(v).bit_length())
        bits.amplitude(v)
        run = 0
    if run:
        bits.symbol(0)


def ac_refine(bits, block):
    run, corrections = 0, []
    for k in range(1, 64):
        v = block[ZIG[k]]
        if abs(v) >= 2:
            corrections.append(abs(v) & 1)
        elif not v:
            run += 1
        else:
            if run >= 16:
                # The fixtures keep new coefficients within the first 16
                # positions, so no correction-bit partitioning is needed.
                raise ValueError("new refinement coefficient after a long run")
            bits.symbol(run * 16 + 1)
            bits.put(int(v > 0), 1)
            for bit in corrections:
                bits.put(bit, 1)
            run, corrections = 0, []
    bits.symbol(0)
    for bit in corrections:
        bits.put(bit, 1)


def encode(w, h, ids, sampling, blocks, quant, progressive, restart, separate, adobe):
    hm = max(s[0] for s in sampling)
    vm = max(s[1] for s in sampling)
    mx, my = (w + hm * 8 - 1) // (hm * 8), (h + vm * 8 - 1) // (vm * 8)
    bw = [mx * s[0] for s in sampling]
    data = b"\xff\xd8"
    if adobe is not None:
        data += segment(0xEE, b"Adobe\0\x64\0\0\0\0" + bytes([adobe]))
    wide = max(quant) > 255
    data += segment(0xDB, bytes([16 if wide else 0]) +
                    b"".join(quant[k].to_bytes(2 if wide else 1, "big") for k in ZIG))
    frame = bytes([12]) + h.to_bytes(2, "big") + w.to_bytes(2, "big") + bytes([len(ids)])
    for cid, (hs, vs) in zip(ids, sampling):
        frame += bytes([cid, hs * 16 + vs, 0])
    data += segment(0xC2 if progressive else 0xC1, frame)
    data += segment(0xC4, bytes([0] + [0] * 4 + [16] + [0] * 11 + DC) +
                    bytes([16] + [0] * 7 + [len(AC)] + [0] * 8 + AC))
    if restart:
        data += segment(0xDD, restart.to_bytes(2, "big"))

    def scan(indices, ss, se, ah, al):
        nonlocal data
        header = bytes([len(indices)]) + b"".join(bytes([ids[i], 0]) for i in indices)
        data += segment(0xDA, header + bytes([ss, se, ah * 16 + al]))
        if len(indices) == 1:
            i = indices[0]
            rw = ((w * sampling[i][0] + hm - 1) // hm + 7) // 8
            rh = ((h * sampling[i][1] + vm - 1) // vm + 7) // 8
            units = [[(i, y * bw[i] + x)] for y in range(rh) for x in range(rw)]
        else:
            units = [[(i, (y * sampling[i][1] + v) * bw[i] + x * sampling[i][0] + u)
                      for i in indices for v in range(sampling[i][1]) for u in range(sampling[i][0])]
                     for y in range(my) for x in range(mx)]
        bits, pred, rst = Bits(), [0] * len(ids), 0
        for mcu, unit in enumerate(units):
            if restart and mcu and mcu % restart == 0:
                data += bits.finish() + bytes([255, 0xD0 + rst % 8])
                bits, pred, rst = Bits(), [0] * len(ids), rst + 1
            for i, index in unit:
                block = blocks[i][index]
                if ss == 0:
                    if ah:
                        bits.put(block[0] & 1, 1)
                    else:
                        dc = block[0] >> al
                        dc_value(bits, dc - pred[i])
                        pred[i] = dc
                if se:
                    if ah:
                        ac_refine(bits, block)
                    else:
                        ac_first(bits, block, al)
        data += bits.finish()

    groups = [[i] for i in range(len(ids))] if separate else [list(range(len(ids)))]
    if progressive:
        for group in groups:
            scan(group, 0, 0, 0, 1)
        for i in range(len(ids)):
            scan([i], 1, 63, 0, 1)
        for group in groups:
            scan(group, 0, 0, 1, 0)
        for i in range(len(ids)):
            scan([i], 1, 63, 1, 0)
    else:
        for group in groups:
            scan(group, 0, 63, 0, 0)
    return data + b"\xff\xd9"


def encode_arith(w, h, ids, sampling, blocks, quant, progressive, restart):
    """SOF9/SOF10 12-bit stream: coefficients are entropy coded with the T.81 Annex D/F/G QM-coder port in
    make_jpeg_ext_fixtures.py (the probability table is read from JpegArith.cs, so a table typo would also
    be caught by the independent libjpeg-turbo decode in check_reference)."""
    import make_jpeg_ext_fixtures as ext
    hm = max(s[0] for s in sampling)
    vm = max(s[1] for s in sampling)
    mx, my = (w + hm * 8 - 1) // (hm * 8), (h + vm * 8 - 1) // (vm * 8)
    comps = []
    for i, (hs, vs) in enumerate(sampling):
        bw, bh = mx * hs, my * vs
        comps.append(dict(id=ids[i], H=hs, V=vs, tq=0, td=0,
                          blocks=[[blocks[i][y * bw + x] for x in range(bw)] for y in range(bh)],
                          rbw=((w * hs + hm - 1) // hm + 7) // 8, rbh=((h * vs + vm - 1) // vm + 7) // 8))
    wide = max(quant) > 255
    data = b"\xff\xd8" + segment(0xDB, bytes([16 if wide else 0]) +
                                 b"".join(quant[k].to_bytes(2 if wide else 1, "big") for k in ZIG))
    frame = bytes([12]) + h.to_bytes(2, "big") + w.to_bytes(2, "big") + bytes([len(ids)])
    for cid, (hs, vs) in zip(ids, sampling):
        frame += bytes([cid, hs * 16 + vs, 0])
    data += segment(0xCA if progressive else 0xC9, frame)
    if restart:
        data += segment(0xDD, restart.to_bytes(2, "big"))
    cond = dict(dcl=[0] * 4, dcu=[1] * 4, ack=[5] * 4)
    for sc in ext.prog_scans(len(ids)) if progressive else ext.seq_scans(len(ids)):
        sc = dict(sc, prog=progressive)
        header = bytes([len(sc["ids"])]) + b"".join(bytes([ids[ci], 0]) for ci in sc["ids"])
        data += segment(0xDA, header + bytes([sc["ss"], sc["se"], sc["ah"] * 16 + sc["al"]]))
        data += ext.encode_scan(comps, sc, hm, vm, mx, my, restart, cond)
    return data + b"\xff\xd9"


def clamp(v):
    return max(0, min(4095, v))


def reference(w, h, ids, sampling, blocks, quant, adobe):
    basis = [[(1 / math.sqrt(2) if k == 0 else 1) *
              math.cos((2 * x + 1) * k * math.pi / 16) / 2 for k in range(8)] for x in range(8)]
    hm, vm = max(s[0] for s in sampling), max(s[1] for s in sampling)
    mx = (w + hm * 8 - 1) // (hm * 8)
    planes = []
    for i, (hs, _) in enumerate(sampling):
        plane = []
        for block in blocks[i]:
            plane.append([clamp(math.floor(2048 + sum(
                basis[x][u] * basis[y][v] * block[v * 8 + u] * quant[v * 8 + u]
                for v in range(8) for u in range(8)) + 0.5)) for y in range(8) for x in range(8)])
        planes.append(plane)
    expected = []
    ycc = len(ids) == 3 and adobe != 0 and ids != [82, 71, 66] or len(ids) == 4 and adobe == 2
    for y in range(h):
        for x in range(w):
            values = []
            for i, (hs, vs) in enumerate(sampling):
                sx, sy = x * hs // hm, y * vs // vm
                values.append(planes[i][sy // 8 * mx * hs + sx // 8][sy % 8 * 8 + sx % 8])
            if ycc:
                yy, cb, cr = values[0], values[1] - 2048, values[2] - 2048
                # T.81/JFIF color conversion, independently expressed as real coefficients.
                rgb = [clamp(math.floor(yy + 1.402 * cr + 0.5)),
                       clamp(math.floor(yy - 0.344136 * cb - 0.714136 * cr + 0.5)),
                       clamp(math.floor(yy + 1.772 * cb + 0.5))]
                values = [4095 - v for v in rgb] + values[3:] if len(ids) == 4 else rgb
            expected.extend(values)
    return expected


def fixtures():
    cases = [
        ("gray-extremes", 33, 9, [1], [(1, 1)], False, 0, False, None, False),
        ("gray-restart", 33, 9, [1], [(1, 1)], False, 2, False, None, False),
        ("gray-progressive", 33, 9, [1], [(1, 1)], True, 2, False, None, False),
        ("gray-wide-quant", 13, 9, [1], [(1, 1)], False, 0, False, None, True),
        ("rgb-interleaved", 17, 9, [82, 71, 66], [(1, 1)] * 3, False, 2, False, None, False),
        ("rgb-separate", 17, 9, [82, 71, 66], [(1, 1)] * 3, False, 2, True, 0, False),
        ("ycc-420", 17, 9, [1, 2, 3], [(2, 2), (1, 1), (1, 1)], False, 1, False, None, False),
        ("ycc-420-progressive", 17, 9, [1, 2, 3], [(2, 2), (1, 1), (1, 1)], True, 1, False, None, False),
        ("cmyk", 9, 9, [67, 77, 89, 75], [(1, 1)] * 4, False, 1, False, 0, False),
        ("ycck-progressive", 9, 9, [1, 2, 3, 4], [(1, 1)] * 4, True, 1, True, 2, False),
        ("arith-gray-extremes", 33, 9, [1], [(1, 1)], False, 0, False, None, False, True),
        ("arith-gray-restart", 33, 9, [1], [(1, 1)], False, 2, False, None, False, True),
        ("arith-gray-progressive", 33, 9, [1], [(1, 1)], True, 2, False, None, False, True),
        ("arith-gray-wide-quant", 13, 9, [1], [(1, 1)], False, 0, False, None, True, True),
        ("arith-rgb-interleaved", 17, 9, [82, 71, 66], [(1, 1)] * 3, False, 2, False, None, False, True),
        ("arith-ycc-420", 17, 9, [1, 2, 3], [(2, 2), (1, 1), (1, 1)], False, 1, False, None, False, True),
        ("arith-ycc-420-progressive", 17, 9, [1, 2, 3], [(2, 2), (1, 1), (1, 1)], True, 1, False, None, False, True),
    ]
    for name, w, h, ids, sampling, prog, restart, separate, adobe, wide, *arith in cases:
        hm, vm = max(s[0] for s in sampling), max(s[1] for s in sampling)
        mx, my = (w + hm * 8 - 1) // (hm * 8), (h + vm * 8 - 1) // (vm * 8)
        quant = [1] * 64
        if wide:
            quant[1], quant[8], quant[63] = 257, 513, 65535
        blocks = []
        for i, (hs, vs) in enumerate(sampling):
            component = []
            for j in range(mx * my * hs * vs):
                block = [0] * 64
                if len(ids) == 1 and not wide:
                    block[0] = [-16384, 16376, -16376, 8, -8, 0, 8191, -8191, 1, -1][j % 10]
                    if j >= 5:
                        block[1], block[8], block[16], block[63] = 8193, -1025, 1, -2
                else:
                    block[0] = [-8001, 4097, 9001, -2047][(i + j) % 4]
                    block[1], block[8], block[16], block[63] = (3, -3, 1, 0) if wide else (513, -257, 1, -3)
                component.append(block)
            blocks.append(component)
        jpeg = (encode_arith(w, h, ids, sampling, blocks, quant, prog, restart) if arith
                else encode(w, h, ids, sampling, blocks, quant, prog, restart, separate, adobe))
        yield dict(name=name, width=w, height=h, components=len(ids),
                   jpeg=base64.b64encode(jpeg).decode("ascii"),
                   samples=reference(w, h, ids, sampling, blocks, quant, adobe))


def check_reference(case):
    decoded = imagecodecs.jpeg_decode(base64.b64decode(case["jpeg"]), fancyupsampling=False)
    samples = decoded.reshape(-1).tolist()
    assert str(decoded.dtype) == "uint16", decoded.dtype
    assert len(samples) == len(case["samples"])
    error = max(abs(a - b) for a, b in zip(samples, case["samples"]))
    assert error <= 3, (case["name"], error)
    case["reference"] = f"T.81 A.3.3 float IDCT; checked against {imagecodecs.jpeg_version()}"
    case["referenceMaxError"] = error
    print(f'{case["name"]}: libjpeg-turbo native sample max error {error}')


def main():
    cases = list(fixtures())
    for case in cases:
        check_reference(case)
    dst = ROOT / "tests" / "fixtures" / "jpeg12"
    dst.mkdir(parents=True, exist_ok=True)
    for case in cases:
        (dst / (case["name"] + ".json")).write_text(json.dumps(case, separators=(",", ":")) + "\n", encoding="ascii")


if __name__ == "__main__":
    main()
