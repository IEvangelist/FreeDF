"""Generates the font/CMap test fixtures under tests/fixtures/fonts.

Needs: fontTools, pikepdf, reportlab (for Vera.ttf) and the Adobe cmap-resources clone used by gen_cmaps.py.
Outputs: type1-*.pdf (hand-built Type 1 programs: PFA hex, PFB and raw binary eexec), cff-seac.pdf (CFF with seac, arithmetic
operators and glyphs named by SIDs 229-390), cjk-rksj.pdf / cjk-identity.pdf (same glyphs through 90ms-RKSJ-H vs Identity-H),
cjk-noembed.pdf (non-embedded Japan1/GB1/Korea1 fonts) and cmap-samples.txt (expected CIDs sampled from the Adobe CMap sources).
"""
import importlib.util, io, os, random, struct, sys
import pikepdf
from pikepdf import Name, Dictionary, Array, String, Stream

ROOT = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(ROOT, '..', '..', 'tests', 'fixtures', 'fonts')
os.makedirs(OUT, exist_ok=True)


def load_gen():
    src = open(os.path.join(ROOT, 'gen_cmaps.py')).read().replace('\nmain()\n', '\n')
    ns = {'__file__': os.path.join(ROOT, 'gen_cmaps.py')}
    exec(compile(src, 'gen_cmaps', 'exec'), ns)
    return ns


# ---------------------------------------------------------------- Type 1

def num(v):
    if -107 <= v <= 107: return bytes([v + 139])
    if 108 <= v <= 1131: v -= 108; return bytes([247 + (v >> 8), v & 255])
    if -1131 <= v <= -108: v = -v - 108; return bytes([251 + (v >> 8), v & 255])
    return b'\xff' + struct.pack('>i', v)


CMD = {'hstem': 1, 'vstem': 3, 'vmoveto': 4, 'rlineto': 5, 'hlineto': 6, 'vlineto': 7, 'rrcurveto': 8, 'closepath': 9, 'callsubr': 10,
       'return': 11, 'hsbw': 13, 'endchar': 14, 'rmoveto': 21, 'hmoveto': 22, 'vhcurveto': 30, 'hvcurveto': 31}
ESC = {'seac': 6, 'div': 12, 'callothersubr': 16, 'pop': 17, 'setcurrentpoint': 33}


def cs(text):
    out = bytearray()
    for t in text.split():
        if t in CMD: out.append(CMD[t])
        elif t in ESC: out += bytes([12, ESC[t]])
        else: out += num(int(t))
    return bytes(out)


def encrypt(data, r, lead):
    out = bytearray()
    for p in lead + data:
        c = p ^ (r >> 8)
        r = ((c + r) * 52845 + 22719) & 0xFFFF
        out.append(c)
    return bytes(out)


