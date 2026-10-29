import zlib
import numpy as np
from PIL import Image, ImageDraw
from theme import *
import imgs


def build(path):
    d = Doc("Images", subject="DCT, Flate+predictor, grayscale, indexed, image masks, colour-key, SMask, Decode, inline images, CMYK JPEG", lang="en-US")
    t = Type(d)
    pg = d.page(*A4)
    background(pg, P["paper"])
    page_header(pg, t, "Image gallery", "Images  /  01", 1, 1)
    pg.text(t.sans, 11, M, pg.h - 106, "Twelve image XObjects (plus one inline image) exercising filters, colour spaces and masking.", color=P["slate"])

    def image(w, h, cs, data, extra="", bpc=8, filt="/FlateDecode", compress=False):
        dd = f"/Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace {cs} /BitsPerComponent {bpc} {extra}"
        if filt:
            dd += f" /Filter {filt}"
        return d.stream(dd, data, compress=compress)

    def flate_rgb(im, extra=""):
        return image(im.width, im.height, "/DeviceRGB", zlib.compress(im.tobytes(), 9), extra)

    W, H = 300, 200
    land = imgs.landscape(360, 240)
    plas = imgs.plasma(W, H, 7)
    objs = []
    # 1 JPEG
    objs.append(("DCTDecode", "baseline JPEG, passed through", image(360, 240, "/DeviceRGB", imgs.jpeg_bytes(land, 80), filt="/DCTDecode")))
    # 2 flate
    objs.append(("FlateDecode RGB", "8-bit RGB, zlib", flate_rgb(plas)))
    # 3 predictor
    arr = np.asarray(plas)
    objs.append(("Flate + PNG predictor", "/Predictor 15 (Up filter)",
                 image(W, H, "/DeviceRGB", zlib.compress(imgs.png_predict_up(arr), 9), f"/DecodeParms << /Predictor 15 /Colors 3 /BitsPerComponent 8 /Columns {W} >>")))
    # 4 gray
    g = plas.convert("L")
    objs.append(("DeviceGray", "8-bit, 1 component", image(W, H, "/DeviceGray", zlib.compress(g.tobytes(), 9))))
    # 5 indexed 4-bit
    q = plas.quantize(16, method=Image.Quantize.MEDIANCUT)
    pal = q.getpalette()[:48]
    pal += [0] * (48 - len(pal))
    qa = np.asarray(q, dtype=np.uint8)
    if qa.shape[1] % 2:
        qa = np.pad(qa, ((0, 0), (0, 1)))
    packed = ((qa[:, 0::2] << 4) | qa[:, 1::2]).astype(np.uint8)
    objs.append(("Indexed, 4 bpc", "16-colour palette, packed nibbles",
                 image(W, H, f"[/Indexed /DeviceRGB 15 <{bytes(pal).hex()}>]", zlib.compress(packed.tobytes(), 9), bpc=4)))
    # 6 stencil mask (ImageMask)
    sm = Image.new("1", (200, 200), 1)
    dr = ImageDraw.Draw(sm)
    dr.ellipse((10, 10, 190, 190), fill=0)
    dr.ellipse((60, 60, 140, 140), fill=1)
    for k in range(8):
        a = k * math.pi / 4
        dr.line((100 + 30 * math.cos(a), 100 + 30 * math.sin(a), 100 + 70 * math.cos(a), 100 + 70 * math.sin(a)), fill=1, width=7)
    stencil = d.stream("/Type /XObject /Subtype /Image /Width 200 /Height 200 /ImageMask true /BitsPerComponent 1 /Filter /FlateDecode",
                       zlib.compress(np.packbits(np.asarray(sm, dtype=np.uint8), axis=1).tobytes(), 9), compress=False)
    objs.append(("Stencil ImageMask", "1-bit, painted in the fill colour", stencil))
    # 7 explicit mask
    cm = Image.new("1", (W, H), 1)
    ImageDraw.Draw(cm).rounded_rectangle((20, 20, W - 20, H - 20), 60, fill=0)
    emask = d.stream(f"/Type /XObject /Subtype /Image /Width {W} /Height {H} /ImageMask true /BitsPerComponent 1 /Filter /FlateDecode",
                     zlib.compress(np.packbits(np.asarray(cm, dtype=np.uint8) ^ 1, axis=1).tobytes(), 9), compress=False)
    objs.append(("Explicit /Mask", "1-bit stencil clips a photograph", flate_rgb(plas, f"/Mask {emask} 0 R")))
    # 8 colour key
    ck = Image.new("RGB", (W, H), (0, 255, 0))
    dk = ImageDraw.Draw(ck)
    for i in range(6):
        dk.ellipse((20 + i * 40, 40 + (i % 2) * 40, 100 + i * 40, 120 + (i % 2) * 40), fill=[(79, 70, 229), (14, 165, 233), (245, 158, 11), (225, 29, 72), (16, 185, 129), (124, 58, 237)][i])
    objs.append(("Colour-key mask", "/Mask [0 0 255 255 0 0] removes pure green", flate_rgb(ck, "/Mask [0 0 255 255 0 0]")))
    # 9 smask
    yy, xx = np.mgrid[0:H, 0:W]
    al = np.clip(1 - np.hypot((xx - W / 2) / (W / 2), (yy - H / 2) / (H / 2)), 0, 1) ** 0.7
    smask = image(W, H, "/DeviceGray", zlib.compress((al * 255).astype(np.uint8).tobytes(), 9), "")
    objs.append(("SMask", "8-bit soft alpha channel", flate_rgb(plas, f"/SMask {smask} 0 R")))
    # 10 decode
    objs.append(("Decode [1 0 1 0 1 0]", "inverts every component", flate_rgb(land.resize((W, H)), "/Decode [1 0 1 0 1 0]")))
    # 11 inline image + interpolate placeholder (object image 16x16 interpolated)
    px = np.zeros((16, 16, 3), np.uint8)
    for j in range(16):
        for i in range(16):
            px[j, i] = (int(79 + (i / 15) * 150), int(70 + (j / 15) * 120), 229 - int((i + j) / 30 * 200))
            if (i // 4 + j // 4) % 2:
                px[j, i] = px[j, i] // 2 + 100
    small = Image.fromarray(px)
    objs.append(("Interpolate true", "16 x 16 px, smoothed on upscale", flate_rgb(small, "/Interpolate true")))
    # 12 CMYK JPEG
    cmyk = plas.convert("CMYK")
    objs.append(("CMYK JPEG", "DeviceCMYK + /Decode for Adobe inversion",
                 image(W, H, "/DeviceCMYK", imgs.jpeg_bytes(cmyk, 85), "/Decode [1 0 1 0 1 0 1 0]", filt="/DCTDecode")))

    cols, gap = 3, 16
    tw = (pg.w - 2 * M - 2 * gap) / 3
    ih = 100
    top = pg.h - 132
    for i, (title, sub, ref) in enumerate(objs):
        c, r = i % cols, i // cols
        x, y = M + c * (tw + gap), top - ih - r * (ih + 44)
        # checkerboard backdrop for transparent images
        box(pg, x, y, tw, ih, fill=P["white"], stroke=P["mist"], r=6)
        pg.q()
        if title == "Stencil ImageMask":
            pg.fill(P["rose"]); pg.cm(ih - 12, 0, 0, ih - 12, x + (tw - ih + 12) / 2, y + 6); pg.xobj(ref)
        else:
            pad = 6
            pg.cm(tw - 2 * pad, 0, 0, ih - 2 * pad, x + pad, y + pad); pg.xobj(ref)
        pg.Q()
        pg.text(t.sans_b, 8.5, x, y - 13, title, color=P["ink"])
        pg.text(t.sans, 7.5, x, y - 23, sub, color=P["muted"], kern=False)

    # inline image row (below grid)
    iy = top - 4 * (ih + 44) - 6
    pg.text(t.sans_b, 8.5, M, iy + 14, "Inline image (BI / ID / EI)", color=P["ink"])
    hexdata = "".join(f"{v:02X}" for v in px.reshape(-1)[:16 * 16 * 3])
    pg.q(); pg.cm(64, 0, 0, 64, M, iy - 54)
    pg.raw(f"BI /W 16 /H 16 /CS /RGB /BPC 8 /F /AHx ID\n{hexdata}>\nEI")
    pg.Q()
    pg.q(); pg.cm(64, 0, 0, 64, M + 80, iy - 54)
    pg.raw(f"BI /W 16 /H 16 /CS /RGB /BPC 8 /I true /F /AHx ID\n{hexdata}>\nEI")
    pg.Q()
    pg.text(t.sans, 7.5, M, iy - 66, "nearest-neighbour", color=P["muted"], kern=False)
    pg.text(t.sans, 7.5, M + 80, iy - 66, "/I true", color=P["muted"], kern=False)
    d.save(path)


OUT = "09-images.pdf"
