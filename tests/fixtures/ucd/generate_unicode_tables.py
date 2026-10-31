"""Generates src/PortableDocument.Content/Shaping/UnicodeTables.cs from the Unicode Character Database.

    python generate_unicode_tables.py [cache-dir]

Inputs are downloaded from unicode.org (pinned to UNICODE_VERSION) into the cache directory (default: %TEMP%/ucd-<version>)
and verified against the SHA-256 pins below before use. Properties generated: General_Category, Bidi_Class,
Joining_Type (ArabicShaping.txt, T for unlisted Mn/Me/Cf), Bidi_Mirroring_Glyph, Bidi_Paired_Bracket(_Type),
Line_Break (folded onto the classes UAX #14 basics uses), Script (folded onto ScriptId) and Extended_Pictographic.

Unicode Character Database files are (c) Unicode, Inc. and distributed under the Unicode License v3
(https://www.unicode.org/license.txt); see THIRD-PARTY-NOTICES.md.
"""
import bisect
import hashlib
import os
import re
import sys
import tempfile
import urllib.request

UNICODE_VERSION = "16.0.0"
BASE = f"https://www.unicode.org/Public/{UNICODE_VERSION}/ucd/"
FILES = {
    "UnicodeData.txt": "ff58e5823bd095166564a006e47d111130813dcf8bf234ef79fa51a870edb48f",
    "extracted/DerivedBidiClass.txt": "71ed943a49c58568d8d92e80ecc2ba2f06e62aee9c8ebb0e6e8bd2c3ed8b180e",
    "ArabicShaping.txt": "764f420cedfc8b43d9fec251c957a5d55fc45d40f6573f162990ed1dce7e36e0",
    "BidiMirroring.txt": "d7afdadd1bbd66f5a663ac0e8f7958f18fd9491fc0bc59ec5877cb82db71db7d",
    "BidiBrackets.txt": "b8f32554c6f658821fb0ee742d21c5b1f2086b9bf13071fed04894b022f93d67",
    "LineBreak.txt": "e97e4259d0d20fab150b9c7b4b28abfae5cd78ca97e7f4ac6ed20d685d5f4a7c",
    "Scripts.txt": "9e88f0a677df47311106340be8ede2ecdacd9c1c931831218d2be6d5508e0039",
    "emoji/emoji-data.txt": "f1365a5173eee18e1f98b240cdc492e84a25f1ce7e0c9d1094eb29c41a22696a",
}

MAX = 0x110000
# Orders must match the C# enums (UnicodeCategory, BidiClass, JoiningType, UnicodeData.LB, ScriptId).
GC = "Lu Ll Lt Lm Lo Mn Mc Me Nd Nl No Zs Zl Zp Cc Cf Cs Co Pc Pd Ps Pe Pi Pf Po Sm Sc Sk So Cn".split()
BIDI = "L R AL EN ES ET AN CS NSM BN B S WS ON LRE LRO RLE RLO PDF LRI RLI FSI PDI".split()
JOIN = {"U": 0, "R": 1, "D": 2, "C": 3, "T": 4, "L": 5}  # None, Right, Dual, Causing, Transparent, Left
LB = "XX BK CR LF NL SP ZW ZWJ CM WJ GL BA BB HY CL CP EX IS NS OP QU SY AL NU PR PO ID HL RI EB EM IN B2 CB".split()
LB_FOLD = {"XX": "AL", "AI": "AL", "SG": "AL", "SA": "AL", "CJ": "NS", "H2": "ID", "H3": "ID", "JL": "ID", "JV": "ID", "JT": "ID", "CB": "CB"}
SCRIPTS = "Common Inherited Latin Greek Cyrillic Armenian Hebrew Arabic Syriac Thaana Devanagari Bengali Gurmukhi Gujarati Tamil Thai Han Hiragana Katakana Hangul Other".split()


def fetch(cache):
    os.makedirs(cache, exist_ok=True)
    paths = {}
    for rel, digest in FILES.items():
        p = os.path.join(cache, os.path.basename(rel))
        if not os.path.exists(p):
            urllib.request.urlretrieve(BASE + rel, p)
        actual = hashlib.sha256(open(p, "rb").read()).hexdigest()
        if actual != digest:
            raise SystemExit(f"{rel}: sha256 {actual} does not match pin {digest}")
        paths[rel] = p
    return paths