def type1_program():
    subrs = [
        '3 0 callothersubr pop pop setcurrentpoint return',
        '0 1 callothersubr return',
        '0 2 callothersubr return',
        'return',
        '1 3 callothersubr pop callsubr return',
        '0 400 hstem return',
        '300 hlineto return',
    ]
    glyphs = {
        '.notdef': '0 500 hsbw endchar',
        # 300x400 box at x 50..350
        'A': '50 700 hsbw 0 400 hstem 50 300 vstem 0 0 rmoveto 300 hlineto 400 vlineto -300 hlineto closepath endchar',
        # flex on the top edge: bulges up between x 200..350 and dips down between 50..200
        'B': ('50 700 hsbw 0 0 rmoveto 300 hlineto 300 vlineto 1 callsubr -150 0 rmoveto 2 callsubr 100 40 rmoveto 2 callsubr '
              '-50 0 rmoveto 2 callsubr -50 -40 rmoveto 2 callsubr -50 -40 rmoveto 2 callsubr -50 0 rmoveto 2 callsubr '
              '-50 40 rmoveto 2 callsubr 50 50 300 0 callsubr -300 vlineto closepath endchar'),
        # hint replacement + subroutine lines
        'C': '50 700 hsbw 5 4 callsubr 0 0 rmoveto 6 callsubr 400 vlineto -300 hlineto closepath endchar',
        # div operator
        'D': '50 1400 2 div hsbw 0 0 rmoveto 600 2 div hlineto 400 vlineto -300 hlineto closepath endchar',
        'acute': '50 300 hsbw 0 0 rmoveto 100 hlineto 80 vlineto -100 hlineto closepath endchar',
        # seac: accent origin = sbx - asb + adx
        'Aacute': '50 700 hsbw 50 125 450 65 194 seac',
        # curve outline: circle-ish blob
        'O': ('50 700 hsbw 200 0 rmoveto 110 0 90 90 0 110 rrcurveto 0 110 -90 90 -110 0 rrcurveto -110 0 -90 -90 0 -110 rrcurveto '
              '0 -110 90 -90 110 0 rrcurveto closepath endchar'),
    }
    enc = {65: 'A', 66: 'B', 67: 'C', 68: 'D', 69: 'Aacute', 70: 'O'}
    clear = ('%!PS-AdobeFont-1.0: TestType1 001.000\n11 dict begin\n/FontInfo 2 dict dup begin /FullName (Test Type1) def end readonly def\n'
             '/FontName /TestType1 def\n/PaintType 0 def\n/FontType 1 def\n/FontMatrix [0.001 0 0 0.001 0 0] readonly def\n'
             '/Encoding 256 array\n0 1 255 {1 index exch /.notdef put} for\n' +
             ''.join('dup %d /%s put\n' % (c, n) for c, n in enc.items()) +
             'readonly def\n/FontBBox {0 0 700 600} readonly def\ncurrentdict end\ncurrentfile eexec\n').encode()
    lead = b'\0\0\0\0'
    priv = bytearray(b'dup /Private 8 dict dup begin\n/RD {string currentfile exch readstring pop} executeonly def\n'
                     b'/ND {noaccess def} executeonly def\n/NP {noaccess put} executeonly def\n/lenIV 4 def\n/BlueValues [] def\n')
    priv += b'/Subrs %d array\n' % len(subrs)
    for i, s in enumerate(subrs):
        e = encrypt(cs(s), 4330, lead)
        priv += b'dup %d %d RD ' % (i, len(e)) + e + b' NP\n'
    priv += b'ND\n2 index /CharStrings %d dict dup begin\n' % len(glyphs)
    for n, s in glyphs.items():
        e = encrypt(cs(s), 4330, lead)
        priv += b'/%s %d RD ' % (n.encode(), len(e)) + e + b' ND\n'
    priv += b'end\nend\nreadonly put\nnoaccess put\ndup /FontName get exch definefont pop\nmark currentfile closefile\n'
    cipher = encrypt(bytes(priv), 55665, lead)
    trailer = (b'0' * 64 + b'\n') * 8 + b'cleartomark\n'
    return clear, cipher, trailer


def pfb(clear, cipher, trailer):
    def seg(t, d): return b'\x80' + bytes([t]) + struct.pack('<I', len(d)) + d
    return seg(1, clear) + seg(2, cipher) + seg(1, trailer) + b'\x80\x03'


def pfa(clear, cipher, trailer):
    h = cipher.hex().upper()
    return clear + '\n'.join(h[i:i + 64] for i in range(0, len(h), 64)).encode() + b'\n' + trailer


def page_pdf(path, fonts, content, w=400, h=200):
    pdf = pikepdf.new()
    page = pdf.add_blank_page(page_size=(w, h))
    page.Resources = Dictionary(Font=Dictionary(fonts(pdf)))
    page.Contents = pdf.make_stream(content.encode('latin-1'))
    pdf.save(path, compress_streams=False, object_stream_mode=pikepdf.ObjectStreamMode.disable)


def t1_font(pdf, data, l1, l2, enc_mode):
    ff = Stream(pdf, data)
    ff.Length1, ff.Length2, ff.Length3 = l1, l2, len(data) - l1 - l2
    desc = pdf.make_indirect(Dictionary(Type=Name.FontDescriptor, FontName=Name.TestType1, Flags=4 if enc_mode == 'builtin' else 32,
                                        FontBBox=[0, 0, 700, 600], ItalicAngle=0, Ascent=800, Descent=0, CapHeight=600, StemV=80, FontFile=ff))
    d = Dictionary(Type=Name.Font, Subtype=Name.Type1, BaseFont=Name.TestType1, FontDescriptor=desc, FirstChar=65, LastChar=70,
                   Widths=[700, 700, 700, 700, 700, 700])
    if enc_mode == 'winansi': d.Encoding = Name.WinAnsiEncoding
    elif enc_mode == 'diff':
        d.Encoding = Dictionary(Type=Name.Encoding, Differences=[65, Name.A, Name.B, Name.C, Name.D, Name.Aacute, Name.O])
    return pdf.make_indirect(d)


def make_type1():
    clear, cipher, trailer = type1_program()
    variants = {
        'pfb': (pfb(clear, cipher, trailer), 'builtin'),
        'pfa': (pfa(clear, cipher, trailer), 'winansi'),
        'raw': (clear + cipher + trailer, 'diff'),
    }
    for name, (data, mode) in variants.items():
        l1 = len(clear) if name != 'pfb' else len(data)
        if name == 'raw': l1, l2 = len(clear), len(cipher)
        elif name == 'pfa': l1, l2 = len(clear), len(data) - len(clear) - len(trailer)
        else: l1, l2 = len(data), 0
        page_pdf(os.path.join(OUT, f'type1-{name}.pdf'), lambda pdf, d=data, m=mode, a=l1, b=l2: {'/F1': t1_font(pdf, d, a, b, m)},
                 'BT /F1 100 Tf 10 50 Td (ABCDEF) Tj ET')


