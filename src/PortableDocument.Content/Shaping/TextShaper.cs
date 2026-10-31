using System.Text;

namespace PortableDocument.Content.Shaping;

/// <summary>Options for <see cref="TextShaper"/>.</summary>
public sealed class ShapingOptions
{
    /// <summary>BCP-47-ish OpenType language system tag, e.g. "TRK" (Turkish) or "ROM"; null selects the script default (used by locl).</summary>
    public string? Language { get; init; }
    /// <summary>Applies the vertical features (vert/vrt2, vkrn/vpal) instead of horizontal kerning.</summary>
    public bool Vertical { get; init; }
    /// <summary>Pair kerning (GPOS kern, or the legacy kern table when GPOS has none).</summary>
    public bool Kerning { get; init; } = true;
    /// <summary>Feature overrides: 0 disables a default feature, any other value enables it (e.g. "smcp":1, "liga":0). For "salt" the value selects the alternate.</summary>
    public IReadOnlyDictionary<string, int>? Features { get; init; }
    /// <summary>Base direction for bidi resolution in <see cref="TextShaper.Layout"/>.</summary>
    public BidiDirection Direction { get; init; } = BidiDirection.Auto;
}

/// <summary>A positioned glyph in 1/1000 em; <see cref="TextStart"/>/<see cref="TextLength"/> is the cluster of UTF-16 units of the source text it represents.</summary>
public readonly record struct ShapedGlyph(int GlyphId, int TextStart, int TextLength, double XAdvance, double YAdvance, double XOffset, double YOffset, bool IsLigature);

/// <summary>Shaped output for one run of constant script and direction. <see cref="Glyphs"/> are in visual (left-to-right) order.</summary>
public sealed class ShapedRun
{
    public required string Text { get; init; }
    public required int Start { get; init; }
    public required int Length { get; init; }
    public required bool RightToLeft { get; init; }
    public required ScriptId Script { get; init; }
    public required ShapedGlyph[] Glyphs { get; init; }
    public double Advance { get; init; }
    public bool HasMissingGlyphs { get; init; }
    public bool Vertical { get; init; }
}

/// <summary>A run of text with one resolved direction and script, or an emoji cluster sequence.</summary>
public readonly record struct TextItem(int Start, int Length, bool RightToLeft, ScriptId Script, bool IsEmoji);

public sealed class ShapedText
{
    public required string Text { get; init; }
    public required IReadOnlyList<ShapedRun> Runs { get; init; }
    public required int ParagraphLevel { get; init; }
    public double Advance { get; init; }
}

/// <summary>
/// OpenType text shaper (GSUB 1-8, GPOS 1-9, GDEF, kern fallback) with Arabic joining, a pragmatic Devanagari (Indic) shaper,
/// bidi (UAX #9) itemization and emoji cluster detection. No native code or reflection; trim/AOT/WASM safe.
/// </summary>
public static class TextShaper
{
    const int Global = 1, Isol = 2, Init = 4, Medi = 8, Fina = 16, Rphf = 1 << 8, Half = 1 << 9, Pref = 1 << 10, Blwf = 1 << 11, Pstf = 1 << 12;

    static StageSpec S(string name, params string[] tags) => new(name, [.. tags.Select(t => (t, Global))]);
    static StageSpec P(string name, params (string, int)[] f) => new(name, f);

    static readonly StageSpec[] DefaultPos = [S("pos", "kern", "dist", "abvm", "blwm", "mark", "mkmk", "curs")];
    static readonly StageSpec[] DefaultPosNoKern = [S("pos", "dist", "abvm", "blwm", "mark", "mkmk", "curs")];
    static readonly StageSpec[] VerticalPos = [S("pos", "vkrn", "vpal", "valt", "dist", "abvm", "blwm", "mark", "mkmk", "curs")];

    static readonly StageSpec[] DefaultLtr = [S("main", "ccmp", "locl", "rvrn", "ltrm", "rclt", "calt", "rlig", "clig", "liga")];
    static readonly StageSpec[] DefaultRtl = [S("main", "ccmp", "locl", "rvrn", "rtlm", "rclt", "calt", "rlig", "clig", "liga")];
    static readonly StageSpec[] DefaultVertical = [S("main", "ccmp", "locl", "rvrn", "vert", "vrt2", "rclt", "calt", "rlig", "clig", "liga")];

