"""Generates src/PortableDocument.Content/CffStandardStrings.cs from fontTools tables."""
import os
from fontTools.cffLib import cffStandardStrings as s
from fontTools.encodings.StandardEncoding import StandardEncoding as se

assert len(s) == 391 and len(se) == 256


def block(items):
    lines, cur = [], '        '
    for x in items:
        t = '"%s", ' % x
        if len(cur) + len(t) > 150:
            lines.append(cur.rstrip()); cur = '        '
        cur += t
    lines.append(cur.rstrip())
    return '\n'.join(lines)


out = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'src', 'PortableDocument.Content', 'CffStandardStrings.cs')
open(out, 'w', newline='\n').write(
    'namespace PortableDocument.Content;\n\n'
    '/// <summary>The 391 CFF standard strings (CFF spec appendix A) and Adobe StandardEncoding, generated from fontTools by tools/cmapgen/gen_cff_strings.py.</summary>\n'
    'internal static class CffStandardStrings\n{\n    public static readonly string[] Strings =\n    [\n' + block(s) +
    '\n    ];\n\n    /// <summary>StandardEncoding code to glyph name (".notdef" when unassigned).</summary>\n'
    '    public static readonly string[] StandardEncoding =\n    [\n' + block(se) + '\n    ];\n}\n')