# ---------------------------------------------------------------- CFF

def make_cff():
    from fontTools.fontBuilder import FontBuilder
    from fontTools.misc.psCharStrings import T2CharString
    from fontTools.ttLib import TTFont

    def box(x0, y0, w, h, width=700):
        return [width, x0, y0, 'rmoveto', w, 'hlineto', h, 'vlineto', -w, 'hlineto', 'endchar']

    progs = {
        '.notdef': [500, 'endchar'],
        'A': box(50, 0, 300, 400),
        'acute': box(50, 0, 100, 80, 300),
        # arithmetic: 100+200 = 300, 20*20 = 400, 3 dup add
        'Arith': [700, 50, 0, 'rmoveto', 100, 200, 'add', 'hlineto', 20, 20, 'mul', 'vlineto', 600, 2, 'div', 'neg', 'hlineto', 'endchar'],
        # seac form: width adx ady bchar achar endchar (A = 65, acute = 194 in StandardEncoding)
        'Aacute': [700, 125, 450, 65, 194, 'endchar'],
        'exclamsmall': box(50, 0, 300, 200),
        'Semibold': box(50, 0, 200, 400),
        'Asmall': box(50, 100, 300, 300),
    }
    order = list(progs)
    fb = FontBuilder(1000, isTTF=False)
    fb.setupGlyphOrder(order)
    fb.setupCharacterMap({65: 'A'})
    css = {n: T2CharString(program=p) for n, p in progs.items()}
    for c in css.values():  # fontTools cannot evaluate arithmetic operators when computing bounds
        c.calcBounds = lambda glyphSet=None: (0, 0, 700, 600)
    fb.setupCFF('TestCff', {'FullName': 'TestCff'}, css, {'defaultWidthX': 0, 'nominalWidthX': 0})
    fb.setupHorizontalMetrics({n: (700, 0) for n in order})
    fb.setupHorizontalHeader(ascent=800, descent=0)
    fb.setupNameTable({'familyName': 'TestCff', 'styleName': 'Regular'})
    fb.setupOS2()
    fb.setupPost()
    buf = io.BytesIO(); fb.save(buf); buf.seek(0)
    cff = TTFont(buf).getTableData('CFF ')

    def fonts(pdf):
        ff = Stream(pdf, cff); ff.Subtype = Name.Type1C
        desc = pdf.make_indirect(Dictionary(Type=Name.FontDescriptor, FontName=Name.TestCff, Flags=32, FontBBox=[0, 0, 700, 600],
                                            ItalicAngle=0, Ascent=800, Descent=0, CapHeight=600, StemV=80, FontFile3=ff))
        d = Dictionary(Type=Name.Font, Subtype=Name.Type1, BaseFont=Name.TestCff, FontDescriptor=desc, FirstChar=65, LastChar=70,
                       Widths=[700] * 6,
                       Encoding=Dictionary(Type=Name.Encoding, Differences=[65, Name.A, Name.Arith, Name.Aacute, Name.exclamsmall, Name.Semibold, Name.Asmall]))
        return {'/F1': pdf.make_indirect(d)}
    page_pdf(os.path.join(OUT, 'cff-seac.pdf'), fonts, 'BT /F1 100 Tf 10 50 Td (ABCDEF) Tj ET')


# ---------------------------------------------------------------- CJK

