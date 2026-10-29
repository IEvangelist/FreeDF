from theme import *


def build(path):
    d = Doc("Page boxes and rotation", subject="MediaBox, CropBox, BleedBox, TrimBox, ArtBox, trim marks, /Rotate 0/90/180/270", lang="en-US")
    t = Type(d)
    W, H = 612, 792
    boxes = dict(CropBox=(18, 18, 594, 774), BleedBox=(27, 27, 585, 765), TrimBox=(36, 36, 576, 756), ArtBox=(72, 72, 540, 720))
    cols = dict(MediaBox=P["slate"], CropBox=P["sky"], BleedBox=P["rose"], TrimBox=P["emerald"], ArtBox=P["violet"])
    pg = d.page(W, H)
    pg.boxes.update(boxes)
    # bleed artwork reaches the bleed box, not the trim
    bx0, by0, bx1, by1 = boxes["BleedBox"]
    pg.q(); pg.rect(bx0, by0, bx1 - bx0, by1 - by0); pg.clip()
    pg.fill(P["indigo_d"]); pg.rect(0, 0, W, H); pg.f()
    pg.sh(radial(W * 0.8, H * 0.85, 0, W * 0.8, H * 0.85, 560, [(0, P["violet"]), (0.5, P["indigo"]), (1, P["indigo_d"])]))
    pg.Q()
    # trim marks in the margin between trim and crop
    tx0, ty0, tx1, ty1 = boxes["TrimBox"]
    pg.q(); pg.stroke("#FFFFFF"); pg.width(0.5)
    for (x, y, dx, dy) in ((tx0, ty0, -1, -1), (tx1, ty0, 1, -1), (tx0, ty1, -1, 1), (tx1, ty1, 1, 1)):
        pg.move(x, y + dy * 3); pg.line(x, y + dy * 17); pg.S()
        pg.move(x + dx * 3, y); pg.line(x + dx * 17, y); pg.S()
    pg.Q()
    pg.text(t.sans_b, 9, 90, H - 100, "PAGE BOXES  /  SAMPLE 13", color="#C7D2FE", tracking=1.6, kern=False)
    pg.text(t.serif_b, 46, 90, H - 160, "Five boxes,", color="#FFFFFF")
    pg.text(t.serif_b, 46, 90, H - 212, "one page.", color="#FFFFFF")
    rows = [("MediaBox", "0 0 612 792", "Physical sheet, the largest extent."),
            ("CropBox", "18 18 594 774", "What viewers show and print; this page opens at this size."),
            ("BleedBox", "27 27 585 765", "Where artwork must extend to survive trimming."),
            ("TrimBox", "36 36 576 756", "The finished page after cutting, marked in white."),
            ("ArtBox", "72 72 540 720", "The meaningful content, kept clear of the edge.")]
    y = H - 300
    for nm, val, desc in rows:
        c = cols[nm]
        pg.q(); pg.fill(c); pg.rrect(90, y - 8, 10, 28, 3); pg.f(); pg.Q()
        pg.text(t.sans_b, 13, 114, y + 8, nm, color="#FFFFFF")
        pg.text(t.mono, 9.5, 220, y + 8.5, val, color="#C7D2FE", kern=False)
        pg.text(t.sans, 10, 114, y - 6, desc, color="#E0E7FF")
        y -= 52
    # outlines of every box so the nesting is visible
    pg.q(); pg.width(0.9)
    for nm, (x0, y0, x1, y1) in dict(MediaBox=(0.5, 0.5, W - 0.5, H - 0.5), **boxes).items():
        pg.stroke(cols[nm]); pg.dash([4, 3] if nm != "TrimBox" else []); pg.rect(x0, y0, x1 - x0, y1 - y0); pg.S()
    pg.Q()

    for deg in (90, 180, 270):
        p = d.page(W, H)
        p.extra = f"/Rotate {deg}"
        background(p, P["paper"])
        p.q(); p.fill(P["indigo"]); p.rect(0, H - 8, W, 8); p.f(); p.Q()
        p.text(t.sans_b, 9, M, H - 40, f"PAGE ROTATION  /  SAMPLE 13", color=P["indigo"], tracking=1.6, kern=False)
        p.text(t.serif_b, 40, M, H - 100, f"/Rotate {deg}", color=P["ink"])
        paragraph(p, t.sans, 11.5, M, H - 134, 420,
                  "The page dictionary stores the content upright; the viewer turns the whole page clockwise by this many degrees at display time. The arrow below always points to the content's top edge.",
                  color=P["slate"], leading=17)
        # up arrow
        cx, cy = W / 2, 340
        p.q(); p.fill(P["indigo"]); p.move(cx, cy + 120); p.line(cx + 70, cy + 10); p.line(cx + 26, cy + 10); p.line(cx + 26, cy - 100)
        p.line(cx - 26, cy - 100); p.line(cx - 26, cy + 10); p.line(cx - 70, cy + 10); p.close(); p.f(); p.Q()
        p.text(t.sans_b, 12, cx - t.sans_b.width("content top", 12) / 2, cy - 130, "content top", color=P["slate"])
        # corner markers: a quadrant colour per corner shows the turn
        for (x, y, c, lab) in ((M, M, P["rose"], "BL"), (W - M - 40, M, P["teal"], "BR"), (M, H - 200, P["amber"], "TL"), (W - M - 40, H - 200, P["sky"], "TR")):
            box(p, x, y, 40, 40, fill=c, r=8)
            p.text(t.sans_b, 12, x + 20 - t.sans_b.width(lab, 12) / 2, y + 15, lab, color="#FFFFFF", kern=False)
    d.save(path)


OUT = "13-page-boxes-rotation.pdf"
