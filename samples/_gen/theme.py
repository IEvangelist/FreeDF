"""Shared design system for the sample documents: palette, type scale and small drawing helpers."""
from pdfkit import *

P = dict(
    ink="#0F172A", slate="#475569", muted="#94A3B8", mist="#E2E8F0", paper="#F8FAFC", white="#FFFFFF",
    indigo="#4F46E5", indigo_d="#312E81", violet="#7C3AED", teal="#0D9488", sky="#0EA5E9",
    amber="#F59E0B", rose="#E11D48", emerald="#10B981", orange="#F97316",
)
A4 = (595.276, 841.89)
LETTER = (612, 792)
M = 54  # outer margin (6 units of the 9pt baseline grid)


class Type:
    """The type family: Source Serif 4 for display, Source Sans 3 for text, Source Code Pro for code (all OFL, embedded as OpenType/CFF subsets)."""

    def __init__(self, doc):
        self.serif = doc.font("SourceSerif4-Regular.otf")
        self.serif_b = doc.font("SourceSerif4-Semibold.otf")
        self.serif_i = doc.font("SourceSerif4-It.otf")
        self.sans = doc.font("SourceSans3-Regular.otf")
        self.sans_b = doc.font("SourceSans3-Semibold.otf")
        self.sans_i = doc.font("SourceSans3-It.otf")
        self.mono = doc.font("SourceCodePro-Regular.otf")


def background(pg, color):
    pg.q()
    pg.fill(color)
    pg.rect(0, 0, pg.w, pg.h)
    pg.f()
    pg.Q()


def rule(pg, x0, y, x1, color=P["mist"], w=0.75):
    pg.q()
    pg.stroke(color)
    pg.width(w)
    pg.move(x0, y)
    pg.line(x1, y)
    pg.S()
    pg.Q()


def box(pg, x, y, w, h, fill=None, stroke=None, r=0, lw=0.75):
    pg.q()
    if r:
        pg.rrect(x, y, w, h, r)
    else:
        pg.rect(x, y, w, h)
    if fill and stroke:
        pg.fill(fill)
        pg.stroke(stroke)
        pg.width(lw)
        pg.B()
    elif fill:
        pg.fill(fill)
        pg.f()
    elif stroke:
        pg.stroke(stroke)
        pg.width(lw)
        pg.S()
    pg.Q()


def eyebrow(pg, t, x, y, text, color=P["indigo"]):
    pg.text(t.sans_b, 8, x, y, text.upper(), color=color, tracking=1.6, kern=False)


def page_header(pg, t, title, kicker, num=None, total=None):
    """Consistent running head: kicker left, folio right, hairline below."""
    eyebrow(pg, t, M, pg.h - 36, kicker)
    if num is not None:
        s = f"{num}" if total is None else f"{num} / {total}"
        pg.text(t.sans, 8, pg.w - M - t.sans.width(s, 8), pg.h - 36, s, color=P["muted"])
    rule(pg, M, pg.h - 44, pg.w - M)
    pg.text(t.serif_b, 26, M, pg.h - 84, title, color=P["ink"])


def logo_mark(pg, x, y, s, a=P["indigo"], b=P["violet"]):
    """A small geometric mark: two overlapping rounded squares."""
    pg.q()
    pg.gs(ca=0.9)
    box(pg, x, y, s * 0.7, s * 0.7, fill=a, r=s * 0.14)
    pg.gs(ca=0.85)
    box(pg, x + s * 0.3, y + s * 0.3, s * 0.7, s * 0.7, fill=b, r=s * 0.14)
    pg.Q()
