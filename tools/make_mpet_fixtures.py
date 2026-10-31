# Builds hand-assembled ICC v4.3 profiles using D2Bx multiProcessElements tags (self-derived fixtures, not from a vendor) and\n# prints Little CMS 2.17 (Pillow ImageCms, NOOPTIMIZE) reference outputs used by IccMultiProcessTests.
import struct, io, base64, itertools
from PIL import Image, ImageCms

def s15(v): return struct.pack('>i', int(round(v * 65536)))
def f32(v): return struct.pack('>f', v)
def sig(s): return s.encode('ascii')
def pad4(b): return b + b'\0' * (-len(b) % 4)

def parf0(g, a, b, c): return sig('parf') + b'\0' * 4 + struct.pack('>HH', 0, 0) + f32(g) + f32(a) + f32(b) + f32(c)
def samf(vals): return sig('samf') + b'\0' * 4 + struct.pack('>I', len(vals)) + b''.join(f32(v) for v in vals)
def curf(breaks, segs):
    return pad4(sig('curf') + b'\0' * 4 + struct.pack('>HH', len(segs), 0) + b''.join(f32(x) for x in breaks) + b''.join(segs))

def element(kind, nin, nout, body):
    return sig(kind) + b'\0' * 4 + struct.pack('>HH', nin, nout) + body

def cvst(curves):
    n = len(curves)
    head = 12 + 8 * n
    pos = []; blob = b''
    for c in curves:
        pos.append((head + len(blob), len(c))); blob += c
    return element('cvst', n, n, b''.join(struct.pack('>II', o, s) for o, s in pos) + blob)

def matf(nin, nout, m, off):
    return element('matf', nin, nout, b''.join(f32(v) for v in m) + b''.join(f32(v) for v in off))

def clut(grid, nout, data):
    g = bytes(grid) + b'\0' * (16 - len(grid))
    return element('clut', len(grid), nout, g + b''.join(f32(v) for v in data))

def mpet(nin, nout, elems):
    elems = [pad4(e) for e in elems]
    head = 16 + 8 * len(elems)
    pos = []; blob = b''
    for e in elems:
        pos.append((head + len(blob), len(e))); blob += e
    return sig('mpet') + b'\0' * 4 + struct.pack('>HHI', nin, nout, len(elems)) + b''.join(struct.pack('>II', o, s) for o, s in pos) + blob

def profile(space, pcs, cls, tags):
    table = b''; blob = b''
    base = 128 + 4 + 12 * len(tags)
    for name, data in tags:
        data = pad4(data)
        table += sig(name) + struct.pack('>II', base + len(blob), len(data)); blob += data
    size = base + len(blob)
    hdr = struct.pack('>IIIII', size, 0, 0x04300000, int.from_bytes(sig(cls), 'big'), int.from_bytes(sig(space), 'big'))
    hdr += int.from_bytes(sig(pcs), 'big').to_bytes(4, 'big') + b'\0' * 12 + sig('acsp') + b'\0' * 4 * 4 + b'\0' * 8 + struct.pack('>I', 0)
    hdr += s15(0.9642) + s15(1) + s15(0.8249) + b'\0' * 4 + b'\0' * 16 + b'\0' * 28
    assert len(hdr) == 128, len(hdr)
    return hdr + struct.pack('>I', len(tags)) + table + blob

def xyz(x, y, z): return sig('XYZ ') + b'\0' * 4 + s15(x) + s15(y) + s15(z)

# Profile A: RGB -> XYZ, sRGB-like two-segment parametric TRC then colorant matrix.
srgb_curve = curf([0.04045], [parf0(1, 1 / 12.92, 0, 0), parf0(2.4, 1 / 1.055, 0.055 / 1.055, 0)])
M = [0.4360747, 0.3850649, 0.1430804, 0.2225045, 0.7168786, 0.0606169, 0.0139322, 0.0971045, 0.7141733]
pipeA = mpet(3, 3, [cvst([srgb_curve] * 3), matf(3, 3, M, [0, 0, 0])])
pa = profile('RGB ', 'XYZ ', 'mntr', [('wtpt', xyz(0.9642, 1, 0.8249)), ('D2B0', pipeA), ('D2B1', pipeA), ('D2B2', pipeA)])

# Profile B: CMYK -> Lab, K curve has a middle sampled segment, then a 3^4 float CLUT with affine Lab values.
ident = curf([], [parf0(1, 1, 0, 0)])
kcurve = curf([0.25, 0.75], [parf0(1, 1, 0, 0), samf([0.5, 0.75]), parf0(1, 1, 0, 0)])  # implicit first sample = 0.25
# sampled segment: points 0.25(implicit), 0.5->? values at 0.25..0.75: implicit start 0.25, then 0.5, 0.75 => identity
grid = 3
data = []
for c, m, y, k in itertools.product(range(grid), repeat=4):
    c, m, y, k = [v / (grid - 1) for v in (c, m, y, k)]
    L = 100 * (1 - 0.6 * k) * (1 - 0.25 * (c + m + y) / 3 * 2)
    a = 60 * (m - c)
    b = 60 * (y - 0.5 * c)
    data += [L, a, b]
pipeB = mpet(4, 3, [cvst([ident, ident, ident, kcurve]), clut([grid] * 4, 3, data)])
pb = profile('CMYK', 'Lab ', 'prtr', [('wtpt', xyz(0.9642, 1, 0.8249)), ('D2B0', pipeB), ('D2B1', pipeB), ('D2B2', pipeB)])

import os
fx = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'tests', 'fixtures', 'icc')
os.makedirs(fx, exist_ok=True)
open(os.path.join(fx, 'mpet-rgb-d2b.icc'), 'wb').write(pa)
open(os.path.join(fx, 'mpet-cmyk-d2b.icc'), 'wb').write(pb)
srgb = ImageCms.createProfile('sRGB')
out = {}
for name, pr, mode, pts in [
    ('A', pa, 'RGB', [(0, 0, 0), (255, 255, 255), (128, 128, 128), (64, 128, 192), (255, 0, 0), (10, 200, 40), (12, 12, 12)]),
    ('B', pb, 'CMYK', [(0, 0, 0, 0), (0, 0, 0, 255), (255, 0, 0, 0), (0, 255, 0, 0), (0, 0, 255, 0), (51, 102, 153, 26), (128, 64, 32, 200)]),
]:
    p = ImageCms.ImageCmsProfile(io.BytesIO(pr))
    for intent in (0, 1):
        t = ImageCms.buildTransform(p, srgb, mode, 'RGB', renderingIntent=intent, flags=0x100)
        im = Image.new(mode, (len(pts), 1))
        for i, v in enumerate(pts): im.putpixel((i, 0), v)
        res = ImageCms.applyTransform(im, t)
        print(name, intent, [tuple(res.getpixel((i, 0))) for i in range(len(pts))])
