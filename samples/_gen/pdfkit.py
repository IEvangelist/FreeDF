"""pdfkit: a small, dependency-light PDF 1.7/2.0 builder used to author the sample documents.

It writes raw PDF objects so every sample can exercise features (shadings, soft masks, optional content,
tagged structure, AcroForm, ...) that high-level libraries hide. Fonts are embedded as real subsets via fontTools.
"""
import io
import math
import os
import re
import zlib
from fontTools import subset
from fontTools.ttLib import TTFont as FTFont
from reportlab.pdfbase.pdfmetrics import stringWidth as _rl_width

FONT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fonts")


def n(x):
    s = f"{x:.4f}".rstrip("0").rstrip(".")
    return "0" if s in ("", "-0") else s


def pstr(s):
    """PDF literal string from text (WinAnsi/Latin-1) or bytes."""
    b = s if isinstance(s, bytes) else s.encode("cp1252", "replace")
    b = b.replace(b"\\", b"\\\\").replace(b"(", b"\\(").replace(b")", b"\\)").replace(b"\r", b"\\r")
    return "(" + b.decode("latin-1") + ")"


def utf16(s):
    """Text string in UTF-16BE with BOM as a hex string."""
    return "<" + ("\ufeff" + s).encode("utf-16-be").hex().upper() + ">"


def rgb(spec):
    if isinstance(spec, (tuple, list)):
        return tuple(spec)
    h = spec.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


def rgbs(spec):
    return " ".join(n(v) for v in rgb(spec))


# ---------------------------------------------------------------------------------------------------- fonts

class StdFont:
    """One of the 14 standard fonts (no embedding)."""

    def __init__(self, doc, name, encoding="WinAnsiEncoding"):
        self.doc, self.name = doc, name
        self.symbolic = name in ("Symbol", "ZapfDingbats")
        self.encoding = None if self.symbolic else encoding
        self._ref = None

    @property
    def ref(self):
        if self._ref is None:
            enc = f" /Encoding /{self.encoding}" if self.encoding else ""
            self._ref = self.doc.add(f"<< /Type /Font /Subtype /Type1 /BaseFont /{self.name}{enc} >>")
        return self._ref

    def width(self, s, size, kern=False, tracking=0):
        if self.symbolic:
            return _rl_width(s, self.name, size) + tracking * len(s)
        return _rl_width(s, self.name, size) + tracking * len(s)

    def tj(self, s, size, kern=False, space_extra=0):
        if not space_extra:
            return f"[{pstr(s)}] TJ"
        out = []
        for i, p in enumerate(s.split(" ")):
            if i:
                out.append(pstr(" ") + " " + n(-space_extra * 1000 / size))
            out.append(pstr(p))
        return "[" + " ".join(out) + "] TJ"