def lines(path):
    for raw in open(path, encoding="utf-8"):
        yield raw.rstrip("\n")


def parse_range(s):
    a, _, b = s.strip().partition("..")
    return int(a, 16), int(b or a, 16)


def apply_ranges(table, path, mapper, default_pattern=True):
    """Applies '@missing' defaults first (file order), then explicit 'range ; value' lines."""
    explicit = []
    for ln in lines(path):
        m = re.match(r"#\s*@missing:\s*([0-9A-F]+\.\.[0-9A-F]+);\s*(\w+)", ln)
        if m and default_pattern:
            a, b = parse_range(m.group(1))
            v = mapper(m.group(2))
            for c in range(a, b + 1):
                table[c] = v
            continue
        body = ln.split("#")[0].strip()
        if not body:
            continue
        rng, _, val = body.partition(";")
        explicit.append((parse_range(rng), val.strip().split(";")[0].strip()))
    for (a, b), v in explicit:
        mv = mapper(v)
        for c in range(a, b + 1):
            table[c] = mv


def compress(table):
    starts, values = [], []
    for c, v in enumerate(table):
        if not values or values[-1] != v:
            starts.append(c)
            values.append(v)
    return starts, values


def emit_ranges(out, name, table, ctype):
    starts, values = compress(table)
    out.append(f"    static ReadOnlySpan<int> {name}Starts => [{', '.join(f'0x{s:X}' for s in starts)}];")
    out.append(f"    static ReadOnlySpan<{ctype}> {name}Values => [{', '.join(str(v) for v in values)}];")
    out.append(f"    public static {ctype} {name}(int cp) => {name}Values[Find({name}Starts, cp)];")
    out.append("")
    return len(starts)


