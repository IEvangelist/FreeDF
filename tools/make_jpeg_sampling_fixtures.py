"""Sampling-factor / scan-structure matrix encoded by libjpeg-turbo (imagecodecs, version printed) with its own non-fancy-upsampling
decode as the bit-exact reference (our decoder replicates chroma like libjpeg's "simple" upsampler and ports jidctint).
Writes tests/fixtures/jpeg-sampling/<name>.jpg, <name>.ref (raw 8-bit interleaved) and <name>.meta ("w h components").
Run: python tools/make_jpeg_sampling_fixtures.py
"""
import os
import sys
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import imagecodecs  # noqa: E402
from make_jpx_fixtures import picture  # noqa: E402

out = os.path.join(os.path.dirname(__file__), "..", "tests", "fixtures", "jpeg-sampling")
os.makedirs(out, exist_ok=True)
print("libjpeg-turbo", imagecodecs.jpeg8_version())

count = 0
for (w, h) in [(64, 48), (37, 53), (17, 9), (1, 1), (8, 8), (9, 1), (200, 7)]:
    rgb = np.asarray(picture(max(w, 8), max(h, 8), 3))[:h, :w].copy()
    gray = np.asarray(picture(max(w, 8), max(h, 8), 3).convert("L"))[:h, :w].copy()
    for sub in ["444", "422", "420", "440", "411", "410", "gray"]:
        arr = gray if sub == "gray" else rgb
        for prog_opt in (False, True):
            if sub == "gray" and w != 37:
                continue
            try:
                kw = dict(level=90, optimize=prog_opt)
                if sub != "gray":
                    kw.update(subsampling=sub, colorspace="rgb", outcolorspace="ycbcr")
                data = imagecodecs.jpeg8_encode(arr, **kw)
                ref = imagecodecs.jpeg8_decode(data, fancyupsampling=False)
            except Exception as ex:
                print("skip", w, h, sub, prog_opt, str(ex).splitlines()[0][:70])
                continue
            name = f"{sub}-{w}x{h}-{'opt' if prog_opt else 'std'}"
            open(os.path.join(out, name + ".jpg"), "wb").write(bytes(data))
            open(os.path.join(out, name + ".ref"), "wb").write(np.ascontiguousarray(ref).tobytes())
            open(os.path.join(out, name + ".meta"), "w").write(f"{w} {h} {1 if sub == 'gray' else 3}")
            count += 1
print("wrote", count)

# Progressive and restart-interval variants written by Pillow (libjpeg-turbo), decoded by imagecodecs as the reference.
import io  # noqa: E402
from PIL import Image  # noqa: E402

for (w, h) in [(64, 48), (37, 53), (17, 9), (9, 1)]:
    img = picture(max(w, 8), max(h, 8), 3).crop((0, 0, w, h))
    for ss in (0, 1, 2):
        for tag, extra in (("prog", dict(progressive=True)), ("progrst", dict(progressive=True, restart_marker_blocks=3)),
                           ("seqrst", dict(restart_marker_rows=1))):
            b = io.BytesIO()
            img.save(b, format="JPEG", quality=85, subsampling=ss, **extra)
            data = b.getvalue()
            ref = imagecodecs.jpeg8_decode(data, fancyupsampling=False)
            name = f"{tag}-ss{ss}-{w}x{h}"
            open(os.path.join(out, name + ".jpg"), "wb").write(data)
            open(os.path.join(out, name + ".ref"), "wb").write(np.ascontiguousarray(ref).tobytes())
            open(os.path.join(out, name + ".meta"), "w").write(f"{w} {h} 3")
            count += 1
print("total", count)