class EmbFont:
    """TrueType or OpenType(CFF) font embedded as a subset using Identity-H (Type0/CIDFont)."""

    _tag = 0

    def __init__(self, doc, filename):
        self.doc = doc
        self.path = filename if os.path.isabs(filename) else os.path.join(FONT_DIR, filename)
        self.ft = FTFont(self.path)
        self.cff = "CFF " in self.ft
        self.upem = self.ft["head"].unitsPerEm
        self.cmap = self.ft.getBestCmap()
        self.order = self.ft.getGlyphOrder()
        self.used = {}
        self._ref = None
        self._kern = {}
        self._kernt = None
        self._build_kern()
        doc.fin.append(self._finalize)
        EmbFont._tag += 1
        self.tag = "".join(chr(65 + (EmbFont._tag * k) % 26) for k in (1, 3, 5, 7, 11, 13))
        base = None
        for rec in self.ft["name"].names:
            if rec.nameID == 6:
                base = rec.toUnicode()
                break
        self.psname = re.sub(r"[^A-Za-z0-9-]", "", base or "Font")

    # -- metrics
    def gid(self, ch):
        g = self.cmap.get(ord(ch))
        return self.ft.getGlyphID(g) if g else 0

    def adv(self, gid):
        return self.ft["hmtx"][self.order[gid]][0] * 1000 / self.upem

    def _build_kern(self):
        ft = self.ft
        self._kernt = []
        if "GPOS" in ft:
            gpos = ft["GPOS"].table
            idx = set()
            for fr in gpos.FeatureList.FeatureRecord:
                if fr.FeatureTag == "kern":
                    idx.update(fr.Feature.LookupListIndex)
            for i in sorted(idx):
                lk = gpos.LookupList.Lookup[i]
                for st in lk.SubTable:
                    if lk.LookupType == 9:
                        st = st.ExtSubTable
                    if getattr(st, "LookupType", getattr(st, "Format", None)) is None:
                        continue
                    if hasattr(st, "PairSet") or hasattr(st, "Class1Record"):
                        self._kernt.append(st)
        elif "kern" in ft:
            self._legacy = ft["kern"].kernTables[0].kernTable
        self._legacy = getattr(self, "_legacy", None)

    def kern_units(self, a, b):
        key = (a, b)
        if key in self._kern:
            return self._kern[key]
        v = 0
        na, nb = self.order[a], self.order[b]
        if self._legacy is not None:
            v = self._legacy.get((na, nb), 0)
        for st in self._kernt:
            cov = st.Coverage.glyphs
            if na not in cov:
                continue
            if st.Format == 1:
                ps = st.PairSet[cov.index(na)]
                hit = [r for r in ps.PairValueRecord if r.SecondGlyph == nb]
                if hit:
                    v1 = hit[0].Value1
                    v = getattr(v1, "XAdvance", 0) if v1 else 0
                    break
            else:
                c1 = st.ClassDef1.classDefs.get(na, 0)
                c2 = st.ClassDef2.classDefs.get(nb, 0)
                v1 = st.Class1Record[c1].Class2Record[c2].Value1
                val = getattr(v1, "XAdvance", 0) if v1 else 0
                if val:
                    v = val
                    break
        self._kern[key] = v
        return v

    def gids(self, s):
        out = []
        for ch in s:
            g = self.gid(ch)
            self.used[g] = ord(ch)
            out.append(g)
        return out

    def width(self, s, size, kern=True, tracking=0):
        g = [self.gid(c) for c in s]
        w = sum(self.adv(x) for x in g)
        if kern:
            w += sum(self.kern_units(a, b) for a, b in zip(g, g[1:])) * 1000 / self.upem
        return w * size / 1000 + tracking * len(s)

    def tj(self, s, size, kern=True, space_extra=0):
        g = self.gids(s)
        items, cur = [], []
        for i, x in enumerate(g):
            cur.append(x)
            adj = 0
            if kern and i + 1 < len(g):
                adj += self.kern_units(x, g[i + 1]) * 1000 / self.upem
            if space_extra and s[i] == " ":
                adj += space_extra * 1000 / size
            if adj:
                items.append("<" + "".join(f"{c:04X}" for c in cur) + ">")
                items.append(n(-adj))
                cur = []
        if cur:
            items.append("<" + "".join(f"{c:04X}" for c in cur) + ">")
        return "[" + " ".join(items) + "] TJ"

    @property
    def ref(self):
        if self._ref is None:
            self._ref = self.doc.new()
        return self._ref

    def _finalize(self):
        if self._ref is None:
            return
        doc = self.doc
        used = sorted(set(self.used) | {0})
        ft = FTFont(self.path)
        opt = subset.Options()
        opt.retain_gids = True
        opt.notdef_outline = True
        opt.layout_features = []
        opt.hinting = False
        opt.name_IDs = [1, 2, 3, 4, 6]
        opt.name_legacy = True
        opt.glyph_names = False
        s = subset.Subsetter(opt)
        s.populate(gids=used)
        s.subset(ft)
        buf = io.BytesIO()
        ft.save(buf)
        data = buf.getvalue()
        if self.cff:
            ff = doc.stream("/Subtype /OpenType", data)
            fkey, sub, cidsub = "FontFile3", "CIDFontType0", "CIDFontType0"
        else:
            ff = doc.stream(f"/Length1 {len(data)}", data)
            fkey, sub, cidsub = "FontFile2", "CIDFontType2", "CIDFontType2"
        head, hhea, post = self.ft["head"], self.ft["hhea"], self.ft["post"]
        sc = 1000 / self.upem
        os2 = self.ft["OS/2"] if "OS/2" in self.ft else None
        cap = int(getattr(os2, "sCapHeight", 0) * sc) or int(hhea.ascent * sc * 0.7)
        flags = 32 | (64 if post.italicAngle else 0) | (2 if self.psname.lower().find("serif") >= 0 and "Sans" not in self.psname else 0)
        fname = f"{self.tag}+{self.psname}"
        desc = doc.add(
            f"<< /Type /FontDescriptor /FontName /{fname} /Flags {flags} "
            f"/FontBBox [{n(head.xMin * sc)} {n(head.yMin * sc)} {n(head.xMax * sc)} {n(head.yMax * sc)}] "
            f"/ItalicAngle {n(post.italicAngle)} /Ascent {n(hhea.ascent * sc)} /Descent {n(hhea.descent * sc)} "
            f"/CapHeight {cap} /StemV 80 /{fkey} {ff} 0 R >>")
        w = " ".join(f"{g} [{n(self.adv(g))}]" for g in used)
        cid = doc.add(
            f"<< /Type /Font /Subtype /{cidsub} /BaseFont /{fname} /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> "
            f"/FontDescriptor {desc} 0 R /DW 1000 /W [{w}]"
            + ("" if self.cff else " /CIDToGIDMap /Identity") + " >>")
        bf = "\n".join(f"<{g:04X}> <{u:04X}>" if u < 0x10000 else
                       f"<{g:04X}> <{(0xD800 + ((u - 0x10000) >> 10)):04X}{(0xDC00 + ((u - 0x10000) & 0x3FF)):04X}>"
                       for g, u in sorted(self.used.items()))
        cmap = ("/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def "
                "/CMapName /Adobe-Identity-UCS def /CMapType 2 def 1 begincodespacerange <0000> <FFFF> endcodespacerange\n"
                f"{len(self.used)} beginbfchar\n{bf}\nendbfchar\nendcmap CMapName currentdict /CMap defineresource pop end end")
        tu = doc.stream("", cmap.encode("latin-1"))
        doc.put(self._ref, f"<< /Type /Font /Subtype /Type0 /BaseFont /{fname} /Encoding /Identity-H "
                           f"/DescendantFonts [{cid} 0 R] /ToUnicode {tu} 0 R >>")


