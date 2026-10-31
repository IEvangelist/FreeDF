"""Generate a matrix of single-annotation PDFs *without* /AP (viewer-generated appearances) and compare our renderer's
output against PDFium (pypdfium2).  Self-authored fixtures; the reference is PDFium's rendering, not the spec.

  python tools/make_annot_matrix.py OUTDIR            # write PDFs + PDFium PNGs
  python tools/make_annot_matrix.py OUTDIR --ours DIR # additionally diff against our PNGs (name.ours.png) in DIR
"""
import sys, os, struct, zlib

W = H = 200
BASE = {
    "highlight": "/Subtype /Highlight /Rect [20 150 120 170] /QuadPoints [20 170 120 170 20 150 120 150] /C [1 0.5 0]",
    "underline": "/Subtype /Underline /Rect [20 150 120 170] /QuadPoints [20 170 120 170 20 150 120 150] /C [0 0 1]",
    "strikeout": "/Subtype /StrikeOut /Rect [20 150 120 170] /QuadPoints [20 170 120 170 20 150 120 150] /C [1 0 0]",
    "squiggly": "/Subtype /Squiggly /Rect [20 150 120 170] /QuadPoints [20 170 120 170 20 150 120 150] /C [0 0.6 0]",
    "square": "/Subtype /Square /Rect [20 20 100 80] /C [0 0 1] /IC [0.8 1 0.8] /BS << /W 3 >>",
    "square_rd": "/Subtype /Square /Rect [20 20 100 80] /RD [8 8 8 8] /C [0 0 1] /IC [0.8 1 0.8] /BS << /W 3 >>",
    "square_dash": "/Subtype /Square /Rect [20 20 100 80] /C [0 0 0] /BS << /W 2 /S /D /D [6 3] >>",
    "square_cloud": "/Subtype /Square /Rect [20 20 120 100] /C [0 0 1] /BS << /W 1 >> /BE << /S /C /I 1 >>",
    "circle": "/Subtype /Circle /Rect [20 20 100 80] /C [1 0 0] /IC [1 1 0.5] /BS << /W 2 >>",
    "circle_cloud": "/Subtype /Circle /Rect [20 20 120 100] /C [1 0 0] /BS << /W 1 >> /BE << /S /C /I 2 >>",
    "line": "/Subtype /Line /Rect [10 10 190 190] /L [30 30 170 170] /C [0 0 0] /BS << /W 2 >>",
    "line_arrows": "/Subtype /Line /Rect [10 10 190 190] /L [30 30 170 120] /LE [/OpenArrow /ClosedArrow] /C [1 0 0] /IC [0 1 0] /BS << /W 2 >>",
    "line_leader": "/Subtype /Line /Rect [10 10 190 190] /L [40 40 160 40] /LL 20 /LLE 5 /LE [/Square /Circle] /C [0 0 1] /BS << /W 1 >>",
    "line_caption": "/Subtype /Line /Rect [10 10 190 190] /L [30 100 170 100] /Cap true /Contents (Cap) /C [0 0 0]",
    "polygon": "/Subtype /Polygon /Rect [10 10 190 190] /Vertices [30 30 170 40 100 160] /C [0 0 1] /IC [1 0.8 0.8] /BS << /W 2 >>",
    "polygon_cloud": "/Subtype /Polygon /Rect [10 10 190 190] /Vertices [30 30 170 40 100 160] /C [0 0 1] /BS << /W 1 >> /BE << /S /C /I 1 >>",
    "polyline": "/Subtype /PolyLine /Rect [10 10 190 190] /Vertices [30 30 100 160 170 40] /LE [/Diamond /Butt] /C [0 0.5 0] /BS << /W 2 >>",
    "ink": "/Subtype /Ink /Rect [10 10 190 190] /InkList [[30 30 60 150 100 60 170 170]] /C [1 0 1] /BS << /W 3 >>",
    "freetext": "/Subtype /FreeText /Rect [20 100 180 180] /Contents (Hello FreeText) /DA (0 0 1 rg /Helv 14 Tf) /C [1 1 0.8] /BS << /W 1 >>",
    "freetext_q": "/Subtype /FreeText /Rect [20 100 180 180] /Contents (Centered) /Q 1 /DA (0 g /Helv 14 Tf) /C [0.9 0.9 1]",
    "freetext_callout": "/Subtype /FreeText /IT /FreeTextCallout /Rect [60 100 180 180] /CL [20 40 50 100 60 140] /Contents (Callout) /DA (0 g /Helv 12 Tf) /C [1 1 0.8] /LE /OpenArrow",
    "text_note": "/Subtype /Text /Rect [50 50 68 68] /Name /Note /C [1 1 0] /Contents (A note)",
    "text_comment": "/Subtype /Text /Rect [50 50 68 68] /Name /Comment /C [1 0.5 0] /Contents (x)",
    "text_help": "/Subtype /Text /Rect [50 50 68 68] /Name /Help /C [0 1 1] /Contents (x)",
    "text_key": "/Subtype /Text /Rect [50 50 68 68] /Name /Key /C [0.5 1 0.5] /Contents (x)",
    "text_paragraph": "/Subtype /Text /Rect [50 50 68 68] /Name /Paragraph /C [1 0.7 0.7] /Contents (x)",
    "text_newparagraph": "/Subtype /Text /Rect [50 50 68 68] /Name /NewParagraph /C [0.7 0.7 1] /Contents (x)",
    "text_insert": "/Subtype /Text /Rect [50 50 68 68] /Name /Insert /C [0.7 1 1] /Contents (x)",
    "caret": "/Subtype /Caret /Rect [50 50 70 70] /C [1 0 0]",
    "stamp": "/Subtype /Stamp /Rect [20 60 180 120] /Name /Approved /C [0 0.6 0]",
    "fileattach": "/Subtype /FileAttachment /Rect [50 50 68 68] /Name /PushPin /C [0.5 0.5 0.5]",
    "link_border": "/Subtype /Link /Rect [20 20 120 60] /Border [0 0 2] /C [1 0 0]",
    "link_bs": "/Subtype /Link /Rect [20 20 120 60] /BS << /W 2 /S /D /D [4 2] >> /C [0 0 1]",
    "popup_open": "/Subtype /Popup /Rect [100 100 190 190] /Open true",
    "redact": "/Subtype /Redact /Rect [20 20 120 60] /QuadPoints [20 60 120 60 20 20 120 20] /IC [0 0 0] /OC 99 0 R",
    "hidden_square": "/Subtype /Square /Rect [20 20 100 80] /C [0 0 1] /F 2",
    "noview_square": "/Subtype /Square /Rect [20 20 100 80] /C [0 0 1] /F 32",
    "print_only_square": "/Subtype /Square /Rect [20 20 100 80] /C [0 0 1] /F 4",
    "noprint_square": "/Subtype /Square /Rect [20 20 100 80] /C [0 0 1] /F 0",
    "square_ca": "/Subtype /Square /Rect [20 20 100 80] /C [1 0 0] /IC [1 0 0] /CA 0.3",
    "widget_text": "/Subtype /Widget /FT /Tx /T (t) /V (Hello) /DA (0 g /Helv 12 Tf) /Rect [20 20 120 44] /MK << /BC [0] /BG [0.9 0.9 0.9] >>",
}

