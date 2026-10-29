from theme import *

ITEMS = [
    ("Brand identity system", "Logo suite, colour and type guidelines", 1, 4800.00),
    ("Website design", "12 templates, responsive, design tokens", 1, 7200.00),
    ("Front-end build", "Static site, accessibility audit (WCAG 2.2 AA)", 64, 95.00),
    ("Illustration set", "24 spot illustrations, vector + PNG", 24, 120.00),
    ("Content strategy workshop", "Two half-day sessions", 2, 850.00),
    ("Print collateral", "Brochure, business cards, letterhead", 1, 1950.00),
]


def money(v):
    return f"{v:,.2f}"


def build(path):
    d = Doc("Invoice INV-2026-0142", author="Northlight Studio", subject="Invoice with line-item table, totals and payment details", lang="en-US")
    t = Type(d)
    pg = d.page(*A4)
    W = pg.w - 2 * M
    background(pg, P["white"])
    box(pg, 0, pg.h - 14, pg.w, 14, fill=P["indigo"])
    logo_mark(pg, M, pg.h - 92, 30)
    pg.text(t.serif_b, 18, M + 42, pg.h - 79, "Northlight Studio", color=P["ink"])
    pg.text(t.sans, 9, M + 42, pg.h - 92, "Design & engineering for the web", color=P["muted"])
    pg.text(t.serif_b, 34, pg.w - M - t.serif_b.width("Invoice", 34), pg.h - 84, "Invoice", color=P["indigo_d"])

    # meta block
    y = pg.h - 150
    meta = [("Invoice no.", "INV-2026-0142"), ("Issued", "15 January 2026"), ("Due", "14 February 2026"), ("PO reference", "PO-88213")]
    cw = W / 4
    for i, (k, v) in enumerate(meta):
        x = M + i * cw
        eyebrow(pg, t, x, y, k, color=P["muted"])
        pg.text(t.sans_b, 11, x, y - 17, v, color=P["ink"])
    rule(pg, M, y - 32, M + W)

    # parties
    y2 = y - 62
    for i, (head, lines) in enumerate((("From", ["Northlight Studio Ltd", "14 Harbour Row", "Bristol BS1 4RB", "United Kingdom", "VAT GB 123 4567 89"]),
                                       ("Billed to", ["Meridian Analytics Inc.", "Attn: Priya Raman", "500 Market Street, Suite 12", "San Francisco, CA 94105", "United States"]))):
        x = M + i * (W / 2)
        eyebrow(pg, t, x, y2, head)
        for j, ln in enumerate(lines):
            pg.text(t.sans_b if j == 0 else t.sans, 10.5, x, y2 - 18 - j * 14.5, ln, color=P["ink"] if j == 0 else P["slate"])

    # table
    ty = y2 - 120
    cols = [(0, "Description", "l"), (W - 250, "Qty", "r"), (W - 170, "Rate", "r"), (W, "Amount", "r")]
    box(pg, M, ty - 6, W, 26, fill=P["ink"], r=4)
    for x, h, al in cols:
        xx = M + 12 if al == "l" else M + x - t.sans_b.width(h.upper(), 8, tracking=1.2) - 12
        pg.text(t.sans_b, 8, xx, ty + 4, h.upper(), color="#FFFFFF", tracking=1.2, kern=False)
    ry = ty - 6
    sub = 0
    for i, (name, desc, q, rate) in enumerate(ITEMS):
        rh = 42
        ry -= rh
        if i % 2 == 0:
            box(pg, M, ry, W, rh, fill=P["paper"])
        amt = q * rate
        sub += amt
        pg.text(t.sans_b, 10.5, M + 12, ry + 24, name, color=P["ink"])
        pg.text(t.sans, 9, M + 12, ry + 10, desc, color=P["muted"])
        for x, s, f in ((W - 250, f"{q}", t.sans), (W - 170, money(rate), t.sans), (W, money(amt), t.sans_b)):
            pg.text(f, 10.5, M + x - f.width(s, 10.5) - 12, ry + 17, s, color=P["ink"])
    rule(pg, M, ry, M + W)

    # totals
    tax = round(sub * 0.2, 2)
    total = sub + tax
    tx = M + W - 240
    y = ry - 30
    for k, v in (("Subtotal", sub), ("VAT 20 %", tax)):
        pg.text(t.sans, 10.5, tx, y, k, color=P["slate"])
        pg.text(t.sans, 10.5, M + W - 12 - t.sans.width(money(v), 10.5), y, money(v), color=P["ink"])
        y -= 22
    y -= 8
    box(pg, tx - 12, y - 18, 252, 38, fill=P["indigo"], r=8)
    pg.text(t.sans_b, 11, tx, y - 4, "Total due (USD)", color="#E0E7FF")
    pg.text(t.serif_b, 17, M + W - 12 - t.serif_b.width("$" + money(total), 17), y - 6, "$" + money(total), color="#FFFFFF")

    # payment + notes
    py = ry - 30
    eyebrow(pg, t, M, py, "Payment")
    for j, (k, v) in enumerate((("Bank", "Northlight Studio Ltd, Monzo Business"), ("IBAN", "GB29 MONZ 0400 0412 3456 78"), ("BIC", "MONZGB2L"), ("Reference", "INV-2026-0142"))):
        pg.text(t.sans, 9.5, M, py - 18 - j * 15, k, color=P["muted"])
        pg.text(t.mono if k in ("IBAN", "BIC") else t.sans, 9.5, M + 62, py - 18 - j * 15, v, color=P["ink"], kern=False)
    paragraph(pg, t.sans, 9, M, py - 100, W * 0.62,
              "Payment is due within 30 days. Late payments incur 1.5 % monthly interest. Thank you for your business; it was a pleasure working with the Meridian team.",
              color=P["muted"], leading=13)
    rule(pg, M, 56, M + W)
    pg.text(t.sans, 8, M, 40, "Northlight Studio Ltd · Registered in England No. 09871234 · hello@northlight.example", color=P["muted"], kern=False)
    pg.text(t.sans, 8, M + W - t.sans.width("Page 1 of 1", 8), 40, "Page 1 of 1", color=P["muted"])
    d.save(path)


OUT = "22-invoice.pdf"
