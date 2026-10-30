"""Generates CFF predefined tables from fontTools 4.63.0 and Adobe TN 5176."""
import os
import fontTools
from fontTools.cffLib import (
    cffStandardStrings as standard_strings,
    cffISOAdobeStrings,
    cffIExpertStrings,
    cffExpertSubsetStrings,
)
from fontTools.encodings.StandardEncoding import StandardEncoding as se

expert_encoding = [
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    1, 229, 230, 0, 231, 232, 233, 234, 235, 236, 237, 238, 13, 14, 15, 99,
    239, 240, 241, 242, 243, 244, 245, 246, 247, 248, 27, 28, 249, 250, 251, 252,
    0, 253, 254, 255, 256, 257, 0, 0, 0, 258, 0, 0, 259, 260, 261, 262,
    0, 0, 263, 264, 265, 0, 266, 109, 110, 267, 268, 269, 0, 270, 271, 272,
    273, 274, 275, 276, 277, 278, 279, 280, 281, 282, 283, 284, 285, 286, 287, 288,
    289, 290, 291, 292, 293, 294, 295, 296, 297, 298, 299, 300, 301, 302, 303, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    0, 304, 305, 306, 0, 0, 307, 308, 309, 310, 311, 0, 312, 0, 0, 313,
    0, 0, 314, 315, 0, 0, 316, 317, 318, 0, 0, 0, 158, 155, 163, 319,
    320, 321, 322, 323, 324, 325, 0, 0, 326, 150, 164, 169, 327, 328, 329, 330,
    331, 332, 333, 334, 335, 336, 337, 338, 339, 340, 341, 342, 343, 344, 345, 346,
    347, 348, 349, 350, 351, 352, 353, 354, 355, 356, 357, 358, 359, 360, 361, 362,
    363, 364, 365, 366, 367, 368, 369, 370, 371, 372, 373, 374, 375, 376, 377, 378,
]

assert fontTools.__version__ == "4.63.0"
assert len(standard_strings) == 391 and len(se) == 256
assert len(cffISOAdobeStrings) == 229
assert len(cffIExpertStrings) == 166
assert len(cffExpertSubsetStrings) == 87
assert len(expert_encoding) == 256
assert all(sid < len(standard_strings) for sid in expert_encoding)


def block(items, quoted=True):
    lines, cur = [], '        '
    for x in items:
        t = ('"%s"' % x if quoted else str(x)) + ', '
        if len(cur) + len(t) > 150:
            lines.append(cur.rstrip()); cur = '        '
        cur += t
    lines.append(cur.rstrip())
    return '\n'.join(lines)


out = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'src', 'PortableDocument.Content', 'CffStandardStrings.cs')
open(out, 'w', newline='\n').write(
    'namespace PortableDocument.Content;\n\n'
    '/// <summary>CFF standard strings and predefined charset/encoding tables from Adobe TN 5176 and fontTools 4.63.0.</summary>\n'
    'internal static class CffStandardStrings\n{\n    public static readonly string[] Strings =\n    [\n' + block(standard_strings) +
    '\n    ];\n\n    /// <summary>ISOAdobe charset GID to SID.</summary>\n'
    '    public static readonly int[] ISOAdobeCharset =\n    [\n' + block([standard_strings.index(name) for name in cffISOAdobeStrings], False) +
    '\n    ];\n\n    /// <summary>Expert charset GID to SID.</summary>\n'
    '    public static readonly int[] ExpertCharset =\n    [\n' + block([standard_strings.index(name) for name in cffIExpertStrings], False) +
    '\n    ];\n\n    /// <summary>ExpertSubset charset GID to SID.</summary>\n'
    '    public static readonly int[] ExpertSubsetCharset =\n    [\n' + block([standard_strings.index(name) for name in cffExpertSubsetStrings], False) +
    '\n    ];\n\n    /// <summary>Adobe ExpertEncoding code to SID (0 denotes .notdef).</summary>\n'
    '    public static readonly int[] ExpertEncoding =\n    [\n' + block(expert_encoding, False) +
    '\n    ];\n\n    /// <summary>StandardEncoding code to glyph name (".notdef" when unassigned).</summary>\n'
    '    public static readonly string[] StandardEncoding =\n    [\n' + block(se) + '\n    ];\n}\n')
