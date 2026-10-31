"""Gives the Source Sans 3 test subset a new family name.

The subset made with pyftsubset is a Modified Version of Source Sans 3 and the font's licence declares the Reserved Font Name
"Source", so the subset must not carry that name. Glyph order and layout tables are untouched (the HarfBuzz expectations in
hb-expected.json depend on them); only the name table and the CFF name fields change.

    python rename_source_subset.py
"""
import os
from fontTools.ttLib import TTFont

HERE = os.path.dirname(os.path.abspath(__file__))
path = os.path.join(HERE, "SourceSans3-subset.otf")
font = TTFont(path)

FAMILY, PS, FULL, UNIQUE = "Shaping Test Sans", "ShapingTestSans-Regular", "Shaping Test Sans", "3.052;SHAPING;ShapingTestSans-Regular"
names = font["name"]
for rec in list(names.names):
    if rec.nameID in (16, 17, 21, 22):
        names.removeNames(nameID=rec.nameID)
for pid, eid, lid in ((3, 1, 0x409), (1, 0, 0)):
    for nid, value in ((1, FAMILY), (3, UNIQUE), (4, FULL), (6, PS)):
        names.setName(value, nid, pid, eid, lid)

if "CFF " in font:
    cff = font["CFF "].cff
    top = cff.topDictIndex[0]
    cff.fontNames[0] = PS
    top.FamilyName = FAMILY
    top.FullName = FULL
font.save(path)
print("renamed", path)
