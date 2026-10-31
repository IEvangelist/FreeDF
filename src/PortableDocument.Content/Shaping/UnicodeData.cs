using System.Globalization;

namespace PortableDocument.Content.Shaping;

public enum BidiClass : byte { L, R, AL, EN, ES, ET, AN, CS, NSM, BN, B, S, WS, ON, LRE, LRO, RLE, RLO, PDF, LRI, RLI, FSI, PDI }

public enum ScriptId : byte { Common, Inherited, Latin, Greek, Cyrillic, Armenian, Hebrew, Arabic, Syriac, Thaana, Devanagari, Bengali, Gurmukhi, Gujarati, Tamil, Thai, Han, Hiragana, Katakana, Hangul, Other }

public enum JoiningType : byte { None, Right, Dual, Causing, Transparent }

/// <summary>
/// Compact Unicode property tables used by shaping, bidi and line breaking.
/// These are range-based approximations of the UCD (Bidi_Class, Bidi_Mirroring, Bidi_Paired_Bracket, Script, Joining_Type, Line_Break):
/// exact for the Latin, Hebrew, Arabic, Devanagari, CJK and emoji ranges listed, general-category derived elsewhere.
/// </summary>
public static class UnicodeData
{
    // ---------- bidi ----------
    public static BidiClass GetBidiClass(int cp)
    {
        switch (cp)
        {
            case 0x202A: return BidiClass.LRE; case 0x202B: return BidiClass.RLE; case 0x202C: return BidiClass.PDF;
            case 0x202D: return BidiClass.LRO; case 0x202E: return BidiClass.RLO;
            case 0x2066: return BidiClass.LRI; case 0x2067: return BidiClass.RLI; case 0x2068: return BidiClass.FSI; case 0x2069: return BidiClass.PDI;
            case 0x200E: return BidiClass.L; case 0x200F: return BidiClass.R; case 0x061C: return BidiClass.AL;
            case 0x000A: case 0x000D: case 0x001C: case 0x001D: case 0x001E: case 0x0085: case 0x2029: return BidiClass.B;
            case 0x0009: case 0x000B: case 0x001F: return BidiClass.S;
            case 0x000C: case 0x0020: case 0x1680: case 0x2028: case 0x205F: case 0x3000: return BidiClass.WS;
            case 0x002B: case 0x002D: case 0x207A: case 0x207B: case 0x208A: case 0x208B: case 0x2212: case 0xFB29: case 0xFE62: case 0xFE63: case 0xFF0B: case 0xFF0D: return BidiClass.ES;
            case 0x002C: case 0x002E: case 0x002F: case 0x003A: case 0x00A0: case 0x060C: case 0x202F: case 0x2044: case 0xFE50: case 0xFE52: case 0xFE55: case 0xFF0C: case 0xFF0E: case 0xFF0F: case 0xFF1A: return BidiClass.CS;
            case 0x0023: case 0x0024: case 0x0025: case 0x00A2: case 0x00A3: case 0x00A4: case 0x00A5: case 0x00B0: case 0x00B1: case 0x058F: case 0x0609: case 0x060A: case 0x066A:
            case 0x09F2: case 0x09F3: case 0x0E3F: case 0x2030: case 0x2031: case 0x2032: case 0x2033: case 0x2034: case 0x2035: case 0x2036: case 0x2037: case 0x20AC: case 0x2213: case 0xFF03: case 0xFF04: case 0xFF05: case 0xFFE0: case 0xFFE1: case 0xFFE5: case 0xFFE6: return BidiClass.ET;
            case 0x00B2: case 0x00B3: case 0x00B9: case 0x2070: return BidiClass.EN;
            case 0x06DD: case 0x066B: case 0x066C: case 0x08E2: return BidiClass.AN;
        }
        if (cp <= 0x7F)
        {
            if (cp is >= 0x30 and <= 0x39) return BidiClass.EN;
            if (cp < 0x20 || cp == 0x7F) return BidiClass.BN;
            if (cp is >= 0x41 and <= 0x5A or >= 0x61 and <= 0x7A) return BidiClass.L;
            return BidiClass.ON;
        }
        if (cp is >= 0x80 and <= 0x9F) return BidiClass.BN;
        if (cp is >= 0x0600 and <= 0x0605 or >= 0x0660 and <= 0x0669 or >= 0x10E60 and <= 0x10E7E) return BidiClass.AN;
        if (cp is >= 0x06F0 and <= 0x06F9 or >= 0x2074 and <= 0x2079 or >= 0x2080 and <= 0x2089 or >= 0x2488 and <= 0x249B or >= 0x1D7CE and <= 0x1D7FF) return BidiClass.EN;
        if (cp is >= 0x2000 and <= 0x200A) return BidiClass.WS;
        if (cp is >= 0x20A0 and <= 0x20CF) return BidiClass.ET;
        var cat = CharUnicodeInfo.GetUnicodeCategory(cp);
        if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark) return BidiClass.NSM;
        if (cat is UnicodeCategory.Format or UnicodeCategory.Control) return BidiClass.BN;
        if (cp is >= 0x0590 and <= 0x05FF or >= 0x07C0 and <= 0x085F or >= 0xFB1D and <= 0xFB4F or >= 0x10800 and <= 0x10CFF or >= 0x10F00 and <= 0x10FFF or >= 0x1E800 and <= 0x1EC6F or >= 0x1ECC0 and <= 0x1EDFF) return BidiClass.R;
        if (cp is >= 0x0600 and <= 0x07BF or >= 0x0860 and <= 0x08FF or >= 0xFB50 and <= 0xFDFF or >= 0xFE70 and <= 0xFEFF or >= 0x10D00 and <= 0x10D3F or >= 0x1EC70 and <= 0x1ECBF or >= 0x1EE00 and <= 0x1EEFF)
            return cat is UnicodeCategory.OtherPunctuation or UnicodeCategory.MathSymbol or UnicodeCategory.OtherSymbol or UnicodeCategory.ClosePunctuation or UnicodeCategory.OpenPunctuation ? BidiClass.ON : BidiClass.AL;
        return cat switch
        {
            UnicodeCategory.SpaceSeparator => BidiClass.WS,
            UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
                or UnicodeCategory.LetterNumber or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.OtherNumber => BidiClass.L,
            _ => BidiClass.ON,
        };
    }

    static readonly (int Open, int Close)[] BracketPairs =
    [
        (0x28, 0x29), (0x5B, 0x5D), (0x7B, 0x7D), (0x0F3A, 0x0F3B), (0x0F3C, 0x0F3D), (0x169B, 0x169C), (0x2045, 0x2046), (0x207D, 0x207E), (0x208D, 0x208E),
        (0x2308, 0x2309), (0x230A, 0x230B), (0x2329, 0x232A), (0x2768, 0x2769), (0x276A, 0x276B), (0x276C, 0x276D), (0x276E, 0x276F), (0x2770, 0x2771),
        (0x2772, 0x2773), (0x2774, 0x2775), (0x27E6, 0x27E7), (0x27E8, 0x27E9), (0x27EA, 0x27EB), (0x27EC, 0x27ED), (0x27EE, 0x27EF), (0x2983, 0x2984),
        (0x2985, 0x2986), (0x2987, 0x2988), (0x2989, 0x298A), (0x298B, 0x298C), (0x298D, 0x2990), (0x298F, 0x298E), (0x2991, 0x2992), (0x2993, 0x2994),
        (0x2995, 0x2996), (0x2997, 0x2998), (0x29D8, 0x29D9), (0x29DA, 0x29DB), (0x29FC, 0x29FD), (0x2E22, 0x2E23), (0x2E24, 0x2E25), (0x2E26, 0x2E27),
        (0x2E28, 0x2E29), (0x3008, 0x3009), (0x300A, 0x300B), (0x300C, 0x300D), (0x300E, 0x300F), (0x3010, 0x3011), (0x3014, 0x3015), (0x3016, 0x3017),
        (0x3018, 0x3019), (0x301A, 0x301B), (0xFE59, 0xFE5A), (0xFE5B, 0xFE5C), (0xFE5D, 0xFE5E), (0xFF08, 0xFF09), (0xFF3B, 0xFF3D), (0xFF5B, 0xFF5D),
        (0xFF5F, 0xFF60), (0xFF62, 0xFF63),
    ];

    static readonly (int A, int B)[] ExtraMirrors =
    [
        (0x3C, 0x3E), (0xAB, 0xBB), (0x2039, 0x203A), (0x2208, 0x220B), (0x2209, 0x220C), (0x220A, 0x220D), (0x2215, 0x29F5), (0x2264, 0x2265), (0x226A, 0x226B),
        (0x2282, 0x2283), (0x2286, 0x2287), (0x2308, 0x2309), (0x2329, 0x232A), (0x2264, 0x2265), (0x2266, 0x2267), (0x2268, 0x2269), (0x2A7D, 0x2A7E),
    ];

    static readonly Dictionary<int, int> Mirror = BuildMirror();

    static Dictionary<int, int> BuildMirror()
    {
        var m = new Dictionary<int, int>();
        foreach (var (a, b) in BracketPairs) { m[a] = b; m[b] = a; }
        foreach (var (a, b) in ExtraMirrors) { m[a] = b; m[b] = a; }
        return m;
    }

    /// <summary>Bidi_Mirroring_Glyph, or the code point itself.</summary>
    public static int Mirrored(int cp) => cp >= 0x28 && Mirror.TryGetValue(cp, out var m) ? m : cp;

    internal static bool IsBracket(int cp, out int pair, out bool open)
    {
        pair = 0; open = false;
        if (cp < 0x28) return false;
        var c = Canon(cp);
        foreach (var (o, cl) in BracketPairs)
        {
            if (c == o) { pair = Canon(cl); open = true; return true; }
            if (c == cl) { pair = o; open = false; return true; }
        }
        return false;
    }

    static int Canon(int cp) => cp switch { 0x2329 => 0x3008, 0x232A => 0x3009, _ => cp };

    // ---------- scripts ----------
    public static ScriptId GetScript(int cp)
    {
        if (cp < 0x80) return cp is >= 0x41 and <= 0x5A or >= 0x61 and <= 0x7A ? ScriptId.Latin : ScriptId.Common;
        if (cp is 0xAA or 0xBA or >= 0xC0 and <= 0x24F and not 0xD7 and not 0xF7 or >= 0x1E00 and <= 0x1EFF or >= 0x2C60 and <= 0x2C7F or >= 0xA720 and <= 0xA7FF or >= 0xFB00 and <= 0xFB06 or >= 0xFF21 and <= 0xFF3A or >= 0xFF41 and <= 0xFF5A or >= 0x250 and <= 0x2AF) return ScriptId.Latin;
        if (cp is >= 0x0300 and <= 0x036F or >= 0x1AB0 and <= 0x1AFF or >= 0x1DC0 and <= 0x1DFF or >= 0x20D0 and <= 0x20FF or >= 0xFE00 and <= 0xFE0F or >= 0xFE20 and <= 0xFE2F or 0x200D or >= 0xE0100 and <= 0xE01EF) return ScriptId.Inherited;
        if (cp is >= 0x0370 and <= 0x03FF or >= 0x1F00 and <= 0x1FFF) return cp == 0x037E ? ScriptId.Common : ScriptId.Greek;
        if (cp is >= 0x0400 and <= 0x052F or >= 0x1C80 and <= 0x1C8F or >= 0x2DE0 and <= 0x2DFF or >= 0xA640 and <= 0xA69F) return ScriptId.Cyrillic;
        if (cp is >= 0x0530 and <= 0x058F) return cp == 0x058F ? ScriptId.Common : ScriptId.Armenian;
        if (cp is >= 0x0590 and <= 0x05FF or >= 0xFB1D and <= 0xFB4F) return ScriptId.Hebrew;
        if (cp is >= 0x0600 and <= 0x06FF or >= 0x0750 and <= 0x077F or >= 0x08A0 and <= 0x08FF or >= 0xFB50 and <= 0xFDFF or >= 0xFE70 and <= 0xFEFF)
            return cp is 0x060C or 0x061B or 0x061F or 0x0640 or >= 0x0660 and <= 0x0669 or 0x06DD ? ScriptId.Common : ScriptId.Arabic;
        if (cp is >= 0x0700 and <= 0x074F) return ScriptId.Syriac;
        if (cp is >= 0x0780 and <= 0x07BF) return ScriptId.Thaana;
        if (cp is >= 0x0900 and <= 0x097F or >= 0xA8E0 and <= 0xA8FF) return cp is 0x0964 or 0x0965 ? ScriptId.Common : ScriptId.Devanagari;
        if (cp is >= 0x0980 and <= 0x09FF) return ScriptId.Bengali;
        if (cp is >= 0x0A00 and <= 0x0A7F) return ScriptId.Gurmukhi;
        if (cp is >= 0x0A80 and <= 0x0AFF) return ScriptId.Gujarati;
        if (cp is >= 0x0B80 and <= 0x0BFF) return ScriptId.Tamil;
        if (cp is >= 0x0E00 and <= 0x0E7F) return ScriptId.Thai;
        if (cp is >= 0x1100 and <= 0x11FF or >= 0x3130 and <= 0x318F or >= 0xA960 and <= 0xA97F or >= 0xAC00 and <= 0xD7FF) return ScriptId.Hangul;
        if (cp is >= 0x3040 and <= 0x309F) return cp is 0x3099 or 0x309A ? ScriptId.Inherited : ScriptId.Hiragana;
        if (cp is >= 0x30A0 and <= 0x30FF or >= 0x31F0 and <= 0x31FF or >= 0xFF66 and <= 0xFF9D) return cp is 0x30A0 or 0x30FB or 0x30FC ? ScriptId.Common : ScriptId.Katakana;
        if (cp is >= 0x2E80 and <= 0x2FDF or 0x3005 or 0x3007 or >= 0x3021 and <= 0x3029 or >= 0x3038 and <= 0x303B or >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF or >= 0xF900 and <= 0xFAFF or >= 0x20000 and <= 0x3FFFF) return ScriptId.Han;
        var cat = CharUnicodeInfo.GetUnicodeCategory(cp);
        if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark) return ScriptId.Inherited;
        return cat is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.OtherLetter or UnicodeCategory.ModifierLetter ? ScriptId.Other : ScriptId.Common;
    }

    /// <summary>OpenType script tags to try, most specific first (new Indic tags before legacy ones, then DFLT/latn).</summary>
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
    public static JoiningType GetJoiningType(int cp)
    {
        if (cp == 0x200D) return JoiningType.Causing;
        if (cp == 0x200C) return JoiningType.None;
        var cat = CharUnicodeInfo.GetUnicodeCategory(cp);
        if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format) return JoiningType.Transparent;
        if (cp is >= 0x0600 and <= 0x06FF or >= 0x0750 and <= 0x077F or >= 0x08A0 and <= 0x08FF or >= 0xFB50 and <= 0xFDFF or >= 0xFE70 and <= 0xFEFF)
        {
            if (cp is 0x0621 or 0x0674 or >= 0x0600 and <= 0x0605 or 0x0608 or 0x0609 or 0x060A or 0x060B or 0x060C or 0x061B or >= 0x061D and <= 0x061F or >= 0x0660 and <= 0x066D or 0x06D4 or 0x06DD or 0x06DE or 0x06E9 or >= 0x06F0 and <= 0x06F9) return JoiningType.None;
            if (cp == 0x0640) return JoiningType.Dual;
            if (cp is >= 0x0622 and <= 0x0625 or 0x0627 or 0x0629 or >= 0x062F and <= 0x0632 or 0x0648 or >= 0x0671 and <= 0x0673 or 0x0675 or 0x0676 or 0x0677 or >= 0x0688 and <= 0x0699 or 0x06C0 or >= 0x06C3 and <= 0x06CB or 0x06CD or 0x06CF or 0x06D2 or 0x06D3 or 0x06D5 or 0x06EE or 0x06EF
                or >= 0x0759 and <= 0x075B or 0x076B or 0x076C or 0x0771 or 0x0773 or 0x0774 or 0x0778 or 0x0779 or 0x08AA or 0x08AB or 0x08AC or 0x08B1 or 0x08B2 or 0x08B9)
                return JoiningType.Right;
            if (cp is >= 0x0620 and <= 0x064A or 0x066E or 0x066F or >= 0x0678 and <= 0x0687 or >= 0x069A and <= 0x06BF or 0x06C1 or 0x06C2 or 0x06CC or 0x06CE or 0x06D0 or 0x06D1 or 0x06FA or 0x06FB or 0x06FC or 0x06FF or >= 0x0750 and <= 0x077F or >= 0x08A0 and <= 0x08C7)
                return JoiningType.Dual;
        }
        return JoiningType.None;
    }

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
    public static bool IsExtendedPictographic(int cp) =>
        cp is 0xA9 or 0xAE or 0x203C or 0x2049 or 0x2122 or 0x2139 or >= 0x2194 and <= 0x21AA or 0x231A or 0x231B or 0x2328 or 0x23CF or >= 0x23E9 and <= 0x23F3 or >= 0x23F8 and <= 0x23FA
        or 0x24C2 or 0x25AA or 0x25AB or 0x25B6 or 0x25C0 or >= 0x25FB and <= 0x25FE or >= 0x2600 and <= 0x27BF or 0x2934 or 0x2935 or >= 0x2B05 and <= 0x2B07 or 0x2B1B or 0x2B1C or 0x2B50 or 0x2B55
        or 0x3030 or 0x303D or 0x3297 or 0x3299 or >= 0x1F000 and <= 0x1F0FF or >= 0x1F10D and <= 0x1F10F or 0x1F12F or >= 0x1F16C and <= 0x1F171 or 0x1F17E or 0x1F17F or 0x1F18E or >= 0x1F191 and <= 0x1F19A
        or >= 0x1F201 and <= 0x1F20F or >= 0x1F21A and <= 0x1F21A or >= 0x1F22F and <= 0x1F22F or >= 0x1F232 and <= 0x1F23A or >= 0x1F23C and <= 0x1F23F or >= 0x1F249 and <= 0x1F3FA or >= 0x1F400 and <= 0x1F53D
        or >= 0x1F546 and <= 0x1F64F or >= 0x1F680 and <= 0x1F6FF or >= 0x1F774 and <= 0x1F77F or >= 0x1F7D5 and <= 0x1F7FF or >= 0x1F80C and <= 0x1F80F or >= 0x1F848 and <= 0x1F84F or >= 0x1F85A and <= 0x1F85F
        or >= 0x1F888 and <= 0x1F88F or >= 0x1F8AE and <= 0x1F8FF or >= 0x1F90C and <= 0x1F93A or >= 0x1F93C and <= 0x1F945 or >= 0x1F947 and <= 0x1FAFF or >= 0x1FC00 and <= 0x1FFFD;

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

    internal static LB GetLineBreakClass(int cp)
    {
        switch (cp)
        {
            case 0x0A: return LB.LF; case 0x0D: return LB.CR; case 0x85: return LB.NL; case 0x0B: case 0x0C: case 0x2028: case 0x2029: return LB.BK;
            case 0x20: return LB.SP; case 0x200B: return LB.ZW; case 0x200D: return LB.ZWJ; case 0x2060: case 0xFEFF: return LB.WJ;
            case 0xA0: case 0x202F: case 0x2007: case 0x2011: case 0x0F08: return LB.GL;
            case 0x09: case 0x2010: case 0x2012: case 0x2013: case 0x00AD: case 0x058A: return LB.BA;
            case 0x2D: return LB.HY;
            case 0x2014: return LB.B2;
            case 0x28: case 0x5B: case 0x7B: case 0xA1: case 0xBF: case 0x201A: case 0x201E: case 0x3008: case 0x300A: case 0x300C: case 0x300E: case 0x3010: case 0xFF08: case 0xFF3B: case 0xFF5B: return LB.OP;
            case 0x29: case 0x5D: return LB.CP;
            case 0x7D: case 0x3001: case 0x3002: case 0x3009: case 0x300B: case 0x300D: case 0x300F: case 0x3011: case 0xFF09: case 0xFF3D: case 0xFF5D: case 0xFF0C: case 0xFF0E: return LB.CL;
            case 0x21: case 0x3F: case 0x203C: case 0x203D: case 0x2049: case 0xFF01: case 0xFF1F: return LB.EX;
            case 0x2C: case 0x2E: case 0x3A: case 0x3B: case 0x037E: case 0x060C: return LB.IS;
            case 0x2F: return LB.SY;
            case 0x22: case 0x27: case 0xAB: case 0xBB: case 0x2018: case 0x2019: case 0x201B: case 0x201C: case 0x201D: case 0x201F: case 0x2039: case 0x203A: return LB.QU;
            case 0x24: case 0x2B: case 0x5C: case 0xA3: case 0xA4: case 0xA5: case 0x20AC: case 0x2212: case 0xB1: return LB.PR;
            case 0x25: case 0xA2: case 0xB0: case 0x2030: case 0x2031: case 0x2032: case 0x2033: case 0x2103: case 0x2109: return LB.PO;
            case 0x2024: case 0x2025: case 0x2026: return LB.IN;
            case 0x30FC: case 0x3005: case 0x3041: case 0x3043: case 0x3045: case 0x3047: case 0x3049: case 0x3063: case 0x3083: case 0x3085: case 0x3087: case 0x308E: return LB.NS;
        }
        if (cp is >= 0x30 and <= 0x39 or >= 0x0660 and <= 0x0669 or >= 0x06F0 and <= 0x06F9 or >= 0x0966 and <= 0x096F) return LB.NU;
        if (cp is >= 0x2000 and <= 0x2006 or 0x2008 or 0x2009 or 0x200A or 0x205F or 0x3000 or 0x1680) return LB.BA;
        if (IsRegionalIndicator(cp)) return LB.RI;
        if (IsEmojiModifier(cp)) return LB.EM;
        if (IsExtendedPictographic(cp) && cp >= 0x1F000) return LB.ID;
        if (cp is >= 0x0590 and <= 0x05FF or >= 0xFB1D and <= 0xFB4F) return CharUnicodeInfo.GetUnicodeCategory(cp) == UnicodeCategory.NonSpacingMark ? LB.CM : LB.HL;
        var cat = CharUnicodeInfo.GetUnicodeCategory(cp);
        if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.SpacingCombiningMark) return LB.CM;
        if (cat is UnicodeCategory.Control) return LB.CM;
        if (cp is >= 0x2E80 and <= 0x9FFF or >= 0xAC00 and <= 0xD7A3 or >= 0xF900 and <= 0xFAFF or >= 0xFF00 and <= 0xFFEF or >= 0x20000 and <= 0x3FFFF or >= 0x3040 and <= 0x30FF or >= 0x1100 and <= 0x11FF)
            return LB.ID;
        return LB.AL;
    }
}