def make_cjk(g):
    from fontTools import subset
    from fontTools.ttLib import TTFont
    import reportlab, glob
    vera = os.path.join(os.path.dirname(reportlab.__file__), 'fonts', 'Vera.ttf')
    opts = subset.Options(); opts.layout_features = []; opts.hinting = False; opts.notdef_outline = True
    f = TTFont(vera)
    s = subset.Subsetter(opts); s.populate(text='ABC'); s.subset(f)
    buf = io.BytesIO(); f.save(buf); ttf = buf.getvalue()
    cmap = TTFont(io.BytesIO(ttf)).getBestCmap()
    gid = {c: TTFont(io.BytesIO(ttf)).getGlyphID(n) for c, n in cmap.items() if c in (65, 66, 67)}

    parsed = g['parse'](g['find']('90ms-RKSJ-H'))
    use = parsed[0]
    # 90ms-RKSJ-H: ASCII 0x41/0x42 and the double-byte 0x82A0 (hiragana A)
    def cid_of(code, n):
        for lo, hi, cid in parsed[3][n]:
            if lo <= code <= hi: return cid + code - lo
        raise KeyError(hex(code))
    cids = {'A': cid_of(0x41, 1), 'B': cid_of(0x42, 1), 'C': cid_of(0x82A0, 2)}
    c2g = bytearray(2 * (max(cids.values()) + 1))
    for ch, cid in cids.items():
        c2g[cid * 2: cid * 2 + 2] = struct.pack('>H', gid[ord('A' if ch == 'A' else 'B' if ch == 'B' else 'C')])

    def fonts_for(encoding):
        def mk(pdf):
            ff = Stream(pdf, ttf); ff.Length1 = len(ttf)
            desc = pdf.make_indirect(Dictionary(Type=Name.FontDescriptor, FontName=Name.TestVera, Flags=4, FontBBox=[0, -200, 1000, 900],
                                                ItalicAngle=0, Ascent=800, Descent=-200, CapHeight=700, StemV=80, FontFile2=ff))
            cidf = pdf.make_indirect(Dictionary(Type=Name.Font, Subtype=Name.CIDFontType2, BaseFont=Name.TestVera, FontDescriptor=desc,
                                                CIDSystemInfo=Dictionary(Registry=String('Adobe'), Ordering=String('Japan1'), Supplement=4),
                                                DW=700, CIDToGIDMap=Stream(pdf, bytes(c2g))))
            return {'/F1': pdf.make_indirect(Dictionary(Type=Name.Font, Subtype=Name.Type0, BaseFont=Name.TestVera, Encoding=Name(encoding),
                                                        DescendantFonts=[cidf]))}
        return mk
    page_pdf(os.path.join(OUT, 'cjk-rksj.pdf'), fonts_for('/90ms-RKSJ-H'), 'BT /F1 100 Tf 10 50 Td <4142 82A0> Tj ET')
    ident = ''.join('%04X' % cids[c] for c in 'ABC')
    page_pdf(os.path.join(OUT, 'cjk-identity.pdf'), fonts_for('/Identity-H'), f'BT /F1 100 Tf 10 50 Td <{ident}> Tj ET')

    # Non-embedded CJK fonts: rely on installed fonts; the test skips when none is available.
    def noembed(pdf):
        out = {}
        for i, (base, order, enc) in enumerate([('MS-Mincho', 'Japan1', 'UniJIS-UCS2-H'), ('SimSun', 'GB1', 'UniGB-UCS2-H'),
                                                  ('Batang', 'Korea1', 'UniKS-UCS2-H'), ('MingLiU', 'CNS1', 'UniCNS-UCS2-H')]):
            desc = pdf.make_indirect(Dictionary(Type=Name.FontDescriptor, FontName=Name('/' + base), Flags=6, FontBBox=[0, -200, 1000, 900],
                                                ItalicAngle=0, Ascent=800, Descent=-200, CapHeight=700, StemV=80))
            cidf = pdf.make_indirect(Dictionary(Type=Name.Font, Subtype=Name.CIDFontType2, BaseFont=Name('/' + base), FontDescriptor=desc,
                                                CIDSystemInfo=Dictionary(Registry=String('Adobe'), Ordering=String(order), Supplement=2), DW=1000))
            out[f'/F{i + 1}'] = pdf.make_indirect(Dictionary(Type=Name.Font, Subtype=Name.Type0, BaseFont=Name('/' + base), Encoding=Name('/' + enc),
                                                             DescendantFonts=[cidf]))
        return out
    # U+4E2D (middle), U+6587 (text), U+3042 (hiragana A) / Hangul U+D55C
    page_pdf(os.path.join(OUT, 'cjk-noembed.pdf'), noembed,
             'BT /F1 40 Tf 10 150 Td <4E2D6587> Tj ET BT /F2 40 Tf 10 100 Td <4E2D6587> Tj ET '
             'BT /F3 40 Tf 10 50 Td <D55C AE00> Tj ET BT /F4 40 Tf 10 5 Td <4E2D6587> Tj ET')

    # Expected CIDs sampled from the Adobe CMap sources.
    rnd = random.Random(1234)
    with open(os.path.join(OUT, 'cmap-samples.txt'), 'w') as o:
        o.write('# name hexcode bytelen expected-cid (sampled from Adobe cmap-resources by tools/cmapgen/gen_font_fixtures.py)\n')
        for name in g['NAMES']:
            chain = []
            u, wm, cs, rng = g['parse'](g['find'](name))
            seen = 0
            for n in (1, 2, 3, 4):
                rs = rng[n]
                for lo, hi, cid in rnd.sample(rs, min(len(rs), 6)):
                    code = rnd.randint(lo, hi)
                    o.write(f'{name} {code:0{2 * n}X} {n} {cid + code - lo}\n')


if __name__ == '__main__':
    g = load_gen()
    make_type1()
    make_cff()
    make_cjk(g)
    print('done')
