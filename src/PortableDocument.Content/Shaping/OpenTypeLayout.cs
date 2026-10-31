namespace PortableDocument.Content.Shaping;

/// <summary>Bounds-safe big-endian readers: malformed offsets read as zero instead of throwing.</summary>
internal static class Ot
{
    public static int U16(byte[] d, int o) => o >= 0 && o + 2 <= d.Length ? d[o] << 8 | d[o + 1] : 0;
    public static int S16(byte[] d, int o) => (short)U16(d, o);
    public static long U32(byte[] d, int o) => o >= 0 && o + 4 <= d.Length ? (long)d[o] << 24 | (long)d[o + 1] << 16 | (long)d[o + 2] << 8 | d[o + 3] : 0;

    /// <summary>Coverage table lookup: the coverage index of <paramref name="gid"/>, or -1.</summary>
    public static int Coverage(byte[] d, int off, int gid)
    {
        if (off <= 0) return -1;
        var fmt = U16(d, off);
        var n = U16(d, off + 2);
        if (fmt == 1)
        {
            int lo = 0, hi = n - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                var g = U16(d, off + 4 + mid * 2);
                if (g == gid) return mid;
                if (g < gid) lo = mid + 1; else hi = mid - 1;
            }
        }
        else if (fmt == 2)
        {
            int lo = 0, hi = n - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                var r = off + 4 + mid * 6;
                if (gid < U16(d, r)) hi = mid - 1;
                else if (gid > U16(d, r + 2)) lo = mid + 1;
                else return U16(d, r + 4) + gid - U16(d, r);
            }
        }
        return -1;
    }

    /// <summary>ClassDef lookup; glyphs not listed are class 0.</summary>
    public static int Class(byte[] d, int off, int gid)
    {
        if (off <= 0) return 0;
        var fmt = U16(d, off);
        if (fmt == 1)
        {
            var start = U16(d, off + 2);
            var n = U16(d, off + 4);
            var i = gid - start;
            return i >= 0 && i < n ? U16(d, off + 6 + i * 2) : 0;
        }
        if (fmt == 2)
        {
            var n = U16(d, off + 2);
            int lo = 0, hi = n - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                var r = off + 4 + mid * 6;
                if (gid < U16(d, r)) hi = mid - 1;
                else if (gid > U16(d, r + 2)) lo = mid + 1;
                else return U16(d, r + 4);
            }
        }
        return 0;
    }
}

/// <summary>One glyph while a run is being shaped. Positions are font design units.</summary>
internal struct GlyphInfo
{
    public int Gid, Start, End, Mask, Cp, Syllable;
    public byte Class;
    public int LigId, LigComp, LigNum;
    public int Flags;
    public const int FlagBase = 1, FlagPreMatra = 2, FlagReph = 4, FlagLigature = 8, FlagHalant = 16, FlagNukta = 32, FlagZw = 64;
}

internal struct GlyphPos
{
    public int XAdv, YAdv, XOff, YOff;
    public int AttachChain;
    public byte AttachType;
    public const byte AttachMark = 1, AttachCursive = 2;
}

internal sealed class GlyphBuffer
{
    public GlyphInfo[] Info = new GlyphInfo[16];
    public GlyphPos[] Pos = new GlyphPos[16];
    public int Len;
    int _nextLig = 1;
    public int NewLigId() => _nextLig++;

    void Grow(int need)
    {
        if (need <= Info.Length) return;
        var n = Math.Max(need, Info.Length * 2);
        Array.Resize(ref Info, n);
        Array.Resize(ref Pos, n);
    }

    public void Add(in GlyphInfo g)
    {
        Grow(Len + 1);
        Info[Len] = g;
        Pos[Len] = default;
        Len++;
    }

    /// <summary>Replaces <paramref name="remove"/> glyphs at <paramref name="index"/> with <paramref name="gids"/>; the new glyphs inherit mask and cluster span of the replaced range.</summary>
    public void Replace(int index, int remove, ReadOnlySpan<int> gids, OpenTypeLayout layout)
    {
        var first = Info[index];
        var start = first.Start; var end = first.End; var flags = first.Flags;
        for (var i = index; i < index + remove; i++) { start = Math.Min(start, Info[i].Start); end = Math.Max(end, Info[i].End); flags |= Info[i].Flags; }
        var delta = gids.Length - remove;
        Grow(Len + Math.Max(delta, 0));
        if (delta != 0)
        {
            Array.Copy(Info, index + remove, Info, index + gids.Length, Len - index - remove);
            Array.Copy(Pos, index + remove, Pos, index + gids.Length, Len - index - remove);
            Len += delta;
        }
        for (var k = 0; k < gids.Length; k++)
        {
            var g = first;
            g.Gid = gids[k]; g.Start = start; g.End = end; g.Flags = flags; g.Class = layout.ClassFor(gids[k], first.Class);
            Info[index + k] = g;
            Pos[index + k] = default;
        }
    }

    public void RemoveAt(int index)
    {
        Array.Copy(Info, index + 1, Info, index, Len - index - 1);
        Array.Copy(Pos, index + 1, Pos, index, Len - index - 1);
        Len--;
    }

