from theme import *


class Node:
    def __init__(self, role, parent=None, alt=None, attrs="", actual=None):
        self.role, self.parent, self.alt, self.attrs, self.actual = role, parent, alt, attrs, actual
        self.kids = []
        self.ref = None
        if parent:
            parent.kids.append(self)


class Tagger:
    def __init__(self, doc):
        self.doc = doc
        self.counts = {}
        self.parents = {}

    def mc(self, pg, node, draw, props=""):
        m = self.counts.get(pg, 0)
        self.counts[pg] = m + 1
        pg.raw(f"/{node.role} << /MCID {m} {props}>> BDC")
        draw()
        pg.raw("EMC")
        node.kids.append(("mc", pg, m))
        self.parents.setdefault(pg, {})[m] = node

    def artifact(self, pg, draw, typ="Pagination"):
        pg.raw(f"/Artifact << /Type /{typ} >> BDC")
        draw()
        pg.raw("EMC")

    def build(self, root):
        d = self.doc
        order = []

        def alloc(nd):
            nd.ref = d.new()
            for k in nd.kids:
                if isinstance(k, Node):
                    alloc(k)
        alloc(root)

        def emit(nd, parent_ref):
            kids = []
            pg = None
            for k in nd.kids:
                if isinstance(k, Node):
                    emit(k, nd.ref)
                    kids.append(f"{k.ref} 0 R")
                else:
                    _, p, m = k
                    kids.append(f"<< /Type /MCR /Pg {p.ref} 0 R /MCID {m} >>")
                    pg = p
            extra = ""
            if nd.alt:
                extra += f" /Alt {utf16(nd.alt)}"
            if nd.actual:
                extra += f" /ActualText {utf16(nd.actual)}"
            if nd.attrs:
                extra += f" /A {nd.attrs}"
            pgref = f" /Pg {pg.ref} 0 R" if pg is not None else ""
            par = f" /P {parent_ref} 0 R" if parent_ref else ""
            d.put(nd.ref, f"<< /Type /StructElem /S /{nd.role}{par}{pgref}{extra} /K [{' '.join(kids)}] >>")
        emit(root, None)
        # root's parent is the StructTreeRoot
        nums = []
        for i, pg in enumerate(d.pages):
            pg.extra += f" /StructParents {i} /Tabs /S"
            pm = self.parents.get(pg, {})
            arr = " ".join(f"{pm[m].ref} 0 R" for m in range(self.counts.get(pg, 0)))
            nums.append(f"{i} [{arr}]")
        str_root = d.new()
        d.put(str_root, f"<< /Type /StructTreeRoot /K {root.ref} 0 R /ParentTree << /Nums [{' '.join(nums)}] >> /RoleMap << /Callout /P >> /ParentTreeNextKey {len(d.pages)} >>")
        s = d.objs[root.ref].decode("latin-1")
        d.put(root.ref, s.replace(f"/S /{root.role}", f"/S /{root.role} /P {str_root} 0 R", 1))
        d.cat.append(f"/StructTreeRoot {str_root} 0 R /MarkInfo << /Marked true /Suspects false >> /ViewerPreferences << /DisplayDocTitle true >>")


