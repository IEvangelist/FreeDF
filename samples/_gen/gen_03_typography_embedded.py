from theme import *

TEXT = ("Good typography is invisible. The reader notices the argument, never the letterforms; spacing, rhythm and contrast do their work "
        "quietly beneath the surface. A well-set page offers a clear entry point, a steady measure, and enough air around each block "
        "for the eye to rest before it moves on. Kerning adjusts the space between particular pairs of glyphs, so that the optical "
        "colour of a line stays even whether it contains a wide W or a narrow l.")


def build(path):
    d = Doc("Typography: embedded fonts", subject="Embedded OpenType/CFF subsets, kerning, alignment, columns, drop caps", lang="en-US")
    t = Type(d)
    pg = d.page(*A4)
    background(pg, P["paper"])
    page_header(pg, t, "Embedded typography", "Typography  /  01", 1, 2)
    pg.text(t.sans, 11, M, pg.h - 106, "Source Serif 4, Source Sans 3 and Source Code Pro: embedded OpenType (CFF) subsets, GPOS kerning.", color=P["slate"])

    # kerning comparison
    y = pg.h - 170
    eyebrow(pg, t, M, y + 30, "Pair kerning, off versus on")
    cw = (pg.w - 2 * M - 24) / 2
    for i, (label, k) in enumerate((("Kerning off", False), ("Kerning on (GPOS kern feature)", True))):
        x = M + i * (cw + 24)
        box(pg, x, y - 62, cw, 80, fill=P["white"], stroke=P["mist"], r=6)
        pg.text(t.serif_b, 34, x + 14, y - 26, "AVATAR To", color=P["ink"], kern=k)
        pg.text(t.sans, 8, x + 14, y - 52, label, color=P["muted"], tracking=0.4, kern=False)

    # alignment row
    y -= 112
    eyebrow(pg, t, M, y + 6, "Alignment")
    cw = (pg.w - 2 * M - 3 * 14) / 4
    for i, al in enumerate(("left", "center", "right", "justify")):
        x = M + i * (cw + 14)
        box(pg, x, y - 160, cw, 150, fill=P["white"], stroke=P["mist"], r=6)
        paragraph(pg, t.serif, 8.6, x + 10, y - 38, cw - 20, TEXT[:200], leading=12, align=al, color=P["ink"])
        pg.text(t.sans_b, 7, x + 10, y - 24, al.upper(), color=P["indigo"], tracking=1.2, kern=False)

    # two-column with drop cap
    y -= 200
    eyebrow(pg, t, M, y + 6, "Columns and a drop cap")
    colw = (pg.w - 2 * M - 28) / 2
    top = y - 20
    cap_size = 58
    pg.text(t.serif_b, cap_size, M - 1, top - cap_size * 0.66, "G", color=P["indigo"])
    capw = t.serif_b.width("G", cap_size) + 6
    words = (TEXT[1:] + " " + TEXT + " " + TEXT).split(" ")
    lines, cur, per_col = [], "", 11
    for w in words:
        i = len(lines)
        avail = colw - (capw if (i < 4) else 0)
        t_ = (cur + " " + w) if cur else w
        if cur and t.serif.width(t_, 10) > avail:
            lines.append(cur)
            cur = w
        else:
            cur = t_
    lines.append(cur)
    for i, ln in enumerate(lines[:per_col * 2]):
        c_i, r_i = divmod(i, per_col)
        indent = capw if (c_i == 0 and r_i < 4) else 0
        avail = colw - indent
        extra = (avail - t.serif.width(ln, 10)) / ln.count(" ") if ln.count(" ") and i < len(lines) - 1 else 0
        pg.text(t.serif, 10, M + c_i * (colw + 28) + indent, top - 10 - r_i * 14, ln, color=P["ink"], space_extra=max(0, min(extra, 3)))
    # code block + tracking
    y = 150
    box(pg, M, y - 90, pg.w - 2 * M, 100, fill=P["ink"], r=8)
    code = ["BT", "  /F1 12 Tf", "  1 0 0 1 72 720 Tm", "  [<0041> -80 <0056> 40 <0041>] TJ", "ET"]
    for i, ln in enumerate(code):
        pg.text(t.mono, 9.5, M + 18, y - 4 - i * 14, ln, color="#A5B4FC", kern=False)
    pg.text(t.sans_b, 8, pg.w - M - 140, y - 4, "TJ ARRAY KERNING", color=P["muted"], tracking=1.4, kern=False)

    # page 2: scale + unicode
    p2 = d.page(*A4)
    background(p2, P["paper"])
    page_header(p2, t, "Scale, tracking, glyph coverage", "Typography  /  01", 2, 2)
    y = p2.h - 150
    for size, fam in ((46, t.serif_b), (36, t.serif), (24, t.serif_i), (16, t.sans), (12, t.sans_b), (9, t.sans_i)):
        p2.text(fam, size, M, y, "Hamburgefonstiv 0123456789", color=P["ink"])
        p2.text(t.sans, 7.5, p2.w - M - 40, y, f"{size} pt", color=P["muted"], kern=False)
        y -= size * 1.35
    y -= 14
    rule(p2, M, y + 6, p2.w - M)
    eyebrow(p2, t, M, y - 12, "Letterspacing (Tc)")
    for i, tr in enumerate((0, 1.5, 4)):
        p2.text(t.sans_b, 12, M, y - 36 - i * 22, "CAPITALS NEED AIR", color=P["indigo_d"], tracking=tr, kern=False)
        p2.text(t.sans, 8, M + 260, y - 36 - i * 22, f"Tc = {tr}", color=P["muted"], kern=False)
    y -= 120
    eyebrow(p2, t, M, y, "Glyph coverage")
    samples = ["Latin: Zażółć gęślą jaźń — café, naïve, Œuvre, ß, ñ, å, ø, Þ",
               "Greek: Αλφάβητος · α β γ δ ε ζ η θ · Σ Ω Δ",
               "Cyrillic: Съешь же ещё этих мягких французских булок",
               "Symbols: © ® ™ § ¶ † ‡ • … « » ‹ › € £ ¥ ¢ ° ± × ÷ ≈ ≠ ≤ ≥ ∞ → ←"]
    for i, s in enumerate(samples):
        paragraph(p2, t.serif, 13, M, y - 28 - i * 30, pg.w - 2 * M, s, color=P["ink"])
    d.save(path)


OUT = "03-typography-embedded.pdf"