# ---------------------------------------------------------------------------------------------------- resources & content

class Res:
    PREFIX = {"Font": "F", "XObject": "X", "ExtGState": "G", "Shading": "S", "Pattern": "P", "ColorSpace": "C", "Properties": "M"}

    def __init__(self):
        self.rd = {k: {} for k in self.PREFIX}

    def reg(self, cat, val):
        m = self.rd[cat]
        for k, v in m.items():
            if v == val:
                return k
        k = f"{self.PREFIX[cat]}{len(m) + 1}"
        m[k] = val
        return k

    def res_str(self):
        parts = []
        for cat, m in self.rd.items():
            if m:
                parts.append(f"/{cat} << " + " ".join(f"/{k} {v}" for k, v in m.items()) + " >>")
        return "<< /ProcSet [/PDF /Text /ImageB /ImageC /ImageI] " + " ".join(parts) + " >>"


class Content(Res):
    def __init__(self, doc):
        super().__init__()
        self.doc = doc
        self.out = []

    # -- raw / state
    def raw(self, s):
        self.out.append(s)

    def q(self):
        self.out.append("q")

    def Q(self):
        self.out.append("Q")

    def cm(self, a, b, c, d, e, f):
        self.out.append(f"{n(a)} {n(b)} {n(c)} {n(d)} {n(e)} {n(f)} cm")

    def translate(self, x, y):
        self.cm(1, 0, 0, 1, x, y)

    def scale(self, sx, sy):
        self.cm(sx, 0, 0, sy, 0, 0)

    def rotate(self, deg):
        r = math.radians(deg)
        self.cm(math.cos(r), math.sin(r), -math.sin(r), math.cos(r), 0, 0)

    def fill(self, spec):
        self.out.append(rgbs(spec) + " rg")

    def stroke(self, spec):
        self.out.append(rgbs(spec) + " RG")

    def width(self, w):
        self.out.append(f"{n(w)} w")

    def cap(self, c):
        self.out.append(f"{c} J")

    def join(self, j):
        self.out.append(f"{j} j")

    def miter(self, m):
        self.out.append(f"{n(m)} M")

    def dash(self, arr, phase=0):
        self.out.append("[" + " ".join(n(a) for a in arr) + f"] {n(phase)} d")

    def gs(self, **kw):
        """ExtGState: ca, CA, BM, SMask (object ref string), AIS, op, OP, LW ..."""
        items = []
        for k, v in kw.items():
            if isinstance(v, bool):
                v = "true" if v else "false"
            elif isinstance(v, (int, float)):
                v = n(v)
            items.append(f"/{k} {v}")
        name = self.reg("ExtGState", "<< /Type /ExtGState " + " ".join(items) + " >>")
        self.out.append(f"/{name} gs")

    # -- paths
    def move(self, x, y):
        self.out.append(f"{n(x)} {n(y)} m")

    def line(self, x, y):
        self.out.append(f"{n(x)} {n(y)} l")

    def curve(self, x1, y1, x2, y2, x3, y3):
        self.out.append(f"{n(x1)} {n(y1)} {n(x2)} {n(y2)} {n(x3)} {n(y3)} c")

    def close(self):
        self.out.append("h")

    def rect(self, x, y, w, h):
        self.out.append(f"{n(x)} {n(y)} {n(w)} {n(h)} re")

    def rrect(self, x, y, w, h, r):
        r = min(r, w / 2, h / 2)
        k = 0.5523 * r
        self.move(x + r, y)
        self.line(x + w - r, y)
        self.curve(x + w - r + k, y, x + w, y + r - k, x + w, y + r)
        self.line(x + w, y + h - r)
        self.curve(x + w, y + h - r + k, x + w - r + k, y + h, x + w - r, y + h)
        self.line(x + r, y + h)
        self.curve(x + r - k, y + h, x, y + h - r + k, x, y + h - r)
        self.line(x, y + r)
        self.curve(x, y + r - k, x + r - k, y, x + r, y)
        self.close()

    def ellipse(self, cx, cy, rx, ry):
        k = 0.5523
        self.move(cx + rx, cy)
        self.curve(cx + rx, cy + k * ry, cx + k * rx, cy + ry, cx, cy + ry)
        self.curve(cx - k * rx, cy + ry, cx - rx, cy + k * ry, cx - rx, cy)
        self.curve(cx - rx, cy - k * ry, cx - k * rx, cy - ry, cx, cy - ry)
        self.curve(cx + k * rx, cy - ry, cx + rx, cy - k * ry, cx + rx, cy)
        self.close()

    def circle(self, cx, cy, r):
        self.ellipse(cx, cy, r, r)

    def arc_path(self, cx, cy, r, a0, a1, move=True):
        """Circular arc from a0 to a1 degrees (counter-clockwise) approximated with beziers."""
        steps = max(1, int(math.ceil(abs(a1 - a0) / 90)))
        da = math.radians(a1 - a0) / steps
        a = math.radians(a0)
        if move:
            self.move(cx + r * math.cos(a), cy + r * math.sin(a))
        k = 4 / 3 * math.tan(da / 4)
        for _ in range(steps):
            b = a + da
            self.curve(cx + r * (math.cos(a) - k * math.sin(a)), cy + r * (math.sin(a) + k * math.cos(a)),
                       cx + r * (math.cos(b) + k * math.sin(b)), cy + r * (math.sin(b) - k * math.cos(b)),
                       cx + r * math.cos(b), cy + r * math.sin(b))
            a = b

    def f(self):
        self.out.append("f")

    def S(self):
        self.out.append("S")

    def B(self):
        self.out.append("B")

    def fe(self):
        self.out.append("f*")

    def clip(self, eo=False):
        self.out.append("W* n" if eo else "W n")

    def sh(self, shading_dict):
        name = self.reg("Shading", shading_dict)
        self.out.append(f"/{name} sh")

    def pattern_fill(self, pat_ref, cs="/Pattern", stroke=False, comps=""):
        name = self.reg("Pattern", f"{pat_ref} 0 R" if isinstance(pat_ref, int) else pat_ref)
        csn = self.reg("ColorSpace", cs) if cs != "/Pattern" else None
        if csn:
            self.out.append(f"/{csn} {'CS' if stroke else 'cs'} {comps} /{name} {'SCN' if stroke else 'scn'}")
        else:
            self.out.append(f"/Pattern {'CS' if stroke else 'cs'} /{name} {'SCN' if stroke else 'scn'}")

    def xobj(self, ref):
        name = self.reg("XObject", f"{ref} 0 R")
        self.out.append(f"/{name} Do")

    # -- text
    def text(self, font, size, x, y, s, color=None, kern=True, tracking=0, mode=None, space_extra=0, stroke_color=None, lw=None, rise=0):
        fname = self.reg("Font", f"{font.ref} 0 R")
        ops = ["BT"]
        if color is not None:
            ops.append(rgbs(color) + " rg")
        if stroke_color is not None:
            ops.append(rgbs(stroke_color) + " RG")
        if lw is not None:
            ops.append(f"{n(lw)} w")
        if mode is not None:
            ops.append(f"{mode} Tr")
        if tracking:
            ops.append(f"{n(tracking)} Tc")
        if rise:
            ops.append(f"{n(rise)} Ts")
        ops.append(f"/{fname} {n(size)} Tf 1 0 0 1 {n(x)} {n(y)} Tm")
        ops.append(font.tj(s, size, kern, space_extra))
        if tracking:
            ops.append("0 Tc")
        if mode is not None:
            ops.append("0 Tr")
        if rise:
            ops.append("0 Ts")
        ops.append("ET")
        self.out.append(" ".join(ops))

    def text_w(self, font, size, s, kern=True, tracking=0):
        return font.width(s, size, kern, tracking)

    def tbegin(self):
        self.out.append("BT")

    def data(self):
        return "\n".join(self.out).encode("latin-1")


