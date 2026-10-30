"""Generates the hand-built Type 1 Multiple Master / Expert fixtures under tests/fixtures/fonts.

Needs: pikepdf (and the helpers in gen_font_fixtures.py). Optional: pypdfium2 to print the independent PDFium render probes.
Outputs:
  type1-mm.pdf     F1 = 4-master (Weight x Width) MMType1 program using OtherSubrs 14/15/16/17, F2 = 2-master program using 14/18,
                   F3 = pre-blended "snapshot" of F1 whose numbers are computed here with exact fractions (independent of the C# blend code).
  type1-expert.pdf F1 = Type 1 program with an explicit Expert `dup N /name put` encoding, F2 = same glyphs with `/Encoding ExpertEncoding def`.
All expected geometry is hand-derived from Adobe TN #5015 section 3.12: result = a1 + sum_{i>=1} W[i] * (a(i+1) - a1).
"""
import os, sys
from fractions import Fraction as Fr

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_font_fixtures as g
from pikepdf import Name, Dictionary, Stream

OUT = g.OUT


class B:
    """A value that differs per master."""
    def __init__(self, *v): self.v = v


def blend(vals, w):
    return vals[0] + sum(Fr(w[i]) * (vals[i] - vals[0]) for i in range(1, len(vals)))


def emit(prog, k, w, snapshot):
    out, i = [], 0
    while i < len(prog):
        if not isinstance(prog[i], B):
            out.append(prog[i]); i += 1; continue
        grp = []
        while i < len(prog) and isinstance(prog[i], B): grp.append(prog[i]); i += 1
        if snapshot:
            for b in grp:
                val = blend(b.v, w) * 32
                assert val.denominator == 1, 'snapshot weights must be multiples of 1/32'
                out += [str(int(val)), '32', 'div']
        else:
            out += [str(b.v[0]) for b in grp]
            for b in grp: out += [str(b.v[m] - b.v[0]) for m in range(1, k)]
            out += [str(3 + len(grp)), 'callsubr']
    return ' '.join(map(str, out))


def program(name, clear_extra, subrs, glyphs, enc, fontbbox='0 0 1000 500'):
    clear = ('%!PS-AdobeFont-1.0: ' + name + ' 001.000\n11 dict begin\n' + clear_extra +
             '/FontName /' + name + ' def\n/PaintType 0 def\n/FontType 1 def\n/FontMatrix [0.001 0 0 0.001 0 0] readonly def\n' +
             ('/Encoding ' + enc + ' def\n' if isinstance(enc, str) else
              '/Encoding 256 array\n0 1 255 {1 index exch /.notdef put} for\n' + ''.join('dup %d /%s put\n' % (c, n) for c, n in enc.items()) + 'readonly def\n') +
             '/FontBBox {' + fontbbox + '} readonly def\ncurrentdict end\ncurrentfile eexec\n').encode()
    lead = b'\0\0\0\0'
    priv = bytearray(b'dup /Private 8 dict dup begin\n/RD {string currentfile exch readstring pop} executeonly def\n'
                     b'/ND {noaccess def} executeonly def\n/NP {noaccess put} executeonly def\n/lenIV 4 def\n/BlueValues [] def\n')
    priv += b'/Subrs %d array\n' % len(subrs)
    for i, s in enumerate(subrs):
        e = g.encrypt(g.cs(s), 4330, lead)
        priv += b'dup %d %d RD ' % (i, len(e)) + e + b' NP\n'
    priv += b'ND\n2 index /CharStrings %d dict dup begin\n' % len(glyphs)
    for n, s in glyphs.items():
        e = g.encrypt(g.cs(s), 4330, lead)
        priv += b'/%s %d RD ' % (n.encode(), len(e)) + e + b' ND\n'
    priv += b'end\nend\nreadonly put\nnoaccess put\ndup /FontName get exch definefont pop\nmark currentfile closefile\n'
    cipher = g.encrypt(bytes(priv), 55665, lead)
    trailer = (b'0' * 64 + b'\n') * 8 + b'cleartomark\n'
    return clear, cipher, trailer


def base_subrs(k):
    s = ['3 0 callothersubr pop pop setcurrentpoint return', '0 1 callothersubr return', '0 2 callothersubr return', 'return']
    for n, os_ in ((1, 14), (2, 15), (3, 16), (4, 17), (5, 'x'), (6, 18)):
        s.append('return' if os_ == 'x' else f'{n * k} {os_} callothersubr ' + 'pop ' * n + 'return')
    return s  # index 3 + n calls the n-value blend


def mm_font(name, k, weights, axis_info, glyphs_prog, enc, snapshot):
    extra = ''
    if not snapshot:
        extra = ('/FontInfo 5 dict dup begin /FullName (%s) def\n%s end readonly def\n' % (name, axis_info))
    clear_extra = extra
    wv = ('/WeightVector [' + ' '.join(str(float(x)) for x in weights) + '] def\n') if not snapshot else ''
    glyphs = {n: emit(p, k, weights, snapshot) for n, p in glyphs_prog.items()}
    clear, cipher, trailer = program(name, clear_extra + wv, base_subrs(k), glyphs, enc)
    return clear + cipher + trailer, len(clear), len(cipher)


