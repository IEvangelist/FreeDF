"""Generates the JPEG 2000 conformance-matrix fixtures under tests/fixtures/jpxmatrix.

Each case writes name.pdf (one /Im0 JPXDecode image), name.bin (expected 8-bit interleaved pixels) and name.meta
("MODE width height tolerance PROVENANCE REASON..."). MODE is L, RGB or RGBA (channels compared against the decoder's
RGBA output). Reference pixels come from `opj_decompress` (OpenJPEG 2.5.4 official release, set OPJ_DIR), never from
this repository's decoder; encoders (Pillow/imagecodecs) only produce the codestreams. PROVENANCE is:

  openjpeg               pixels are opj_decompress output for the exact fixture bytes.
  openjpeg-samples+rule  native-depth samples from opj_decompress, then the decoder's documented 8-bit scaling rule.
  derived                PDF-level semantics OpenJPEG does not implement (alpha un-premultiply, /Decode, /ColorSpace,
                         /Mask, /Indexed); the underlying samples are still confirmed with opj_decompress. REASON says why.

JP2-container cases (emit_jp2) must agree with opj_decompress within the tolerance or the generator fails, unless the
fixture is listed in FALLBACK_ALLOW (currently empty). manifest.json records tool versions and hashes.
Lossy (9/7) cases use tolerance 2.
"""
import hashlib
import io
import json
import os
import re
import shutil
import struct
import subprocess
import tempfile
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


OPJ_DIR = os.environ.get("OPJ_DIR") or os.path.dirname(shutil.which("opj_decompress") or "")
EXE = ".exe" if os.name == "nt" else ""
DECOMPRESS = os.path.join(OPJ_DIR, "opj_decompress" + EXE)
MANIFEST = {"cases": {}}
# fixtures whose expected pixels are NOT confirmed by opj_decompress even though the model could be compared:
# name -> why. The generator fails if any other fixture falls back to the model.
FALLBACK_ALLOW = {}


def write_case(name, data, mode, expected, tol, prov, extra=None, reason=None):
    if prov != "openjpeg" and not reason:
        raise ValueError(f"{name}: provenance {prov} requires a reason")
    h, w = expected.shape[:2]
    make_pdf(os.path.join(OUT, name + ".pdf"), w, h, data, extra)
    open(os.path.join(OUT, name + ".bin"), "wb").write(np.ascontiguousarray(expected.astype(np.uint8)).tobytes())
    open(os.path.join(OUT, name + ".meta"), "w").write(f"{mode} {w} {h} {tol} {prov} {reason or 'pixels decoded by opj_decompress'}")
    MANIFEST["cases"][name] = {"mode": mode, "tolerance": tol, "provenance": prov, "reason": reason or "pixels decoded by opj_decompress"}
    print("wrote", name, mode, w, h, tol, prov, len(data), "bytes")


def j2k(arr, **kw):
    buf = io.BytesIO()
    Image.fromarray(arr).save(buf, format="JPEG2000", no_jp2=True, **kw)
    return buf.getvalue()


def opj_channels(data):
    """Decodes with opj_decompress to per-channel PGX at native depth (pclr/cmap/cdef/sYCC applied by OpenJPEG).
    Returns (array HxWxC int64, [precision], [signed])."""
    with tempfile.TemporaryDirectory() as tmp:
        src = os.path.join(tmp, "in." + ("jp2" if data[4:8] == b"jP  " else "j2k"))
        open(src, "wb").write(data)
        r = subprocess.run([DECOMPRESS, "-i", src, "-o", os.path.join(tmp, "o.pgx")], capture_output=True, text=True)
        if r.returncode != 0:
            raise RuntimeError("opj_decompress failed: " + (r.stdout + r.stderr).strip()[-200:])
        files = sorted(f for f in os.listdir(tmp) if f.startswith("o") and f.endswith(".pgx"))
        if len(files) > 1:
            files.sort(key=lambda f: int(re.search(r"_(\d+)\.pgx", f).group(1)))
        chans, precs, sgn = [], [], []
        for f in files:
            raw = open(os.path.join(tmp, f), "rb").read()
            nl = raw.index(b"\n")
            p = raw[:nl].decode().split()
            s, prec, w, h = p[2] == "-", int(p[3]), int(p[4]), int(p[5])
            size = 1 if prec <= 8 else 2 if prec <= 16 else 4
            dt = (">i" if s else ">u") + str(size)
            chans.append(np.frombuffer(raw[nl + 1:], dtype=dt)[:w * h].reshape(h, w).astype(np.int64))
            precs.append(prec)
            sgn.append(s)
        return np.stack(chans, -1), precs, sgn


