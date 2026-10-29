from theme import *

CHAPTERS = [
    ("Foreword", "roman", "Why navigation matters in long documents."),
    ("Introduction", "roman", "Bookmarks, destinations and labels: three views of one idea."),
    ("Objects and syntax", "dec", "Eight object types, indirect references and the xref table."),
    ("Page content", "dec", "Content streams hold operators; resources are looked up by name."),
    ("Fonts and text", "dec", "Simple and composite fonts, encodings, CMaps and the text-state parameters."),
    ("Appendix: Glossary", "app", "Short definitions of the terms used in this sample."),
]


def build(path):
    d = Doc("Navigation: bookmarks, named destinations, page labels", subject="Outlines (nested, coloured, collapsed), name tree of destinations, page-label ranges, GoTo links, PageMode UseOutlines", lang="en-US")
    t = Type(d)
    pages = []
    for i in range(len(CHAPTERS) + 1):
        pages.append(d.page(*A4))
    toc = pages[0]
    labels = ["i", "ii", "1", "2", "3", "4", "A-1"]

    # ---- table of contents (page 1)
    background(toc, P["paper"])
    toc.text(t.sans_b, 8, M, toc.h - 36, "NAVIGATION  /  SAMPLE 12", color=P["indigo"], tracking=1.6, kern=False)
    rule(toc, M, toc.h - 44, toc.w - M)
    toc.text(t.serif_b, 36, M, toc.h - 110, "Contents", color=P["ink"])
    paragraph(toc, t.sans, 11, M, toc.h - 138, toc.w - 2 * M - 120,
              "Open the bookmarks panel to see the outline. Each entry below is a GoTo link to a named destination; the printed page numbers are page labels (roman, decimal, then an A- prefixed appendix).",
              color=P["slate"], leading=16)
    y = toc.h - 220
    for i, (title, _, blurb) in enumerate(CHAPTERS):
        pidx = i + 1
        lab = labels[pidx]
        toc.text(t.serif, 16, M, y, title, color=P["ink"])
        toc.text(t.sans, 9, M, y - 15, blurb.split(". ")[0][:92], color=P["muted"], kern=False)
        w = t.sans_b.width(lab, 11)
        toc.text(t.sans_b, 11, toc.w - M - w, y, lab, color=P["indigo"])
        # dot leader
        x0 = M + t.serif.width(title, 16) + 10
        toc.q(); toc.stroke(P["mist"]); toc.width(1.2); toc.cap(1); toc.dash([0, 4]); toc.move(x0, y + 1); toc.line(toc.w - M - w - 10, y + 1); toc.S(); toc.Q()
        toc.add_annot(f"<< /Type /Annot /Subtype /Link /Rect [{n(M)} {n(y - 20)} {n(toc.w - M)} {n(y + 14)}] /Border [0 0 0] /Dest (chapter{pidx}) >>")
        y -= 56

    # ---- chapter pages
    colors = [P["indigo"], P["teal"], P["violet"], P["sky"], P["orange"], P["emerald"]]
    for i, (title, style, blurb) in enumerate(CHAPTERS):
        pg = pages[i + 1]
        c = colors[i]
        background(pg, P["white"])
        box(pg, 0, pg.h - 220, pg.w, 220, fill=c)
        pg.q(); pg.gs(ca=0.18); pg.fill("#FFFFFF")
        pg.circle(pg.w - 60, pg.h - 60, 150); pg.f(); pg.circle(pg.w - 20, pg.h - 200, 80); pg.f(); pg.Q()
        pg.text(t.sans_b, 9, M, pg.h - 60, f"CHAPTER {i + 1}  ·  PAGE LABEL {labels[i + 1]}", color="#FFFFFF", tracking=1.6, kern=False)
        pg.text(t.serif_b, 40, M, pg.h - 150, title, color="#FFFFFF")
        paragraph(pg, t.serif, 15, M, pg.h - 290, pg.w - 2 * M - 40, blurb, color=P["ink"], leading=24)
        paragraph(pg, t.sans, 10.5, M, pg.h - 400, (pg.w - 2 * M - 30) / 2,
                  "Navigation targets are resolved lazily: a viewer reads the name tree, finds the destination array, then scrolls to the page, honouring the fit mode in the array. Because destinations are named, pages can be reordered without breaking links elsewhere.",
                  color=P["slate"], leading=15, align="justify")
        pg.text(t.sans, 9, pg.w / 2 - 6, 36, labels[i + 1], color=P["muted"])
        # back link
        box(pg, M, 56, 110, 26, fill=P["paper"], stroke=P["mist"], r=13)
        pg.text(t.sans_b, 9, M + 16, 65, "← Contents", color=P["indigo"], kern=False)
        pg.add_annot(f"<< /Type /Annot /Subtype /Link /Rect [{n(M)} 56 {n(M + 110)} 82] /Border [0 0 0] /Dest (contents) >>")

    # ---- name tree of destinations
    dests = [("contents", f"[{toc.ref} 0 R /Fit]")] + [(f"chapter{i + 1}", f"[{pages[i + 1].ref} 0 R /XYZ 0 {n(pages[i + 1].h)} 0]") for i in range(len(CHAPTERS))]
    dests.sort(key=lambda a: a[0])
    names = " ".join(f"{pstr(k)} {v}" for k, v in dests)
    dnode = d.add(f"<< /Names [{names}] /Limits [{pstr(dests[0][0])} {pstr(dests[-1][0])}] >>")
    d.cat.append(f"/Names << /Dests << /Kids [{dnode} 0 R] >> >>")

    # ---- outline tree
    root = d.new()
    items = {}
    spec = [("Contents", "contents", 0, None, []),
            ("Front matter", None, 0, None, [("Foreword", "chapter1", 0, None, []), ("Introduction", "chapter2", 0, None, [])]),
            ("Main text", None, 1, None, [("Objects and syntax", "chapter3", 0, None, []), ("Page content", "chapter4", 0, None, []), ("Fonts and text", "chapter5", 2, None, [])]),
            ("Appendix: Glossary", "chapter6", 1, (0.88, 0.11, 0.28), [])]

    def make(nodes, parent):
        refs = [d.new() for _ in nodes]
        for k, (title, dest, style, color, kids) in enumerate(nodes):
            r = refs[k]
            cnt = 0
            kid_s = ""
            if kids:
                first, last, total = make(kids, r)
                kid_s = f"/First {first} 0 R /Last {last} 0 R /Count {-total if title == 'Main text' else total}"
            dest_s = f"/Dest {pstr(dest)}" if dest else f"/Dest [{pages[0].ref} 0 R /Fit]"
            if dest is None and kids:
                dest_s = f"/Dest {pstr('chapter' + str(CHAPTERS.index((next(c for c in CHAPTERS if c[0] == kids[0][0])))+1))}"
            col = f"/C [{' '.join(n(v) for v in color)}]" if color else ""
            prev = f"/Prev {refs[k - 1]} 0 R" if k else ""
            nxt = f"/Next {refs[k + 1]} 0 R" if k + 1 < len(refs) else ""
            d.put(r, f"<< /Title {utf16(title)} /Parent {parent} 0 R {dest_s} /F {style} {col} {prev} {nxt} {kid_s} >>")
        total = len(nodes) + sum(abs(1) for _ in [])
        return refs[0], refs[-1], len(nodes)
    first, last, total = make(spec, root)
    d.put(root, f"<< /Type /Outlines /First {first} 0 R /Last {last} 0 R /Count {total + 2} >>")
    d.cat.append(f"/Outlines {root} 0 R /PageMode /UseOutlines")

    # ---- page labels
    d.cat.append("/PageLabels << /Nums [0 << /S /r >> 2 << /S /D >> 6 << /S /D /P (A-) /St 1 >>] >>")
    d.cat.append("/ViewerPreferences << /DisplayDocTitle true >>")
    d.save(path)


OUT = "12-navigation.pdf"