class XForm(Content):
    """Form XObject / tiling pattern / soft-mask group / OCG appearance. Call .build() to obtain its object number."""

    def __init__(self, doc, bbox, matrix=None, group=None):
        super().__init__(doc)
        self.bbox, self.matrix, self.group = bbox, matrix, group
        self._ref = None

    def build(self, kind="form", xstep=None, ystep=None, paint=1, extra=""):
        b = " ".join(n(v) for v in self.bbox)
        if kind == "pattern":
            d = f"/Type /Pattern /PatternType 1 /PaintType {paint} /TilingType 1 /BBox [{b}] /XStep {n(xstep)} /YStep {n(ystep)} /Resources {self.res_str()}"
        else:
            d = f"/Type /XObject /Subtype /Form /BBox [{b}] /Resources {self.res_str()}"
            if self.group:
                d += f" /Group {self.group}"
        if self.matrix:
            d += " /Matrix [" + " ".join(n(v) for v in self.matrix) + "]"
        self._ref = self.doc.stream(d + " " + extra, self.data())
        return self._ref


class Page(Content):
    def __init__(self, doc, w, h):
        super().__init__(doc)
        self.w, self.h = w, h
        self.ref = doc.new()
        self.annots = []
        self.extra = ""
        self.boxes = {}

    def add_annot(self, body):
        r = self.doc.add(body)
        self.annots.append(r)
        return r


