"""Generates src/AdoBye.Content/Resources/glyphlist.txt (Adobe Glyph List: name;HEX) from fontTools.agl."""
import os
from fontTools.agl import AGL2UV

out = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'src', 'AdoBye.Content', 'Resources', 'glyphlist.txt')
os.makedirs(os.path.dirname(out), exist_ok=True)
with open(out, 'w', newline='\n') as f:
    for k in sorted(AGL2UV):
        f.write('%s;%X\n' % (k, AGL2UV[k]))
print(len(AGL2UV))
