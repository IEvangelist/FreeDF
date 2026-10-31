using System.Globalization;

namespace PortableDocument.Content.Shaping;

public enum BidiClass : byte { L, R, AL, EN, ES, ET, AN, CS, NSM, BN, B, S, WS, ON, LRE, LRO, RLE, RLO, PDF, LRI, RLI, FSI, PDI }

public enum ScriptId : byte { Common, Inherited, Latin, Greek, Cyrillic, Armenian, Hebrew, Arabic, Syriac, Thaana, Devanagari, Bengali, Gurmukhi, Gujarati, Tamil, Thai, Han, Hiragana, Katakana, Hangul, Other }

public enum JoiningType : byte { None, Right, Dual, Causing, Transparent, Left }

/// <summary>
/// Compact Unicode property tables used by shaping, bidi and line breaking.
/// Backed by range tables generated from the Unicode Character Database (see UnicodeTables.cs and tests/fixtures/ucd/generate_unicode_tables.py):
/// General_Category, Bidi_Class, Joining_Type, Bidi_Mirroring_Glyph, Bidi_Paired_Bracket, Line_Break (folded onto the UAX #14 classes used here), Script and Extended_Pictographic.
/// </summary>
public static class UnicodeData
{
    // ---------- generated Unicode properties (UnicodeTables.cs) ----------
    public static System.Globalization.UnicodeCategory GetCategory(int cp) => (System.Globalization.UnicodeCategory)UnicodeTables.Category(cp);

    public static BidiClass GetBidiClass(int cp) => (BidiClass)UnicodeTables.BidiClass(cp);

    /// <summary>Bidi_Mirroring_Glyph, or the code point itself.</summary>
    public static int Mirrored(int cp) => UnicodeTables.Mirrored(cp);

    internal static bool IsBracket(int cp, out int pair, out bool open)
    {
        pair = 0; open = false;
        if (!UnicodeTables.TryGetBracket(cp, out var p, out var type)) return false;
        pair = Canon(p);
        open = type == 1;
        return true;
    }

    // U+2329/232A are canonically equivalent to U+3008/3009 for bracket matching (UAX #9 BD16).
    internal static int Canon(int cp) => cp switch { 0x2329 => 0x3008, 0x232A => 0x3009, _ => cp };

    // ---------- scripts ----------
    public static ScriptId GetScript(int cp) => (ScriptId)UnicodeTables.Script(cp);
    public static string[] OpenTypeScriptTags(ScriptId s) => s switch
    {
        ScriptId.Latin => ["latn", "DFLT"],
        ScriptId.Greek => ["grek", "DFLT", "latn"],
        ScriptId.Cyrillic => ["cyrl", "DFLT", "latn"],
        ScriptId.Armenian => ["armn", "DFLT"],
        ScriptId.Hebrew => ["hebr", "DFLT"],
        ScriptId.Arabic => ["arab", "DFLT"],
        ScriptId.Syriac => ["syrc", "DFLT"],
        ScriptId.Thaana => ["thaa", "DFLT"],
        ScriptId.Devanagari => ["dev2", "deva", "DFLT"],
        ScriptId.Bengali => ["bng2", "beng", "DFLT"],
        ScriptId.Gurmukhi => ["gur2", "guru", "DFLT"],
        ScriptId.Gujarati => ["gjr2", "gujr", "DFLT"],
        ScriptId.Tamil => ["tml2", "taml", "DFLT"],
        ScriptId.Thai => ["thai", "DFLT"],
        ScriptId.Han => ["hani", "DFLT"],
        ScriptId.Hiragana or ScriptId.Katakana => ["kana", "DFLT"],
        ScriptId.Hangul => ["hang", "DFLT"],
        _ => ["DFLT", "latn"],
    };

    public static bool IsRightToLeftScript(ScriptId s) => s is ScriptId.Hebrew or ScriptId.Arabic or ScriptId.Syriac or ScriptId.Thaana;

    // ---------- Arabic joining ----------
    public static JoiningType GetJoiningType(int cp) => (JoiningType)UnicodeTables.JoiningType(cp);

