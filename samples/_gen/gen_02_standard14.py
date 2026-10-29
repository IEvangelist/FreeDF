from theme import *

FONTS = ["Helvetica", "Helvetica-Bold", "Helvetica-Oblique", "Helvetica-BoldOblique", "Times-Roman", "Times-Bold", "Times-Italic",
         "Times-BoldItalic", "Courier", "Courier-Bold", "Courier-Oblique", "Courier-BoldOblique", "Symbol", "ZapfDingbats"]


def build(path):
    d = Doc("The standard 14 fonts", subject="Base-14 Type 1 fonts, text state operators", lang="en-US")
    t = Type(d)
    pg = d.page(*A4)
    background(pg, P["white"])
    page_header(pg, t, "The standard 14 fonts", "Typography  /  02", 1, 1)
    pg.text(t.sans, 11, M, pg.h - 106, "Every conforming reader supplies these Type 1 fonts; no embedding needed.", color=P["slate"])
    y = pg.h - 150
    for i, name in enumerate(FONTS):
        f = d.std(name)
        rowh = 34
        if i % 2 == 0:
            box(pg, M - 8, y - 11, pg.w - 2 * M + 16, rowh, fill=P["paper"])
        pg.text(t.sans_b, 8, M, y + 10, f"{i + 1:02d}", color=P["indigo"], kern=False)
        pg.text(t.mono, 7.5, M + 22, y + 10, name, color=P["slate"], kern=False)
        sample = "Sphinx of black quartz, judge my vow 0123" if not f.symbolic else ("abcdefghij ABCDEFGHIJ klmnop" if name == "Symbol" else "abcdefghijklmnopqrstuvwxyz 0123456789")
        pg.text(f, 16, M + 22, y - 6, sample, color=P["ink"], kern=False)
        y -= rowh
    # text state operators
    y -= 22
    eyebrow(pg, t, M, y, "Text state operators on Times and Helvetica")
    h, tm = d.std("Helvetica"), d.std("Times-Italic")
    hn = pg.reg("Font", f"{h.ref} 0 R")
    tn = pg.reg("Font", f"{tm.ref} 0 R")
    y -= 24
    pg.raw(f"BT {rgbs(P['ink'])} rg /{hn} 13 Tf 1 0 0 1 {M} {n(y)} Tm 15 TL "
           f"(Word spacing ) Tj 12 Tw (adds space between words) Tj 0 Tw T* "
           f"60 Tz (Horizontal scaling 60%) Tj 100 Tz 120 Tz ( then 120%) Tj 100 Tz T* "
           f"3 Tc (Character spacing) Tj 0 Tc T* "
           f"(Rise: base) Tj 5 Ts (super) Tj -4 Ts (sub) Tj 0 Ts ( and back.) Tj T* "
           f"/{tn} 13 Tf (Italic via a second Tf, same T* leading.) Tj ET")
    d.save(path)


OUT = "02-standard-14-fonts.pdf"