def pdf(body, extra_page=""):
    objs = [
        "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R] /DA (0 g /Helv 0 Tf) /DR << /Font << /Helv 6 0 R >> >> >> >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        f"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {W} {H}] /Annots [4 0 R] {extra_page} >>",
        f"<< /Type /Annot {body} >>",
        "null",
        "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
    ]
    out = b"%PDF-1.7\n"; offs = []
    for i, o in enumerate(objs, 1):
        offs.append(len(out)); out += f"{i} 0 obj\n{o}\nendobj\n".encode()
    x = len(out)
    out += f"xref\n0 {len(objs)+1}\n0000000000 65535 f \n".encode()
    for o in offs: out += f"{o:010d} 00000 n \n".encode()
    out += f"trailer\n<< /Root 1 0 R /Size {len(objs)+1} >>\nstartxref\n{x}\n%%EOF\n".encode()
    return out

def write_png(path, w, h, rgba):
    raw = b"".join(b"\0" + rgba[y*w*4:(y+1)*w*4] for y in range(h))
    def ch(t, d): return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d))
    open(path, "wb").write(b"\x89PNG\r\n\x1a\n" + ch(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) + ch(b"IDAT", zlib.compress(raw)) + ch(b"IEND", b""))

def main():
    out = sys.argv[1]; os.makedirs(out, exist_ok=True)
    import pypdfium2 as p
    for name, body in BASE.items():
        data = pdf(body)
        path = os.path.join(out, name + ".pdf"); open(path, "wb").write(data)
        doc = p.PdfDocument(data)
        bmp = doc[0].render(scale=1, may_draw_forms=True, rev_byteorder=True)
        pil = bmp.to_pil().convert("RGBA")
        write_png(os.path.join(out, name + ".pdfium.png"), pil.width, pil.height, pil.tobytes())
    print(f"wrote {len(BASE)} cases to {out}")

if __name__ == "__main__":
    main()
