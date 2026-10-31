"""Generates the JPEG 2000 conformance-matrix fixtures under tests/fixtures/jpxmatrix.

Each case writes name.pdf (one /Im0 JPXDecode image), name.bin (expected 8-bit interleaved RGBA-compare
pixels) and name.meta ("MODE width height tolerance PROVENANCE"). MODE is L, RGB or RGBA (channels compared
against the decoder's RGBA output). PROVENANCE is:

  openjpeg  expected pixels come from OpenJPEG (via Pillow/imagecodecs) decoding the exact fixture bytes, or
            from a source image that OpenJPEG round-tripped losslessly.
  derived   expected pixels come from the formula in the case's docstring/comment (T.800 / ISO 32000-2 rule
            implemented here, NOT by OpenJPEG). Used where OpenJPEG defines no behaviour or disagrees with PDF.

Container (JP2 box) cases decode through OpenJPEG as well; when OpenJPEG's output differs from the hand-derived
model the fixture is stored as "derived" and the disagreement is printed. Lossy (9/7) cases use tolerance 2.
"""
import io
import os
import struct
import numpy as np
from pikepdf import Pdf, Stream, Name, Dictionary, Array
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "tests", "fixtures", "jpxmatrix")
os.makedirs(OUT, exist_ok=True)


def picture(w, h, seed=1):
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:h, 0:w]
    r = (xx * 255 // max(1, w - 1)).astype(np.uint8)
    g = (yy * 255 // max(1, h - 1)).astype(np.uint8)
    b = ((xx ^ yy) * 4 & 255).astype(np.uint8)
    n = rng.integers(-12, 13, size=(h, w, 3))
    return np.clip(np.stack([r, g, b], -1).astype(int) + n, 0, 255).astype(np.uint8)


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


def write_case(name, data, mode, expected, tol, prov, extra=None):
    h, w = expected.shape[:2]
    make_pdf(os.path.join(OUT, name + ".pdf"), w, h, data, extra)
    open(os.path.join(OUT, name + ".bin"), "wb").write(np.ascontiguousarray(expected.astype(np.uint8)).tobytes())
    open(os.path.join(OUT, name + ".meta"), "w").write(f"{mode} {w} {h} {tol} {prov}")
    print("wrote", name, mode, w, h, tol, prov, len(data), "bytes")


def j2k(arr, **kw):
    buf = io.BytesIO()
    Image.fromarray(arr).save(buf, format="JPEG2000", no_jp2=True, **kw)
    return buf.getvalue()


def opj_decode(data):
    im = Image.open(io.BytesIO(data))
    im.load()
    return np.asarray(im)


def imagecodecs_decode(data):
    import imagecodecs
    return imagecodecs.jpeg2k_decode(data)


def to8(arr):
    if arr.dtype == np.uint16:
        return ((arr.astype(np.uint32) * 255 + 32767) // 65535).astype(np.uint8)
    return arr


# ---- Part 1 matrix: progression x precinct x tiles/offsets x layers x transform -------------------------------

def matrix():
    im = picture(70, 50, seed=3)
    for prog in ("LRCP", "RLCP", "RPCL", "PCRL", "CPRL"):
        for irrev in (False, True):
            name = f"mx-{prog.lower()}-{'97' if irrev else '53'}-tiles-offsets-layers"
            kw = dict(progression=prog, num_resolutions=4, precinct_size=(16, 16), tile_size=(48, 40),
                      tile_offset=(3, 4), offset=(5, 7), quality_mode="rates", quality_layers=[8, 3, 1],
                      irreversible=irrev)
            data = j2k(im, **kw)
            ref = opj_decode(data)
            write_case(name, data, "RGB", ref, 2 if irrev else 0, "openjpeg")


# ---- bit depths: unsigned 1..16, signed, and >16 ----------------------------------------------------------------

def depths():
    import imagecodecs
    for bits in (1, 2, 3, 5, 7, 9, 11, 13, 15):
        rng = np.random.default_rng(bits)
        mx = (1 << bits) - 1
        x = rng.integers(0, mx + 1, (24, 40)).astype(np.uint8 if bits <= 8 else np.uint16)
        x[0, :4] = (0, mx, min(1, mx), max(mx - 1, 0))
        data = imagecodecs.jpeg2k_encode(x, reversible=True, mct=False, bitspersample=bits, codecformat=imagecodecs.JPEG2K.CODEC.J2K)
        if not np.array_equal(imagecodecs.jpeg2k_decode(data), x):
            raise ValueError(f"{bits}-bit OpenJPEG round trip differs")
        v = (x.astype(np.int64) * 255 + mx // 2) // mx
        write_case(f"mx-depth{bits}-lossless", data, "L", v, 0, "openjpeg")
    # signed 12-bit: OpenJPEG round-trips the signed samples; PDF has no signed samples, so the displayed value is
    # the DC-level-shifted unsigned sample (x + 2^(b-1)) scaled exactly like unsigned data (hand-derived).
    rng = np.random.default_rng(99)
    s = rng.integers(-2048, 2048, (24, 40)).astype(np.int16)
    s[0, :4] = (-2048, 2047, 0, -1)
    try:
        data = imagecodecs.jpeg2k_encode(s, reversible=True, mct=False, bitspersample=12, codecformat=imagecodecs.JPEG2K.CODEC.J2K)
        if not np.array_equal(imagecodecs.jpeg2k_decode(data), s):
            raise ValueError("signed round trip differs")
        u = s.astype(np.int64) + 2048
        write_case("mx-signed12-lossless", data, "L", (u * 255 + 2047) // 4095, 0, "derived")
    except Exception as e:  # pragma: no cover - encoder-version dependent
        print("skip signed12:", e)
    # >16 bits: the decoder keeps the top 16 bits (hand-derived: v16 = x >> (b - 16), then 16 -> 8 bit scaling).
    for bits in (20, 24):
        rng = np.random.default_rng(bits)
        mx = (1 << bits) - 1
        x = rng.integers(0, mx + 1, (16, 24)).astype(np.uint32)
        x[0, :4] = (0, mx, 1, mx - 1)
        try:
            data = imagecodecs.jpeg2k_encode(x, reversible=True, mct=False, bitspersample=bits, codecformat=imagecodecs.JPEG2K.CODEC.J2K)
            if not np.array_equal(imagecodecs.jpeg2k_decode(data), x):
                raise ValueError("round trip differs")
            v16 = x.astype(np.int64) >> (bits - 16)
            write_case(f"mx-depth{bits}-lossless", data, "L", (v16 * 255 + 32767) // 65535, 1, "derived")
        except Exception as e:  # pragma: no cover
            print(f"skip depth{bits}:", e)


# ---- JP2 box helpers -------------------------------------------------------------------------------------------

def box(t, payload, xl=False, to_eof=False):
    if xl:
        return struct.pack(">I4sQ", 1, t, len(payload) + 16) + payload
    if to_eof:
        return struct.pack(">I4s", 0, t) + payload
    return struct.pack(">I4s", len(payload) + 8, t) + payload


def ihdr(w, h, nc, bpc=7, unk=0):
    return box(b"ihdr", struct.pack(">IIHBBBB", h, w, nc, bpc, 7, 0, unk))


def colr_enum(cs):
    return box(b"colr", struct.pack(">BBBI", 1, 0, 0, cs))


def jp2(boxes_header, cs, before=b"", after=b"", xl=False, to_eof=False):
    sig = box(b"jP  ", b"\r\n\x87\n")
    ftyp = box(b"ftyp", b"jp2 \x00\x00\x00\x00jp2 ")
    return sig + ftyp + box(b"jp2h", boxes_header) + before + box(b"jp2c", cs, xl=xl, to_eof=to_eof) + after


def emit_jp2(name, data, derived, mode="RGB", tol=0, extra=None):
    """Stores OpenJPEG's output when it agrees with the hand-derived model, otherwise the model."""
    prov, exp = "derived", derived
    candidates = []
    for label, fn in (("pillow", opj_decode), ("imagecodecs", imagecodecs_decode)):
        try:
            ref = to8(fn(data))
        except Exception as e:
            print(f"  NOTE {name}: {label} could not decode ({e})")
            continue
        if mode == "RGB" and ref.ndim == 3 and ref.shape[2] == 4:
            ref = ref[..., :3]
        if ref.shape == derived.shape and int(np.abs(ref.astype(int) - derived.astype(int)).max()) <= tol:
            prov = "openjpeg"
            break
        candidates.append(f"{label} shape {ref.shape}")
    if prov == "derived":
        print(f"  NOTE {name}: OpenJPEG ({'; '.join(candidates)}) disagrees with the derived model; storing derived")
    write_case(name, data, mode, exp, tol, prov, extra)


def unpremultiply(rgb, a):
    out = rgb.astype(np.int64).copy()
    a = a.astype(np.int64)
    m = (a > 0) & (a < 255)
    for k in range(3):
        ch = out[..., k]
        ch[m] = np.minimum(255, (ch[m] * 255 + a[m] // 2) // a[m])
    return out


def containers():
    w, h = 40, 30
    rgb = picture(w, h, seed=7)
    cs_rgb = j2k(rgb)
    hdr = ihdr(w, h, 3) + box(b"bpcc", bytes([7, 7, 7])) + colr_enum(16) + box(
        b"res ", box(b"resc", struct.pack(">HHHHBB", 72, 1, 72, 1, 0, 0)) + box(b"resd", struct.pack(">HHHHBB", 72, 1, 72, 1, 0, 0)))
    junk = box(b"uuid", bytes(range(16)) + b"payload") + box(b"xml ", b"<x><a>1</a></x>") + box(b"abcd", b"unknown box") + box(b"jp2i", b"ip")
    emit_jp2("ct-extra-boxes", jp2(hdr, cs_rgb, before=junk, after=junk), rgb)
    emit_jp2("ct-extra-boxes-xlbox", jp2(hdr, cs_rgb, before=junk, xl=True), rgb)
    emit_jp2("ct-codestream-box-to-eof", jp2(hdr, cs_rgb, before=junk, to_eof=True), rgb)
    # unsupported/vendor colr (meth 4 restricted-by-PDF, JPX Part 2) before a supported enumerated colr
    colr4 = box(b"colr", struct.pack(">BBB", 4, 0, 1) + bytes(16))
    colr3 = box(b"colr", struct.pack(">BBB", 3, 0, 1) + bytes(8))
    emit_jp2("ct-colr-unsupported-methods-first", jp2(ihdr(w, h, 3) + colr4 + colr3 + colr_enum(16), cs_rgb), rgb)
    emit_jp2("ct-colr-method-4-only", jp2(ihdr(w, h, 3) + colr4, cs_rgb), rgb)

    # channel definitions: swap channel 0 <-> 2 (B,G,R stored as associations 3,2,1)
    cdef = box(b"cdef", struct.pack(">H", 3) + struct.pack(">HHH", 0, 0, 3) + struct.pack(">HHH", 1, 0, 2) + struct.pack(">HHH", 2, 0, 1))
    emit_jp2("ct-cdef-bgr-reordered", jp2(ihdr(w, h, 3) + colr_enum(16) + cdef, cs_rgb), rgb[..., ::-1].copy())

    # alpha: straight vs premultiplied opacity (cdef type 1 / 2, association 0); alpha ramp includes 0, 1, 254, 255
    a = ((np.arange(w * h).reshape(h, w) * 255) // (w * h - 1)).astype(np.uint8)
    a[0, :4] = (0, 1, 254, 255)
    straight = np.dstack([rgb, a])
    cs_rgba = j2k(straight)
    cdef_a = lambda typ: box(b"cdef", struct.pack(">H", 4) + struct.pack(">HHH", 0, 0, 1) + struct.pack(">HHH", 1, 0, 2)
                             + struct.pack(">HHH", 2, 0, 3) + struct.pack(">HHH", 3, typ, 0))
    emit_jp2("ct-alpha-straight", jp2(ihdr(w, h, 4) + colr_enum(16) + cdef_a(1), cs_rgba), straight, "RGBA", extra={"SMaskInData": 1})
    pre_rgb = (rgb.astype(np.int64) * a[..., None] + 127) // 255
    pre = np.dstack([pre_rgb, a]).astype(np.uint8)
    cs_pre = j2k(pre)
    exp_pre = np.dstack([unpremultiply(pre[..., :3], a), a])
    write_case("ct-alpha-premultiplied-cdef", jp2(ihdr(w, h, 4) + colr_enum(16) + cdef_a(2), cs_pre), "RGBA", exp_pre, 0, "derived", {"SMaskInData": 1})
    write_case("ct-alpha-ignored-smaskindata0", jp2(ihdr(w, h, 4) + colr_enum(16) + cdef_a(1), cs_rgba), "RGBA",
               np.dstack([rgb, np.full((h, w), 255, np.uint8)]), 0, "derived", {"SMaskInData": 0})
    # no cdef at all: SMaskInData 1 = first extra channel is straight opacity, 2 = premultiplied (ISO 32000-2 Table 89)
    write_case("pdf-smaskindata1-nocdef", cs_rgba, "RGBA", straight, 0, "derived", {"SMaskInData": 1})
    write_case("pdf-smaskindata2-nocdef", cs_pre, "RGBA", exp_pre, 0, "derived", {"SMaskInData": 2})
    write_case("pdf-smaskindata2-overrides-cdef-straight", jp2(ihdr(w, h, 4) + colr_enum(16) + cdef_a(1), cs_pre), "RGBA", exp_pre, 0, "derived", {"SMaskInData": 2})
    write_case("pdf-smaskindata0-nocdef", jp2(ihdr(w, h, 4) + colr_enum(16), cs_rgba), "RGBA", np.dstack([rgb, np.full((h, w), 255, np.uint8)]), 0, "derived", {"SMaskInData": 0})
    # colour-key /Mask combined with embedded opacity: masked pixels become fully transparent, others keep the channel
    key = rgb[5, 5].astype(int)
    keyed = np.all(rgb == rgb[5, 5], axis=-1)
    exp = straight.copy()
    exp[keyed, 3] = 0
    write_case("pdf-mask-colorkey-with-smaskindata", cs_rgba, "RGBA", exp, 0, "derived",
               {"SMaskInData": 1, "Mask": Array([int(key[0]), int(key[0]), int(key[1]), int(key[1]), int(key[2]), int(key[2])])})

    # palettes: 3 columns from a single index component, plus a 4th (alpha) column
    idx = (np.random.default_rng(5).integers(0, 200, (h, w))).astype(np.uint8)
    cs_idx = j2k(idx)
    pal = np.random.default_rng(6).integers(0, 256, (200, 4)).astype(np.uint8)
    pclr3 = box(b"pclr", struct.pack(">HB", 200, 3) + bytes([7, 7, 7]) + pal[:, :3].tobytes())
    cmap3 = box(b"cmap", b"".join(struct.pack(">HBB", 0, 1, c) for c in range(3)))
    emit_jp2("ct-palette-3col", jp2(ihdr(w, h, 1) + colr_enum(16) + pclr3 + cmap3, cs_idx), pal[idx][..., :3])
    pclr4 = box(b"pclr", struct.pack(">HB", 200, 4) + bytes([7] * 4) + pal.tobytes())
    cmap4 = box(b"cmap", b"".join(struct.pack(">HBB", 0, 1, c) for c in range(4)))
    cdef4 = cdef_a(1)
    emit_jp2("ct-palette-4col-alpha", jp2(ihdr(w, h, 1) + colr_enum(16) + pclr4 + cmap4 + cdef4, cs_idx), pal[idx], "RGBA", extra={"SMaskInData": 1})
    # palette with mixed 4-bit / 8-bit columns (depth is per column; values scale to 8 bit by round-half-up)
    pal2 = np.random.default_rng(8).integers(0, 16, (200, 3)).astype(np.uint8)
    pal2[:, 1] = np.random.default_rng(9).integers(0, 256, 200)
    pclr_m = box(b"pclr", struct.pack(">HB", 200, 3) + bytes([3, 7, 3]) + pal2.tobytes())
    exp_m = np.stack([(pal2[:, 0].astype(int) * 255 + 7) // 15, pal2[:, 1], (pal2[:, 2].astype(int) * 255 + 7) // 15], -1)
    emit_jp2("ct-palette-mixed-depth", jp2(ihdr(w, h, 1) + colr_enum(16) + pclr_m + cmap3, cs_idx), exp_m[idx].astype(np.uint8))
    # PDF /Indexed supplies its own palette: the JP2 pclr must be ignored (indices are samples)
    lookup = bytes((i * 7 + k * 31) & 255 for i in range(256) for k in range(3))
    lut = np.frombuffer(lookup, np.uint8).reshape(256, 3)
    write_case("pdf-indexed-ignores-pclr", jp2(ihdr(w, h, 1) + colr_enum(16) + pclr3 + cmap3, cs_idx), "RGB", lut[idx], 0, "derived",
               {"ColorSpace": Array([Name.Indexed, Name.DeviceRGB, 255, lookup])})

    # enumerated colour spaces
    ycc = Image.fromarray(rgb).convert("YCbCr")
    ycc_arr = np.asarray(ycc)
    cs_ycc = j2k(ycc_arr)
    yf = ycc_arr.astype(np.float64)
    r = yf[..., 0] + 1.402 * (yf[..., 2] - 128)
    g = yf[..., 0] - 0.344136 * (yf[..., 1] - 128) - 0.714136 * (yf[..., 2] - 128)
    b = yf[..., 0] + 1.772 * (yf[..., 1] - 128)
    ycc_rgb = np.clip(np.floor(np.stack([r, g, b], -1) + 0.5), 0, 255).astype(np.uint8)
    emit_jp2("ct-colr-sycc", jp2(ihdr(w, h, 3) + colr_enum(18), cs_ycc), ycc_rgb, tol=1)
    emit_jp2("ct-colr-esrgb", jp2(ihdr(w, h, 3) + colr_enum(20), cs_rgb), rgb)
    gray = rgb[..., 1]
    emit_jp2("ct-colr-greyscale", jp2(ihdr(w, h, 1) + colr_enum(17), j2k(gray)), gray, "L")
    # PDF /ColorSpace overrides the JP2 colour specification (a gray colr on RGB data is ignored; first channel kept
    # when the PDF names fewer components -- hand-derived, undefined by OpenJPEG).
    write_case("pdf-colorspace-gray-over-rgb", jp2(ihdr(w, h, 3) + colr_enum(16), cs_rgb), "L", rgb[..., 0], 0, "derived", {"ColorSpace": Name.DeviceGray})
    write_case("pdf-colorspace-rgb-over-gray-component-mismatch", jp2(ihdr(w, h, 1) + colr_enum(17), j2k(gray)), "L", gray, 0, "derived", {"ColorSpace": Name.DeviceRGB})
    write_case("pdf-decode-inverted-rgb", cs_rgb, "RGB", 255 - rgb, 0, "derived", {"Decode": Array([1, 0, 1, 0, 1, 0])})
    write_case("pdf-decode-subrange-gray", j2k(gray), "L", np.round((0.25 + gray / 255.0 * 0.5) * 255).astype(np.uint8), 1, "derived",
               {"ColorSpace": Name.DeviceGray, "Decode": Array([0.25, 0.75])})

    # CMYK (enumerated CS 12): raw four-channel samples are kept untouched (not a PDF case; read by a direct test)
    cmyk = np.dstack([rgb, gray])
    cs_cmyk = j2k(cmyk)
    cmyk_jp2 = jp2(ihdr(w, h, 4) + colr_enum(12), cs_cmyk)
    open(os.path.join(OUT, "raw-ct-colr-cmyk.jp2"), "wb").write(cmyk_jp2)
    open(os.path.join(OUT, "raw-ct-colr-cmyk.bin"), "wb").write(np.ascontiguousarray(cmyk).tobytes())
    print("wrote raw-ct-colr-cmyk (", "openjpeg" if np.array_equal(opj_decode(cs_cmyk), cmyk) else "derived", ")")


def main():
    matrix()
    depths()
    containers()


if __name__ == "__main__":
    main()