# ---------------------------------------------------------------------------------------------------- document

class Doc:
    def __init__(self, title="", author="Pdf sample generator", subject="", keywords="", lang=None):
        self.objs = [None]
        self.pages = []
        self.fin = []
        self.cat = []
        self.info = dict(title=title, author=author, subject=subject, keywords=keywords)
        self.lang = lang
        self._std = {}
        self._emb = {}
        self.trailer_extra = ""
        self.compress = True

    def new(self):
        self.objs.append(None)
        return len(self.objs) - 1

    def put(self, num, body):
        self.objs[num] = body if isinstance(body, bytes) else body.encode("latin-1")

    def add(self, body):
        num = self.new()
        self.put(num, body)
        return num

    def stream(self, d, data, compress=None, num=None):
        if (self.compress if compress is None else compress):
            data = zlib.compress(data, 9)
            d = (d + " /Filter /FlateDecode").strip()
        num = num or self.new()
        self.put(num, f"<< {d} /Length {len(data)} >>\nstream\n".encode("latin-1") + data + b"\nendstream")
        return num

    def std(self, name, encoding="WinAnsiEncoding"):
        key = (name, encoding)
        if key not in self._std:
            self._std[key] = StdFont(self, name, encoding)
        return self._std[key]

    def font(self, filename):
        if filename not in self._emb:
            self._emb[filename] = EmbFont(self, filename)
        return self._emb[filename]

    def page(self, w=595.276, h=841.89):
        p = Page(self, w, h)
        self.pages.append(p)
        return p

    def save(self, path, version="1.7", page_tree_extra=""):
        for f in self.fin:
            f()
        kids = []
        pages_ref = self.new()
        for p in self.pages:
            cs = self.stream("", p.data())
            boxes = "".join(f" /{k} [" + " ".join(n(v) for v in b) + "]" for k, b in p.boxes.items())
            annots = f" /Annots [{' '.join(f'{a} 0 R' for a in p.annots)}]" if p.annots else ""
            self.put(p.ref, f"<< /Type /Page /Parent {pages_ref} 0 R /MediaBox [0 0 {n(p.w)} {n(p.h)}]{boxes} "
                            f"/Resources {p.res_str()} /Contents {cs} 0 R{annots} {p.extra} >>")
            kids.append(f"{p.ref} 0 R")
        self.put(pages_ref, f"<< /Type /Pages /Kids [{' '.join(kids)}] /Count {len(kids)} {page_tree_extra} >>")
        i = self.info
        info = self.add(f"<< /Title {utf16(i['title'])} /Author {utf16(i['author'])} /Subject {utf16(i['subject'])} "
                        f"/Keywords {utf16(i['keywords'])} /Creator (csharp-pdf samples generator) /Producer (pdfkit.py) >>")
        lang = f" /Lang {pstr(self.lang)}" if self.lang else ""
        root = self.add(f"<< /Type /Catalog /Pages {pages_ref} 0 R{lang} {' '.join(self.cat)} >>")
        out = bytearray(f"%PDF-{version}\n%\xe2\xe3\xcf\xd3\n".encode("latin-1"))
        offs = [0] * len(self.objs)
        for k in range(1, len(self.objs)):
            offs[k] = len(out)
            out += f"{k} 0 obj\n".encode() + (self.objs[k] or b"null") + b"\nendobj\n"
        x = len(out)
        out += f"xref\n0 {len(self.objs)}\n0000000000 65535 f \n".encode()
        for k in range(1, len(self.objs)):
            out += f"{offs[k]:010d} 00000 n \n".encode()
        ident = os.urandom(0).hex() or "00" * 16
        import hashlib
        ident = hashlib.md5(bytes(out)).hexdigest().upper()
        out += (f"trailer\n<< /Size {len(self.objs)} /Root {root} 0 R /Info {info} 0 R /ID [<{ident}> <{ident}>] {self.trailer_extra} >>\n"
                f"startxref\n{x}\n%%EOF\n").encode()
        os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
        with open(path, "wb") as fh:
            fh.write(out)


