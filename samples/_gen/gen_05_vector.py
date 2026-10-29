from theme import *


def star(pg, cx, cy, r, pts=5, skip=2):
    for i in range(pts):
        a = math.pi / 2 + i * skip * 2 * math.pi / pts
        (pg.move if i == 0 else pg.line)(cx + r * math.cos(a), cy + r * math.sin(a))
    pg.close()


def build(path):
    d = Doc("Vector graphics", subject="Paths, curves, dashes, joins, caps, winding rules, parametric art", lang="en-US")
    t = Type(d)
    pg = d.page(*A4)
    background(pg, P["paper"])
    page_header(pg, t, "Vector graphics", "Graphics  /  01", 1, 2)
    pg.text(t.sans, 11, M, pg.h - 106, "Everything on this page is drawn with path operators: lines, Bézier curves, strokes and fills.", color=P["slate"])
    W = pg.w - 2 * M

    # line caps / joins / miter
    y = pg.h - 150
    eyebrow(pg, t, M, y, "Line caps")
    for i, (cap, name) in enumerate(((0, "butt"), (1, "round"), (2, "square"))):
        x = M + i * 70
        pg.q(); pg.stroke(P["indigo"]); pg.width(18); pg.cap(cap)
        pg.move(x + 10, y - 20); pg.line(x + 60, y - 20); pg.S(); pg.Q()
        pg.q(); pg.stroke(P["rose"]); pg.width(0.8); pg.move(x + 10, y - 32); pg.line(x + 10, y - 8); pg.move(x + 60, y - 32); pg.line(x + 60, y - 8); pg.S(); pg.Q()
        pg.text(t.sans, 8, x + 10, y - 42, name, color=P["slate"], kern=False)
    eyebrow(pg, t, M + 250, y, "Line joins")
    for i, (j, name) in enumerate(((0, "miter"), (1, "round"), (2, "bevel"))):
        x = M + 250 + i * 70
        pg.q(); pg.stroke(P["teal"]); pg.width(14); pg.join(j); pg.miter(10)
        pg.move(x + 4, y - 44); pg.line(x + 24, y - 20); pg.line(x + 44, y - 44); pg.S(); pg.Q()
        pg.text(t.sans, 8, x + 8, y - 56, name, color=P["slate"], kern=False)

    # dashes
    y -= 90
    eyebrow(pg, t, M, y, "Dash patterns")
    pats = [([], 0), ([6, 3], 0), ([1, 4], 0), ([12, 3, 2, 3], 0), ([8, 4], 6), ([0.1, 5], 0)]
    for i, (arr, ph) in enumerate(pats):
        yy = y - 18 - i * 14
        pg.q(); pg.stroke(P["ink"]); pg.width(2.5 if i == 5 else 1.6); pg.cap(1 if i == 5 else 0)
        pg.dash(arr, ph); pg.move(M, yy); pg.line(M + 230, yy); pg.S(); pg.Q()
        pg.text(t.mono, 7, M + 250, yy - 2, f"[{' '.join(str(a) for a in arr)}] {ph} d", color=P["slate"], kern=False)

    # winding rules
    yfr = y
    eyebrow(pg, t, M + 340, yfr, "Fill rules")
    for i, eo in enumerate((False, True)):
        cx = M + 380 + i * 100
        pg.q(); pg.fill(P["violet"]); pg.stroke(P["indigo_d"]); pg.width(1.5)
        star(pg, cx, yfr - 52, 38)
        (pg.out.append("B*") if eo else pg.B())
        pg.Q()
        pg.text(t.sans, 8, cx - 28, yfr - 108, "even-odd (f*)" if eo else "non-zero (f)", color=P["slate"], kern=False)

    # bezier art: rose curves
    y_art = 340
    eyebrow(pg, t, M, y_art, "Parametric roses (cubic Béziers)")
    cx, cy = M + 110, y_art - 120
    for k, (kk, col) in enumerate(((5, P["indigo"]), (7, P["rose"]), (3, P["teal"]))):
        pg.q(); pg.stroke(col); pg.width(0.9); pg.gs(CA=0.8)
        steps = 360
        pts = []
        for s_ in range(steps + 1):
            th = s_ / steps * 2 * math.pi
            rr = (96 - k * 12) * math.cos(kk * th)
            pts.append((cx + rr * math.cos(th), cy + rr * math.sin(th)))
        pg.move(*pts[0])
        # Catmull-Rom to Bézier
        for i in range(len(pts) - 1):
            p0 = pts[max(0, i - 1)]; p1 = pts[i]; p2 = pts[i + 1]; p3 = pts[min(len(pts) - 1, i + 2)]
            pg.curve(p1[0] + (p2[0] - p0[0]) / 6, p1[1] + (p2[1] - p0[1]) / 6, p2[0] - (p3[0] - p1[0]) / 6, p2[1] - (p3[1] - p1[1]) / 6, p2[0], p2[1])
        pg.S(); pg.Q()

    # annotated cubic Bézier with its control polygon
    bx, by = M + 270, y_art
    eyebrow(pg, t, bx, by, "Anatomy of a cubic Bézier")
    p0, p1_, p2_, p3 = (bx + 6, by - 150), (bx + 40, by - 20), (bx + 170, by - 40), (bx + 220, by - 140)
    pg.q(); pg.stroke(P["muted"]); pg.width(0.8); pg.dash([3, 3])
    pg.move(*p0); pg.line(*p1_); pg.move(*p3); pg.line(*p2_); pg.S(); pg.Q()
    pg.q(); pg.stroke(P["indigo"]); pg.width(3); pg.cap(1)
    pg.move(*p0); pg.curve(*p1_, *p2_, *p3); pg.S(); pg.Q()
    for i, (px, py) in enumerate((p0, p1_, p2_, p3)):
        pg.q(); box(pg, px - 4, py - 4, 8, 8, fill=P["rose"] if i in (1, 2) else P["ink"], r=4); pg.Q()
        pg.text(t.mono, 8, px + 8, py + 4, f"P{i}", color=P["slate"], kern=False)
    pg.text(t.sans, 8.5, bx, by - 180, "B(t) = (1-t)³P0 + 3(1-t)²t P1 + 3(1-t)t² P2 + t³P3", color=P["slate"], kern=False)

    # page 2: spirograph + wave mesh + geometric composition
    p2 = d.page(*A4)
    background(p2, P["ink"])
    eyebrow(p2, t, M, p2.h - 36, "Graphics  /  01", color="#A5B4FC")
    p2.text(t.sans, 8, p2.w - M - 20, p2.h - 36, "2 / 2", color=P["muted"])
    p2.text(t.serif_b, 26, M, p2.h - 84, "Bézier art", color=P["white"])
    cx, cy = p2.w / 2, p2.h - 330
    R, r_, dd = 200, 77, 120
    p2.q(); p2.width(0.45); p2.gs(CA=0.55)
    for layer in range(5):
        p2.stroke(mix(P["sky"], P["rose"], layer / 4))
        ph = layer * math.pi / 10
        pts = []
        for s_ in range(0, 2400):
            th = s_ * 0.0262
            x_ = (R - r_) * math.cos(th + ph) + dd * math.cos((R - r_) / r_ * th)
            y_ = (R - r_) * math.sin(th + ph) - dd * math.sin((R - r_) / r_ * th)
            pts.append((cx + x_ * 0.95, cy + y_ * 0.95))
        p2.move(*pts[0])
        for q in pts[1:]:
            p2.line(*q)
        p2.S()
    p2.Q()
    # flowing wave bands
    base = 190
    for b in range(14):
        col = mix(P["indigo"], P["emerald"], b / 13)
        p2.q(); p2.stroke(col); p2.width(1.4); p2.gs(CA=0.85)
        y0 = base + b * 6
        p2.move(M, y0 + math.sin(b * 0.4) * 10)
        for s_ in range(0, 5):
            x0 = M + s_ * (p2.w - 2 * M) / 5
            x1 = M + (s_ + 1) * (p2.w - 2 * M) / 5
            amp = 46 * math.sin(b * 0.22 + 0.3 * s_ + 0.5)
            p2.curve(x0 + 40, y0 + amp, x1 - 40, y0 - amp, x1, y0 + math.sin(b * 0.4 + s_) * 10)
        p2.S(); p2.Q()
    p2.text(t.sans, 9, M, 80, "Spirograph (hypotrochoid, 5 phase-shifted strokes with 55% stroke alpha) and 14 stitched cubic wave strands.", color=P["muted"])
    d.save(path)


OUT = "05-vector-graphics.pdf"
