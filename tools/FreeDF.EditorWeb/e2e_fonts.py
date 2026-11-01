"""End-to-end check of the web editor's font output against independent parsers.

Usage: python e2e_fonts.py <server-url> <source.pdf> <out.pdf>
Drives a running FreeDF.EditorWeb (start it with PDF_EDITOR_FONT_DIRS pointing at a folder that has an .otf),
adds text items using a CFF OpenType font, a .ttc member, fallback glyphs and the original embedded font,
downloads the result and checks it with pikepdf (structure/fonts), pdfminer.six and pypdf (text extraction).
"""
import json, subprocess, sys, urllib.request

base, src, out = sys.argv[1].rstrip('/'), sys.argv[2], sys.argv[3]

def get(path):
    with urllib.request.urlopen(base + path) as r:
        return r.read()

def put(path, obj):
    req = urllib.request.Request(base + path, data=json.dumps(obj).encode(), method='PUT', headers={'Content-Type': 'application/json'})
    urllib.request.urlopen(req).read()

subprocess.run(['curl.exe', '-s', '-F', f'file=@{src}', base + '/api/open'], check=True)
spans = json.loads(get('/api/page/0/text'))
orig = next(s for s in spans if any(c.isalpha() for c in s['text']))
state = json.loads(get('/api/state'))
print('page', state['pages'][0], 'editing span', orig['index'], repr(orig['text']), orig['font'])

def item(id, text, font, y, **kw):
    d = dict(id=id, type='text', x=20, y=y, w=0, h=0, text=text, font=font, size=22, bold=False, italic=False, color='#000000',
             fill='', stroke='', strokeWidth=0, opacity=1, asset='', x2=0, y2=0)
    d.update(kw)
    return d

fonts = {f['family'] for f in json.loads(get('/api/fonts'))}
items = [
    item('a', 'Edited ' + orig['text'], 'Helvetica', orig['y'] - 0.95 * orig['size'], size=orig['size'], x=orig['x'], orig=orig['index'],
         oStyle='Helvetica|false|false', color=orig['color']),
]
expect = ['Edited']
if 'Source Code Pro' in fonts:
    items.append(item('b', 'OTF Wörld Ж 日本語 ΑΒΓ', 'Source Code Pro', 60))
    expect += ['OTF', 'Wörld', 'Ж', 'ΑΒΓ']
if 'Cambria Math' in fonts:
    items.append(item('c', 'TTC ∑∫ x²', 'Cambria Math', 100))
    expect += ['TTC', '∑', 'x²']
put('/api/page/0/items', items)
data = get('/api/download')
open(out, 'wb').write(data)
print('saved', len(data), 'bytes')

import pikepdf
pdf = pikepdf.open(out)
page = pdf.pages[0]
ok = True
for name, f in page.Resources.get('/Font', {}).items():
    sub = str(f.get('/Subtype'))
    info = [name, str(f.get('/BaseFont')), sub]
    if sub == '/Type0':
        d = f.DescendantFonts[0]
        fd = d.FontDescriptor
        ff = fd.get('/FontFile2') or fd.get('/FontFile3')
        info += [str(d.Subtype), 'FontFile2' if '/FontFile2' in fd else 'FontFile3:' + str(fd.FontFile3.get('/Subtype')), len(ff.read_bytes())]
        tu = f.get('/ToUnicode')
        if tu is None: ok = False; info.append('NO ToUnicode')
        else: info.append('ToUnicode %d bytes' % len(tu.read_bytes()))
    print(*info)
print('pikepdf: opened, %d page(s), qpdf check:' % len(pdf.pages), end=' ')
for o in pdf.objects:
    if isinstance(o, pikepdf.Stream): o.read_bytes()  # decode every stream
problems = pdf.get_warnings()
print('clean' if not problems else problems)
ok &= not problems

from pdfminer.high_level import extract_text
mine = extract_text(out)
import pypdf
theirs = pypdf.PdfReader(out).pages[0].extract_text()
for label, text in (('pdfminer', mine), ('pypdf', theirs)):
    flat = text.replace(' ', '').replace('\n', '')
    missing = [e for e in expect if e.replace(' ', '') not in flat]
    print(label, 'missing:', missing, '| text:', ' '.join(text.split())[:160])
    ok &= not missing
sys.exit(0 if ok else 1)
