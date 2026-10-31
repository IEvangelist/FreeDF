namespace PortableDocument.Content;

/// <summary>The family, weight and slant chosen for a non-embedded font.</summary>
internal readonly record struct SubstitutionClass(bool Bold, bool Italic, bool Mono, bool Serif);

/// <summary>Name and descriptor heuristics that pick a substitute for a non-embedded font (ISO 32000-2 9.6.2.2 and 9.8).</summary>
internal static class FontSubstitution
{
    /// <summary>Removes the six-letter subset tag ("ABCDEF+").</summary>
    public static string StripSubsetTag(string baseFont) =>
        baseFont.Length > 7 && baseFont[6] == '+' && IsTag(baseFont.AsSpan(0, 6)) ? baseFont[7..] : baseFont;

    static bool IsTag(ReadOnlySpan<char> s)
    {
        foreach (var c in s) if (c is < 'A' or > 'Z') return false;
        return true;
    }

    static string Key(string baseFont)
    {
        var s = StripSubsetTag(baseFont);
        Span<char> buf = stackalloc char[s.Length];
        var n = 0;
        foreach (var c in s) if (char.IsLetterOrDigit(c)) buf[n++] = char.ToLowerInvariant(c);
        return new string(buf[..n]);
    }

    static bool Has(string n, params string[] parts)
    {
        foreach (var p in parts) if (n.Contains(p, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <param name="flags">FontDescriptor /Flags.</param>
    /// <param name="italicAngle">FontDescriptor /ItalicAngle, 0 when absent.</param>
    /// <param name="fontWeight">FontDescriptor /FontWeight, 0 when absent.</param>
    /// <param name="stemV">FontDescriptor /StemV, 0 when absent.</param>
    public static SubstitutionClass Classify(string baseFont, int flags, double italicAngle = 0, double fontWeight = 0, double stemV = 0)
    {
        var n = Key(baseFont);
        var light = Has(n, "light", "thin", "hairline");
        var bold = Has(n, "bold", "black", "heavy", "demi", "extrabd") || (flags & 1 << 18) != 0 || fontWeight >= 700 || !light && fontWeight == 0 && stemV >= 150;
        var italic = Has(n, "italic", "oblique", "kursiv") || n.EndsWith("ital", StringComparison.Ordinal) || (flags & 64) != 0 || Math.Abs(italicAngle) >= 1;
        var mono = Has(n, "courier", "mono", "consolas", "typewriter", "lucidaconsole", "menlo", "monaco") || (flags & 1) != 0;
        var sans = Has(n, "sans", "arial", "helvetica", "calibri", "verdana", "tahoma", "gothic", "segoe", "univers", "frutiger", "myriad", "swiss");
        var sansWord = Has(n, "sans");
        var serifName = Has(n, "times", "georgia", "garamond", "palatino", "roman", "cambria", "bookman", "century", "minion", "baskerville", "bodoni", "caslon", "didot", "tinos") || Has(n, "serif") && !sansWord;
        var serif = serifName && (!sans || Has(n, "times")) || (flags & 2) != 0 && !sans;
        if (mono) serif = false;
        return new(bold, italic, mono, serif);
    }

    /// <summary>The standard-14 font a name stands for (including the common Arial, Times New Roman and Courier New aliases), or null.</summary>
    public static string? Std14(string baseFont, SubstitutionClass c)
    {
        var n = Key(baseFont);
        if (Has(n, "narrow", "condensed", "black", "unicode", "light", "rounded", "cond")) return null;
        if (n is "symbol" or "symbolmt") return "Symbol";
        if (n is "zapfdingbats" or "itczapfdingbats") return "ZapfDingbats";
        if (n.StartsWith("helvetica", StringComparison.Ordinal) || n.StartsWith("arial", StringComparison.Ordinal))
            return c.Bold ? c.Italic ? "Helvetica-BoldOblique" : "Helvetica-Bold" : c.Italic ? "Helvetica-Oblique" : "Helvetica";
        if (n.StartsWith("timesnewroman", StringComparison.Ordinal) || n.StartsWith("times", StringComparison.Ordinal))
            return c.Bold ? c.Italic ? "Times-BoldItalic" : "Times-Bold" : c.Italic ? "Times-Italic" : "Times-Roman";
        if (n.StartsWith("couriernew", StringComparison.Ordinal) || n.StartsWith("courier", StringComparison.Ordinal))
            return c.Bold ? c.Italic ? "Courier-BoldOblique" : "Courier-Bold" : c.Italic ? "Courier-Oblique" : "Courier";
        return null;
    }
}
