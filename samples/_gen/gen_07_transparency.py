from theme import *

MODES = ["Normal", "Multiply", "Screen", "Overlay", "Darken", "Lighten", "ColorDodge", "ColorBurn", "HardLight", "SoftLight",
         "Difference", "Exclusion", "Hue", "Saturation", "Color", "Luminosity"]


def build(path):
    d = Doc("Transparency", subject="Constant alpha, 16 blend modes, luminosity/alpha soft masks, isolated and knockout groups", lang="en-US")
    t = Type(d)
    pg = d.page(*A4)
    background(pg, P["white"])
    page_header(pg, t, "Transparency", "Graphics  /  03", 1, 1)
    pg.text(t.sans, 11, M, pg.h - 106, "PDF 1.4 transparency model: alpha, blend modes, soft masks and groups, composited over a checkerboard.", color=P["slate"])

    chk = XForm(d, (0, 0, 16, 16))
    chk.fill("#F1F5F9"); chk.rect(0, 0, 16, 16); chk.f()
    chk.fill("#E2E8F0"); chk.rect(0, 0, 8, 8); chk.rect(8, 8, 8, 8); chk.f()
    chk_ref = chk.build("pattern", 16, 16)

    def checker(x, y, w, h):
        pg.q(); pg.pattern_fill(chk_ref); pg.rect(x, y, w, h); pg.f(); pg.Q()

    # constant alpha
    x0, y0 = M, pg.h - 280
    eyebrow(pg, t, x0, y0 + 150, "Constant alpha (ca / CA)")
    checker(x0, y0, 220, 140)
    for i, (cx, col) in enumerate(((70, P["rose"]), (110, P["amber"]), (150, P["sky"]))):
        pg.q(); pg.gs(ca=0.6, CA=0.9); pg.fill(col); pg.stroke(P["ink"]); pg.width(2)
        pg.circle(x0 + cx, y0 + 80 - (i == 1) * 0 + (i % 2) * -20, 46); pg.B(); pg.Q()

    # group: isolated + knockout vs separate
    gx = M + 250
    eyebrow(pg, t, gx, y0 + 150, "Group composited as one object")
    checker(gx, y0, 220, 140)
    for k, (kind, ox) in enumerate((("separate", 0), ("group", 110))):
        pg.q(); pg.translate(gx + ox, y0)
        if kind == "separate":
            pg.gs(ca=0.55)
            for cx, cy, col in ((34, 85, P["indigo"]), (62, 85, P["teal"]), (48, 56, P["rose"])):
                pg.fill(col); pg.circle(cx, cy, 30); pg.f()
        else:
            g = XForm(d, (0, 0, 110, 140), group="<< /S /Transparency /I true /K true >>")
            for cx, cy, col in ((34, 85, P["indigo"]), (62, 85, P["teal"]), (48, 56, P["rose"])):
                g.fill(col); g.circle(cx, cy, 30); g.f()
            pg.gs(ca=0.55); pg.xobj(g.build())
        pg.Q()
        pg.text(t.sans, 8, gx + ox + 14, y0 - 12, "per-object ca 0.55" if kind == "separate" else "isolated knockout group, ca 0.55", color=P["slate"], kern=False)

    # blend modes
    by = pg.h - 330
    eyebrow(pg, t, M, by, "Blend modes (BM)")
    cols, tw, th = 4, (pg.w - 2 * M - 3 * 12) / 4, 50
    for i, mname in enumerate(MODES):
        c, r = i % cols, i // cols
        x = M + c * (tw + 12)
        y = by - 20 - th - r * (th + 22)
        pg.q(); pg.rrect(x, y, tw, th, 5); pg.clip()
        pg.sh(axial(x, y, x + tw, y + th, [(0, P["teal"]), (0.5, P["amber"]), (1, P["rose"])]))
        pg.gs(BM=f"/{mname}")
        pg.fill(P["indigo"]); pg.circle(x + tw * 0.35, y + th / 2, 21); pg.f()
        pg.fill("#C4B5FD"); pg.rect(x + tw * 0.55, y - 2, 34, th + 4); pg.f()
        pg.Q()
        pg.text(t.sans, 8, x, y - 10, mname, color=P["ink"], kern=False)

    # soft masks
    sy = 118
    eyebrow(pg, t, M, sy + 66, "Soft masks (SMask)")
    lum = XForm(d, (0, 0, 150, 60), group="<< /S /Transparency /CS /DeviceGray >>")
    lum.sh(radial(75, 30, 0, 75, 30, 72, [(0, "#FFFFFF"), (0.55, "#FFFFFF"), (1, "#000000")], extend=False, cs="/DeviceRGB"))
    lref = lum.build()
    alp = XForm(d, (0, 0, 150, 60), group="<< /S /Transparency >>")
    alp.gs(ca=1)
    for i in range(15):
        alp.q(); alp.gs(ca=(i + 1) / 15); alp.fill("#000000"); alp.rect(i * 10, 0, 10, 60); alp.f(); alp.Q()
    aref = alp.build()
    for k, (kind, ref) in enumerate((("Luminosity", lref), ("Alpha", aref))):
        x = M + k * 250
        checker(x, sy, 150, 60)
        pg.q(); pg.translate(x, sy)
        pg.gs(SMask=f"<< /Type /Mask /S /{kind} /G {ref} 0 R >>")
        pg.sh(axial(0, 0, 150, 60, [(0, P["indigo"]), (1, P["rose"])]))
        pg.Q()
        pg.text(t.sans, 8, x + 160, sy + 30, f"/S /{kind}", color=P["slate"], kern=False)
    d.save(path)


OUT = "07-transparency.pdf"
