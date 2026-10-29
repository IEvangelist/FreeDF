"""Procedural, copyright-free raster content for the image sample."""
import io
import math
import numpy as np
from PIL import Image


def plasma(w, h, seed=1, pal=None):
    rng = np.random.default_rng(seed)
    y, x = np.mgrid[0:h, 0:w].astype(np.float32)
    v = np.zeros((h, w), np.float32)
    for _ in range(6):
        fx, fy, ph = rng.uniform(0.01, 0.06), rng.uniform(0.01, 0.06), rng.uniform(0, 6.28)
        v += np.sin(x * fx * 2 + y * fy * 2 + ph + np.sin(y * fy * 3 + ph))
    v = (v - v.min()) / (v.max() - v.min())
    pal = pal or [(15, 23, 42), (79, 70, 229), (14, 165, 233), (245, 158, 11), (255, 247, 237)]
    stops = np.linspace(0, 1, len(pal))
    out = np.stack([np.interp(v, stops, [p[c] for p in pal]) for c in range(3)], -1)
    return Image.fromarray(out.astype(np.uint8), "RGB")


def landscape(w, h):
    """Sunset over layered hills: gradient sky, sun, ridges, with film grain (compresses like a photograph)."""
    y = np.linspace(0, 1, h)[:, None]
    sky0, sky1 = np.array([30, 27, 75]), np.array([251, 146, 60])
    img = (sky0 * (1 - y**0.8)[..., None] + sky1 * (y**0.8)[..., None]) * np.ones((h, w, 1))
    yy, xx = np.mgrid[0:h, 0:w]
    sun = np.exp(-(((xx - w * 0.68) ** 2 + (yy - h * 0.62) ** 2) / (2 * (h * 0.09) ** 2)))
    img += sun[..., None] * np.array([255, 220, 140]) * 0.9
    for i, col in enumerate(((120, 53, 92), (76, 29, 79), (40, 20, 60), (15, 15, 35))):
        base = h * (0.55 + i * 0.1)
        ridge = base + np.sin(xx[0] * (0.012 + i * 0.006) + i) * h * 0.05 + np.sin(xx[0] * 0.04 * (i + 1)) * h * 0.012
        mask = yy >= ridge[None, :]
        img[mask] = col
    img += np.random.default_rng(3).normal(0, 4, img.shape)
    return Image.fromarray(np.clip(img, 0, 255).astype(np.uint8), "RGB")


def png_predict_up(arr):
    """PNG 'Up' filtered rows (predictor 15) for a (h, w, c) uint8 array."""
    h, w, c = arr.shape
    rows = arr.reshape(h, w * c).astype(np.int16)
    out = bytearray()
    prev = np.zeros(w * c, np.int16)
    for r in rows:
        out.append(2)
        out += ((r - prev) & 255).astype(np.uint8).tobytes()
        prev = r
    return bytes(out)


def jpeg_bytes(im, q=82):
    b = io.BytesIO()
    im.save(b, "JPEG", quality=q, optimize=True)
    return b.getvalue()