def scale8(x, prec):
    mx = (1 << prec) - 1
    return (x.astype(np.int64) * 255 + mx // 2) // mx


def require_opj(name, data, expected, tol=0):
    """Fails the generator unless opj_decompress output equals `expected` (channel-last array)."""
    got, _, _ = opj_channels(data)
    got = got.reshape(expected.shape) if got.shape != expected.shape and got.shape[-1] == 1 else got
    if got.shape != expected.shape or int(np.abs(got - expected.astype(np.int64)).max()) > tol:
        raise ValueError(f"{name}: opj_decompress output differs from the expected samples (shape {got.shape} vs {expected.shape})")

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
            ref = opj_channels(data)[0]
            write_case(name, data, "RGB", ref, 1 if irrev else 0, "openjpeg")


# ---- bit depths: unsigned 1..16, signed, and >16 ----------------------------------------------------------------

def depths():
    import imagecodecs
    rule = "native samples from opj_decompress (PGX), scaled to 8 bit with round(x*255/max) (decoder's PDF sample-scaling rule)"
    for bits in (1, 2, 3, 5, 7, 9, 11, 13, 15):
        rng = np.random.default_rng(bits)
        mx = (1 << bits) - 1
        x = rng.integers(0, mx + 1, (24, 40)).astype(np.uint8 if bits <= 8 else np.uint16)
        x[0, :4] = (0, mx, min(1, mx), max(mx - 1, 0))
        data = imagecodecs.jpeg2k_encode(x, reversible=True, mct=False, bitspersample=bits, codecformat=imagecodecs.JPEG2K.CODEC.J2K)
        native, precs, sgn = opj_channels(data)
        if precs != [bits] or sgn != [False] or not np.array_equal(native[..., 0], x):
            raise ValueError(f"{bits}-bit: opj_decompress native samples differ from the input")
        write_case(f"mx-depth{bits}-lossless", data, "L", scale8(native[..., 0], bits), 0, "openjpeg-samples+rule", reason=rule)
    # signed 12-bit: PDF has no signed samples; the display value is the DC-shifted unsigned sample (x + 2^(b-1)).
    rng = np.random.default_rng(99)
    s = rng.integers(-2048, 2048, (24, 40)).astype(np.int16)
    s[0, :4] = (-2048, 2047, 0, -1)
    data = imagecodecs.jpeg2k_encode(s, reversible=True, mct=False, bitspersample=12, codecformat=imagecodecs.JPEG2K.CODEC.J2K)
    native, precs, sgn = opj_channels(data)
    if precs != [12] or sgn != [True] or not np.array_equal(native[..., 0], s):
        raise ValueError("signed12: opj_decompress native samples differ from the input")
    write_case("mx-signed12-lossless", data, "L", scale8(native[..., 0] + 2048, 12), 0, "openjpeg-samples+rule",
               reason="signed native samples from opj_decompress; PDF has no signed samples so the decoder displays x+2^(b-1) (DC level shift), then " + rule.split("scaled to ")[1])
    # >16 bits: the decoder keeps the top 16 bits before scaling to 8 bit.
    for bits in (20, 24):
        rng = np.random.default_rng(bits)
        mx = (1 << bits) - 1
        x = rng.integers(0, mx + 1, (16, 24)).astype(np.uint32)
        x[0, :4] = (0, mx, 1, mx - 1)
        data = imagecodecs.jpeg2k_encode(x, reversible=True, mct=False, bitspersample=bits, codecformat=imagecodecs.JPEG2K.CODEC.J2K)
        native, precs, sgn = opj_channels(data)
        if precs != [bits] or not np.array_equal(native[..., 0], x):
            raise ValueError(f"{bits}-bit: opj_decompress native samples differ from the input")
        write_case(f"mx-depth{bits}-lossless", data, "L", scale8(native[..., 0] >> (bits - 16), 16), 1, "openjpeg-samples+rule",
                   reason=f"native {bits}-bit samples from opj_decompress; decoder keeps the top 16 bits (x >> {bits - 16}) then scales to 8 bit; tolerance 1 for float precision")

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
    """Stores opj_decompress output (channels after pclr/cmap/cdef/sYCC handling). The model must agree within tol;
    a disagreement fails the generator unless the case is explicitly listed in FALLBACK_ALLOW."""
    try:
        ref, precs, _ = opj_channels(data)
        ref = np.stack([ref[..., c] if precs[c] == 8 else scale8(ref[..., c], precs[c]) for c in range(ref.shape[-1])], -1)
        if mode == "L" and ref.ndim == 3 and ref.shape[2] == 1:
            ref = ref[..., 0]
        if mode == "RGB" and ref.shape[-1] == 4:
            ref = ref[..., :3]
        problem = None if ref.shape == derived.shape and int(np.abs(ref - derived.astype(np.int64)).max()) <= tol else f"opj shape {ref.shape} vs model {derived.shape}"
    except Exception as e:
        ref, problem = None, f"opj_decompress failed: {e}"
    if problem is None:
        write_case(name, data, mode, ref, tol, "openjpeg", extra)
    elif name in FALLBACK_ALLOW:
        write_case(name, data, mode, derived, tol, "derived", extra, reason=f"FALLBACK ({problem}): {FALLBACK_ALLOW[name]}")
    else:
        raise ValueError(f"{name}: OpenJPEG disagrees with the model ({problem}); investigate or add an explicit FALLBACK_ALLOW entry")


def pdf_case(name, data, mode, expected, reason, extra=None, tol=0, opj_samples=None):
    """PDF-semantics fixture: the underlying decoded samples are confirmed with opj_decompress (opj_samples, channel-last),
    the PDF-level rule (alpha, /Decode, /ColorSpace, /Mask, /Indexed) has no OpenJPEG counterpart and is derived."""
    if opj_samples is not None:
        require_opj(name, data, opj_samples)
    write_case(name, data, mode, expected, tol, "derived", extra, reason=reason + ("; underlying samples confirmed by opj_decompress" if opj_samples is not None else ""))

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
    unpre = "premultiplied opacity (cdef type 2 / SMaskInData 2) is un-premultiplied with c = min(255, (c*255 + a/2) // a) for 0 < a < 255; OpenJPEG does not un-premultiply"
    opaque = np.dstack([rgb, np.full((h, w), 255, np.uint8)])
    pdf_case("ct-alpha-premultiplied-cdef", jp2(ihdr(w, h, 4) + colr_enum(16) + cdef_a(2), cs_pre), "RGBA", exp_pre, unpre, {"SMaskInData": 1}, opj_samples=pre)
    pdf_case("ct-alpha-ignored-smaskindata0", jp2(ihdr(w, h, 4) + colr_enum(16) + cdef_a(1), cs_rgba), "RGBA", opaque,
             "/SMaskInData 0: the embedded opacity channel is ignored (alpha 255)", {"SMaskInData": 0}, opj_samples=straight)
    # no cdef at all: SMaskInData 1 = first extra channel is straight opacity, 2 = premultiplied (ISO 32000-2 Table 89)
    pdf_case("pdf-smaskindata1-nocdef", cs_rgba, "RGBA", straight, "/SMaskInData 1 without cdef: the fourth channel is straight opacity", {"SMaskInData": 1}, opj_samples=straight)
    pdf_case("pdf-smaskindata2-nocdef", cs_pre, "RGBA", exp_pre, "/SMaskInData 2 without cdef: " + unpre, {"SMaskInData": 2}, opj_samples=pre)
    pdf_case("pdf-smaskindata2-overrides-cdef-straight", jp2(ihdr(w, h, 4) + colr_enum(16) + cdef_a(1), cs_pre), "RGBA", exp_pre,
             "/SMaskInData 2 overrides a cdef that declares straight opacity: " + unpre, {"SMaskInData": 2}, opj_samples=pre)
    pdf_case("pdf-smaskindata0-nocdef", jp2(ihdr(w, h, 4) + colr_enum(16), cs_rgba), "RGBA", opaque,
             "/SMaskInData 0 with colr enumerated RGB and an unassociated fourth channel: alpha ignored", {"SMaskInData": 0}, opj_samples=straight)
    # colour-key /Mask combined with embedded opacity: masked pixels become fully transparent, others keep the channel
    key = rgb[5, 5].astype(int)
    keyed = np.all(rgb == rgb[5, 5], axis=-1)
    exp = straight.copy()
    exp[keyed, 3] = 0
    pdf_case("pdf-mask-colorkey-with-smaskindata", cs_rgba, "RGBA", exp,
             "colour-key /Mask zeroes alpha of matching pixels, other pixels keep the SMaskInData 1 opacity", 
             {"SMaskInData": 1, "Mask": Array([int(key[0]), int(key[0]), int(key[1]), int(key[1]), int(key[2]), int(key[2])])}, opj_samples=straight)

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
    pdf_case("pdf-indexed-ignores-pclr", jp2(ihdr(w, h, 1) + colr_enum(16) + pclr3 + cmap3, cs_idx), "RGB", lut[idx],
             "PDF /Indexed supplies the palette; the JP2 pclr is ignored and the codestream samples are the indices",
             {"ColorSpace": Array([Name.Indexed, Name.DeviceRGB, 255, lookup])}, opj_samples=pal[idx][..., :3])

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
    pdf_case("pdf-colorspace-gray-over-rgb", jp2(ihdr(w, h, 3) + colr_enum(16), cs_rgb), "L", rgb[..., 0],
             "PDF /ColorSpace DeviceGray names fewer components than the JPX has: the first channel is used (undefined by OpenJPEG)", {"ColorSpace": Name.DeviceGray}, opj_samples=rgb)
    pdf_case("pdf-colorspace-rgb-over-gray-component-mismatch", jp2(ihdr(w, h, 1) + colr_enum(17), j2k(gray)), "L", gray,
             "PDF /ColorSpace DeviceRGB on a one-component JPX is a component-count mismatch; this decoder falls back to gray (undefined by OpenJPEG)", {"ColorSpace": Name.DeviceRGB}, opj_samples=gray[..., None])
    pdf_case("pdf-decode-inverted-rgb", cs_rgb, "RGB", 255 - rgb, "/Decode [1 0 1 0 1 0] inverts samples: 255 - x", {"Decode": Array([1, 0, 1, 0, 1, 0])}, opj_samples=rgb)
    pdf_case("pdf-decode-subrange-gray", j2k(gray), "L", np.round((0.25 + gray / 255.0 * 0.5) * 255).astype(np.uint8),
             "/Decode [0.25 0.75]: round((0.25 + x/255*0.5)*255); tolerance 1 for float rounding", {"ColorSpace": Name.DeviceGray, "Decode": Array([0.25, 0.75])},
             tol=1, opj_samples=gray[..., None])

    # CMYK (enumerated CS 12): raw four-channel samples are kept untouched (not a PDF case; read by a direct test)
    cmyk = np.dstack([rgb, gray])
    cs_cmyk = j2k(cmyk)
    cmyk_jp2 = jp2(ihdr(w, h, 4) + colr_enum(12), cs_cmyk)
    # opj_decompress converts enumerated CMYK to RGB on output, so the raw four-channel samples are confirmed on the bare codestream
    ref_cmyk = opj_channels(cs_cmyk)[0]
    if not np.array_equal(ref_cmyk, cmyk):
        raise ValueError("CMYK: opj_decompress output differs from the input")
    open(os.path.join(OUT, "raw-ct-colr-cmyk.jp2"), "wb").write(cmyk_jp2)
    open(os.path.join(OUT, "raw-ct-colr-cmyk.bin"), "wb").write(np.ascontiguousarray(ref_cmyk.astype(np.uint8)).tobytes())
    print("wrote raw-ct-colr-cmyk (openjpeg)")


def main():
    for fn in os.listdir(OUT):
        os.remove(os.path.join(OUT, fn))
    import PIL
    import imagecodecs
    from PIL import features
    r = subprocess.run([DECOMPRESS, "-h"], capture_output=True, text=True)
    MANIFEST["openjpeg_decoder"] = {"version": re.search(r"openjp2 library v([0-9.]+)", r.stdout + r.stderr).group(1),
                                    "opj_decompress_sha256": hashlib.sha256(open(DECOMPRESS, "rb").read()).hexdigest(),
                                    "command": "opj_decompress -i <fixture codestream/jp2> -o o.pgx (reference channels at native depth)"}
    MANIFEST["encoders"] = {"pillow": PIL.__version__, "pillow_openjpeg": features.version("jpg_2000"), "imagecodecs": imagecodecs.__version__}
    matrix()
    depths()
    containers()
    json.dump(MANIFEST, open(os.path.join(OUT, "manifest.json"), "w"), indent=1)


if __name__ == "__main__":
    main()
