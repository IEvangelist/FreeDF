from theme import *


def build(path):
    d = Doc("Poster: Pages in motion", author="Pdf sample generator", subject="A3 poster: layered gradients, blend modes, large display type, clipped text", lang="en-US")
    t = Type(d)
    W, H = 841.89, 1190.55
    pg = d.page(W, H)
    pg.boxes["TrimBox"] = (0, 0, W, H)
    pg.fill(P["ink"]); pg.rect(0, 0, W, H); pg.f()
    # gradient field
    pg.q(); pg.rect(0, 0, W, H); pg.clip()
    pg.sh(axial(0, H, W, 0, [(0, "#1E1B4B"), (0.45, "#4338CA"), (0.8, "#7C3AED"), (1, "#DB2777")]))
    # sun disc + stripes
    cx, cy, R = W * 0.5, H * 0.58, 250
    pg.q(); pg.circle(cx, cy, R); pg.clip()
    pg.sh(axial(cx, cy + R, cx, cy - R, [(0, "#FDE68A"), (0.5, "#FB923C"), (1, "#E11D48")]))
    pg.fill("#4338CA")
    for i in range(9):
        h = 3 + i * 2.6
        y = cy - R * 0.15 - i * 30
        pg.rect(cx - R, y - h, 2 * R, h)
    pg.f(); pg.Q()
    # concentric rings with transparency
    pg.q(); pg.stroke("#FFFFFF"); pg.gs(CA=0.16)
    for k in range(1, 9):
        pg.width(1 + k * 0.25); pg.circle(cx, cy, R + k * 38); pg.S()
    pg.Q()
    # blend-mode overlapping shapes at the bottom
    pg.q(); pg.gs(BM="/Screen", ca=0.9)
    pg.fill(P["sky"]); pg.circle(W * 0.2, 190, 110); pg.f()
    pg.fill(P["emerald"]); pg.circle(W * 0.32, 170, 110); pg.f()
    pg.fill(P["amber"]); pg.circle(W * 0.26, 250, 110); pg.f()
    pg.Q()
    pg.Q()
    # headline
    M2 = 56
    pg.text(t.sans_b, 15, M2, H - 70, "PORTABLE DOCUMENT FESTIVAL  ·  2026", color="#E0E7FF", tracking=4, kern=False)
    for i, word in enumerate(("PAGES", "IN", "MOTION")):
        size = 190 if word != "IN" else 120
        pg.text(t.serif_b, size, M2 - 4, H - 230 - i * 190, word, color="#FFFFFF", kern=True)
    # outlined word using stroke mode
    pg.text(t.serif_b, 190, M2 - 4, H - 230 - 3 * 190, "", color="#FFFFFF")
    pg.q()
    pg.text(t.serif_b, 70, M2, H - 230 - 3 * 190 + 40, "every operator counts", color=None, mode=1, stroke_color="#FFFFFF", lw=1.2, kern=True)
    pg.Q()
    # info strip
    box(pg, 0, 0, W, 170, fill="#0B1020")
    pg.text(t.sans_b, 9, M2, 128, "WHEN", color="#A5B4FC", tracking=2, kern=False)
    pg.text(t.serif_b, 24, M2, 98, "12 – 14 June 2026", color="#FFFFFF")
    pg.text(t.sans_b, 9, M2 + 290, 128, "WHERE", color="#A5B4FC", tracking=2, kern=False)
    pg.text(t.serif_b, 24, M2 + 290, 98, "The Old Print Hall, Bristol", color="#FFFFFF")
    pg.text(t.sans_b, 9, M2, 62, "TALKS ON", color="#A5B4FC", tracking=2, kern=False)
    pg.text(t.sans, 12, M2, 42, "Rendering  ·  Fonts & text  ·  Colour  ·  Accessibility  ·  Security  ·  Editing", color="#CBD5E1")
    logo_mark(pg, W - M2 - 60, 60, 60, a="#6366F1", b="#EC4899")
    d.save(path)


OUT = "24-poster.pdf"
