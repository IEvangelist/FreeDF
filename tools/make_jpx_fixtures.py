"""Generates JPXDecode test fixtures with Pillow (OpenJPEG) under tests/fixtures/jpx.

Each case writes name.pdf (one /Im0 image XObject using JPXDecode), name.bin (the reference pixels
decoded by OpenJPEG through Pillow, 8-bit interleaved) and name.meta ("MODE width height tolerance").
"""
import io
import os
import numpy as np
from pikepdf import Pdf, Stream, Name, Dictionary
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "tests", "fixtures", "jpx")
os.makedirs(OUT, exist_ok=True)


def picture(w, h, seed=1):
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:h, 0:w]
    r = (xx * 255 // max(1, w - 1)).astype(np.uint8)
    g = (yy * 255 // max(1, h - 1)).astype(np.uint8)
    b = ((xx ^ yy) * 4 & 255).astype(np.uint8)
    im = Image.fromarray(np.stack([r, g, b], -1), "RGB")
    d = ImageDraw.Draw(im)
    d.ellipse((w // 4, h // 4, w * 3 // 4, h * 3 // 4), outline=(255, 255, 0), width=3, fill=(20, 40, 160))
    d.line((0, 0, w, h), fill=(255, 255, 255), width=2)
    d.text((4, h // 2 - 5), "JPX", fill=(255, 0, 0))
    n = rng.integers(-12, 13, size=(h, w, 3))
    arr = np.clip(np.asarray(im).astype(int) + n, 0, 255).astype(np.uint8)
    return Image.fromarray(arr, "RGB")


def make_pdf(path, w, h, data, extra=None):
    pdf = Pdf.new()
    page = pdf.add_blank_page(page_size=(max(w, 3), max(h, 3)))
    img = Stream(pdf, data)
    img.Type = Name.XObject
    img.Subtype = Name.Image
    img.Width = w
    img.Height = h
    img.Filter = Name.JPXDecode
    for k, v in (extra or {}).items():
        img["/" + k] = v
    page.Resources = Dictionary(XObject=Dictionary(Im0=img))
    page.Contents = pdf.make_stream(f"q {w} 0 0 {h} 0 0 cm /Im0 Do Q".encode())
    pdf.save(path)


class PacketBits:
    def __init__(self, data):
        self.data = data
        self.pos = 0
        self.count = 0
        self.current = 0

    def bit(self):
        if self.count == 0:
            previous = self.current
            self.current = self.data[self.pos]
            self.pos += 1
            self.count = 7 if previous == 0xFF else 8
        self.count -= 1
        return self.current >> self.count & 1

    def bits(self, count):
        value = 0
        for _ in range(count):
            value = value << 1 | self.bit()
        return value

    def tag_tree(self, threshold):
        low = 0
        value = 0x7FFFFFFF
        while low < threshold and low < value:
            if self.bit():
                value = low
            else:
                low += 1
        return value < threshold

    def align(self):
        if self.current == 0xFF and self.pos < len(self.data):
            self.pos += 1
        return self.pos


def _marker(data, pos):
    marker = int.from_bytes(data[pos:pos + 2], "big")
    if marker in (0xFF4F, 0xFFD9, 0xFF93):
        return marker, pos + 2
    length = int.from_bytes(data[pos + 2:pos + 4], "big")
    if length < 2:
        raise ValueError(f"invalid marker length at offset {pos}")
    return marker, pos + 2 + length


def _packet_header_length(packet):
    bits = PacketBits(packet)
    if bits.bit():
        if bits.tag_tree(1):
            threshold = 1
            while not bits.tag_tree(threshold):
                threshold += 1
            if bits.bit() == 0:
                passes = 1
            elif bits.bit() == 0:
                passes = 2
            else:
                value = bits.bits(2)
                if value != 3:
                    passes = 3 + value
                else:
                    value = bits.bits(5)
                    passes = 6 + value if value != 31 else 37 + bits.bits(7)
            block_length = 3
            while bits.bit():
                block_length += 1
            data_length = bits.bits(block_length + (passes.bit_length() - 1))
            if data_length != len(packet) - bits.pos:
                raise ValueError("fixture must encode one complete code-block packet")
    header_length = bits.align()
    if header_length <= 0 or header_length >= len(packet):
        raise ValueError("fixture packet must have separate header and code-block data")
    return header_length


def _packed_segments(marker, payload, split_at):
    parts = (payload[:split_at], payload[split_at:])
    encoded = bytearray()
    for index, part in enumerate(parts):
        encoded += marker.to_bytes(2, "big")
        encoded += (len(part) + 3).to_bytes(2, "big")
        encoded.append(index)
        encoded += part
    return bytes(encoded)


def pack_packet_headers(data, marker):
    if not data.startswith(b"\xff\x4f"):
        raise ValueError("packed-header fixture requires a raw codestream")

    pos = 2
    while pos < len(data):
        current, next_pos = _marker(data, pos)
        if current == 0xFF90:
            sot = pos
            break
        pos = next_pos
    else:
        raise ValueError("codestream has no tile-part")

    psot = int.from_bytes(data[sot + 6:sot + 10], "big")
    tile_end = sot + psot
    if psot == 0 or tile_end > len(data):
        raise ValueError("fixture requires a bounded tile-part")

    pos = sot + 12
    while pos < tile_end:
        current, next_pos = _marker(data, pos)
        if current == 0xFF93:
            sod = pos
            break
        pos = next_pos
    else:
        raise ValueError("tile-part has no SOD marker")

    packet = data[sod + 2:tile_end]
    header_length = _packet_header_length(packet)
    header = packet[:header_length]
    body = packet[header_length:]

    if marker == "PPM":
        payload = len(header).to_bytes(4, "big") + header
        segments = _packed_segments(0xFF60, payload, 5)
        result = bytearray(data[:sot] + segments + data[sot:sod + 2] + body + data[tile_end:])
        shifted_sot = sot + len(segments)
        new_psot = psot - header_length
    elif marker == "PPT":
        segments = _packed_segments(0xFF61, header, max(1, len(header) // 2))
        result = bytearray(data[:sod] + segments + data[sod:sod + 2] + body + data[tile_end:])
        shifted_sot = sot
        new_psot = psot + len(segments) - header_length
    else:
        raise ValueError(f"unknown packed-header marker {marker}")

    result[shifted_sot + 6:shifted_sot + 10] = new_psot.to_bytes(4, "big")
    return bytes(result)


def emit(name, im, tol=0, extra=None, expected_mode=None, packed=None, **kw):
    buf = io.BytesIO()
    if packed is not None:
        kw["no_jp2"] = True
    im.save(buf, format="JPEG2000", **kw)
    data = buf.getvalue()
    if packed is not None:
        data = pack_packet_headers(data, packed)
    ref = Image.open(io.BytesIO(data))
    ref.load()
    arr = np.asarray(ref)
    if arr.dtype == np.uint16:
        arr = (arr >> 8).astype(np.uint8)
    mode = {1: "L", 2: "LA", 3: "RGB", 4: "RGBA"}[1 if arr.ndim == 2 else arr.shape[2]]
    if expected_mode == "RGB" and mode == "RGBA":
        arr = arr[..., :3]
        mode = "RGB"
    w, h = ref.size
    make_pdf(os.path.join(OUT, name + ".pdf"), w, h, data, extra)
    open(os.path.join(OUT, name + ".bin"), "wb").write(np.ascontiguousarray(arr).tobytes())
    open(os.path.join(OUT, name + ".meta"), "w").write(f"{mode} {w} {h} {tol}")
    print("wrote", name, mode, w, h, len(data), "bytes")


def main():
    rgb = picture(96, 72)
    gray = rgb.convert("L")
    emit("rgb-lossless", rgb)
    emit("rgb-lossless-nomct", rgb, mct=0)
    emit("gray-lossless", gray)
    emit("rgb-lossy97", rgb, tol=2, irreversible=True, quality_mode="rates", quality_layers=[8])
    emit("gray-lossy97", gray, tol=2, irreversible=True, quality_mode="rates", quality_layers=[10])
    emit("rgb-lossy-psnr", rgb, tol=2, irreversible=True, quality_mode="dB", quality_layers=[38])
    emit("rgb-layers", rgb, tol=2, irreversible=True, quality_mode="rates", quality_layers=[40, 15, 5, 1])
    emit("rgb-layers-lossless", rgb, quality_mode="rates", quality_layers=[30, 10, 1])
    for prog in ("LRCP", "RLCP", "RPCL", "PCRL", "CPRL"):
        emit("prog-" + prog.lower(), rgb, progression=prog, num_resolutions=4)
        emit("prog-" + prog.lower() + "-prec", rgb, progression=prog, num_resolutions=4, precinct_size=(32, 32), codeblock_size=(16, 16))
    emit("tiles", rgb, tile_size=(40, 32))
    emit("tiles-lossy", rgb, tol=2, irreversible=True, quality_mode="rates", quality_layers=[8], tile_size=(40, 32))
    emit("tile-offset", rgb, tile_size=(50, 40), tile_offset=(3, 2), offset=(5, 4))
    emit("res1", rgb, num_resolutions=1)
    emit("res6", rgb, num_resolutions=6)
    emit("cblk-small", rgb, codeblock_size=(8, 8))
    emit("cblk-wide", rgb, codeblock_size=(64, 4))
    odd = picture(37, 53, 7)
    emit("odd-size", odd)
    emit("odd-size-lossy", odd, tol=2, irreversible=True, quality_mode="rates", quality_layers=[6])
    emit("tiny", picture(3, 2, 3))
    emit("one-px", picture(1, 1, 4).convert("L"))
    emit("raw-codestream", rgb, no_jp2=True, extra={"ColorSpace": Name.DeviceRGB})
    emit("plt", rgb, plt=True)
    emit("with-cs", rgb, extra={"ColorSpace": Name.DeviceRGB})
    emit("gray-as-graycs", gray, extra={"ColorSpace": Name.DeviceGray})

    rgba = rgb.copy()
    rgba.putalpha(Image.linear_gradient("L").resize(rgb.size))
    emit("rgba-ignore-alpha", rgba, expected_mode="RGB")
    emit("rgba-smaskindata", rgba, extra={"SMaskInData": 1})

    g16 = np.asarray(gray).astype(np.uint16) * 257
    g16 = g16 ^ np.random.default_rng(5).integers(0, 256, g16.shape).astype(np.uint16)
    emit("gray16-lossless", Image.fromarray(g16, "I;16"))

    packed = Image.fromarray((np.arange(64, dtype=np.uint8).reshape((8, 8)) + 64), "L")
    emit("ppm-lossless", packed, packed="PPM", num_resolutions=1, irreversible=False)
    emit("ppt-lossless", packed, packed="PPT", num_resolutions=1, irreversible=False)


if __name__ == "__main__":
    main()