# ---------------------------------------------------------------------------------------------------- shading helpers

def fn_exp(c0, c1, n_=1):
    return (f"<< /FunctionType 2 /Domain [0 1] /C0 [{' '.join(n(v) for v in c0)}] "
            f"/C1 [{' '.join(n(v) for v in c1)}] /N {n(n_)} >>")


def fn_stops(stops):
    """stops: list of (t, '#rrggbb' or tuple). Returns a type 2 or 3 function string."""
    cols = [rgb(c) for _, c in stops]
    ts = [t for t, _ in stops]
    if len(stops) == 2:
        return fn_exp(cols[0], cols[1])
    fns = " ".join(fn_exp(cols[i], cols[i + 1]) for i in range(len(stops) - 1))
    bounds = " ".join(n(t) for t in ts[1:-1])
    enc = " ".join("0 1" for _ in range(len(stops) - 1))
    return f"<< /FunctionType 3 /Domain [0 1] /Functions [{fns}] /Bounds [{bounds}] /Encode [{enc}] >>"


def axial(x0, y0, x1, y1, stops, extend=True, cs="/DeviceRGB"):
    e = "[true true]" if extend else "[false false]"
    return f"<< /ShadingType 2 /ColorSpace {cs} /Coords [{n(x0)} {n(y0)} {n(x1)} {n(y1)}] /Function {fn_stops(stops)} /Extend {e} >>"