def font_obj(pdf, name, data, l1, l2, enc_first, widths, subtype=Name.Type1):
    ff = Stream(pdf, data); ff.Length1, ff.Length2, ff.Length3 = l1, l2, len(data) - l1 - l2
    desc = pdf.make_indirect(Dictionary(Type=Name.FontDescriptor, FontName=Name('/' + name), Flags=4, FontBBox=[0, 0, 1000, 500],
                                        ItalicAngle=0, Ascent=800, Descent=0, CapHeight=600, StemV=80, FontFile=ff))
    return pdf.make_indirect(Dictionary(Type=Name.Font, Subtype=subtype, BaseFont=Name('/' + name), FontDescriptor=desc,
                                        FirstChar=enc_first, LastChar=enc_first + len(widths) - 1, Widths=[float(w) for w in widths]))


# ---------------------------------------------------------------- F1: 4 masters, two axes (design order from TN #5015 Table 1)
W4 = [Fr(9, 32), Fr(3, 32), Fr(15, 32), Fr(5, 32)]  # weight 600 -> blend .625, width 400 -> blend .25 (see BlendDesignMap below)
assert sum(W4) == 1
# Weight map [[200 0][500 .5][900 1]]: 600 -> .5 + (100/400)*.5 = .625; Width map [[300 0][700 1]]: 400 -> .25.
# Positions [0 0][0 1][1 0][1 1]: w = (1-wt)(1-wd), (1-wt)wd, wt(1-wd), wt*wd
assert W4 == [(1 - Fr(5, 8)) * (1 - Fr(1, 4)), (1 - Fr(5, 8)) * Fr(1, 4), Fr(5, 8) * (1 - Fr(1, 4)), Fr(5, 8) * Fr(1, 4)]
AXES4 = ('/BlendAxisTypes [/Weight /Width] def\n/BlendDesignPositions [[0 0][0 1][1 0][1 1]] def\n'
         '/BlendDesignMap [[[200 0][500 .5][900 1]][[300 0][700 1]]] def\n')
WX = {'A': B(700, 900, 760, 960), 'B': B(500, 500, 500, 500), 'C': B(600, 800, 640, 840), 'D': B(550, 750, 550, 750)}
PROG4 = {
    '.notdef': [0, 500, 'hsbw', 'endchar'],
    # box: width W, height H both blended; sbx 50 plain
    'A': [50, WX['A'], 'hsbw', 0, 0, 'rmoveto', B(300, 500, 360, 560), 'hlineto', B(400, 400, 450, 450), 'vlineto',
          B(-300, -500, -360, -560), 'hlineto', 'closepath', 'endchar'],
    # slanted parallelogram through a 2-value blend (OtherSubr 15)
    'B': [50, 500, 'hsbw', 0, 0, 'rmoveto', B(100, 160, 120, 180), B(200, 200, 260, 260), 'rlineto', 200, 'hlineto',
          B(-100, -160, -120, -180), B(-200, -200, -260, -260), 'rlineto', 'closepath', 'endchar'],
    # rrcurveto as two 3-value blends (OtherSubr 16 twice, TN #5015 section 3.13)
    'C': [50, WX['C'], 'hsbw', 0, 0, 'rmoveto',
          B(60, 80, 60, 80), B(200, 200, 260, 260), B(140, 180, 140, 180), B(100, 100, 60, 60), B(100, 140, 100, 140), B(-300, -300, -320, -320),
          'rrcurveto', 'closepath', 'endchar'],
    # vhcurveto through a 4-value blend (OtherSubr 17)
    'D': [50, WX['D'], 'hsbw', 0, 0, 'rmoveto', B(150, 150, 200, 200), B(100, 140, 100, 140), B(100, 100, 80, 80), B(150, 200, 150, 200),
          'vhcurveto', -300, 'hlineto', 'closepath', 'endchar'],
}


def fix_groups(prog, split6):
    """rrcurveto's 6 values come in two 3-value blends: split the run into groups of 3 for the calls."""
    out, run = [], []
    for t in prog + [None]:
        if isinstance(t, B): run.append(t); continue
        if run:
            n = len(run)
            groups = [3, 3] if n == 6 and split6 else [n]
            pos = 0
            for gsz in groups:
                out += run[pos:pos + gsz]; pos += gsz
                out.append('\0split')
        if t is not None: out.append(t)
        run = []
    return out


def emit_split(prog, k, w, snapshot):
    pieces, cur = [], []
    for t in prog:
        if t == '\0split':
            pieces.append(emit(cur, k, w, snapshot)); cur = []
        else: cur.append(t)
    pieces.append(emit(cur, k, w, snapshot))
    return ' '.join(p for p in pieces if p)


def mm_font_split(name, k, weights, axis_info, glyphs_prog, enc, snapshot, split6=True):
    glyphs = {n: emit_split(fix_groups(p, split6), k, weights, snapshot) for n, p in glyphs_prog.items()}
    extra = '' if snapshot else '/FontInfo 5 dict dup begin /FullName (%s) def\n%s end readonly def\n' % (name, axis_info)
    wv = '' if snapshot else '/WeightVector [' + ' '.join(str(float(x)) for x in weights) + '] def\n'
    clear, cipher, trailer = program(name, extra + wv, base_subrs(k), glyphs, enc)
    return clear + cipher + trailer, len(clear), len(cipher)