    static readonly StageSpec[] Arabic =
    [
        S("pre", "ccmp", "locl", "rtlm"),
        P("join", ("isol", Isol), ("fina", Fina), ("fin2", Fina), ("fin3", Fina), ("medi", Medi), ("med2", Medi), ("init", Init)),
        S("rlig", "rlig"),
        S("main", "calt", "rclt", "liga", "clig", "mset"),
    ];

    static readonly StageSpec[] Indic =
    [
        S("pre", "ccmp", "locl"), S("nukt", "nukt"), S("akhn", "akhn"), P("rphf", ("rphf", Rphf)), S("rkrf", "rkrf"), P("pref", ("pref", Pref)),
        P("blwf", ("blwf", Blwf)), S("abvf", "abvf"), P("half", ("half", Half)), P("pstf", ("pstf", Pstf)), S("vatu", "vatu"), S("cjct", "cjct"),
        S("post", "pres", "abvs", "blws", "psts", "haln", "calt", "clig", "liga", "rclt"),
    ];

    [ThreadStatic] static GlyphBuffer? _pool;

    static bool IsDefaultIgnorable(int cp) =>
        cp is 0x00AD or 0x034F or 0x061C or >= 0x115F and <= 0x1160 or 0x17B4 or 0x17B5 or >= 0x180B and <= 0x180F or >= 0x200B and <= 0x200F or >= 0x202A and <= 0x202E
        or >= 0x2060 and <= 0x206F or 0x3164 or >= 0xFE00 and <= 0xFE0F or 0xFEFF or 0xFFA0 or >= 0xFFF0 and <= 0xFFF8 or >= 0xE0000 and <= 0xE0FFF;

    /// <summary>Shapes a single run. The caller guarantees constant script and direction; use <see cref="Layout"/> for arbitrary text.</summary>
    public static ShapedRun Shape(TrueTypeFont font, string text, int start, int length, bool rightToLeft, ScriptId script, ShapingOptions? options = null)
    {
        options ??= new ShapingOptions();
        var layout = font.Layout;
        var buf = _pool ??= new GlyphBuffer();
        buf.Len = 0;
        var end = start + length;
        var missing = false;
        var cps = new List<(int Cp, int S, int E)>(length);
        for (var i = start; i < end;)
        {
            var cp = char.IsHighSurrogate(text[i]) && i + 1 < end && char.IsLowSurrogate(text[i + 1]) ? char.ConvertToUtf32(text[i], text[i + 1]) : text[i];
            var w = cp > 0xFFFF ? 2 : 1;
            cps.Add((cp, i, i + w));
            i += w;
        }

        var arabic = script == ScriptId.Arabic;
        var indic = script == ScriptId.Devanagari;
        var joinMasks = arabic ? ArabicMasks(cps) : null;

        for (var k = 0; k < cps.Count; k++)
        {
            var (cp, s, e) = cps[k];
            var lookup = rightToLeft ? UnicodeData.Mirrored(cp) : cp;
            var gid = font.LookupAny(lookup);
            if (gid == 0 && lookup != cp) gid = font.LookupAny(cp);
            if (gid == 0 && IsDefaultIgnorable(cp))
            {
                if (buf.Len > 0) buf.Info[buf.Len - 1].End = Math.Max(buf.Info[buf.Len - 1].End, e);
                continue;
            }
            if (gid == 0) missing = true;
            var gi = new GlyphInfo { Gid = gid, Start = s, End = e, Cp = cp, Mask = Global | (joinMasks?[k] ?? 0), Class = layout.HasGlyphClasses || gid == 0 ? layout.GlyphClass(gid) : SynthClass(cp) };
            buf.Add(gi);
        }
        // A glyph-less default ignorable at the very start has no predecessor; fold its cluster into the next glyph.
        if (buf.Len > 0 && cps.Count > 0 && buf.Info[0].Start > start) buf.Info[0].Start = start;

        if (!arabic && !indic && script != ScriptId.Hebrew) ComposeMarks(font, buf);

        // Unicode marks without GDEF classes (fonts lacking GDEF) still behave as marks.
        if (!layout.HasGsub && !layout.HasGpos && !layout.HasKernTable)
        {
            for (var i = 0; i < buf.Len; i++) buf.Pos[i].XAdv = font.AdvanceUnits(buf.Info[i].Gid);
            return Finish(font, buf, text, start, length, rightToLeft, script, missing, options.Vertical);
        }

        var user = options.Features;
        var userKey = user is null ? "" : string.Join(',', user.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value));
        var kind = arabic ? "ar" : indic ? "in" : options.Vertical ? "v" : rightToLeft ? "r" : "l";
        var key = $"{kind}|{string.Join('/', UnicodeData.OpenTypeScriptTags(script))}|{options.Language}|{options.Kerning}|{userKey}";
        var stages = GsubStages(kind, user);
        var plan = layout.GetPlan(key, UnicodeData.OpenTypeScriptTags(script), options.Language, stages, PosStages(options, user), user);
        var alt = user is not null && user.TryGetValue("salt", out var sv) && sv > 0 ? sv - 1 : 0;
        var engine = new LookupEngine(layout, buf, rightToLeft, alt);

