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


def emit(name, im, tol=0, extra=None, expected_mode=None, **kw):
    buf = io.BytesIO()
    im.save(buf, format="JPEG2000", **kw)
    data = buf.getvalue()
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


if __name__ == "__main__":
    main()