def radial(x0, y0, r0, x1, y1, r1, stops, extend=True, cs="/DeviceRGB"):
    e = "[true true]" if extend else "[false false]"
    return (f"<< /ShadingType 3 /ColorSpace {cs} /Coords [{n(x0)} {n(y0)} {n(r0)} {n(x1)} {n(y1)} {n(r1)}] "
            f"/Function {fn_stops(stops)} /Extend {e} >>")


# ---------------------------------------------------------------------------------------------------- text layout

def wrap(font, size, text, width, kern=True, tracking=0):
    lines = []
    for para in text.split("\n"):
        words, cur = para.split(" "), ""
        for w in words:
            t = (cur + " " + w) if cur else w
            if cur and font.width(t, size, kern, tracking) > width:
                lines.append(cur)
                cur = w
            else:
                cur = t
        lines.append(cur)
    return lines


def paragraph(c, font, size, x, y, width, text, leading=None, align="left", color=None, kern=True, tracking=0, indent=0, first_indent=None):
    """Draws wrapped text with its first baseline at y. Returns the y of the next baseline."""
    leading = leading or size * 1.4
    first = indent if first_indent is None else first_indent
    lines = []
    rest = text
    # greedy wrap honouring first-line indent
    for pi, para in enumerate(text.split("\n")):
        cur, words = "", para.split(" ")
        li = 0
        start = len(lines)
        for w in words:
            t = (cur + " " + w) if cur else w
            avail = width - (first if (pi == 0 or True) and len(lines) == start else indent)
            if cur and font.width(t, size, kern, tracking) > avail:
                lines.append((cur, False))
                cur = w
            else:
                cur = t
        lines.append((cur, True))
    for i, (ln, last) in enumerate(lines):
        off = first if i == 0 else indent
        avail = width - off
        w = font.width(ln, size, kern, tracking)
        xx, extra = x + off, 0
        if align == "right":
            xx = x + width - w
        elif align == "center":
            xx = x + off + (avail - w) / 2
        elif align == "justify" and not last and ln.count(" "):
            extra = (avail - w) / ln.count(" ")
        c.text(font, size, xx, y, ln, color=color, kern=kern, tracking=tracking, space_extra=extra)
        y -= leading
    return y


def lighten(spec, t):
    r, g, b = rgb(spec)
    return "#%02x%02x%02x" % tuple(int(255 * (v + (1 - v) * t)) for v in (r, g, b))


def mix(a, b, t):
    ra, rb = rgb(a), rgb(b)
    return "#%02x%02x%02x" % tuple(int(255 * (x + (y - x) * t)) for x, y in zip(ra, rb))