# ---------------------------------------------------------------- F2: 2 masters, one axis, OtherSubr 18 (6 values)
W2 = [Fr(1, 4), Fr(3, 4)]
AXES2 = '/BlendAxisTypes [/Weight] def\n/BlendDesignPositions [[0][1]] def\n/BlendDesignMap [[[1 0][999 1]]] def\n'
WX2 = {'A': B(700, 900), 'O': B(700, 900)}
PROG2 = {
    '.notdef': [0, 500, 'hsbw', 'endchar'],
    'A': [50, WX2['A'], 'hsbw', 0, 0, 'rmoveto', B(300, 500), 'hlineto', B(400, 400), 'vlineto', B(-300, -500), 'hlineto', 'closepath', 'endchar'],
    # six-value blend feeding rrcurveto directly (OtherSubr 18)
    'O': [50, WX2['O'], 'hsbw', 200, 0, 'rmoveto',
          B(110, 150), B(0, 0), B(90, 130), B(90, 120), B(0, 0), B(110, 150), 'rrcurveto',
          B(-110, -150), B(0, 0), B(-90, -130), B(90, 120), 'rlineto', 'closepath', 'endchar'],
}


def adv(wx, w): return blend(wx.v, w)


def make_mm():
    enc4 = {65: 'A', 66: 'B', 67: 'C', 68: 'D'}
    mm4, a1, a2 = mm_font_split('TestMM4', 4, W4, AXES4, PROG4, enc4, False)
    snap, s1, s2 = mm_font_split('TestMM4Snap', 4, W4, '', PROG4, enc4, True)
    mm2, b1, b2 = mm_font_split('TestMM2', 2, W2, AXES2, PROG2, {65: 'A', 66: 'O'}, False, split6=False)
    widths4 = [adv(WX['A'], W4), 500, adv(WX['C'], W4), adv(WX['D'], W4)]
    widths2 = [adv(WX2['A'], W2), adv(WX2['O'], W2)]
    print('F1 advances', [float(x) for x in widths4], 'F2 advances', [float(x) for x in widths2])

    def fonts(pdf):
        return {'/F1': font_obj(pdf, 'TestMM4', mm4, a1, a2, 65, widths4, Name.MMType1),
                '/F3': font_obj(pdf, 'TestMM4Snap', snap, s1, s2, 65, widths4),
                '/F2': font_obj(pdf, 'TestMM2', mm2, b1, b2, 65, widths2, Name.MMType1)}
    g.page_pdf(os.path.join(OUT, 'type1-mm.pdf'), fonts,
               'BT /F1 100 Tf 10 220 Td (ABCD) Tj ET BT /F3 100 Tf 10 120 Td (ABCD) Tj ET BT /F2 100 Tf 10 20 Td (AB) Tj ET', 450, 300)


# ---------------------------------------------------------------- Expert
EXPERT = {33: 'exclamsmall', 36: 'dollaroldstyle', 48: 'zerooldstyle', 49: 'oneoldstyle', 86: 'ff', 87: 'fi', 97: 'Asmall',
          98: 'Bsmall', 126: 'Tildesmall', 188: 'onequarter'}


def make_expert():
    glyphs = {'.notdef': '0 500 hsbw endchar'}
    for i, n in enumerate(EXPERT.values()):  # distinct heights so rendering identifies the glyph
        glyphs[n] = f'50 600 hsbw 0 0 rmoveto 400 hlineto {100 + 40 * i} vlineto -400 hlineto closepath endchar'
    subrs = base_subrs(2)
    explicit, e1, e2 = _plain('TestExpert', subrs, glyphs, EXPERT)
    named, n1, n2 = _plain('TestExpertNamed', subrs, glyphs, 'ExpertEncoding')
    codes = list(EXPERT)

    def fonts(pdf):
        out = {}
        for key, (name, data, l1, l2) in {'/F1': ('TestExpert', explicit, e1, e2), '/F2': ('TestExpertNamed', named, n1, n2)}.items():
            out[key] = font_obj(pdf, name, data, l1, l2, 33, [600] * (188 - 33 + 1))
        return out
    text = ''.join(chr(c) for c in codes)
    g.page_pdf(os.path.join(OUT, 'type1-expert.pdf'), fonts,
               f'BT /F1 40 Tf 10 120 Td ({_esc(text)}) Tj ET BT /F2 40 Tf 10 20 Td ({_esc(text)}) Tj ET', 450, 200)


def _esc(s): return s.replace('\\', '\\\\').replace('(', '\\(').replace(')', '\\)')


def _plain(name, subrs, glyphs, enc):
    clear, cipher, trailer = program(name, '', subrs, glyphs, enc, '0 0 600 500')
    return clear + cipher + trailer, len(clear), len(cipher)


if __name__ == '__main__':
    make_mm()
    make_expert()
    print('done')