def build(path):
    d = Doc("Writing accessible PDFs", author="Pdf sample generator", subject="Tagged PDF: structure tree, reading order, headings, list, table, figure with alt text, artifacts, document language",
            keywords="accessibility, PDF/UA, tagged", lang="en-US")
    t = Type(d)
    tg = Tagger(d)
    doc = Node("Document")
    p1, p2 = d.page(*A4), d.page(*A4)
    W = p1.w - 2 * M

    # ---- page 1
    tg.artifact(p1, lambda: background(p1, P["paper"]), "Background")
    tg.artifact(p1, lambda: (eyebrow(p1, t, M, p1.h - 36, "Accessibility  /  Tagged PDF"), rule(p1, M, p1.h - 44, p1.w - M)))
    h1 = Node("H1", doc)
    tg.mc(p1, h1, lambda: p1.text(t.serif_b, 34, M, p1.h - 100, "Writing accessible PDFs", color=P["ink"]))
    lead = Node("P", doc)
    tg.mc(p1, lead, lambda: paragraph(p1, t.serif_i, 15, M, p1.h - 135, W - 40,
          "A tagged PDF carries a logical structure beside its visual one, so assistive technology can read it in the right order.", color=P["slate"], leading=22))
    h2 = Node("H2", doc)
    tg.mc(p1, h2, lambda: p1.text(t.sans_b, 15, M, p1.h - 215, "What makes a document accessible", color=P["indigo_d"]))
    p = Node("P", doc)
    tg.mc(p1, p, lambda: paragraph(p1, t.sans, 11, M, p1.h - 240, W - 20,
          "Marked content in the page stream is linked to structure elements through the parent tree. Reading order comes from the structure tree rather than from drawing order, and decoration such as rules and page furniture is marked as an artifact so it is skipped.", color=P["ink"], leading=16.5))
    lst = Node("L", doc)
    items = ["Set the document language and title (/Lang, /Title, DisplayDocTitle).",
             "Mark every heading with H1 to H6 and keep the levels in order.",
             "Give each figure alternative text with /Alt.",
             "Use real table structure: Table, TR, TH with scope, TD."]
    yy = p1.h - 330
    for it in items:
        li = Node("LI", lst)
        lbl = Node("Lbl", li)
        body = Node("LBody", li)

        def bullet(yy=yy):
            p1.q(); p1.fill(P["indigo"]); p1.circle(M + 4, yy + 3.5, 2.6); p1.f(); p1.Q()
        tg.mc(p1, lbl, bullet)
        tg.mc(p1, body, lambda it=it, yy=yy: p1.text(t.sans, 11, M + 18, yy, it, color=P["ink"], kern=True))
        yy -= 24
    # figure: bar chart
    fig = Node("Figure", doc, alt="Bar chart of the share of tagged PDFs by year: 18 percent in 2018, 27 in 2020, 41 in 2022 and 58 percent in 2024.")
    fy, fh = yy - 40, 200

    def chart():
        box(p1, M, fy - fh, W, fh, fill=P["white"], stroke=P["mist"], r=10)
        vals = [(2018, 18), (2020, 27), (2022, 41), (2024, 58)]
        bw = 60
        gap = (W - 80 - 4 * bw) / 3
        for i, (yr, v) in enumerate(vals):
            x = M + 40 + i * (bw + gap)
            h = v / 60 * (fh - 70)
            p1.q(); p1.fill(mix(P["indigo"], P["violet"], i / 3)); p1.rrect(x, fy - fh + 34, bw, h, 5); p1.f(); p1.Q()
            p1.text(t.sans_b, 11, x + bw / 2 - t.sans_b.width(f"{v}%", 11) / 2, fy - fh + 34 + h - 18, f"{v}%", color="#FFFFFF" if h > 30 else P["ink"])
            p1.text(t.sans, 9.5, x + bw / 2 - t.sans.width(str(yr), 9.5) / 2, fy - fh + 14, str(yr), color=P["slate"])
    tg.mc(p1, fig, chart)
    cap = Node("Caption", doc)
    tg.mc(p1, cap, lambda: p1.text(t.sans_i, 9.5, M, fy - fh - 16, "Figure 1. Illustrative share of tagged PDFs by year (sample data).", color=P["slate"]))
    tg.artifact(p1, lambda: p1.text(t.sans, 8, p1.w - M - 6, 30, "1", color=P["muted"]))

    # ---- page 2: table
    tg.artifact(p2, lambda: background(p2, P["paper"]), "Background")
    tg.artifact(p2, lambda: (eyebrow(p2, t, M, p2.h - 36, "Accessibility  /  Tagged PDF"), rule(p2, M, p2.h - 44, p2.w - M)))
    h = Node("H2", doc)
    tg.mc(p2, h, lambda: p2.text(t.sans_b, 20, M, p2.h - 90, "Structure types at a glance", color=P["indigo_d"]))
    tab = Node("Table", doc, attrs="<< /O /Table /Summary (Structure element types, their purpose and an example) >>")
    cols = [(160, "Element"), (200, "Purpose"), (W - 360, "Example")]
    rows = [("Document", "Root of the tree", "Whole file"), ("H1 – H6", "Headings", "Chapter title"), ("P", "Paragraph", "Body text"),
            ("L / LI / LBody", "Lists", "Bullets"), ("Figure", "Images, charts", "Chart with /Alt"), ("Table / TR / TH / TD", "Tabular data", "This table"),
            ("Artifact", "Not content", "Running header")]
    ry = p2.h - 130
    rh = 30
    for ri, row in enumerate([[c[1] for c in cols]] + [list(r) for r in rows]):
        tr = Node("TR", tab)
        x = M
        head = ri == 0
        if head:
            tg.artifact(p2, lambda ry=ry: box(p2, M, ry - rh + 8, W, rh, fill=P["ink"]), "Layout")
        elif ri % 2 == 0:
            tg.artifact(p2, lambda ry=ry: box(p2, M, ry - rh + 8, W, rh, fill=P["white"]), "Layout")
        for ci, (cw, _) in enumerate(cols):
            cell = Node("TH" if head else "TD", tr, attrs="<< /O /Table /Scope /Column >>" if head else "")
            txt = row[ci]
            tg.mc(p2, cell, lambda x=x, txt=txt, head=head, ci=ci: p2.text(t.sans_b if (head or ci == 0) else t.sans, 10.5, x + 12, ry - 12, txt, color="#FFFFFF" if head else P["ink"]))
            x += cw
        ry -= rh
    note = Node("P", doc)
    tg.mc(p2, note, lambda: paragraph(p2, t.sans, 10.5, M, ry - 24, W - 40,
          "Role mapping (/RoleMap) can alias custom tags onto the standard ones. Here, Callout maps to P.", color=P["slate"], leading=15))
    tg.artifact(p2, lambda: p2.text(t.sans, 8, p2.w - M - 6, 30, "2", color=P["muted"]))
    tg.build(doc)
    d.save(path, version="1.7")


OUT = "19-tagged-accessible.pdf"