def main():
    cache = sys.argv[1] if len(sys.argv) > 1 else os.path.join(tempfile.gettempdir(), f"ucd-{UNICODE_VERSION}")
    f = fetch(cache)
    stats = {}

    gc = [GC.index("Cn")] * MAX
    prev_first = None
    for ln in lines(f["UnicodeData.txt"]):
        parts = ln.split(";")
        cp = int(parts[0], 16)
        cat = GC.index(parts[2])
        if parts[1].endswith(", First>"):
            prev_first = cp
        elif parts[1].endswith(", Last>"):
            for c in range(prev_first, cp + 1):
                gc[c] = cat
        else:
            gc[cp] = cat

    bidi = [BIDI.index("L")] * MAX
    long_names = {"Left_To_Right": "L", "Right_To_Left": "R", "Arabic_Letter": "AL", "European_Terminator": "ET"}
    apply_ranges(bidi, f["extracted/DerivedBidiClass.txt"], lambda v: BIDI.index(long_names.get(v, v)))

    join = [JOIN["U"]] * MAX
    for c in range(MAX):
        if GC[gc[c]] in ("Mn", "Me", "Cf"):
            join[c] = JOIN["T"]
    for ln in lines(f["ArabicShaping.txt"]):
        body = ln.split("#")[0].strip()
        if not body:
            continue
        p = [x.strip() for x in body.split(";")]
        join[int(p[0], 16)] = JOIN[p[2]]

    mirror = []
    for ln in lines(f["BidiMirroring.txt"]):
        if "[BEST FIT]" in ln:
            continue
        body = ln.split("#")[0].strip()
        if body:
            a, b = body.split(";")
            mirror.append((int(a, 16), int(b, 16)))
    mirror.sort()

    brackets = []
    for ln in lines(f["BidiBrackets.txt"]):
        body = ln.split("#")[0].strip()
        if body:
            a, b, t = [x.strip() for x in body.split(";")]
            brackets.append((int(a, 16), int(b, 16), 1 if t == "o" else 2))
    brackets.sort()

    lb = [LB.index("AL")] * MAX
    def lb_map(v):
        v = LB_FOLD.get(v, v)
        return LB.index(v) if v in LB else LB.index("AL")
    apply_ranges(lb, f["LineBreak.txt"], lb_map)
    # LB1/LB9 style folding: combining marks in SA stay AL unless Mn/Mc (CM).
    for ln in lines(f["LineBreak.txt"]):
        body = ln.split("#")[0].strip()
        if body.endswith("SA") or ";SA" in body.replace(" ", ""):
            rng = parse_range(body.split(";")[0])
            for c in range(rng[0], rng[1] + 1):
                if GC[gc[c]] in ("Mn", "Mc"):
                    lb[c] = LB.index("CM")

    script = [SCRIPTS.index("Other")] * MAX
    def script_map(v):
        return SCRIPTS.index(v) if v in SCRIPTS else SCRIPTS.index("Other")
    for ln in lines(f["Scripts.txt"]):
        body = ln.split("#")[0].strip()
        if body:
            rng, name = [x.strip() for x in body.split(";")]
            a, b = parse_range(rng)
            v = script_map(name)
            for c in range(a, b + 1):
                script[c] = v

    pict = [0] * MAX
    for ln in lines(f["emoji/emoji-data.txt"]):
        body = ln.split("#")[0].strip()
        if body:
            rng, name = [x.strip() for x in body.split(";")]
            if name == "Extended_Pictographic":
                a, b = parse_range(rng)
                for c in range(a, b + 1):
                    pict[c] = 1

    out = [
        "// <auto-generated>",
        f"// Generated by tests/fixtures/ucd/generate_unicode_tables.py from the Unicode Character Database {UNICODE_VERSION}. Do not edit.",
        "// Unicode Character Database files are (c) Unicode, Inc., used under the Unicode License v3 (https://www.unicode.org/license.txt).",
        "// </auto-generated>",
        "namespace PortableDocument.Content.Shaping;",
        "",
        "/// <summary>Range tables for the Unicode properties used by shaping, bidi and line breaking (binary searched, no allocation).</summary>",
        "internal static class UnicodeTables",
        "{",
        f'    public const string Version = "{UNICODE_VERSION}";',
        "",
        "    static int Find(ReadOnlySpan<int> starts, int cp)",
        "    {",
        "        int lo = 0, hi = starts.Length - 1;",
        "        while (lo < hi)",
        "        {",
        "            var mid = (lo + hi + 1) >> 1;",
        "            if (starts[mid] <= cp) lo = mid; else hi = mid - 1;",
        "        }",
        "        return lo;",
        "    }",
        "",
    ]
    for name, table in [("Category", gc), ("BidiClass", bidi), ("JoiningType", join), ("LineBreak", lb), ("Script", script), ("ExtendedPictographic", pict)]:
        stats[name] = emit_ranges(out, name, table, "byte")
    out.append(f"    static ReadOnlySpan<int> MirrorFrom => [{', '.join(f'0x{a:X}' for a, _ in mirror)}];")
    out.append(f"    static ReadOnlySpan<int> MirrorTo => [{', '.join(f'0x{b:X}' for _, b in mirror)}];")
    out.append("    /// <summary>Bidi_Mirroring_Glyph, or the code point itself.</summary>")
    out.append("    public static int Mirrored(int cp) { var i = MirrorFrom.BinarySearch(cp); return i >= 0 ? MirrorTo[i] : cp; }")
    out.append("")
    out.append(f"    static ReadOnlySpan<int> BracketFrom => [{', '.join(f'0x{a:X}' for a, _, _ in brackets)}];")
    out.append(f"    static ReadOnlySpan<int> BracketPair => [{', '.join(f'0x{b:X}' for _, b, _ in brackets)}];")
    out.append(f"    static ReadOnlySpan<byte> BracketType => [{', '.join(str(t) for _, _, t in brackets)}];")
    out.append("    /// <summary>Bidi_Paired_Bracket and Bidi_Paired_Bracket_Type (1 = open, 2 = close).</summary>")
    out.append("    public static bool TryGetBracket(int cp, out int pair, out int type) { var i = BracketFrom.BinarySearch(cp); if (i < 0) { pair = 0; type = 0; return false; } pair = BracketPair[i]; type = BracketType[i]; return true; }")
    out.append("}")
    target = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "src", "PortableDocument.Content", "Shaping", "UnicodeTables.cs"))
    with open(target, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(out) + "\n")
    print("wrote", target, stats, "mirror", len(mirror), "brackets", len(brackets))


if __name__ == "__main__":
    main()
