"""Generates hb-expected.json: reference glyph sequences from HarfBuzz (uharfbuzz) for the OFL test fonts in this directory.

    pip install uharfbuzz
    python generate_expected.py

Each case records the font, text (as code points), direction/script/language/features and the HarfBuzz result
(glyph id, character cluster, advance/offset in font units, visual order). The C# tests (ShapingTests) shape the same
input with PortableDocument.Content.Shaping.TextShaper and compare. Cases the C# shaper is known to differ on are listed
in "known_divergences" with the reason; everything else must match exactly.
"""
import json
import os
import uharfbuzz as hb

HERE = os.path.dirname(os.path.abspath(__file__))
FONTS = {
    "latin": "SourceSans3-subset.otf",
    "arabic": "NotoSansArabic-subset.ttf",
    "hebrew": "NotoSansHebrew-subset.ttf",
    "devanagari": "NotoSansDevanagari-subset.ttf",
}

CASES = [
    # id, font, text, direction, script, language, features
    ("latin-plain", "latin", "Hello World", "ltr", "Latn", None, {}),
    ("latin-fi", "latin", "office fi fl ffi", "ltr", "Latn", None, {}),
    ("latin-fi-off", "latin", "office fi fl ffi", "ltr", "Latn", None, {"liga": False, "clig": False}),
    ("latin-kern", "latin", "AV To Wa. P, Ty", "ltr", "Latn", None, {}),
    ("latin-kern-off", "latin", "AV To Wa. P, Ty", "ltr", "Latn", None, {"kern": False}),
    ("latin-combining", "latin", "e\u0301 a\u0308 o\u0302", "ltr", "Latn", None, {}),
    ("latin-mark-gpos", "latin", "x\u0301 a\u0323 W\u030a", "ltr", "Latn", None, {}),
    ("latin-precomposed", "latin", "\u00e9\u00e0\u00f1 \u00c5", "ltr", "Latn", None, {}),
    ("latin-smcp", "latin", "Small Caps", "ltr", "Latn", None, {"smcp": True}),
    ("latin-turkish", "latin", "I\u0131 \u0130i", "ltr", "Latn", "TRK ", {}),
    ("latin-emoji-notdef", "latin", "a\U0001F600b", "ltr", "Latn", None, {}),
    ("arabic-hello", "arabic", "\u0645\u0631\u062d\u0628\u0627", "rtl", "Arab", None, {}),
    ("arabic-salam", "arabic", "\u0627\u0644\u0633\u0644\u0627\u0645 \u0639\u0644\u064a\u0643\u0645", "rtl", "Arab", None, {}),
    ("arabic-lam-alef", "arabic", "\u0644\u0627 \u0644\u0623 \u0644\u0625 \u0644\u0622", "rtl", "Arab", None, {}),
    ("arabic-basmala", "arabic", "\u0628\u0633\u0645 \u0627\u0644\u0644\u0647", "rtl", "Arab", None, {}),
    ("arabic-marks", "arabic", "\u0645\u064f\u062d\u064e\u0645\u0651\u064e\u062f", "rtl", "Arab", None, {}),
    ("arabic-digits", "arabic", "\u0661\u0662\u0663 \u0664\u0665", "rtl", "Arab", None, {}),
    ("arabic-zwnj", "arabic", "\u0628\u200c\u0628 \u0628\u200d\u0628", "rtl", "Arab", None, {}),
    ("arabic-persian", "arabic", "\u067e\u0686\u0698\u06af \u06a9\u06cc", "rtl", "Arab", None, {}),
    ("hebrew-shalom", "hebrew", "\u05e9\u05dc\u05d5\u05dd", "rtl", "Hebr", None, {}),
    ("hebrew-niqqud", "hebrew", "\u05e2\u05b4\u05d1\u05b0\u05e8\u05b4\u05d9\u05ea", "rtl", "Hebr", None, {}),
    ("hebrew-final-forms", "hebrew", "\u05db\u05da \u05de\u05dd \u05e0\u05df \u05e4\u05e3 \u05e6\u05e5", "rtl", "Hebr", None, {}),
    ("devanagari-hindi", "devanagari", "\u0939\u093f\u0928\u094d\u0926\u0940", "ltr", "Deva", None, {}),
    ("devanagari-namaste", "devanagari", "\u0928\u092e\u0938\u094d\u0924\u0947", "ltr", "Deva", None, {}),
    ("devanagari-karma", "devanagari", "\u0915\u0930\u094d\u092e", "ltr", "Deva", None, {}),
    ("devanagari-kssa", "devanagari", "\u0915\u094d\u0937\u0924\u094d\u0930\u093f\u092f", "ltr", "Deva", None, {}),
    ("devanagari-prakash", "devanagari", "\u092a\u094d\u0930\u0915\u093e\u0936", "ltr", "Deva", None, {}),
    ("devanagari-kitab", "devanagari", "\u0915\u093f\u0924\u093e\u092c", "ltr", "Deva", None, {}),
    ("devanagari-nukta", "devanagari", "\u0915\u093c\u093e \u091c\u093c", "ltr", "Deva", None, {}),
    ("devanagari-reph-matra", "devanagari", "\u0926\u0930\u094d\u0936\u0928\u093f", "ltr", "Deva", None, {}),
    ("devanagari-sentence", "devanagari", "\u092d\u093e\u0930\u0924 \u090f\u0915 \u0926\u0947\u0936 \u0939\u0948", "ltr", "Deva", None, {}),
]


def shape(case):
    cid, fkey, text, direction, script, lang, features = case
    blob = hb.Blob.from_file_path(os.path.join(HERE, FONTS[fkey]))
    face = hb.Face(blob)
    font = hb.Font(face)
    buf = hb.Buffer()
    buf.add_str(text)
    buf.direction = direction
    buf.script = script
    if lang:
        buf.language = lang.strip().lower()
    buf.cluster_level = hb.BufferClusterLevel.MONOTONE_CHARACTERS
    hb.shape(font, buf, features)
    glyphs = []
    for info, pos in zip(buf.glyph_infos, buf.glyph_positions):
        glyphs.append({
            "gid": info.codepoint, "cluster": info.cluster,
            "xadv": pos.x_advance, "yadv": pos.y_advance, "xoff": pos.x_offset, "yoff": pos.y_offset,
        })
    return {
        "id": cid, "font": FONTS[fkey], "text": [ord(c) for c in text], "direction": direction, "script": script,
        "language": lang, "features": {k: (1 if v else 0) for k, v in features.items()},
        "upem": face.upem, "glyphs": glyphs,
    }


if __name__ == "__main__":
    out = {"generator": "uharfbuzz " + hb.__version__ + " / HarfBuzz " + hb.version_string(), "cases": [shape(c) for c in CASES]}
    with open(os.path.join(HERE, "hb-expected.json"), "w", encoding="utf-8") as f:
        json.dump(out, f, indent=1)
    print("wrote", len(out["cases"]), "cases")