    // ---------- Indic (Devanagari subset) ----------
    internal enum IndicCat : byte { Other, Consonant, Vowel, Nukta, Virama, Matra, PreMatra, Modifier, Zwj, Zwnj, Ra }

    internal static IndicCat GetIndicCat(int cp)
    {
        switch (cp)
        {
            case 0x0930: return IndicCat.Ra;
            case 0x093C: return IndicCat.Nukta;
            case 0x094D: return IndicCat.Virama;
            case 0x093F: return IndicCat.PreMatra;
            case 0x200D: return IndicCat.Zwj;
            case 0x200C: return IndicCat.Zwnj;
        }
        if (cp is >= 0x0915 and <= 0x0939 or >= 0x0958 and <= 0x095F or >= 0x0978 and <= 0x097F) return IndicCat.Consonant;
        if (cp is >= 0x0904 and <= 0x0914 or 0x0960 or 0x0961 or >= 0x0972 and <= 0x0977) return IndicCat.Vowel;
        if (cp is >= 0x093E and <= 0x094C or 0x094E or 0x094F or >= 0x0955 and <= 0x0957 or 0x0962 or 0x0963) return IndicCat.Matra;
        if (cp is >= 0x0900 and <= 0x0903 or 0x093A or 0x093B or >= 0x0951 and <= 0x0954) return IndicCat.Modifier;
        return IndicCat.Other;
    }

    // ---------- emoji ----------
    public static bool IsExtendedPictographic(int cp) => UnicodeTables.ExtendedPictographic(cp) != 0;

    public static bool IsRegionalIndicator(int cp) => cp is >= 0x1F1E6 and <= 0x1F1FF;
    public static bool IsEmojiModifier(int cp) => cp is >= 0x1F3FB and <= 0x1F3FF;

    /// <summary>Length in UTF-16 units of the (extended) emoji cluster at <paramref name="i"/>, or 0 when no emoji starts there.</summary>
    public static int EmojiClusterLength(string s, int i, int end)
    {
        if (i >= end || System.Text.Rune.DecodeFromUtf16(s.AsSpan(i, end - i), out var r0, out _) != System.Buffers.OperationStatus.Done) return 0;
        var cp = r0.Value;
        var p = i + r0.Utf16SequenceLength;
        bool Peek(out int c, out int len)
        {
            c = 0; len = 0;
            if (p >= end) return false;
            if (char.IsHighSurrogate(s[p]) && p + 1 < end && char.IsLowSurrogate(s[p + 1])) { c = char.ConvertToUtf32(s[p], s[p + 1]); len = 2; }
            else { c = s[p]; len = 1; }
            return true;
        }
        if (IsRegionalIndicator(cp))
        {
            if (Peek(out var c2, out var l2) && IsRegionalIndicator(c2)) p += l2;
            return p - i;
        }
        var keycap = cp is >= 0x30 and <= 0x39 or 0x23 or 0x2A;
        if (!IsExtendedPictographic(cp) && !keycap) return 0;
        if (keycap)
        {
            var q = p;
            if (Peek(out var c, out var l) && c == 0xFE0F) { p += l; }
            if (Peek(out c, out l) && c == 0x20E3) { p += l; return p - i; }
            p = q;
            return 0;
        }
        while (true)
        {
            while (Peek(out var c, out var l) && (c == 0xFE0F || c == 0xFE0E || IsEmojiModifier(c) || c is >= 0xE0020 and <= 0xE007F)) p += l;
            if (Peek(out var z, out var lz) && z == 0x200D)
            {
                var save = p; p += lz;
                if (Peek(out var n, out var ln) && IsExtendedPictographic(n)) { p += ln; continue; }
                p = save + lz;
            }
            break;
        }
        return p - i;
    }

    // ---------- line breaking ----------
    internal enum LB : byte { XX, BK, CR, LF, NL, SP, ZW, ZWJ, CM, WJ, GL, BA, BB, HY, CL, CP, EX, IS, NS, OP, QU, SY, AL, NU, PR, PO, ID, HL, RI, EB, EM, IN, B2, CB }

    internal static LB GetLineBreakClass(int cp) => (LB)UnicodeTables.LineBreak(cp);
}