    public void Move(int from, int to)
    {
        if (from == to) return;
        var gi = Info[from]; var gp = Pos[from];
        if (from < to) { Array.Copy(Info, from + 1, Info, from, to - from); Array.Copy(Pos, from + 1, Pos, from, to - from); }
        else { Array.Copy(Info, to, Info, to + 1, from - to); Array.Copy(Pos, to, Pos, to + 1, from - to); }
        Info[to] = gi; Pos[to] = gp;
    }

    public void Reverse()
    {
        Array.Reverse(Info, 0, Len);
        Array.Reverse(Pos, 0, Len);
    }
}

/// <summary>A lookup with the glyph mask it is enabled for; lookups run in lookup-list order within a stage.</summary>
internal readonly record struct PlanLookup(int Index, int Mask);

/// <summary>Resolved script/language feature selection for one font: ordered lookups per named stage.</summary>
internal sealed class ShapePlan
{
    public readonly Dictionary<string, PlanLookup[]> Gsub = [];
    public readonly Dictionary<string, PlanLookup[]> Gpos = [];
    public bool HasGposKern;
    public readonly HashSet<string> Features = [];
}

internal sealed record StageSpec(string Name, (string Tag, int Mask)[] Features);

/// <summary>
/// OpenType Layout (GDEF/GSUB/GPOS) reader.
/// Coverage and class tables are binary-searched in place in the font bytes (no per-glyph allocation);
/// only script/feature to lookup selections are cached. The lookup engine lives in <see cref="LookupEngine"/>.
/// </summary>
public sealed class OpenTypeLayout
{
    internal readonly byte[] D;
    readonly int _gsub, _gpos, _gdef, _kern;
    readonly int _gsubLookups, _gposLookups, _glyphClassDef, _markAttachClassDef, _markGlyphSets;
    readonly Dictionary<string, ShapePlan> _plans = [];
    readonly object _gate = new();

    internal OpenTypeLayout(byte[] data, Func<string, (int Off, int Len)?> table, int unitsPerEm)
    {
        D = data; UnitsPerEm = unitsPerEm;
        _gsub = table("GSUB")?.Off ?? -1; _gpos = table("GPOS")?.Off ?? -1; _gdef = table("GDEF")?.Off ?? -1; _kern = table("kern")?.Off ?? -1;
        if (_gsub >= 0) _gsubLookups = _gsub + Ot.U16(D, _gsub + 8);
        if (_gpos >= 0) _gposLookups = _gpos + Ot.U16(D, _gpos + 8);
        if (_gdef >= 0)
        {
            var o = Ot.U16(D, _gdef + 4); if (o != 0) _glyphClassDef = _gdef + o;
            o = Ot.U16(D, _gdef + 10); if (o != 0) _markAttachClassDef = _gdef + o;
            if (Ot.U16(D, _gdef + 2) >= 2) { o = Ot.U16(D, _gdef + 12); if (o != 0) _markGlyphSets = _gdef + o; }
        }
    }

    public bool HasGsub => _gsub >= 0;
    public bool HasGpos => _gpos >= 0;
    public bool HasKernTable => _kern >= 0;
    public int UnitsPerEm { get; }

    internal bool HasGlyphClasses => _glyphClassDef > 0;

    /// <summary>The class of a substituted glyph: from GDEF when the font has glyph classes, otherwise the class the glyph already had (Unicode-derived).</summary>
    internal byte ClassFor(int gid, byte existing) => _glyphClassDef > 0 ? GlyphClass(gid) : existing;

    internal byte GlyphClass(int gid) => (byte)Ot.Class(D, _glyphClassDef, gid);

    /// <summary>The feature tags available for a script (and language) in GSUB and GPOS; empty when the font has no matching script.</summary>
    public IReadOnlyCollection<string> FeaturesFor(string[] scriptTags, string? language = null)
    {
        var set = new HashSet<string>();
        foreach (var off in new[] { _gsub, _gpos })
            foreach (var f in Features(off, scriptTags, language)) set.Add(f.Tag);
        return set;
    }

    internal readonly record struct Feat(string Tag, int FeatureOffset);

    List<Feat> Features(int table, string[] scriptTags, string? language)
    {
        var result = new List<Feat>();
        if (table < 0) return result;
        var scriptList = table + Ot.U16(D, table + 4);
        var featureList = table + Ot.U16(D, table + 6);
        var langSys = FindLangSys(scriptList, scriptTags, language);
        if (langSys < 0) return result;
        var required = Ot.U16(D, langSys + 2);
        var count = Ot.U16(D, langSys + 4);
        var featureCount = Ot.U16(D, featureList);
        void Add(int idx)
        {
            if (idx >= featureCount) return;
            var r = featureList + 2 + idx * 6;
            result.Add(new Feat(Tag(r), featureList + Ot.U16(D, r + 4)));
        }
        if (required != 0xFFFF) Add(required);
        for (var i = 0; i < count; i++) Add(Ot.U16(D, langSys + 6 + i * 2));
        return result;
    }

