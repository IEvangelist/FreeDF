"""Vendor Adobe's complete AGL and Zapf Dingbats lists, retaining their licenses.

Run with Python 3; no third-party packages are needed. The pinned upstream bytes
are embedded unchanged, including legacy names and multi-scalar mappings.
Algorithm: https://github.com/adobe-type-tools/agl-specification (version 2.9).
"""
import hashlib
from pathlib import Path
from urllib.request import urlopen

REVISION = "4036a9ca80a62f64f9de4f7321a9a045ad0ecfd6"
FILES = {
    "glyphlist.txt": ("a3b2f61ced9f3644cc0d4ecde5c59df34ca286c689d9484a43a710a81c466789", 4281),
    "zapfdingbats.txt": ("f6394e3cb8a447e84a1dad75d4baaf2aa7f45dc104faf369f4720e1a774ef2dc", 201),
}
OUT = Path(__file__).resolve().parents[2] / "src" / "PortableDocument.Core" / "Resources"

for name, (digest, count) in FILES.items():
    url = f"https://raw.githubusercontent.com/adobe-type-tools/agl-aglfn/{REVISION}/{name}"
    with urlopen(url, timeout=30) as response:
        data = response.read()
    if hashlib.sha256(data).hexdigest() != digest:
        raise ValueError(f"{name}: upstream SHA-256 mismatch")
    entries = [line for line in data.decode("ascii").splitlines() if line and not line.startswith("#")]
    if len(entries) != count:
        raise ValueError(f"{name}: expected {count} mappings, got {len(entries)}")
    (OUT / name).write_bytes(data)
    print(f"{name}: {count} mappings ({REVISION})")
