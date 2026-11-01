"""Generate a deterministic 10,000-page inheritance/allocation fixture.

Usage: python gen_page_tree.py <output.pdf>
The pages are intentionally empty: this isolates open/page-tree work, not rendering.
"""
from pathlib import Path
import sys

n = 10000
objects = [
    "<< /Type /Catalog /Pages 2 0 R >>",
    f"<< /Type /Pages /Count {n} /MediaBox [0 0 612 792] /Resources << >> /Rotate 0 /Kids ["
    + " ".join(f"{i + 3} 0 R" for i in range(n)) + "] >>",
]
objects += ["<< /Type /Page /Parent 2 0 R >>"] * n
data = bytearray(b"%PDF-1.7\n")
offsets = [0]
for i, obj in enumerate(objects, 1):
    offsets.append(len(data))
    data.extend(f"{i} 0 obj\n{obj}\nendobj\n".encode("ascii"))
xref = len(data)
data.extend(f"xref\n0 {len(offsets)}\n0000000000 65535 f \n".encode("ascii"))
for offset in offsets[1:]:
    data.extend(f"{offset:010} 00000 n \n".encode("ascii"))
data.extend(f"trailer\n<< /Size {len(offsets)} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n".encode("ascii"))
Path(sys.argv[1]).write_bytes(data)
