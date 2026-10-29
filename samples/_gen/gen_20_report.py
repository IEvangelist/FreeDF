from theme import *

LOREM = [
    "Document technology rarely makes headlines, yet it quietly carries contracts, invoices, manuals and archives for billions of people. This report reviews how a mature page-description format stays reliable across decades of devices, operating systems and software generations.",
    "Three forces explain its longevity: a small, fully specified imaging model; a conservative file structure that tolerates partial damage; and an ecosystem of independent implementations that keep each other honest. Each is examined in the sections that follow.",
    "The findings are drawn from a corpus of 4,812 public documents and from the behaviour of four independent renderers. Where results differ we say so, and where a difference is a bug we link it to the clause of the specification that decides the matter.",
]
SECTIONS = ["Executive summary", "Findings", "Outlook", "Method and sources"]


def build(path):
    d = Doc("Annual report: The state of the page", author="Pdf sample generator", subject="Multi-page report with cover, contents, columns, tables, charts, outlines and internal links", keywords="report, outlines, links, charts", lang="en-US")
    t = Type(d)
    pages = [d.page(*A4) for _ in range(6)]
    cover, toc, p1, p2, p3, p4 = pages
    total = len(pages)

    # cover
    c = cover
    background(c, P["ink"])
    c.q(); c.rect(0, 0, c.w, c.h); c.clip()
    c.sh(radial(c.w * 0.85, c.h * 0.78, 0, c.w * 0.85, c.h * 0.78, 520, [(0, "#6366F1"), (0.45, "#312E81"), (1, P["ink"])]))
    c.q(); c.gs(CA=0.18); c.stroke("#FFFFFF"); c.width(0.8)
    for k in range(1, 12):
        c.circle(c.w * 0.85, c.h * 0.78, 30 + k * 38); c.S()
    c.Q(); c.Q()
    logo_mark(c, M, c.h - 110, 34, a="#818CF8", b="#C084FC")
    c.text(t.sans_b, 10, M + 48, c.h - 98, "NORTHLIGHT RESEARCH", color="#C7D2FE", tracking=2.4, kern=False)
    c.text(t.serif_b, 66, M, 440, "The State", color="#FFFFFF")
    c.text(t.serif_b, 66, M, 372, "of the Page", color="#FFFFFF")
    c.text(t.serif_i, 20, M, 322, "How a 30-year-old format keeps getting better", color="#C7D2FE")
    rule(c, M, 140, c.w - M, color="#4338CA")
    c.text(t.sans, 10, M, 112, "ANNUAL REPORT  ·  2026 EDITION", color="#A5B4FC", tracking=1.6, kern=False)
    c.text(t.sans, 10, c.w - M - t.sans.width("Not for redistribution — a sample document", 10), 112, "Not for redistribution — a sample document", color="#818CF8")

    def furniture(pg, num, title):
        page_header(pg, t, title, "The state of the page", num, total)

    # contents
    furniture(toc, 2, "Contents")
    y = toc.h - 170
    for i, (name, pg) in enumerate(zip(SECTIONS, (p1, p2, p3, p4))):
        toc.text(t.serif_b, 40, M, y - 6, f"0{i + 1}", color=P["mist"])
        toc.text(t.serif_b, 18, M + 70, y, name, color=P["ink"])
        s = str(i + 3)
        toc.text(t.sans, 12, toc.w - M - t.sans.width(s, 12), y, s, color=P["slate"])
        dots_x0 = M + 78 + t.serif_b.width(name, 18)
        toc.q(); toc.stroke(P["muted"]); toc.width(0.8); toc.cap(1); toc.dash([0.1, 4]); toc.move(dots_x0, y + 2); toc.line(toc.w - M - 24, y + 2); toc.S(); toc.Q()
        toc.add_annot(f"<< /Type /Annot /Subtype /Link /Rect [{n(M)} {n(y - 14)} {n(toc.w - M)} {n(y + 24)}] /Border [0 0 0] /Dest [{pg.ref} 0 R /XYZ 0 {n(pg.h)} 0] >>")
        y -= 78
    box(toc, M, 110, toc.w - 2 * M, 70, fill=P["paper"], r=8)
    paragraph(toc, t.sans, 10, M + 18, 156, toc.w - 2 * M - 36, "Every entry above is a link annotation with a GoTo destination, and the same four sections are listed in the document outline (bookmarks panel).", leading=15, color=P["slate"])

    # section 1: executive summary with drop cap & two columns
    furniture(p1, 3, SECTIONS[0])
    eyebrow(p1, t, M, p1.h - 110, "Key message")
    colw = (p1.w - 2 * M - 24) / 2
    ytop = p1.h - 150
    # drop cap
    p1.text(t.serif_b, 64, M - 2, ytop - 46, "D", color=P["indigo"])
    lines = wrap(t.sans, 10.5, LOREM[0][1:], colw - 50)
    y = ytop
    for i, ln in enumerate(lines):
        x = M + 50 if i < 4 else M
        if i == 3:
            pass
        p1.text(t.sans, 10.5, x, y, ln, color=P["ink"])
        y -= 15
    y = paragraph(p1, t.sans, 10.5, M, y - 8, colw, LOREM[1], leading=15, align="justify", color=P["ink"])
    paragraph(p1, t.sans, 10.5, M + colw + 24, ytop, colw, LOREM[2], leading=15, align="justify", color=P["ink"])
    # pull quote
    qy = p1.h - 400
    p1.q(); p1.fill(P["indigo"]); p1.rect(M, qy - 90, 4, 110); p1.f(); p1.Q()
    p1.text(t.serif_i, 22, M + 22, qy, "“Longevity is a design choice,", color=P["ink"])
    p1.text(t.serif_i, 22, M + 22, qy - 30, "not an accident of adoption.”", color=P["ink"])
    p1.text(t.sans, 9, M + 22, qy - 60, "— Dr. Imani Okafor, lead author", color=P["muted"])
    # stat tiles
    stats = [("4,812", "documents analysed"), ("97.3 %", "rendered identically"), ("12", "format clauses clarified"), ("30 yrs", "of backward compatibility")]
    tw = (p1.w - 2 * M - 3 * 12) / 4
    for i, (v, l) in enumerate(stats):
        x = M + i * (tw + 12)
        box(p1, x, 190, tw, 90, fill=P["paper"], stroke=P["mist"], r=8)
        p1.text(t.serif_b, 24, x + 14, 238, v, color=P["indigo"])
        paragraph(p1, t.sans, 9, x + 14, 218, tw - 24, l, leading=12, color=P["slate"])

    # section 2: table + bar chart
    furniture(p2, 4, SECTIONS[1])
    paragraph(p2, t.sans, 10.5, M, p2.h - 114, p2.w - 2 * M, "Compatibility improved across every feature family we measured. The table lists the share of corpus documents that used each feature and the share rendered without visible difference.", leading=15, color=P["slate"])
    rows = [("Text and fonts", 99.1, 98.2), ("Vector graphics", 87.4, 97.9), ("Transparency", 41.6, 94.1), ("Colour management", 33.8, 91.7), ("Forms and annotations", 22.5, 96.4), ("Encryption", 9.3, 99.0)]
    ty = p2.h - 190
    box(p2, M, ty - 8, p2.w - 2 * M, 28, fill=P["ink"], r=6)
    for x, h in ((M + 14, "FEATURE FAMILY"), (M + 230, "USAGE"), (M + 330, "IDENTICAL")):
        p2.text(t.sans_b, 8, x, ty + 3, h, color="#FFFFFF", tracking=1.2, kern=False)
    for i, (name, use, ok) in enumerate(rows):
        yy = ty - 36 - i * 30
        if i % 2 == 0:
            box(p2, M, yy - 9, p2.w - 2 * M, 28, fill=P["paper"])
        p2.text(t.sans, 10.5, M + 14, yy, name, color=P["ink"])
        p2.text(t.sans, 10.5, M + 230, yy, f"{use:.1f} %", color=P["slate"])
        p2.text(t.sans_b, 10.5, M + 330, yy, f"{ok:.1f} %", color=P["emerald"] if ok > 95 else P["amber"])
        bw = 90
        box(p2, M + 400, yy - 1, bw, 7, fill=P["mist"], r=3.5)
        box(p2, M + 400, yy - 1, bw * ok / 100, 7, fill=P["indigo"], r=3.5)
    # bar chart
    cy = 150
    eyebrow(p2, t, M, 372, "Figure 1")
    p2.text(t.sans_b, 12, M, 354, "Identical rendering by feature family, %", color=P["ink"])
    cx0, cx1 = M + 28, p2.w - M
    for g in range(0, 101, 25):
        yy = cy + g * 1.6
        rule(p2, cx0, yy, cx1, color=P["mist"], w=0.5)
        p2.text(t.sans, 7.5, cx0 - 8 - t.sans.width(str(g), 7.5), yy - 2.5, str(g), color=P["muted"])
    slot = (cx1 - cx0) / len(rows)
    cols = [P["indigo"], P["violet"], P["sky"], P["teal"], P["amber"], P["rose"]]
    for i, (name, use, ok) in enumerate(rows):
        x = cx0 + i * slot + slot * 0.2
        box(p2, x, cy, slot * 0.6, ok * 1.6, fill=cols[i], r=3)
        p2.text(t.sans_b, 8.5, x + slot * 0.3 - t.sans_b.width(f"{ok:.0f}", 8.5) / 2, cy + ok * 1.6 + 5, f"{ok:.0f}", color=P["ink"])
        lab = name.split(" ")[0]
        p2.text(t.sans, 8, x + slot * 0.3 - t.sans.width(lab, 8) / 2, cy - 14, lab, color=P["slate"])

    # section 3: line chart
    furniture(p3, 5, SECTIONS[2])
    paragraph(p3, t.sans, 10.5, M, p3.h - 114, p3.w - 2 * M, "Adoption of tagged, accessible documents is the clearest growth story. If the current trend holds, a majority of new documents will carry a structure tree by 2029.", leading=15, color=P["slate"])
    xs0, xs1, ys0, ys1 = M + 30, p3.w - M, 420, p3.h - 210
    yrs = list(range(2020, 2030))
    vals = [8, 12, 17, 24, 31, 39, 46, 53, 59, 64]
    for g in range(0, 81, 20):
        yy = ys0 + g / 80 * (ys1 - ys0)
        rule(p3, xs0, yy, xs1, color=P["mist"], w=0.5)
        p3.text(t.sans, 7.5, xs0 - 8 - t.sans.width(str(g), 7.5), yy - 2.5, f"{g}", color=P["muted"])
    pts = [(xs0 + i * (xs1 - xs0) / 9, ys0 + v / 80 * (ys1 - ys0)) for i, v in enumerate(vals)]
    p3.q(); p3.move(pts[0][0], ys0)
    for pt in pts:
        p3.line(*pt)
    p3.line(pts[-1][0], ys0); p3.close(); p3.clip()
    p3.sh(axial(0, ys0, 0, ys1, [(0, "#FFFFFF"), (1, "#C7D2FE")]))
    p3.Q()
    p3.q(); p3.stroke(P["indigo"]); p3.width(2.2); p3.join(1); p3.move(*pts[0])
    for pt in pts[1:6]:
        p3.line(*pt)
    p3.S(); p3.dash([5, 4]); p3.move(*pts[5])
    for pt in pts[6:]:
        p3.line(*pt)
    p3.S(); p3.Q()
    for i, (px, py) in enumerate(pts):
        p3.q(); p3.fill("#FFFFFF"); p3.stroke(P["indigo"]); p3.width(1.5); p3.circle(px, py, 3); p3.B(); p3.Q()
        p3.text(t.sans, 7.5, px - t.sans.width(str(yrs[i]), 7.5) / 2, ys0 - 14, str(yrs[i]), color=P["muted"])
    p3.text(t.sans_i, 8.5, pts[6][0] + 6, pts[6][1] - 22, "dashed: projection", color=P["slate"])
    eyebrow(p3, t, M, ys1 + 36, "Figure 2")
    p3.text(t.sans_b, 12, M, ys1 + 18, "Share of new documents with a structure tree, %", color=P["ink"])
    cw = (p3.w - 2 * M - 24) / 3
    for i, (h, b, col) in enumerate((("Accessible by default", "Authoring tools tag structure automatically.", P["indigo"]), ("Smaller files", "Object streams and subsetting cut size by a third.", P["teal"]), ("Safer by design", "AES-256 is now the baseline for protected files.", P["amber"]))):
        x = M + i * (cw + 12)
        box(p3, x, 160, cw, 150, fill=P["paper"], r=8)
        p3.q(); p3.fill(col); p3.rrect(x, 160 + 146, cw, 4, 2); p3.f(); p3.Q()
        p3.text(t.serif_b, 14, x + 14, 280, h, color=P["ink"])
        paragraph(p3, t.sans, 9.5, x + 14, 258, cw - 28, b, leading=14, color=P["slate"])

    # section 4: method
    furniture(p4, 6, SECTIONS[3])
    y = paragraph(p4, t.sans, 10.5, M, p4.h - 114, p4.w - 2 * M, "Documents were sampled from public repositories, converted to a common baseline where necessary, and rendered at 96 dpi by four independent engines. A document counts as identical when no channel differs by more than 2 % over more than 2 % of pixels.", leading=15, color=P["slate"])
    refs = [("ISO 32000-2:2020", "Document management — Portable document format — Part 2: PDF 2.0"), ("ISO 14289-1:2014", "PDF/UA-1 — accessible PDF"), ("ISO 19005-4:2020", "PDF/A-4 — archival"), ("RFC 8118", "The application/pdf media type")]
    eyebrow(p4, t, M, y - 28, "References")
    y -= 56
    for code, desc in refs:
        p4.text(t.mono, 9.5, M, y, code, color=P["indigo"])
        p4.text(t.sans, 10, M + 130, y, desc, color=P["ink"])
        rule(p4, M, y - 9, p4.w - M)
        y -= 28
    box(p4, M, 110, p4.w - 2 * M, 86, fill=P["ink"], r=10)
    p4.text(t.serif_b, 16, M + 22, 166, "Colophon", color="#FFFFFF")
    paragraph(p4, t.sans, 9.5, M + 22, 146, p4.w - 2 * M - 44, "Set in Source Serif 4 and Source Sans 3 (embedded CFF subsets). Charts are drawn with path operators and shadings; there are no raster images in this file.", leading=14, color="#CBD5E1")

    # outlines
    items = [("Cover", cover), ("Contents", toc)] + [(f"{i + 1}. {s}", pg) for i, (s, pg) in enumerate(zip(SECTIONS, (p1, p2, p3, p4)))]
    root = d.new()
    nums = [d.new() for _ in items]
    for i, ((title, pg), num) in enumerate(zip(items, nums)):
        links = f"/Prev {nums[i - 1]} 0 R " if i else ""
        links += f"/Next {nums[i + 1]} 0 R " if i < len(items) - 1 else ""
        d.put(num, f"<< /Title {pstr(title)} /Parent {root} 0 R {links}/Dest [{pg.ref} 0 R /Fit] >>")
    d.put(root, f"<< /Type /Outlines /First {nums[0]} 0 R /Last {nums[-1]} 0 R /Count {len(items)} >>")
    d.cat.append(f"/Outlines {root} 0 R /PageMode /UseOutlines /ViewerPreferences << /DisplayDocTitle true >>")
    d.save(path)


OUT = "20-report.pdf"