    string Tag(int o)
    {
        if (o < 0 || o + 4 > D.Length) return "";
        return string.Create(4, (D, o), static (s, st) => { for (var i = 0; i < 4; i++) s[i] = (char)st.D[st.o + i]; });
    }

    int FindLangSys(int scriptList, string[] scriptTags, string? language)
    {
        var count = Ot.U16(D, scriptList);
        var script = -1;
        foreach (var tag in scriptTags)
        {
            for (var i = 0; i < count; i++)
            {
                var r = scriptList + 2 + i * 6;
                if (Tag(r) == tag) { script = scriptList + Ot.U16(D, r + 4); break; }
            }
            if (script >= 0) break;
        }
        if (script < 0) return -1;
        if (language is { Length: > 0 })
        {
            var tag = language.PadRight(4)[..4];
            var n = Ot.U16(D, script + 2);
            for (var i = 0; i < n; i++)
            {
                var r = script + 4 + i * 6;
                if (Tag(r) == tag) return script + Ot.U16(D, r + 4);
            }
        }
        var def = Ot.U16(D, script);
        return def == 0 ? -1 : script + def;
    }

    /// <summary>Builds (and caches) the lookup plan for a script/language/stage definition.</summary>
    internal ShapePlan GetPlan(string key, string[] scriptTags, string? language, IReadOnlyList<StageSpec> stages, IReadOnlyList<StageSpec> posStages, IReadOnlyDictionary<string, int>? user)
    {
        lock (_gate)
        {
            if (_plans.TryGetValue(key, out var p)) return p;
            p = new ShapePlan();
            Fill(p.Gsub, _gsub, scriptTags, language, stages, user, p, false);
            Fill(p.Gpos, _gpos, scriptTags, language, posStages, user, p, true);
            return _plans[key] = p;
        }
    }

    void Fill(Dictionary<string, PlanLookup[]> into, int table, string[] scriptTags, string? language, IReadOnlyList<StageSpec> stages, IReadOnlyDictionary<string, int>? user, ShapePlan plan, bool gpos)
    {
        var feats = Features(table, scriptTags, language);
        foreach (var f in feats) plan.Features.Add(f.Tag);
        foreach (var st in stages)
        {
            var masks = new SortedDictionary<int, int>();
            foreach (var (tag, mask) in st.Features)
            {
                if (user is not null && user.TryGetValue(tag, out var v) && v == 0) continue;
                foreach (var f in feats)
                {
                    if (f.Tag != tag) continue;
                    var n = Ot.U16(D, f.FeatureOffset + 2);
                    for (var i = 0; i < n; i++)
                    {
                        var li = Ot.U16(D, f.FeatureOffset + 4 + i * 2);
                        masks[li] = masks.GetValueOrDefault(li) | mask;
                    }
                    if (gpos && tag == "kern") plan.HasGposKern = true;
                }
            }
            into[st.Name] = [.. masks.Select(kv => new PlanLookup(kv.Key, kv.Value))];
        }
    }

    internal int LookupListOffset(bool gpos) => gpos ? _gposLookups : _gsubLookups;

    internal int LookupOffset(bool gpos, int index)
    {
        var list = LookupListOffset(gpos);
        if (list <= 0 || index < 0 || index >= Ot.U16(D, list)) return -1;
        return list + Ot.U16(D, list + 2 + index * 2);
    }

    internal bool InMarkSet(int set, int gid) =>
        _markGlyphSets > 0 && set < Ot.U16(D, _markGlyphSets + 2) && Ot.Coverage(D, _markGlyphSets + (int)Ot.U32(D, _markGlyphSets + 4 + set * 4), gid) >= 0;

    internal int MarkAttachClass(int gid) => Ot.Class(D, _markAttachClassDef, gid);

    /// <summary>Legacy kern table (format 0) pair lookup; used only when GPOS has no kern feature.</summary>
    internal bool TryKernPair(int left, int right, out int value)
    {
        value = 0;
        if (_kern < 0) return false;
        var tables = Ot.U16(D, _kern + 2);
        var o = _kern + 4;
        var found = false;
        for (var t = 0; t < tables; t++)
        {
            var len = Ot.U16(D, o + 2);
            var coverage = Ot.U16(D, o + 4);
            if ((coverage >> 8) == 0 && (coverage & 1) != 0 && (coverage & 4) == 0)
            {
                var n = Ot.U16(D, o + 6);
                var key = (long)left << 16 | (uint)right;
                int lo = 0, hi = n - 1;
                while (lo <= hi)
                {
                    var mid = (lo + hi) >> 1;
                    var r = o + 14 + mid * 6;
                    var k = (long)Ot.U16(D, r) << 16 | (uint)Ot.U16(D, r + 2);
                    if (k == key) { value += Ot.S16(D, r + 4); found = true; break; }
                    if (k < key) lo = mid + 1; else hi = mid - 1;
                }
            }
            o += Math.Max(len, 6);
        }
        return found;
    }
}
