"""OpenJPEG command-line conformance matrix -> tests/fixtures/jpxopj.

Every codestream is produced by `opj_compress` (OpenJPEG 2.5.4 official Windows release) from raw planar
samples, or by marker surgery on such a codestream (COC/QCC/CRG/derived quantization/guard bits/PPM/PPT/POC; the
surgery helpers are shared with make_jpx_fixtures.py). Reference samples are ALWAYS produced by `opj_decompress`
(PGX, one file per component at native bit depth, signed data kept signed) -- never by this repository's decoder.
Irreversible (9/7) tolerances are the measured maximum difference to opj_decompress (1; 2 for 16-bit) - see JPX_REPORT_MAX in JpxOpjMatrixTests. Lossless cases are additionally checked here against the encoder input.

Per case writes:  name.j2k  name.cN.pgx (reference planes)  name.meta (JSON: tolerance, command lines)
Plus manifest.json: OpenJPEG version, binary SHA-256, every command line, and out-of-scope notes.

Usage: OPJ_DIR=<dir with opj_compress.exe/opj_decompress.exe> python tools/make_jpx_opj_matrix.py
"""
import hashlib
import json
import os
import re
import shutil
import struct
import subprocess
import sys
import tempfile
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
OUT = os.path.join(ROOT, "tests", "fixtures", "jpxopj")
sys.path.insert(0, HERE)
os.makedirs(OUT, exist_ok=True)

OPJ_DIR = os.environ.get("OPJ_DIR") or os.path.dirname(shutil.which("opj_compress") or "")
EXE = ".exe" if os.name == "nt" else ""
COMPRESS = os.path.join(OPJ_DIR, "opj_compress" + EXE)
DECOMPRESS = os.path.join(OPJ_DIR, "opj_decompress" + EXE)
manifest = {"cases": {}, "outOfScope": {}}


def portable(cmd, tmp):
    """Command line without platform-specific executable suffix or temp directory."""
    parts = [os.path.splitext(os.path.basename(cmd[0]))[0]] + [c.replace(tmp, "<tmp>").replace("\\", "/") for c in cmd[1:]]
    return " ".join(parts)


def run(cmd):
    r = subprocess.run(cmd, capture_output=True, text=True)
    return r.returncode, (r.stdout or "") + (r.stderr or "")


def read_pgx(path):
    with open(path, "rb") as f:
        raw = f.read()
    nl = raw.index(b"\n")
    parts = raw[:nl].decode().split()
    signed = parts[2] == "-"
    prec, w, h = int(parts[3]), int(parts[4]), int(parts[5])
    size = 1 if prec <= 8 else 2 if prec <= 16 else 4
    dt = {1: ">i1" if signed else ">u1", 2: ">i2" if signed else ">u2", 4: ">i4" if signed else ">u4"}[size]
    return np.frombuffer(raw[nl + 1:nl + 1 + w * h * size], dtype=dt).reshape(h, w).astype(np.int64), prec, signed


def write_pgx(path, arr, prec, signed):
    size = 1 if prec <= 8 else 2 if prec <= 16 else 4
    dt = (">i" if signed else ">u") + str(size)
    with open(path, "wb") as f:
        f.write(f"PG ML {'-' if signed else '+'} {prec} {arr.shape[1]} {arr.shape[0]}\n".encode())
        f.write(arr.astype(dt).tobytes())


