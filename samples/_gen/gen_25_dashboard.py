from theme import *

MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"]
REV = [42, 47, 51, 49, 58, 64, 61, 70, 76, 74, 83, 91]
COST = [31, 33, 36, 35, 39, 42, 43, 46, 49, 50, 54, 57]
SPLIT = [("Subscriptions", 46, P["indigo"]), ("Services", 24, P["teal"]), ("Marketplace", 17, P["amber"]), ("Licensing", 13, P["rose"])]


def build(path):
    d = Doc("Q4 Performance dashboard", author="Pdf sample generator", subject="Bar, line/area and donut charts built from vector path operators", lang="en-US")
    t = Type(d)
    W, H = 841.89, 595.276
    pg = d.page(W, H)
    background(pg, "#EEF2F7")
    m = 36
    # header
    pg.text(t.sans_b, 8, m, H - 38, "FINANCE  /  FY 2026", color=P["indigo"], tracking=1.8, kern=False)
    pg.text(t.serif_b, 28, m, H - 68, "Performance dashboard", color=P["ink"])
    pg.text(t.sans, 10, W - m - t.sans.width("Updated 31 Dec 2026  ·  all figures in $ thousands", 10), H - 62, "Updated 31 Dec 2026  ·  all figures in $ thousands", color=P["slate"])

    def card(x, y, w, h, r=10):
        pg.q(); pg.gs(ca=0.07); pg.fill("#0F172A"); pg.rrect(x + 1, y - 2, w, h, r); pg.f(); pg.Q()
        box(pg, x, y, w, h, fill=P["white"], r=r)

    # KPI cards
    kw = (W - 2 * m - 3 * 14) / 4
    kpis = [("Revenue", "$786k", "+18.4 %", P["indigo"], REV), ("Gross margin", "41.2 %", "+2.1 pt", P["teal"], [a - b for a, b in zip(REV, COST)]),
            ("Active customers", "4,812", "+9.7 %", P["amber"], [20, 22, 21, 25, 28, 27, 30, 33, 35, 34, 38, 41]), ("Churn", "1.9 %", "-0.3 pt", P["rose"], [3, 2.8, 3.1, 2.7, 2.5, 2.6, 2.4, 2.2, 2.3, 2.1, 2.0, 1.9])]
    ky = H - 160
    for i, (lab, val, delta, col, series) in enumerate(kpis):
        x = m + i * (kw + 14)
        card(x, ky, kw, 78)
        pg.text(t.sans_b, 8, x + 14, ky + 60, lab.upper(), color=P["muted"], tracking=1.4, kern=False)
        pg.text(t.serif_b, 24, x + 14, ky + 28, val, color=P["ink"])
        good = not delta.startswith("-") or lab == "Churn"
        dc = P["emerald"] if good else P["rose"]
        pg.text(t.sans_b, 9, x + 14, ky + 11, ("▲ " if delta[0] == "+" else "▼ ") if False else delta, color=dc)
        # sparkline
        sx0, sx1, sy0, sy1 = x + kw - 92, x + kw - 14, ky + 22, ky + 56
        lo, hi = min(series), max(series)
        pts = [(sx0 + k * (sx1 - sx0) / 11, sy0 + (v - lo) / (hi - lo) * (sy1 - sy0)) for k, v in enumerate(series)]
        pg.q(); pg.stroke(col); pg.width(1.6); pg.join(1); pg.cap(1); pg.move(*pts[0])
        for p in pts[1:]:
            pg.line(*p)
        pg.S(); pg.fill(col); pg.circle(*pts[-1], 2.6); pg.f(); pg.Q()

    # bar chart
    cy0, ch = 36, 345
    bx, bw = m, 350
    card(bx, cy0, bw, ch)
    pg.text(t.sans_b, 11, bx + 16, cy0 + ch - 26, "Monthly revenue vs cost", color=P["ink"])
    pg.text(t.sans, 8.5, bx + 16, cy0 + ch - 40, "Clustered bars, $k", color=P["muted"])
    px0, px1, py0, py1 = bx + 40, bx + bw - 16, cy0 + 34, cy0 + ch - 60
    for g in range(0, 101, 25):
        y = py0 + g / 100 * (py1 - py0)
        pg.q(); pg.stroke(P["mist"]); pg.width(0.6); pg.move(px0, y); pg.line(px1, y); pg.S(); pg.Q()
        pg.text(t.sans, 7.5, px0 - 8 - t.sans.width(str(g), 7.5), y - 2.5, str(g), color=P["muted"])
    slot = (px1 - px0) / 12
    for i in range(12):
        x = px0 + i * slot + slot * 0.16
        bwid = slot * 0.32
        for k, (v, c) in enumerate(((REV[i], P["indigo"]), (COST[i], mix(P["indigo"], "#FFFFFF", 0.65)))):
            h = v / 100 * (py1 - py0)
            pg.q(); pg.fill(c); pg.rrect(x + k * (bwid + 1.5), py0, bwid, h, 1.5); pg.f(); pg.Q()
        pg.text(t.sans, 7.5, px0 + i * slot + slot / 2 - t.sans.width(MONTHS[i], 7.5) / 2, py0 - 14, MONTHS[i], color=P["muted"])
    for k, (lab, c) in enumerate((("Revenue", P["indigo"]), ("Cost", mix(P["indigo"], "#FFFFFF", 0.65)))):
        lx = bx + bw - 130 + k * 66
        box(pg, lx, cy0 + ch - 30, 9, 9, fill=c, r=2)
        pg.text(t.sans, 8.5, lx + 14, cy0 + ch - 28, lab, color=P["slate"])

    # line/area chart
    lx0 = bx + bw + 14
    lw = 230
    card(lx0, cy0, lw, ch)
    pg.text(t.sans_b, 11, lx0 + 16, cy0 + ch - 26, "Margin trend", color=P["ink"])
    pg.text(t.sans, 8.5, lx0 + 16, cy0 + ch - 40, "Revenue minus cost, $k", color=P["muted"])
    ax0, ax1, ay0, ay1 = lx0 + 30, lx0 + lw - 16, cy0 + 34, cy0 + ch - 60
    net = [a - b for a, b in zip(REV, COST)]
    for g in range(0, 41, 10):
        y = ay0 + g / 40 * (ay1 - ay0)
        pg.q(); pg.stroke(P["mist"]); pg.width(0.6); pg.move(ax0, y); pg.line(ax1, y); pg.S(); pg.Q()
        pg.text(t.sans, 7.5, ax0 - 6 - t.sans.width(str(g), 7.5), y - 2.5, str(g), color=P["muted"])
    pts = [(ax0 + i * (ax1 - ax0) / 11, ay0 + v / 40 * (ay1 - ay0)) for i, v in enumerate(net)]
    # smooth (Catmull-Rom to bezier)
    def smooth(c, pts):
        c.move(*pts[0])
        for i in range(len(pts) - 1):
            p0, p1, p2, p3 = pts[max(i - 1, 0)], pts[i], pts[i + 1], pts[min(i + 2, len(pts) - 1)]
            c.curve(p1[0] + (p2[0] - p0[0]) / 6, p1[1] + (p2[1] - p0[1]) / 6, p2[0] - (p3[0] - p1[0]) / 6, p2[1] - (p3[1] - p1[1]) / 6, p2[0], p2[1])
    pg.q(); smooth(pg, pts); pg.line(pts[-1][0], ay0); pg.line(pts[0][0], ay0); pg.close(); pg.clip()
    pg.sh(axial(0, ay0, 0, ay1, [(0, "#FFFFFF"), (1, lighten(P["teal"], 0.45))]))
    pg.Q()
    pg.q(); pg.stroke(P["teal"]); pg.width(2.2); pg.cap(1); pg.join(1); smooth(pg, pts); pg.S()
    for p in pts:
        pg.fill("#FFFFFF"); pg.stroke(P["teal"]); pg.width(1.6); pg.circle(*p, 3); pg.B()
    pg.Q()
    for i in (0, 3, 6, 9, 11):
        pg.text(t.sans, 7.5, pts[i][0] - t.sans.width(MONTHS[i], 7.5) / 2, ay0 - 14, MONTHS[i], color=P["muted"])
    pg.text(t.sans_b, 9, pts[-1][0] - 28, pts[-1][1] + 10, f"{net[-1]}", color=P["teal"])

    # donut
    dx0 = lx0 + lw + 14
    dw = W - m - dx0
    card(dx0, cy0, dw, ch)
    pg.text(t.sans_b, 11, dx0 + 16, cy0 + ch - 26, "Revenue mix", color=P["ink"])
    pg.text(t.sans, 8.5, dx0 + 16, cy0 + ch - 40, "Share by stream", color=P["muted"])
    cx, cyy, ro, ri = dx0 + dw / 2, cy0 + ch - 130, 62, 38
    a = 90.0
    tot = sum(s[1] for s in SPLIT)
    for lab, v, c in SPLIT:
        sweep = -360 * v / tot
        pg.q(); pg.fill(c); pg.stroke("#FFFFFF"); pg.width(2); pg.join(1)
        pg.arc_path(cx, cyy, ro, a, a + sweep, True)
        pg.line(cx + ri * math.cos(math.radians(a + sweep)), cyy + ri * math.sin(math.radians(a + sweep)))
        pg.arc_path(cx, cyy, ri, a + sweep, a, False)
        pg.close(); pg.B(); pg.Q()
        a += sweep
    s = "100%"
    pg.text(t.serif_b, 18, cx - t.serif_b.width("$786k", 18) / 2,     cyy - 2, "$786k", color=P["ink"])
    pg.text(t.sans, 8, cx - t.sans.width("FY total", 8) / 2, cyy - 14, "FY total", color=P["muted"])
    for k, (lab, v, c) in enumerate(SPLIT):
        yy = cy0 + 78 - k * 17
        box(pg, dx0 + 16, yy - 1, 9, 9, fill=c, r=2)
        pg.text(t.sans, 9, dx0 + 32, yy, lab, color=P["slate"])
        pg.text(t.sans_b, 9, dx0 + dw - 16 - t.sans_b.width(f"{v} %", 9), yy, f"{v} %", color=P["ink"])
    d.save(path)


OUT = "25-dashboard-charts.pdf"
