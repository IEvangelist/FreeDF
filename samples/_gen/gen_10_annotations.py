from theme import *


def ap(d, rect, draw, group=None):
    """Appearance stream whose BBox equals the annotation rectangle (identity mapping)."""
    x0, y0, x1, y1 = rect
    xf = XForm(d, (x0, y0, x1, y1), group=group)
    draw(xf)
    return xf.build()


def build(path):
    d = Doc("Annotations", subject="Link, Highlight, Underline, StrikeOut, Squiggly, Text, FreeText, Stamp, Ink, Line, Square, Circle, Polygon with appearance streams", lang="en-US")
    t = Type(d)
    helv = d.std("Helvetica")
    helv_b = d.std("Helvetica-Bold")
    pg = d.page(*A4)
    pg2 = d.page(*A4)
    background(pg, P["paper"])
    page_header(pg, t, "Annotations", "Interactive  /  01", 1, 2)
    pg.text(t.sans, 11, M, pg.h - 106, "Every annotation carries an appearance stream, so it looks identical in any viewer.", color=P["slate"])

    def hexa(c):
        return " ".join(n(v) for v in rgb(c))

    # --- text block to annotate
    body = ("Portable documents keep their meaning when content is reviewed. Reviewers mark passages with highlights, "
            "underlines and strike-outs, attach sticky notes, draw freehand ink, and stamp approvals without touching the "
            "page content beneath. Each markup is a separate object that can be listed, edited or removed.")
    col_w = pg.w - 2 * M
    y = pg.h - 150
    lines = wrap(t.serif, 13.5, body, col_w)
    lead = 24
    line_y = []
    for ln in lines:
        pg.text(t.serif, 13.5, M, y, ln, color=P["ink"])
        line_y.append((y, ln))
        y -= lead

    def span(i, s, e=None):
        i = next(k for k, (_, l) in enumerate(line_y) if s in l)
        yy, ln = line_y[i]
        pre = t.serif.width(ln[:ln.index(s)], 13.5)
        w = t.serif.width(s, 13.5)
        return M + pre, yy - 4.5, M + pre + w, yy + 12.5

    def markup(sub, i, s, color, extra=""):
        x0, y0, x1, y1 = span(i, s)
        rect = (x0, y0, x1, y1)
        quad = f"[{n(x0)} {n(y1)} {n(x1)} {n(y1)} {n(x0)} {n(y0)} {n(x1)} {n(y0)}]"

        def draw(c):
            if sub == "Highlight":
                c.gs(BM="/Multiply")
                c.fill(color); c.rect(x0, y0, x1 - x0, y1 - y0); c.f()
            elif sub == "Underline":
                c.stroke(color); c.width(1.2); c.move(x0, y0 + 1.5); c.line(x1, y0 + 1.5); c.S()
            elif sub == "StrikeOut":
                c.stroke(color); c.width(1.2); c.move(x0, (y0 + y1) / 2); c.line(x1, (y0 + y1) / 2); c.S()
            else:
                c.stroke(color); c.width(1); c.move(x0, y0 + 2)
                k, xx = 0, x0
                while xx < x1:
                    xx = min(xx + 2.5, x1); c.line(xx, y0 + 2 + (2 if k % 2 == 0 else 0)); k += 1
                c.S()
        a = ap(d, rect, draw)
        pg.add_annot(f"<< /Type /Annot /Subtype /{sub} /Rect [{n(x0)} {n(y0)} {n(x1)} {n(y1)}] /QuadPoints {quad} "
                     f"/C [{hexa(color)}] /T (Reviewer) /Contents ({sub}) /F 4 /AP << /N {a} 0 R >> {extra} >>")

    markup("Highlight", 0, "meaning when content", "#FDE047")
    markup("Underline", 1, "highlights,", P["indigo"])
    markup("StrikeOut", 2, "separate object", P["rose"])
    markup("Squiggly", 3, "removed.", P["teal"])

    # --- sticky note
    ny = y - 14
    nx = M + 6
    r = (nx, ny - 20, nx + 20, ny)

    def note(c):
        c.fill("#FACC15"); c.stroke("#A16207"); c.width(1)
        c.rrect(nx + 1, ny - 19, 18, 18, 3); c.B()
        c.stroke("#A16207"); c.width(1)
        for k in range(3):
            c.move(nx + 4, ny - 6 - k * 4.5); c.line(nx + 16, ny - 6 - k * 4.5); c.S()
    a = ap(d, r, note)
    pg.add_annot(f"<< /Type /Annot /Subtype /Text /Rect [{n(r[0])} {n(r[1])} {n(r[2])} {n(r[3])}] /Name /Comment /Contents (Sticky note: remember to cite the source.) /T (Reviewer) /C [1 0.85 0] /Open false /F 28 /AP << /N {a} 0 R >> >>")
    pg.text(t.sans, 9, nx + 30, ny - 13, "Text annotation (sticky note)", color=P["slate"])

    # --- link annotations
    def card(x, ytop, w, h, title, sub, color):
        box(pg, x, ytop - h, w, h, fill=P["white"], stroke=P["mist"], r=8)
        box(pg, x, ytop - h, 4, h, fill=color, r=0)
        pg.text(t.sans_b, 11, x + 16, ytop - 24, title, color=P["ink"])
        pg.text(t.sans, 9, x + 16, ytop - 40, sub, color=P["slate"])
    cw = (col_w - 20) / 2
    top = ny - 46
    card(M, top, cw, 66, "URI link", "Opens https://github.com in a browser", P["indigo"])
    pg.add_annot(f"<< /Type /Annot /Subtype /Link /Rect [{n(M)} {n(top - 66)} {n(M + cw)} {n(top)}] /Border [0 0 0] /A << /S /URI /URI (https://github.com) >> /F 4 >>")
    card(M + cw + 20, top, cw, 66, "GoTo link", "Jumps to page 2 at an explicit /XYZ", P["teal"])
    pg.add_annot(f"<< /Type /Annot /Subtype /Link /Rect [{n(M + cw + 20)} {n(top - 66)} {n(M + 2 * cw + 20)} {n(top)}] /Border [0 0 0] "
                 f"/Dest [{pg2.ref} 0 R /XYZ 54 780 null] /F 4 >>")

    # --- FreeText
    fy = top - 96
    fx, fw, fh = M, 250, 50
    fr = (fx, fy - fh, fx + fw, fy)

    def free(c):
        c.fill("#FEF3C7"); c.stroke(P["amber"]); c.width(1.2); c.rrect(fx + 0.6, fy - fh + 0.6, fw - 1.2, fh - 1.2, 6); c.B()
        c.text(helv, 11, fx + 10, fy - 20, "Free text: typeset in the", color=P["ink"], kern=False)
        c.text(helv, 11, fx + 10, fy - 34, "annotation's own font.", color=P["ink"], kern=False)
    a = ap(d, fr, free)
    pg.add_annot(f"<< /Type /Annot /Subtype /FreeText /Rect [{n(fr[0])} {n(fr[1])} {n(fr[2])} {n(fr[3])}] /DA (0 g /Helv 11 Tf) /Contents (Free text) /F 4 /AP << /N {a} 0 R >> >>")
    # --- Stamp
    sx, sy_, sw, sh = M + 290, fy - 52, 180, 52
    sr = (sx, sy_, sx + sw, sy_ + sh)

    def stamp(c):
        c.gs(CA=0.9, ca=0.9)
        c.stroke(P["rose"]); c.width(3); c.rrect(sx + 2, sy_ + 2, sw - 4, sh - 4, 8); c.S()
        c.width(1); c.rrect(sx + 6, sy_ + 6, sw - 12, sh - 12, 5); c.S()
        w = helv_b.width("APPROVED", 24)
        c.text(helv_b, 24, sx + (sw - w) / 2, sy_ + 17, "APPROVED", color=P["rose"], kern=False)
    a = ap(d, sr, stamp)
    pg.add_annot(f"<< /Type /Annot /Subtype /Stamp /Rect [{n(sr[0])} {n(sr[1])} {n(sr[2])} {n(sr[3])}] /Name /Approved /Contents (Approved) /F 4 /AP << /N {a} 0 R >> >>")

    # --- Ink
    iy = fy - 90
    ir = (M, iy - 80, M + col_w, iy)
    pts = []
    for k in range(0, 121):
        u = k / 120
        pts.append((M + 20 + u * (col_w - 40), iy - 40 + math.sin(u * 14) * 22 * math.sin(u * math.pi)))

    def ink(c):
        c.stroke(P["violet"]); c.width(2.4); c.cap(1); c.join(1)
        c.move(*pts[0])
        for p in pts[1:]:
            c.line(*p)
        c.S()
    a = ap(d, ir, ink)
    flat = " ".join(f"{n(x)} {n(y)}" for x, y in pts)
    pg.add_annot(f"<< /Type /Annot /Subtype /Ink /Rect [{n(ir[0])} {n(ir[1])} {n(ir[2])} {n(ir[3])}] /InkList [[{flat}]] /C [{hexa(P['violet'])}] /BS << /W 2.4 >> /F 4 /AP << /N {a} 0 R >> >>")
    pg.text(t.sans, 9, M, iy - 94, "Ink annotation: freehand stroke stored as an /InkList polyline.", color=P["slate"])

    # --- Square / Circle / Line / Polygon
    shapes_y = iy - 124
    shapes = [("Square", P["sky"]), ("Circle", P["emerald"]), ("Line", P["orange"]), ("Polygon", P["violet"])]
    sw_ = (col_w - 3 * 14) / 4
    for k, (sub, colr) in enumerate(shapes):
        x = M + k * (sw_ + 14)
        r = (x, shapes_y - 70, x + sw_, shapes_y)
        cx0, cy0, cx1, cy1 = x + 8, shapes_y - 62, x + sw_ - 8, shapes_y - 8

        def draw(c, sub=sub, colr=colr, cx0=cx0, cy0=cy0, cx1=cx1, cy1=cy1):
            c.fill(lighten(colr, 0.7)); c.stroke(colr); c.width(2)
            if sub == "Square":
                c.rect(cx0, cy0, cx1 - cx0, cy1 - cy0); c.B()
            elif sub == "Circle":
                c.ellipse((cx0 + cx1) / 2, (cy0 + cy1) / 2, (cx1 - cx0) / 2, (cy1 - cy0) / 2); c.B()
            elif sub == "Line":
                c.move(cx0, cy0); c.line(cx1, cy1); c.S()
                c.fill(colr); c.move(cx1, cy1); c.line(cx1 - 12, cy1 - 3); c.line(cx1 - 3, cy1 - 12); c.close(); c.f()
            else:
                c.move(cx0, cy0); c.line(cx1, cy0 + 8); c.line(cx1 - 14, cy1); c.line(cx0 + 10, cy1 - 12); c.close(); c.B()
        a = ap(d, r, draw)
        rr = f"/Rect [{n(r[0])} {n(r[1])} {n(r[2])} {n(r[3])}]"
        if sub == "Line":
            extra = f"/L [{n(cx0)} {n(cy0)} {n(cx1)} {n(cy1)}] /LE [/None /ClosedArrow]"
        elif sub == "Polygon":
            extra = f"/Vertices [{n(cx0)} {n(cy0)} {n(cx1)} {n(cy0 + 8)} {n(cx1 - 14)} {n(cy1)} {n(cx0 + 10)} {n(cy1 - 12)}]"
        else:
            extra = ""
        pg.add_annot(f"<< /Type /Annot /Subtype /{sub} {rr} {extra} /C [{hexa(colr)}] /IC [{hexa(lighten(colr, 0.7))}] /F 4 /AP << /N {a} 0 R >> >>")
        pg.text(t.sans_b, 9, x, shapes_y - 84, sub, color=P["ink"])

    # --- page 2 (link target)
    background(pg2, P["paper"])
    page_header(pg2, t, "Link target", "Interactive  /  01", 2, 2)
    paragraph(pg2, t.sans, 11, M, pg2.h - 120, pg2.w - 2 * M,
              "You followed the GoTo link on page 1. The destination is an explicit /XYZ array, [page /XYZ left top zoom], "
              "with zoom null so the viewer keeps its current magnification.", color=P["slate"], leading=16)
    d.save(path)


OUT = "10-annotations.pdf"