def source_planes(w, h, nc, depth, signed, subs, seed):
    rng = np.random.default_rng(seed)
    planes = []
    mx = (1 << depth) - 1
    for c in range(nc):
        dx, dy = subs[c]
        cw, ch = -(-w // dx), -(-h // dy)
        yy, xx = np.mgrid[0:ch, 0:cw]
        base = ((xx * (c + 3) + yy * (c + 5)) % 64) / 63.0 * 0.6 + 0.2
        noise = rng.normal(0, 0.05, (ch, cw))
        v = np.clip(np.round((base + noise) * mx), 0, mx).astype(np.int64)
        v[0, :min(cw, 4)] = [0, mx, 1, max(mx - 1, 0)][:min(cw, 4)]
        if signed:
            v = v - (1 << (depth - 1))
        planes.append(v)
    return planes


def write_raw(path, planes, depth, signed):
    with open(path, "wb") as f:
        for p in planes:
            q = p.astype(np.int64)
            size = 1 if depth <= 8 else 2
            dt = (">i" if signed else ">u") + str(size)
            f.write(q.astype(dt).tobytes())


def decode_ref(codestream, tmp, nc):
    """Runs opj_decompress; returns list of (planes, prec, signed) per output channel and the command line."""
    out = os.path.join(tmp, "ref.pgx")
    cmd = [DECOMPRESS, "-i", codestream, "-o", out]
    rc, log = run(cmd)
    if rc != 0:
        return None, cmd, log
    files = sorted(f for f in os.listdir(tmp) if f.startswith("ref") and f.endswith(".pgx"))
    if files == ["ref.pgx"]:
        order = ["ref.pgx"]
    else:
        order = sorted(files, key=lambda f: int(re.search(r"_(\d+)\.pgx", f).group(1)))
    return [(*read_pgx(os.path.join(tmp, f)), f) for f in order], cmd, log


def store(name, codestream_bytes, comps, tol, extra):
    with open(os.path.join(OUT, name + ".j2k"), "wb") as f:
        f.write(codestream_bytes)
    # one packed reference file per case: u32 count, then per component u32 length + PGX bytes
    with open(os.path.join(OUT, name + ".ref"), "wb") as f:
        f.write(struct.pack(">I", len(comps)))
        for arr, prec, signed, tmpfile in comps:
            pgx = open(os.path.join(extra["_tmp"], tmpfile), "rb").read()
            f.write(struct.pack(">I", len(pgx)) + pgx)
    meta = {"tolerance": tol, "components": len(comps), **{k: v for k, v in extra.items() if not k.startswith("_")}}
    json.dump(meta, open(os.path.join(OUT, name + ".meta"), "w"), indent=1)
    manifest["cases"][name] = meta
    print("wrote", name, len(codestream_bytes), "bytes", "tol", tol)


def encode_case(name, w, h, nc=3, depth=8, signed=False, subs=None, args=(), tol=0, lossless=True, seed=1, surgery=None, note=None,
                expect="decode", exempt_x=None):
    subs = subs or [(1, 1)] * nc
    planes = source_planes(w, h, nc, depth, signed, subs, seed)
    with tempfile.TemporaryDirectory() as tmp:
        raw = os.path.join(tmp, "in.raw")
        write_raw(raw, planes, depth, signed)
        sub = ":".join(f"{a}x{b}" for a, b in subs)
        fmt = f"{w},{h},{nc},{depth},{'s' if signed else 'u'}" + ("" if all(s == (1, 1) for s in subs) else "@" + sub)
        j2k = os.path.join(tmp, "case.j2k")
        enc = [COMPRESS, "-i", raw, "-F", fmt, "-o", j2k, *args]
        rc, log = run(enc)
        if rc != 0 or not os.path.exists(j2k):
            raise RuntimeError(f"{name}: opj_compress failed: {log.strip()[-200:]}")
        data = open(j2k, "rb").read()
        cmds = [portable(enc, tmp)]
        if surgery:
            try:
                data = surgery(data)
            except (ValueError, AssertionError, IndexError) as e:
                raise RuntimeError(f"{name}: marker surgery {surgery.__name__} not applicable: {e}")
            cmds.append("marker surgery: " + surgery.__name__)
            open(j2k, "wb").write(data)
        ref, dec, log = decode_ref(j2k, tmp, nc)
        if ref is None:
            raise RuntimeError(f"{name}: opj_decompress failed: {log.strip()[-200:]}")
        cmds.append(portable(dec, tmp))
        if lossless:
            if len(ref) != nc:
                raise ValueError(f"{name}: expected {nc} output channels, got {len(ref)}")
            for i, (arr, prec, sg, _) in enumerate(ref):
                if not np.array_equal(arr, planes[i]):
                    if any(s != (1, 1) for s in subs):
                        note = (note or "") + " Input-equality not asserted: OpenJPEG decoded plane dims/values differ from the generator model for subsampled raw input; the reference is opj_decompress output."
                        break
                    raise ValueError(f"{name}: OpenJPEG lossless round trip differs on component {i} (input assumptions wrong)")
        extra = {"_tmp": tmp, "commands": cmds, "depth": depth, "signed": signed, "lossless": lossless, "expect": expect, "exemptColumnsX": exempt_x or [],
                 "opjEqualsEncoderInput": bool(len(ref) == nc and all(a.shape == p.shape and np.array_equal(a, p) for (a, _, _, _), p in zip(ref, planes)))}
        extra["reference"] = "opj_decompress"
        if note:
            extra["note"] = note
        store(name, data, ref, tol, extra)
        return data


# ---- marker surgery -------------------------------------------------------------------------------------------

def _find(data, marker):
    i = 2
    while i + 4 <= len(data):
        m = (data[i] << 8) | data[i + 1]
        if m == 0xFF90:
            return -1
        ln = (data[i + 2] << 8) | data[i + 3]
        if m == marker:
            return i
        i += 2 + ln
    return -1


def _seg(data, marker):
    i = _find(data, marker)
    ln = (data[i + 2] << 8) | data[i + 3]
    return i, ln


def _csiz(data):
    i, _ = _seg(data, 0xFF51)
    return (data[i + 4 + 34] << 8) | data[i + 4 + 35]


def _cidx(c, csiz):
    return bytes([c]) if csiz < 257 else struct.pack(">H", c)


def add_coc_qcc_identity(data):
    """COC/QCC for every component repeating the main COD/QCD parameters (override path, identical result)."""
    csiz = _csiz(data)
    ci, cl = _seg(data, 0xFF52)
    cod = data[ci + 4:ci + 2 + cl]
    scod, sp = cod[0], cod[5:]
    qi, ql = _seg(data, 0xFF5C)
    qcd = data[qi + 4:qi + 2 + ql]
    cocs = b"".join(struct.pack(">HH", 0xFF53, 2 + (1 if csiz < 257 else 2) + 1 + len(sp)) + _cidx(c, csiz) + bytes([scod & 1]) + sp for c in range(csiz))
    qccs = b"".join(struct.pack(">HH", 0xFF5D, 2 + (1 if csiz < 257 else 2) + len(qcd)) + _cidx(c, csiz) + qcd for c in range(csiz))
    return data[:ci + 2 + cl] + cocs + data[ci + 2 + cl:qi + 2 + ql] + qccs + data[qi + 2 + ql:]


def add_crg(data):
    csiz = _csiz(data)
    crg = struct.pack(">HH", 0xFF63, 2 + 4 * csiz) + b"".join(struct.pack(">HH", 0, 0) for _ in range(csiz))
    qi, ql = _seg(data, 0xFF5C)
    return data[:qi + 2 + ql] + crg + data[qi + 2 + ql:]


def derived_quant(data):
    """Rewrites QCD as scalar-derived (Sqcd style 1) using the LL entry: valid syntax, different step sizes."""
    qi, ql = _seg(data, 0xFF5C)
    sq = data[qi + 4]
    assert sq & 31 == 2, "needs an expounded-quantization stream"
    first = data[qi + 5:qi + 7]
    body = bytes([(sq & 0xE0) | 1]) + first
    return data[:qi] + struct.pack(">HH", 0xFF5C, 2 + len(body)) + body + data[qi + 2 + ql:]


def guard_bits(delta):
    def surgery(data):
        qi, ql = _seg(data, 0xFF5C)
        sq = data[qi + 4]
        g = ((sq >> 5) + delta) & 7
        return data[:qi + 4] + bytes([(g << 5) | (sq & 31)]) + data[qi + 5:]
    surgery.__name__ = f"guard_bits_{delta:+d}"
    return surgery


def packed(marker, kind):
    def surgery(data):
        import make_jpx_fixtures as old
        return old.pack_packet_headers(data, marker)
    surgery.__name__ = f"pack_packet_headers_{kind}"
    return surgery


def poc_whole(layers, res, comps):
    def surgery(data):
        import make_jpx_fixtures as old
        return old.add_poc(data, layers=layers, resolutions=res, components=comps)
    surgery.__name__ = "add_poc"
    return surgery


def main():
    for fn in os.listdir(OUT):
        os.remove(os.path.join(OUT, fn))
    out = run([COMPRESS, "-h"])[1]
    version = re.search(r"openjp2 library v([0-9.]+)", out).group(1)
    manifest["openjpeg"] = {"version": version, "source": "official release openjpeg-v2.5.4-windows-x64.zip (github.com/uclouvain/openjpeg)",
                            "opj_compress_sha256": hashlib.sha256(open(COMPRESS, "rb").read()).hexdigest(),
                            "opj_decompress_sha256": hashlib.sha256(open(DECOMPRESS, "rb").read()).hexdigest()}

    # progression orders x precincts x tiles x offsets x layers x transform
    for prog in ("LRCP", "RLCP", "RPCL", "PCRL", "CPRL"):
        for irrev in (False, True):
            args = ["-p", prog, "-n", "4", "-c", "[32,32],[16,16],[16,16],[8,8]", "-t", "40,32", "-d", "5,7", "-T", "3,4", "-r", "8,3,1" if irrev else "8,3,1"]
            if irrev:
                args += ["-I"]
            encode_case(f"opj-{prog.lower()}-{'97' if irrev else '53'}-prec-tiles-offsets-layers", 70, 50, args=args,
                        tol=1 if irrev else 0, lossless=False)
    # lossless layered with varying precinct sizes per resolution, odd tile grid
    for prog in ("LRCP", "RLCP", "RPCL", "PCRL", "CPRL"):
        encode_case(f"opj-{prog.lower()}-lossless-precincts", 67, 45,
                    args=["-p", prog, "-n", "5", "-c", "[64,64],[32,16],[16,32],[8,8]", "-t", "34,30", "-r", "20,5,1"], lossless=False)

    # degenerate geometry: a 1-pixel-wide tile column (67 = 33 + 33 + 1) with 5 decomposition levels. OpenJPEG 2.5.4 does
    # not even reproduce its own lossless input there, the x=66 column is exempt from comparison and all other samples are exact.
    encode_case("opj-degenerate-1px-tile-column", 67, 45, args=["-n", "5", "-t", "33,29"], tol=0, exempt_x=[66], lossless=False,
                note="Degenerate geometry: OpenJPEG 2.5.4 does not reproduce its own lossless input at x=66 (opjEqualsEncoderInput=false); the 1-pixel tile column (exemptColumnsX) is excluded from the comparison; every other sample must match opj_decompress exactly")

    # code-block styles (-M bit mask: 1 bypass, 2 reset, 4 termall, 8 vcausal, 16 pterm, 32 segsym)
    for mode in (1, 2, 4, 8, 16, 32, 3, 5, 9, 17, 33, 6, 12, 38, 13, 21, 63):
        encode_case(f"opj-cblk-style-{mode}", 64, 48, nc=1, args=["-M", str(mode), "-n", "4"], seed=mode)
        encode_case(f"opj-cblk-style-{mode}-lossy97-layers", 64, 48, nc=3, args=["-M", str(mode), "-I", "-r", "10,4,1", "-n", "4"], tol=1, lossless=False, seed=mode + 100)
    for cb in ("4,64", "64,4", "8,32", "16,16", "32,128", "128,32"):
        encode_case(f"opj-cblk-size-{cb.replace(',', 'x')}", 64, 48, nc=1, args=["-b", cb] if cb in ("4,64", "64,4", "8,32", "16,16") else ["-b", "32,32"])

    # packet markers and tile-parts
    for flag in (["-SOP"], ["-EPH"], ["-SOP", "-EPH"], ["-PLT"], ["-TLM"], ["-TLM", "-PLT", "-SOP", "-EPH"]):
        encode_case("opj-markers-" + "-".join(f.strip("-").lower() for f in flag), 70, 50, args=["-t", "40,32", "-n", "4", "-r", "6,2,1", *flag], lossless=False)
    for tp in "RLC":
        encode_case(f"opj-tileparts-{tp}", 70, 50, args=["-t", "40,32", "-n", "4", "-r", "6,2,1", "-TP", tp, "-TLM", "-PLT"], lossless=False)
        encode_case(f"opj-tileparts-{tp}-rpcl-sop-eph", 70, 50, args=["-t", "40,32", "-n", "4", "-p", "RPCL", "-TP", tp, "-SOP", "-EPH"])
    # POC written by opj_compress
    encode_case("opj-poc-two-ranges", 64, 48, args=["-n", "4", "-POC", "T0=0,0,1,2,3,RLCP/T0=2,0,1,4,3,CPRL"])
    encode_case("opj-poc-tiled", 70, 50, args=["-n", "4", "-t", "40,32", "-POC", "T0=0,0,1,2,3,RLCP/T0=2,0,1,4,3,CPRL/T1=0,0,1,4,3,LRCP"], lossless=False, expect="reject-policy",
                note="opj_compress keeps only the first tile POC (res 0-2 of 4): the stream omits packets. OpenJPEG decodes it leniently, so this is NOT an OpenJPEG-validated expectation: this decoder deliberately rejects POC ranges that leave packets uncovered (policy, no normative sentence is cited; tests/fixtures/jpx poc-malformed-* depend on it)")
    # ROI (max-shift upshift of a component via RGN)
    for up in (3, 9, 20):
        encode_case(f"opj-roi-c1-u{up}", 64, 48, args=["-ROI", f"c=1,U={up}", "-n", "4"])
    # transforms and MCT
    encode_case("opj-97-mct", 70, 50, args=["-I", "-r", "12"], tol=1, lossless=False)
    encode_case("opj-97-nomct-3comp", 70, 50, args=["-I", "-mct", "0", "-r", "12"], tol=1, lossless=False)
    encode_case("opj-53-nomct", 70, 50, args=["-mct", "0"])
    encode_case("opj-53-rct", 70, 50, args=["-mct", "1"])
    # comments, offsets without tiles
    encode_case("opj-image-offset-only", 50, 40, args=["-d", "13,11"])
    encode_case("opj-tile-offset-large", 50, 40, args=["-d", "20,25", "-T", "11,9", "-t", "17,17", "-n", "3"])
    encode_case("opj-one-resolution", 33, 21, args=["-n", "1"])
    encode_case("opj-max-resolutions", 130, 100, nc=1, args=["-n", "7"])

    # bit depths: unsigned/signed, lossless 5/3 and lossy 9/7
    for depth in (1, 2, 3, 4, 5, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16):
        for signed in (False, True):
            if depth == 1 and signed:
                continue
            encode_case(f"opj-depth{depth}{'s' if signed else 'u'}-lossless", 40, 24, nc=1, depth=depth, signed=signed, args=["-n", "4"], seed=depth)
    for depth in (10, 12, 16):
        for signed in (False, True):
            encode_case(f"opj-depth{depth}{'s' if signed else 'u'}-97", 40, 24, nc=1, depth=depth, signed=signed, args=["-I", "-r", "6", "-n", "4"],
                        tol=2 if depth == 16 else 1, lossless=False, seed=depth)
    encode_case("opj-depth12s-rgb-rct", 40, 24, nc=3, depth=12, signed=True, args=["-mct", "1", "-n", "4"])
    manifest["outOfScope"]["depth>16"] = "opj_compress RAW input is limited to 16 bits per sample; a >16-bit PGX writer would be needed (no OpenJPEG-produced >16-bit codestream is available here), so >16-bit handling is covered only by hand-built codestreams in JpxConformanceTests."

    # component subsampling (XRsiz/YRsiz) -- reference planes are the per-component native planes
    encode_case("opj-subsampled-420", 66, 46, nc=4, subs=[(1, 1), (2, 2), (2, 2), (1, 1)], args=["-mct", "0"])
    encode_case("opj-subsampled-422", 66, 46, nc=4, subs=[(1, 1), (2, 1), (2, 1), (1, 1)], args=["-mct", "0"])
    encode_case("opj-subsampled-3x2-odd", 67, 45, nc=4, subs=[(1, 1), (3, 2), (3, 2), (1, 1)], args=["-mct", "0", "-t", "30,28", "-n", "3"])
    encode_case("opj-subsampled-mixed-offsets", 67, 45, nc=3, subs=[(2, 1), (3, 3), (1, 5)], args=["-mct", "0", "-d", "5,3", "-T", "2,1", "-n", "3"], lossless=False)
    encode_case("opj-subsampled-255", 300, 300, nc=2, subs=[(1, 1), (255, 255)], args=["-mct", "0", "-n", "2"])
    encode_case("opj-subsampled-prog-rpcl", 66, 46, nc=4, subs=[(1, 1), (2, 2), (2, 1), (1, 1)], args=["-mct", "0", "-p", "RPCL", "-c", "[16,16],[8,8]", "-n", "3"])
    encode_case("opj-subsampled-prog-pcrl", 66, 46, nc=4, subs=[(1, 1), (2, 2), (2, 1), (1, 1)], args=["-mct", "0", "-p", "PCRL", "-c", "[16,16],[8,8]", "-n", "3"])
    encode_case("opj-subsampled-prog-cprl", 66, 46, nc=4, subs=[(1, 1), (2, 2), (2, 1), (1, 1)], args=["-mct", "0", "-p", "CPRL", "-c", "[16,16],[8,8]", "-n", "3"])

    # many components (16-bit COC/QCC/RGN component indices from Csiz >= 257)
    encode_case("opj-comps-300", 8, 6, nc=300, args=["-mct", "0", "-n", "2"])
    encode_case("opj-comps-16384", 4, 4, nc=16384, args=["-mct", "0", "-n", "1"])

    # marker surgery on OpenJPEG codestreams; references again from opj_decompress
    encode_case("opj-surgery-coc-qcc-identity", 64, 48, args=["-n", "4"], surgery=add_coc_qcc_identity, note="COC/QCC repeat COD/QCD for every component")
    encode_case("opj-surgery-coc-qcc-identity-97", 64, 48, args=["-I", "-r", "10"], surgery=add_coc_qcc_identity, tol=1, lossless=False)
    encode_case("opj-surgery-coc-qcc-identity-300comps", 8, 6, nc=300, args=["-mct", "0", "-n", "2"], surgery=add_coc_qcc_identity)
    encode_case("opj-surgery-crg", 64, 48, nc=4, subs=[(1, 1), (2, 2), (2, 2), (1, 1)], args=["-mct", "0", "-n", "3"], surgery=add_crg)
    encode_case("opj-surgery-derived-quant", 64, 48, args=["-I", "-r", "10", "-n", "3"], surgery=derived_quant, tol=1, lossless=False,
                note="syntactically valid scalar-derived QCD over a stream coded with expounded steps (step sizes and Mb change); output is whatever OpenJPEG decodes")
    for d in (1, -1):
        encode_case(f"opj-surgery-guard-bits{d:+d}", 64, 48, args=["-n", "3"], surgery=guard_bits(d), lossless=False,
                    note="guard-bit field rewritten after encoding (changes Mb); output is whatever OpenJPEG decodes")
    encode_case("opj-surgery-poc-whole-image", 64, 48, args=["-n", "3", "-r", "6,2,1", "-PLT"], surgery=poc_whole(3, 3, 3), lossless=False)

    manifest["outOfScope"].update({
        "PPM/PPT single-tile via surgery": "make_jpx_fixtures.pack_packet_headers only handles one-code-block packets, so opj_compress streams cannot be repackaged.",
        "PLM": "no OpenJPEG encoder option; decoders ignore it. Covered by tests/fixtures/jpx (see make_jpx_fixtures.py).",
        "PPM/PPT multi-tile/multi-part": "covered by tests/fixtures/jpx fixtures (decoded by OpenJPEG through Pillow); only the single-tile forms are regenerated here.",
        "fewer layers/resolutions decode": "the decoder API always decodes every layer/resolution (PDF requires it); opj_decompress -l/-r references would have no counterpart.",
        "JPX Part 2 extensions": "must fail gracefully; no OpenJPEG encoder exists. Out of scope for generated fixtures.",
        "scalar-derived/guard-bits as encoder output": "opj_compress always writes none (5/3) or expounded (9/7) with 2 guard bits; the derived/guard variants are marker surgery whose pixels are still decoded by opj_decompress.",
    })
    json.dump(manifest, open(os.path.join(OUT, "manifest.json"), "w"), indent=1)
    


if __name__ == "__main__":
    main()
