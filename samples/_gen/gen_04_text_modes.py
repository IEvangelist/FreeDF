from theme import *


def caption(pg, t, x, y, text, sub=None):
    pg.text(t.sans_b, 8, x, y, text, color=P["ink"])
    if sub:
        pg.text(t.sans, 7.5, x, y - 10, sub, color=P["muted"], kern=False)


def build(path):
    d = Doc("Text rendering modes and clipping", subject="Tr 0-7, text clipping with shadings and patterns, text on a path", lang="en-US")
    t = Type(d)
    pg = d.page(*A4)
    background(pg, P["paper"])
    page_header(pg, t, "Text rendering modes", "Typography  /  02", 1, 1)
    pg.text(t.sans, 11, M, pg.h - 106, "The eight text rendering modes (Tr 0-7), then text as a clipping path for gradients and patterns.", color=P["slate"])

    modes = [(0, "Fill"), (1, "Stroke"), (2, "Fill + stroke"), (3, "Invisible"), (4, "Fill, add to clip"), (5, "Stroke, add to clip"), (6, "Fill + stroke, clip"), (7, "Clip only")]
    cw = (pg.w - 2 * M - 3 * 12) / 4
    ch = 112
    for i, (m, name) in enumerate(modes):
        col, row = i % 4, i // 4
        x = M + col * (cw + 12)
        y = pg.h - 140 - ch - row * (ch + 12)
        box(pg, x, y, cw, ch, fill=P["white"], stroke=P["mist"], r=6)
        pg.q()
        pg.rect(x + 1, y + 1, cw - 2, ch - 2)
        pg.clip()
        # clipping modes need a visible backdrop to show the clip effect
        pg.text(t.serif_b, 64, x + 12, y + 38, "Ag", color="#C7D2FE" if m in (3,) else P["indigo"], stroke_color=P["rose"], lw=2 if m != 1 else 1.4, mode=m)
        if m >= 4:
            pg.sh(axial(x, y, x + cw, y + ch, [(0, P["amber"]), (0.5, P["rose"]), (1, P["violet"])]))
        pg.Q()
        pg.text(t.sans_b, 8, x + 10, y + ch - 14, f"Tr {m}", color=P["indigo"], tracking=0.8, kern=False)
        pg.text(t.sans, 8, x + 10, y + 10, name, color=P["slate"], kern=False)

    # clipping showcase: big word filled with a gradient and tiling pattern
    y0 = 405
    eyebrow(pg, t, M, y0 + 24, "Clipping text with a shading")
    pg.q()
    pg.text(t.serif_b, 118, M, y0 - 70, "Aurora", mode=7, color=P["ink"])
    pg.sh(axial(M, y0 - 80, M + 360, y0 + 40, [(0, P["indigo"]), (0.4, P["sky"]), (0.7, P["emerald"]), (1, P["amber"])]))
    pg.Q()

    pat = XForm(d, (0, 0, 8, 8))
    pat.fill(P["rose"])
    pat.rect(0, 0, 4, 4)
    pat.rect(4, 4, 4, 4)
    pat.f()
    pat.fill(P["ink"])
    pat.rect(4, 0, 4, 4)
    pat.rect(0, 4, 4, 4)
    pat.f()
    pref = pat.build("pattern", 8, 8)
    eyebrow(pg, t, M, y0 - 118, "Clipping text with a tiling pattern")
    pg.q()
    pg.text(t.serif_b, 96, M, y0 - 200, "Checker", mode=7, color=P["ink"])
    pg.rect(M - 10, y0 - 220, 480, 120)
    pg.pattern_fill(pref)
    pg.f()
    pg.Q()

    # text on a path (per-glyph rotation)
    y1 = 106
    eyebrow(pg, t, M, y1 + 44, "Text on a curve")
    cx, cy, r = pg.w / 2, y1 - 168, 200
    s = "Every glyph is positioned and rotated individually along the arc "
    total = sum(t.serif_i.width(ch_, 16) for ch_ in s)
    ang = 0.5 * total / r
    a = math.pi / 2 + ang
    pg.q()
    for ch_ in s:
        w = t.serif_i.width(ch_, 16)
        da = w / r
        mid = a - da / 2
        px, py = cx + r * math.cos(mid), cy + r * math.sin(mid)
        rot = mid - math.pi / 2
        pg.q()
        pg.cm(math.cos(rot), math.sin(rot), -math.sin(rot), math.cos(rot), px, py)
        pg.text(t.serif_i, 16, -w / 2, 0, ch_, color=mix(P["indigo"], P["rose"], (a - (math.pi / 2 - ang)) / (2 * ang)), kern=False)
        pg.Q()
        a -= da
    pg.Q()
    d.save(path)


OUT = "04-text-render-modes.pdf"