        if (indic) IndicShape(font, layout, buf, plan, engine);
        else
        {
            foreach (var st in stages) if (plan.Gsub.TryGetValue(st.Name, out var l)) engine.RunStage(false, l);
        }
        var spaceGid = font.LookupAny(0x20);
        for (var i = 0; i < buf.Len; i++)
        {
            buf.Pos[i].XAdv = font.AdvanceUnits(buf.Info[i].Gid);
            if (IsDefaultIgnorable(buf.Info[i].Cp) && buf.Info[i].Class == 0 && buf.Info[i].Flags == 0)
            {
                if (spaceGid > 0) { buf.Info[i].Gid = spaceGid; buf.Info[i].Class = layout.GlyphClass(spaceGid); }
                buf.Pos[i].XAdv = 0;
            }
        }
        if (plan.Gpos.TryGetValue("pos", out var gpos)) engine.RunStage(true, gpos);
        if (options.Kerning && !(user is not null && user.TryGetValue("kern", out var kv0) && kv0 == 0) && !plan.HasGposKern && !options.Vertical && layout.HasKernTable)
        {
            for (var i = 0; i + 1 < buf.Len; i++)
                if (buf.Info[i].Class != 3 && buf.Info[i + 1].Class != 3 && layout.TryKernPair(buf.Info[i].Gid, buf.Info[i + 1].Gid, out var kv)) buf.Pos[i].XAdv += kv;
        }
        engine.FinishPositions();
        return Finish(font, buf, text, start, length, rightToLeft, script, missing, options.Vertical);
    }

    // Fonts without GDEF glyph classes: marks are recognised from the Unicode category, as HarfBuzz does.
    static byte SynthClass(int cp) => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(cp) is System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.EnclosingMark ? (byte)3 : (byte)1;

    static StageSpec[] GsubStages(string kind, IReadOnlyDictionary<string, int>? user)
    {
        var baseStages = kind switch { "ar" => Arabic, "in" => Indic, "v" => DefaultVertical, "r" => DefaultRtl, _ => DefaultLtr };
        if (user is null) return baseStages;
        var extra = user.Where(kv => kv.Value != 0).Select(kv => (kv.Key, Global)).ToArray();
        if (extra.Length == 0) return baseStages;
        var copy = (StageSpec[])baseStages.Clone();
        var mainIdx = Array.FindIndex(copy, s => s.Name is "main" or "post");
        copy[mainIdx] = copy[mainIdx] with { Features = [.. copy[mainIdx].Features, .. extra] };
        return copy;
    }

    static StageSpec[] PosStages(ShapingOptions o, IReadOnlyDictionary<string, int>? user)
    {
        var stages = o.Vertical ? VerticalPos : o.Kerning ? DefaultPos : DefaultPosNoKern;
        if (user is null) return stages;
        var extra = user.Where(kv => kv.Value != 0).Select(kv => (kv.Key, Global)).ToArray();
        return extra.Length == 0 ? stages : [stages[0] with { Features = [.. stages[0].Features, .. extra] }];
    }

    static ShapedRun Finish(TrueTypeFont font, GlyphBuffer buf, string text, int start, int length, bool rtl, ScriptId script, bool missing, bool vertical)
    {
        if (rtl) buf.Reverse();
        var scale = 1000.0 / font.UnitsPerEm;
        var glyphs = new ShapedGlyph[buf.Len];
        double total = 0;
        for (var i = 0; i < buf.Len; i++)
        {
            ref var g = ref buf.Info[i]; ref var p = ref buf.Pos[i];
            glyphs[i] = new ShapedGlyph(g.Gid, g.Start, Math.Max(g.End - g.Start, 0), p.XAdv * scale, p.YAdv * scale, p.XOff * scale, p.YOff * scale, (g.Flags & GlyphInfo.FlagLigature) != 0);
            total += p.XAdv * scale;
        }
        return new ShapedRun { Text = text, Start = start, Length = length, RightToLeft = rtl, Script = script, Glyphs = glyphs, Advance = total, HasMissingGlyphs = missing, Vertical = vertical };
    }

    // Canonical composition (base + mark -> precomposed) when the font has the composed glyph; canonical decomposition when it lacks the precomposed one.
    static void ComposeMarks(TrueTypeFont font, GlyphBuffer buf)
    {
        for (var i = 0; i < buf.Len; i++)
        {
            ref var g = ref buf.Info[i];
            if (g.Gid == 0 && CanonicalComposition.TryDecompose(g.Cp, out var b, out var m))
            {
                var gb = font.LookupAny(b); var gm = font.LookupAny(m);
                if (gb > 0 && gm > 0)
                {
                    buf.Replace(i, 1, [gb, gm], font.Layout);
                    buf.Info[i].Cp = b; buf.Info[i + 1].Cp = m;
                    i++;
                }
                continue;
            }
            if (i > 0 && g.Cp is >= 0x0300 and <= 0x036F && CanonicalComposition.TryCompose(buf.Info[i - 1].Cp, g.Cp, out var composed))
            {
                var gc = font.LookupAny(composed);
                if (gc <= 0) continue;
                ref var prev = ref buf.Info[i - 1];
                prev.Gid = gc; prev.Cp = composed; prev.Class = font.Layout.GlyphClass(gc); prev.End = Math.Max(prev.End, g.End);
                buf.RemoveAt(i);
                i--;
            }
        }
    }

    // ---------- Arabic ----------
    static int[] ArabicMasks(List<(int Cp, int S, int E)> cps)
    {
        var n = cps.Count;
        var jt = new JoiningType[n];
        for (var i = 0; i < n; i++) jt[i] = UnicodeData.GetJoiningType(cps[i].Cp);
        var masks = new int[n];
        var prev = -1;
        for (var i = 0; i < n; i++)
        {
            if (jt[i] == JoiningType.Transparent) continue;
            var next = -1;
            for (var j = i + 1; j < n; j++) if (jt[j] != JoiningType.Transparent) { next = j; break; }
            var joinsPrev = prev >= 0 && jt[prev] is JoiningType.Dual or JoiningType.Causing && jt[i] is JoiningType.Dual or JoiningType.Right;
            var joinsNext = jt[i] == JoiningType.Dual && next >= 0 && jt[next] is JoiningType.Dual or JoiningType.Right or JoiningType.Causing;
            if (jt[i] is JoiningType.Dual)
                masks[i] = joinsPrev && joinsNext ? Medi : joinsPrev ? Fina : joinsNext ? Init : Isol;
            else if (jt[i] == JoiningType.Right) masks[i] = joinsPrev ? Fina : Isol;
            prev = i;
        }
        return masks;
    }

    // ---------- Devanagari ----------
    static UnicodeData.IndicCat Cat(GlyphBuffer b, int i) => UnicodeData.GetIndicCat(b.Info[i].Cp);

    static void IndicPrepare(GlyphBuffer b, List<(int Syllable, int Gid)> rephCandidates)
    {
        var n = b.Len; var i = 0; var syl = 1;
        while (i < n)
        {
            var cat = Cat(b, i);
            var s = i; int e; var baseIdx = -1;
            if (cat is UnicodeData.IndicCat.Consonant or UnicodeData.IndicCat.Ra)
            {
                e = i + 1;
                if (e < n && Cat(b, e) == UnicodeData.IndicCat.Nukta) e++;
                var last = s;
                while (e < n && Cat(b, e) == UnicodeData.IndicCat.Virama)
                {
                    var next = e + 1;
                    if (next < n && Cat(b, next) is UnicodeData.IndicCat.Zwj or UnicodeData.IndicCat.Zwnj) next++;
                    if (next < n && Cat(b, next) is UnicodeData.IndicCat.Consonant or UnicodeData.IndicCat.Ra)
                    {
                        last = next; e = next + 1;
                        if (e < n && Cat(b, e) == UnicodeData.IndicCat.Nukta) e++;
                    }
                    else { e = next; break; }
                }
                while (e < n && Cat(b, e) is UnicodeData.IndicCat.Matra or UnicodeData.IndicCat.PreMatra or UnicodeData.IndicCat.Modifier or UnicodeData.IndicCat.Nukta) e++;
                baseIdx = last;
                if (Cat(b, last) == UnicodeData.IndicCat.Ra && last > s)
                {
                    var p = last - 1;
                    if (Cat(b, p) is UnicodeData.IndicCat.Zwj or UnicodeData.IndicCat.Zwnj) p--;
                    if (p >= s && Cat(b, p) == UnicodeData.IndicCat.Virama)
                    {
                        for (var q = p - 1; q >= s; q--)
                            if (Cat(b, q) is UnicodeData.IndicCat.Consonant or UnicodeData.IndicCat.Ra) { baseIdx = q; break; }
                    }
                }
            }
            else if (cat == UnicodeData.IndicCat.Vowel)
            {
                e = i + 1;
                while (e < n && Cat(b, e) is UnicodeData.IndicCat.Modifier or UnicodeData.IndicCat.Matra) e++;
            }
            else e = i + 1;

            for (var k = s; k < e; k++)
            {
                b.Info[k].Syllable = syl;
                var c = Cat(b, k);
                if (c == UnicodeData.IndicCat.PreMatra) b.Info[k].Flags |= GlyphInfo.FlagPreMatra;
                else if (c == UnicodeData.IndicCat.Virama) b.Info[k].Flags |= GlyphInfo.FlagHalant;
                else if (c == UnicodeData.IndicCat.Nukta) b.Info[k].Flags |= GlyphInfo.FlagNukta;
                if (baseIdx >= 0)
                {
                    if (k < baseIdx) b.Info[k].Mask |= Half;
                    else if (k > baseIdx) b.Info[k].Mask |= Blwf | Pstf | Pref;
                }
            }
            if (baseIdx >= 0)
            {
                b.Info[baseIdx].Flags |= GlyphInfo.FlagBase;
                if (Cat(b, s) == UnicodeData.IndicCat.Ra && baseIdx > s + 1 && s + 2 < e && Cat(b, s + 1) == UnicodeData.IndicCat.Virama
                    && Cat(b, s + 2) is not (UnicodeData.IndicCat.Zwj or UnicodeData.IndicCat.Zwnj))
                {
                    b.Info[s].Mask |= Rphf; b.Info[s + 1].Mask |= Rphf;
                    rephCandidates.Add((syl, b.Info[s].Gid));
                }
            }
            syl++;
            i = e;
        }
    }

    // Reordering merges the clusters of every glyph it moves across (HarfBuzz does the same), so each reordered cluster keeps one contiguous text range.
    static void MergeClusters(GlyphBuffer b, int from, int to)
    {
        int s = int.MaxValue, e = 0;
        for (var k = from; k <= to; k++) { s = Math.Min(s, b.Info[k].Start); e = Math.Max(e, b.Info[k].End); }
        for (var k = from; k <= to; k++) { b.Info[k].Start = s; b.Info[k].End = e; }
    }

    static void IndicShape(TrueTypeFont font, OpenTypeLayout layout, GlyphBuffer buf, ShapePlan plan, LookupEngine engine)
    {
        var rephs = new List<(int Syllable, int Gid)>();
        IndicPrepare(buf, rephs);
        void Run(string name) { if (plan.Gsub.TryGetValue(name, out var l)) engine.RunStage(false, l); }
        Run("pre"); Run("nukt"); Run("akhn");
        Run("rphf");
        foreach (var (syl, gid) in rephs)
            for (var i = 0; i < buf.Len; i++)
                if (buf.Info[i].Syllable == syl && (buf.Info[i].Mask & Rphf) != 0)
                {
                    if (buf.Info[i].Gid != gid || (buf.Info[i].Flags & GlyphInfo.FlagLigature) != 0) buf.Info[i].Flags |= GlyphInfo.FlagReph;
                    break;
                }
        Run("rkrf"); Run("pref"); Run("blwf"); Run("abvf"); Run("half"); Run("pstf"); Run("vatu"); Run("cjct");
        for (var i = 0; i < buf.Len; i++)
        {
            if ((buf.Info[i].Flags & GlyphInfo.FlagPreMatra) == 0) continue;
            var s = i;
            while (s > 0 && buf.Info[s - 1].Syllable == buf.Info[i].Syllable) s--;
            MergeClusters(buf, s, i);
            buf.Move(i, s);
        }
        Run("post");
        for (var i = 0; i < buf.Len; i++)
        {
            if ((buf.Info[i].Flags & GlyphInfo.FlagReph) == 0) continue;
            var syl = buf.Info[i].Syllable;
            var b = -1;
            for (var k = i + 1; k < buf.Len && buf.Info[k].Syllable == syl; k++) if ((buf.Info[k].Flags & GlyphInfo.FlagBase) != 0) { b = k; break; }
            if (b < 0) continue;
            var target = b;
            while (target + 1 < buf.Len && buf.Info[target + 1].Syllable == syl && (buf.Info[target + 1].Flags & (GlyphInfo.FlagNukta | GlyphInfo.FlagHalant)) != 0) target++;
            MergeClusters(buf, i, target);
            buf.Move(i, target);
            i--;
        }
    }

    // ---------- itemization and layout ----------
    /// <summary>Splits text into runs of constant resolved direction and script (visual order), isolating emoji clusters.</summary>
    public static List<TextItem> Itemize(string text, BidiDirection direction, out int paragraphLevel)
    {
        var items = new List<TextItem>();
        var bidi = Bidi.Resolve(text, direction);
        paragraphLevel = bidi.ParagraphLevel;
        foreach (var run in bidi.VisualRuns())
        {
            var local = ItemizeRun(text, run.Start, run.Length, run.RightToLeft);
            if (run.RightToLeft) local.Reverse();
            items.AddRange(local);
        }
        return items;
    }

    /// <summary>Splits one same-direction range into script runs and emoji clusters, in logical order.</summary>
    public static List<TextItem> ItemizeRun(string text, int start, int length, bool rightToLeft)
    {
        var local = new List<TextItem>();
        var end = start + length;
        var scripts = ResolveScripts(text, start, end);
        for (var i = start; i < end;)
        {
            var emoji = UnicodeData.EmojiClusterLength(text, i, end);
            if (emoji > 0)
            {
                var j = i + emoji;
                while (j < end)
                {
                    var more = UnicodeData.EmojiClusterLength(text, j, end);
                    if (more == 0) break;
                    j += more;
                }
                local.Add(new TextItem(i, j - i, rightToLeft, ScriptId.Common, true));
                i = j;
                continue;
            }
            var sc = scripts[i - start]; var k = i + 1;
            while (k < end && scripts[k - start] == sc && UnicodeData.EmojiClusterLength(text, k, end) == 0) k++;
            local.Add(new TextItem(i, k - i, rightToLeft, sc, false));
            i = k;
        }
        return local;
    }
    // Script of every UTF-16 unit in [from, to): Common/Inherited characters take the preceding (else following) real script.
    static ScriptId[] ResolveScripts(string text, int from, int to)
    {
        var res = new ScriptId[to - from];
        for (var i = from; i < to;)
        {
            var cp = char.IsHighSurrogate(text[i]) && i + 1 < to && char.IsLowSurrogate(text[i + 1]) ? char.ConvertToUtf32(text[i], text[i + 1]) : text[i];
            var sc = UnicodeData.GetScript(cp);
            res[i - from] = sc;
            if (cp > 0xFFFF) res[i + 1 - from] = sc;
            i += cp > 0xFFFF ? 2 : 1;
        }
        var cur = ScriptId.Common;
        for (var i = 0; i < res.Length; i++)
        {
            if (res[i] is ScriptId.Common or ScriptId.Inherited) { if (cur != ScriptId.Common) res[i] = cur; }
            else cur = res[i];
        }
        cur = ScriptId.Common;
        for (var i = res.Length - 1; i >= 0; i--)
        {
            if (res[i] is ScriptId.Common or ScriptId.Inherited) { if (cur != ScriptId.Common) res[i] = cur; }
            else cur = res[i];
        }
        for (var i = 0; i < res.Length; i++) if (res[i] is ScriptId.Common or ScriptId.Inherited) res[i] = ScriptId.Latin;
        return res;
    }
    /// <summary>Bidi + script itemization + shaping of arbitrary text with a single font (no fallback). Runs are returned in visual order.</summary>
    public static ShapedText Layout(TrueTypeFont font, string text, ShapingOptions? options = null)
    {
        options ??= new ShapingOptions();
        var runs = new List<ShapedRun>();
        double adv = 0;
        var items = Itemize(text, options.Direction, out var level);
        foreach (var it in items)
        {
            var run = Shape(font, text, it.Start, it.Length, it.RightToLeft, it.Script, options);
            runs.Add(run); adv += run.Advance;
        }
        return new ShapedText { Text = text, Runs = runs, ParagraphLevel = level, Advance = adv };
    }
}
