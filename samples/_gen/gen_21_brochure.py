from theme import *


def build(path):
    d = Doc("Brochure: Harbour & Hearth tri-fold", author="Pdf sample generator", subject="US Letter landscape tri-fold: outside and inside spreads, vector illustration, clipping, shadings, TrimBox/BleedBox", lang="en-US")
    t = Type(d)
    W, H = 792, 612
    pw = W / 3
    BL = 9
    outside, inside = d.page(W, H), d.page(W, H)
    for pg in (outside, inside):
        pg.boxes["BleedBox"] = (0, 0, W, H)
        pg.boxes["TrimBox"] = (BL, BL, W - BL, H - BL)
    SEA, SAND, CORAL, DEEP = "#0E4C5A", "#F6EFE4", "#E4572E", "#0A2E36"

    def fold_guides(pg):
        pg.q(); pg.stroke("#00000022" if False else "#CBD5E1"); pg.width(0.4); pg.dash([3, 3])
        for k in (1, 2):
            pg.move(pw * k, 0); pg.line(pw * k, 6); pg.S(); pg.move(pw * k, H - 6); pg.line(pw * k, H); pg.S()
        pg.Q()

    # ---------------- outside: [inside flap | back | front]
    o = outside
    background(o, SAND)
    # front panel (right)
    fx = 2 * pw
    o.q(); o.rect(fx, 0, pw, H); o.clip()
    o.sh(axial(fx, H, fx + pw, 0, [(0, "#0E7490"), (0.6, SEA), (1, DEEP)]))
    # sun & waves
    o.fill("#FDBA74"); o.circle(fx + pw * 0.66, H - 270, 52); o.f()
    for i, (col, amp, yy) in enumerate((("#0891B2", 14, 250), ("#0E7490", 18, 205), (SEA, 22, 160), (DEEP, 26, 115))):
        o.q(); o.fill(col); o.gs(ca=0.9)
        o.move(fx, 0); o.line(fx, yy)
        steps = 6
        for s in range(steps):
            x0 = fx + s * pw / steps
            x1 = x0 + pw / steps
            sign = 1 if (s + i) % 2 == 0 else -1
            o.curve(x0 + pw / steps * 0.35, yy + sign * amp, x0 + pw / steps * 0.65, yy + sign * amp, x1, yy)
        o.line(fx + pw, 0); o.close(); o.f(); o.Q()
    o.Q()
    o.text(t.sans_b, 9, fx + 30, H - 56, "BRISTOL  ·  EST. 1987", color="#FED7AA", tracking=2.2, kern=False)
    o.text(t.serif_b, 44, fx + 30, H - 112, "Harbour", color="#FFFFFF")
    o.text(t.serif_b, 44, fx + 30, H - 158, "& Hearth", color="#FFFFFF")
    o.text(t.serif_i, 14, fx + 30, 62, "Seafood, fire & good company", color="#FFEDD5")
    # back panel (middle)
    bx = pw
    eyebrow(o, t, bx + 30, H - 60, "Find us", color=CORAL)
    o.text(t.serif_b, 20, bx + 30, H - 92, "Visit the Quay", color=SEA)
    y = paragraph(o, t.sans, 10, bx + 30, H - 122, pw - 60, "Our kitchen sits on the waterside, five minutes from the ferry landing. Reservations are recommended at weekends and during festival weeks.", leading=15, color="#334155")
    y -= 14
    for lab, val in (("ADDRESS", "12 Harbour Row, Bristol BS1 4RB"), ("OPEN", "Tue–Sun, 12:00 – 22:30"), ("PHONE", "+44 117 496 0123"), ("WEB", "harbourandhearth.example")):
        o.text(t.sans_b, 7.5, bx + 30, y, lab, color=CORAL, tracking=1.6, kern=False)
        o.text(t.sans, 10, bx + 30, y - 14, val, color=SEA)
        y -= 40
    # map
    mx, my, mw, mh = bx + 30, 52, pw - 60, 120
    o.q(); o.rrect(mx, my, mw, mh, 8); o.clip()
    o.fill("#E7DFD0"); o.rect(mx, my, mw, mh); o.f()
    o.fill("#A5D8E6"); o.move(mx, my + 20); o.curve(mx + 60, my + 60, mx + 90, my + 10, mx + mw, my + 50); o.line(mx + mw, my); o.line(mx, my); o.close(); o.f()
    o.stroke("#FFFFFF"); o.width(5); o.move(mx, my + 85); o.line(mx + mw, my + 95); o.S(); o.move(mx + 70, my + mh); o.line(mx + 90, my + 30); o.S()
    o.stroke("#D6CBB6"); o.width(1); o.dash([2, 2]); o.move(mx + 20, my + mh); o.line(mx + 40, my); o.S()
    o.Q()
    o.q(); o.fill(CORAL); o.circle(mx + 90, my + 80, 6); o.f(); o.fill("#FFFFFF"); o.circle(mx + 90, my + 80, 2.2); o.f(); o.Q()
    # left panel (flap)
    lx = 0
    box(o, lx + 30, 70, pw - 60, H - 150, fill="#FFFFFF", r=10)
    eyebrow(o, t, lx + 50, H - 108, "Good to know", color=CORAL)
    ty = H - 138
    for h, b in (("Allergies", "Ask us about any allergen; our menu marks the common ones."), ("Private dining", "The Lantern Room seats up to 24 for celebrations."), ("Sustainable", "Every fish is sourced from certified day-boat fisheries."), ("Gift cards", "Available in any amount at the counter or online.")):
        o.text(t.serif_b, 13, lx + 50, ty, h, color=SEA)
        ty = paragraph(o, t.sans, 9, lx + 50, ty - 16, pw - 100, b, leading=13, color="#475569") - 14
    fold_guides(o)

    # ---------------- inside: three panels
    n_ = inside
    background(n_, "#FFFFFF")
    box(n_, 0, 0, W, 90, fill=SAND)
    menus = [("To start", CORAL, [("Smoked mackerel pâté", "toasted sourdough, pickled cucumber", "7.5"), ("Charred octopus", "romesco, chorizo crumb, lemon", "11"), ("Harbour chowder", "clams, potato, cream, chives", "9"), ("Burrata & peach", "basil oil, black pepper", "8.5")]),
             ("From the fire", SEA, [("Whole sea bream", "fennel, salsa verde, new potatoes", "26"), ("Wood-fired prawns", "garlic butter, chilli, flatbread", "22"), ("Dry-aged ribeye", "bone marrow butter, watercress", "34"), ("Hearth cauliflower", "tahini, pomegranate, dukkah", "17")]),
             ("To finish", "#B45309", [("Burnt honey tart", "crème fraîche, sea salt", "8"), ("Chocolate pot", "olive oil, flaked salt", "8"), ("Lemon posset", "shortbread, thyme", "7.5"), ("Local cheeses", "quince, oatcakes", "12")])]
    for i, (title, col, items) in enumerate(menus):
        x = i * pw
        eyebrow(n_, t, x + 36, H - 56, f"0{i + 1}", color=col)
        n_.text(t.serif_b, 24, x + 36, H - 88, title, color=SEA)
        n_.q(); n_.fill(col); n_.rect(x + 36, H - 100, 28, 2.5); n_.f(); n_.Q()
        y = H - 138
        for name, desc, price in items:
            n_.text(t.sans_b, 11, x + 36, y, name, color="#0F172A")
            n_.text(t.sans_b, 11, x + pw - 36 - t.sans_b.width(price, 11), y, price, color=col)
            n_.text(t.sans_i, 9, x + 36, y - 14, desc, color="#64748B")
            y -= 70
        if i < 2:
            n_.q(); n_.stroke("#E2E8F0"); n_.width(0.6); n_.move(x + pw, 100); n_.line(x + pw, H - 40); n_.S(); n_.Q()
    n_.text(t.serif_i, 12, 36, 48, "Prices in pounds sterling, service not included. Please tell us about allergies.", color=SEA)
    logo_mark(n_, W - 36 - 28, 38, 28, a=SEA, b=CORAL)
    fold_guides(n_)
    d.save(path)


OUT = "21-brochure.pdf"
