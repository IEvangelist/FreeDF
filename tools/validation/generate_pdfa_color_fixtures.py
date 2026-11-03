"""Generate small PDF/A color-validation fixtures without third-party assets."""

import argparse
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_OUTPUT = ROOT / "tests" / "fixtures" / "validation" / "pdfa-color"
CALRGB = (
    b"[/CalRGB << /WhitePoint [0.9505 1 1.089] "
    b"/Gamma [2.2 2.2 2.2] "
    b"/Matrix [0.4124 0.2126 0.0193 0.3576 0.7152 0.1192 0.1805 0.0722 0.9505] >>]"
)


def build(part: int, mode: str) -> bytes:
    xmp = (
        '<?xpacket begin="\ufeff" id="W5M0MpCehiHzreSzNTczkc9d"?>'
        '<x:xmpmeta xmlns:x="adobe:ns:meta/">'
        '<rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">'
        '<rdf:Description rdf:about="" xmlns:pdfaid="http://www.aiim.org/pdfa/ns/id/"'
        f' pdfaid:part="{part}" pdfaid:conformance="B"/>'
        '</rdf:RDF></x:xmpmeta><?xpacket end="w"?>'
    ).encode("utf-8")

    content = {
        "calrgb": b"/C cs 0.2 0.4 0.6 sc 10 10 80 80 re f",
        "default-gray": b"10 10 80 80 re f",
        "implicit-gray": b"10 10 80 80 re f",
        "explicit-gray": b"0 g 10 10 80 80 re f",
        "text-gray": b"BT /F1 72 Tf 10 10 Td (A) Tj ET",
        "text-default-gray": b"BT /F1 72 Tf 10 10 Td (A) Tj ET",
    }[mode]
    color_spaces = b"/C " + CALRGB
    if mode in ("default-gray", "text-default-gray"):
        color_spaces += b" /DefaultGray [/CalGray << /WhitePoint [0.9505 1 1.089] /Gamma 2.2 >>]"

    def stream(data: bytes, entries: bytes = b"") -> bytes:
        return b"<< /Length %d " % len(data) + entries + b">>\nstream\n" + data + b"\nendstream"

    objects = [
        b"<< /Type /Catalog /Pages 2 0 R /Metadata 5 0 R >>",
        b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R "
        b"/Resources << /ColorSpace << " + color_spaces + b" >> >> >>",
        stream(content),
        stream(xmp, b"/Type /Metadata /Subtype /XML "),
    ]
    if mode in ("text-gray", "text-default-gray"):
        objects[2] = objects[2].replace(b"/Resources <<", b"/Resources << /Font << /F1 6 0 R >>")
        objects.extend(
            [
                b"<< /Type /Font /Subtype /Type3 /Name /F1 /FontBBox [0 0 600 700] "
                b"/FontMatrix [0.001 0 0 0.001 0 0] /Encoding << /Type /Encoding /Differences [65 /A] >> "
                b"/FirstChar 65 /LastChar 65 /Widths [600] /CharProcs << /A 7 0 R >> /Resources << >> >>",
                stream(b"600 0 0 0 600 700 d1 0 0 600 700 re f"),
            ]
        )

    pdf = bytearray(b"%PDF-1.4\n%\xe2\xe3\xcf\xd3\n")
    offsets = []
    for number, body in enumerate(objects, 1):
        offsets.append(len(pdf))
        pdf.extend(f"{number} 0 obj\n".encode("ascii") + body + b"\nendobj\n")
    xref = len(pdf)
    pdf.extend(f"xref\n0 {len(objects) + 1}\n0000000000 65535 f \n".encode("ascii"))
    for offset in offsets:
        pdf.extend(f"{offset:010d} 00000 n \n".encode("ascii"))
    pdf.extend(
        f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R "
        "/ID [<00112233445566778899AABBCCDDEEFF> <00112233445566778899AABBCCDDEEFF>] "
        f">>\nstartxref\n{xref}\n%%EOF\n".encode("ascii")
    )
    return bytes(pdf)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--mode",
        choices=("all", "calrgb", "default-gray", "implicit-gray", "explicit-gray", "text-gray", "text-default-gray"),
        default="all",
    )
    parser.add_argument("--output-dir", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()

    args.output_dir.mkdir(parents=True, exist_ok=True)
    modes = (
        "calrgb",
        "default-gray",
        "implicit-gray",
        "explicit-gray",
        "text-gray",
        "text-default-gray",
    ) if args.mode == "all" else (args.mode,)
    for mode in modes:
        for part in (1, 2, 3):
            output = args.output_dir / f"{mode}-pdfa-{part}b.pdf"
            output.write_bytes(build(part, mode))
            print(output.relative_to(ROOT) if output.is_relative_to(ROOT) else output)


if __name__ == "__main__":
    main()
