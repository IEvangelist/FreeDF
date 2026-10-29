from theme import *
from PIL import ImageCms

WP = "/WhitePoint [0.9505 1 1.089]"


def ramp(cs, c0, c1, x, w, extra=""):
    return (f"<< /ShadingType 2 /ColorSpace {cs} /Coords [{n(x)} 0 {n(x + w)} 0] "
            f"/Function {fn_exp(c0, c1)} /Extend [true true] {extra} >>")


def build(path):
    d = Doc("Colour spaces", subject="DeviceGray/RGB/CMYK, CalGray, CalRGB, Lab, ICCBased, Indexed, Separation, DeviceN", lang="en-US")
    t = Type(d)
    pg = d.page(*A4)
    background(pg, P["white"])
    page_header(pg, t, "Colour spaces", "Colour  /  01", 1, 1)
    pg.text(t.sans, 11, M, pg.h - 106, "Every row paints through a different PDF colour space; the reader converts to its output space.", color=P["slate"])

    icc = ImageCms.ImageCmsProfile(ImageCms.createProfile("sRGB")).tobytes()
    icc_ref = d.stream("/N 3 /Alternate /DeviceRGB", icc)
    spot = d.add("[/Separation /BrandIndigo /DeviceCMYK " + fn_exp((0, 0, 0, 0), (0.82, 0.78, 0, 0.08)) + "]")
    gold = d.add("[/Separation /Gold /DeviceRGB " + fn_exp((1, 1, 1), (0.85, 0.65, 0.13)) + "]")
    dn_fn = d.stream("/FunctionType 4 /Domain [0 1 0 1] /Range [0 1 0 1 0 1]",
                     b"{ 2 copy 0.04 mul exch 0.95 mul add 1 exch sub 3 1 roll 2 copy 0.38 mul exch 0.42 mul add 1 exch sub 3 1 roll 0.96 mul exch 0.47 mul add 1 exch sub }", compress=False)
    devn = d.add(f"[/DeviceN [/Teal /Amber] /DeviceRGB {dn_fn} 0 R]")
    pal = [P["ink"], P["indigo"], P["violet"], P["sky"], P["teal"], P["emerald"], P["amber"], P["rose"]]
    pal_hex = "".join(c[1:] for c in pal)
    cal_rgb = f"[/CalRGB << {WP} /Gamma [2.2 2.2 2.2] /Matrix [0.4124 0.2126 0.0193 0.3576 0.7152 0.1192 0.1805 0.0722 0.9505] >>]"
    cal_gray = f"[/CalGray << {WP} /Gamma 2.2 >>]"
    lab = f"[/Lab << {WP} /Range [-128 127 -128 127] >>]"

    bx, bw, bh = M + 190, pg.w - 2 * M - 190, 24
    y = pg.h - 150
    rows = [
        ("DeviceGray", "/G", "0 g and 1 g ramp"),
        ("DeviceRGB", "/RGB", "additive; sRGB-like"),
        ("DeviceCMYK", "/CMYK", "C, M, Y, K ramps"),
        ("CalGray", "/CAL", "gamma 2.2, D65 white"),
        ("CalRGB", "/CAL", "colorimetric RGB"),
        ("Lab", "/CAL", "L* 55, a* from -90 to +90"),
        ("ICCBased", "/ICC", "embedded sRGB profile, N = 3"),
        ("Indexed", "/IDX", "8-entry palette"),
        ("Separation", "/SEP", "spot colour BrandIndigo, alt. CMYK"),
        ("Separation", "/SEP2", "spot colour Gold, alt. RGB"),
        ("DeviceN", "/DN", "two colorants, calculator tint transform"),
    ]
    for i, (name, kind, desc) in enumerate(rows):
        yy = y - i * 50
        pg.text(t.sans_b, 10, M, yy + 9, name, color=P["ink"])
        pg.text(t.sans, 8, M, yy - 3, desc, color=P["muted"], kern=False)
        pg.q(); pg.rrect(bx, yy - 4, bw, bh + 4 if False else bh, 4); pg.clip()
        if name == "DeviceGray":
            pg.sh(ramp("/DeviceGray", [0], [1], bx, bw))
        elif name == "DeviceRGB":
            pg.sh(axial(bx, 0, bx + bw, 0, [(0, "#FF0000"), (0.2, "#FFFF00"), (0.4, "#00FF00"), (0.6, "#00FFFF"), (0.8, "#0000FF"), (1, "#FF00FF")]))
        elif name == "DeviceCMYK":
            for k, c1 in enumerate(((1, 0, 0, 0), (0, 1, 0, 0), (0, 0, 1, 0), (0, 0, 0, 1))):
                pg.q(); pg.rect(bx + k * bw / 4, yy - 6, bw / 4, bh + 8); pg.clip()
                pg.sh(ramp("/DeviceCMYK", (0, 0, 0, 0), c1, bx + k * bw / 4, bw / 4))
                pg.Q()
        elif name == "CalGray":
            pg.sh(ramp(cal_gray, [0], [1], bx, bw))
        elif name == "CalRGB":
            pg.sh(axial(bx, 0, bx + bw, 0, [(0, "#102040"), (0.5, "#20A0E0"), (1, "#F0FFB0")], cs=cal_rgb))
        elif name == "Lab":
            pg.sh(ramp(lab, (55, -90, 60), (55, 90, -60), bx, bw))
        elif name == "ICCBased":
            pg.sh(axial(bx, 0, bx + bw, 0, [(0, P["indigo_d"]), (0.5, P["violet"]), (1, P["amber"])], cs=f"[/ICCBased {icc_ref} 0 R]"))
        elif name == "Indexed":
            idx = f"[/Indexed /DeviceRGB 7 <{pal_hex}>]"
            for k in range(8):
                pg.out.append(f"/{pg.reg('ColorSpace', idx)} cs {k} sc")
                pg.rect(bx + k * bw / 8, yy - 6, bw / 8 + 0.5, bh + 8); pg.f()
        elif kind == "/SEP":
            pg.sh(ramp(f"{spot} 0 R", [0], [1], bx, bw))
        elif kind == "/SEP2":
            pg.sh(ramp(f"{gold} 0 R", [0], [1], bx, bw))
        else:
            cs = pg.reg("ColorSpace", f"{devn} 0 R")
            for a in range(8):
                for b_ in range(2):
                    pg.out.append(f"/{cs} cs {n(a / 7)} {n(b_)} sc")
                    pg.rect(bx + a * bw / 8, yy - 6 + b_ * (bh + 8) / 2, bw / 8 + 0.5, (bh + 8) / 2 + 0.5); pg.f()
        pg.Q()
        box(pg, bx, yy - 4, bw, bh, stroke=P["mist"], r=4)

    # spot colour tint swatches
    sy = y - 11 * 50 - 6
    eyebrow(pg, t, M, sy, "Spot colour tints (scn on a Separation space)")
    cs = pg.reg("ColorSpace", f"{spot} 0 R")
    for k, tint in enumerate((1, 0.75, 0.5, 0.25, 0.1)):
        x = M + k * 100
        pg.q(); pg.out.append(f"/{cs} cs {n(tint)} scn"); pg.rrect(x, sy - 52, 88, 36, 6); pg.f(); pg.Q()
        pg.text(t.sans, 8, x + 4, sy - 64, f"{int(tint * 100)}% BrandIndigo", color=P["slate"], kern=False)
    d.save(path)


OUT = "08-colour-spaces.pdf"
