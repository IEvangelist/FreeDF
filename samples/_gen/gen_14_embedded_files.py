import hashlib
from theme import *

FILES = [
    ("readings.csv", "text/csv", "Sensor readings, five columns", "Data",
     "time,temp_c,humidity,pressure,status\n" + "\n".join(f"{i * 15:02d}:00,{18 + (i * 7) % 9 + i * 0.3:.1f},{40 + (i * 11) % 30},{1008 + (i * 5) % 14},{'ok' if i % 5 else 'warn'}" for i in range(24)) + "\n"),
    ("notes.md", "text/markdown", "Authoring notes in Markdown", "Supplement",
     "# Notes\n\nThis attachment travels inside the PDF.\n\n- Stored in /EmbeddedFiles\n- Referenced from /AF with /AFRelationship\n- Compressed with FlateDecode\n"),
    ("mark.svg", "image/svg+xml", "Vector logo mark", "Source",
     '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><rect x="10" y="10" width="60" height="60" rx="14" fill="#4F46E5"/><rect x="30" y="30" width="60" height="60" rx="14" fill="#7C3AED" fill-opacity=".85"/></svg>\n'),
]


def build(path):
    d = Doc("Embedded files", subject="EmbeddedFiles name tree, associated files (/AF), FileAttachment annotations, file specification parameters", lang="en-US")
    t = Type(d)
    pg = d.page(*A4)
    background(pg, P["paper"])
    page_header(pg, t, "Embedded files", "Packaging  /  Attachments", 1, 1)
    pg.text(t.sans, 11, M, pg.h - 106, "Three files ride inside this PDF. Open the attachments panel, or click a paperclip.", color=P["slate"])

    specs, names, afs = [], [], []
    for name, mime, desc, rel, text in FILES:
        data = text.encode()
        mod = "D:20260115093000Z"
        ef = d.stream(f"/Type /EmbeddedFile /Subtype /{mime.replace('/', '#2F')} /Params << /Size {len(data)} /CheckSum <{hashlib.md5(data).hexdigest()}> /ModDate ({mod}) /CreationDate ({mod}) >>", data)
        fs = d.add(f"<< /Type /Filespec /F {pstr(name)} /UF {utf16(name)} /Desc {pstr(desc)} /AFRelationship /{rel} /EF << /F {ef} 0 R /UF {ef} 0 R >> >>")
        specs.append(fs)
        names.append(f"{pstr(name)} {fs} 0 R")
        afs.append(f"{fs} 0 R")
    names_sorted = sorted(names)
    d.cat.append(f"/Names << /EmbeddedFiles << /Names [{' '.join(names_sorted)}] >> >> /AF [{' '.join(afs)}]")
    d.cat.append("/PageMode /UseAttachments")

    y = pg.h - 170
    icons = ("Paperclip", "PushPin", "Graph")
    for i, (name, mime, desc, rel, text) in enumerate(FILES):
        h = 92
        box(pg, M, y - h, pg.w - 2 * M, h, fill=P["white"], stroke=P["mist"], r=10)
        pg.text(t.serif_b, 17, M + 76, y - 32, name, color=P["ink"])
        pg.text(t.sans, 10.5, M + 76, y - 50, desc, color=P["slate"])
        pg.text(t.mono, 8.5, M + 76, y - 70, f"{mime}  ·  {len(text.encode())} bytes  ·  /AFRelationship /{rel}", color=P["muted"], kern=False)
        # attachment icon (annotation with AP)
        ix, iy = M + 22, y - 62
        r = (ix, iy, ix + 32, iy + 36)

        def draw(c, ix=ix, iy=iy):
            c.fill(P["indigo"]); c.move(ix + 4, iy + 2); c.line(ix + 20, iy + 2); c.line(ix + 28, iy + 10); c.line(ix + 28, iy + 34); c.line(ix + 4, iy + 34); c.close(); c.f()
            c.fill(lighten(P["indigo"], 0.55)); c.move(ix + 20, iy + 2); c.line(ix + 28, iy + 10); c.line(ix + 20, iy + 10); c.close(); c.f()
            c.stroke("#FFFFFF"); c.width(1.6); c.cap(1)
            for k in range(3):
                c.move(ix + 9, iy + 17 + k * 6); c.line(ix + 23, iy + 17 + k * 6); c.S()
        xf = XForm(d, r)
        draw(xf)
        a = xf.build()
        pg.add_annot(f"<< /Type /Annot /Subtype /FileAttachment /Rect [{n(r[0])} {n(r[1])} {n(r[2])} {n(r[3])}] /FS {specs[i]} 0 R /Name /{icons[i]} /Contents {pstr(desc)} /T (Embedded) /C [0.31 0.27 0.9] /F 4 /AP << /N {a} 0 R >> >>")
        y -= h + 16
    paragraph(pg, t.sans, 10, M, y - 12, pg.w - 2 * M,
              "Each file specification carries /EF with a Flate-compressed stream plus /Params (Size, MD5 CheckSum, dates). /AF at the catalog level marks them as associated files, as PDF/A-3 and PDF 2.0 require.",
              color=P["slate"], leading=15)
    d.save(path, version="2.0")


OUT = "14-embedded-files.pdf"
