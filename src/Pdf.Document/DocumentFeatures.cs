using System.Text;
using Pdf.Core;

namespace Pdf.Document;

public sealed record PdfOutlineItem(string Title, int? PageIndex, string? ActionUri, bool Open, IReadOnlyList<PdfOutlineItem> Children);

public sealed record PdfAnnotation(string Subtype, PdfRect Rect, string? Contents, string? Uri, int? DestPage, int Flags, PdfDictionary Dict);

public sealed record PdfFormField(string FullName, string? FieldType, string? Value, int Flags, PdfDictionary Dict);

public sealed record PdfEmbeddedFile(string Name, string? MimeType, long? Size, byte[] Data);

public static class DocumentFeatures
{
    const int MaxDepth = 64;

    /// <summary>XMP metadata packet (Catalog /Metadata), decoded as UTF-8.</summary>
    public static string? GetXmpMetadata(this PdfDocument doc)
    {
        var f = doc.File;
        return f.Resolve(f.Catalog.Get("Metadata")) is PdfStream s ? Encoding.UTF8.GetString(f.DecodeStream(s)) : null;
    }

    public static string? GetPageMode(this PdfDocument doc) => doc.File.Resolve(doc.File.Catalog.Get("PageMode")).AsName();
    public static string? GetLanguage(this PdfDocument doc) => doc.File.Resolve(doc.File.Catalog.Get("Lang")) is PdfString s ? s.Text : null;

    /// <summary>True when the catalog carries a StructTreeRoot and MarkInfo/Marked is true (tagged PDF).</summary>
    public static bool IsTagged(this PdfDocument doc)
    {
        var f = doc.File;
        return f.Resolve(f.Catalog.Get("StructTreeRoot")) is PdfDictionary
            && f.Resolve(f.Catalog.Get("MarkInfo")) is PdfDictionary mi && f.Resolve(mi.Get("Marked")) is PdfBoolean { Value: true };
    }

    public static IReadOnlyList<PdfOutlineItem> GetOutlines(this PdfDocument doc)
    {
        var f = doc.File;
        if (f.Resolve(f.Catalog.Get("Outlines")) is not PdfDictionary root) return [];
        var pageIndex = PageIndexMap(doc);
        return Siblings(doc, pageIndex, root.Get("First"), 0, new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance));
    }

    static List<PdfOutlineItem> Siblings(PdfDocument doc, Dictionary<PdfDictionary, int> pages, PdfObject? first, int depth, HashSet<PdfDictionary> seen)
    {
        var f = doc.File;
        var list = new List<PdfOutlineItem>();
        var cur = f.Resolve(first) as PdfDictionary;
        while (cur is not null && depth < MaxDepth && seen.Add(cur))
        {
            var title = f.Resolve(cur.Get("Title")) is PdfString t ? t.Text : "";
            var (page, uri) = ResolveTarget(doc, pages, cur.Get("Dest"), cur.Get("A"));
            var open = (f.Resolve(cur.Get("Count")).AsNumber() ?? 0) > 0;
            list.Add(new PdfOutlineItem(title, page, uri, open, Siblings(doc, pages, cur.Get("First"), depth + 1, seen)));
            cur = f.Resolve(cur.Get("Next")) as PdfDictionary;
        }
        return list;
    }

    static Dictionary<PdfDictionary, int> PageIndexMap(PdfDocument doc)
    {
        var m = new Dictionary<PdfDictionary, int>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < doc.Pages.Count; i++) m[doc.Pages[i].Dict] = i;
        return m;
    }

    static (int? Page, string? Uri) ResolveTarget(PdfDocument doc, Dictionary<PdfDictionary, int> pages, PdfObject? dest, PdfObject? action)
    {
        var f = doc.File;
        string? uri = null;
        if (f.Resolve(action) is PdfDictionary a)
        {
            var s = f.Resolve(a.Get("S")).AsName();
            if (s == "URI" && f.Resolve(a.Get("URI")) is PdfString us) uri = us.Text;
            else if (s == "GoTo") dest = a.Get("D");
        }
        return (DestToPage(doc, pages, dest, 0), uri);
    }

    static int? DestToPage(PdfDocument doc, Dictionary<PdfDictionary, int> pages, PdfObject? dest, int depth)
    {
        var f = doc.File;
        if (depth > 4) return null;
        var d = f.Resolve(dest);
        if (d is PdfString or PdfName)
        {
            var key = d is PdfName n ? n.Value : ((PdfString)d).Text;
            return DestToPage(doc, pages, LookupNamedDest(doc, key), depth + 1);
        }
        if (d is PdfDictionary dd) return DestToPage(doc, pages, dd.Get("D"), depth + 1);
        if (d is PdfArray arr && arr.Count > 0)
        {
            if (f.Resolve(arr[0]) is PdfDictionary pd && pages.TryGetValue(pd, out var idx)) return idx;
            if (arr[0] is PdfInteger pi && pi.Value >= 0 && pi.Value < doc.Pages.Count) return (int)pi.Value; // remote-style page index
        }
        return null;
    }

    static PdfObject? LookupNamedDest(PdfDocument doc, string key)
    {
        var f = doc.File;
        if (f.Resolve(f.Catalog.Get("Dests")) is PdfDictionary old && old.Get(key) is { } v) return v;
        if (f.Resolve(f.Catalog.Get("Names")) is PdfDictionary names && f.Resolve(names.Get("Dests")) is PdfDictionary tree)
            return SearchNameTree(f, tree, key, 0);
        return null;
    }

    static PdfObject? SearchNameTree(PdfFile f, PdfDictionary node, string key, int depth)
    {
        if (depth > MaxDepth) return null;
        if (f.Resolve(node.Get("Names")) is PdfArray pairs)
        {
            for (var i = 0; i + 1 < pairs.Count; i += 2)
                if (f.Resolve(pairs[i]) is PdfString s && s.Text == key) return pairs[i + 1];
        }
        if (f.Resolve(node.Get("Kids")) is PdfArray kids)
            foreach (var k in kids.Items)
                if (f.Resolve(k) is PdfDictionary kd && SearchNameTree(f, kd, key, depth + 1) is { } r) return r;
        return null;
    }

    public static IReadOnlyList<PdfAnnotation> GetAnnotations(this PdfPage page)
    {
        var doc = page.Document; var f = doc.File;
        if (f.Resolve(page.Dict.Get("Annots")) is not PdfArray arr) return [];
        var pages = PageIndexMap(doc);
        var list = new List<PdfAnnotation>(arr.Count);
        foreach (var o in arr.Items)
        {
            if (f.Resolve(o) is not PdfDictionary d) continue;
            var rect = default(PdfRect);
            if (f.Resolve(d.Get("Rect")) is PdfArray r && r.Count >= 4)
            {
                var v = new double[4];
                for (var i = 0; i < 4; i++) v[i] = f.Resolve(r[i]).AsNumber() ?? 0;
                rect = new PdfRect(Math.Min(v[0], v[2]), Math.Min(v[1], v[3]), Math.Max(v[0], v[2]), Math.Max(v[1], v[3]));
            }
            var (pg, uri) = ResolveTarget(doc, pages, d.Get("Dest"), d.Get("A"));
            list.Add(new PdfAnnotation(
                f.Resolve(d.Get("Subtype")).AsName() ?? "",
                rect,
                f.Resolve(d.Get("Contents")) is PdfString c ? c.Text : null,
                uri, pg, (int)(f.Resolve(d.Get("F")).AsNumber() ?? 0), d));
        }
        return list;
    }

    /// <summary>Flattened AcroForm fields with inherited FT/V/Ff and fully qualified names.</summary>
    public static IReadOnlyList<PdfFormField> GetFormFields(this PdfDocument doc)
    {
        var f = doc.File;
        if (f.Resolve(f.Catalog.Get("AcroForm")) is not PdfDictionary form || f.Resolve(form.Get("Fields")) is not PdfArray fields) return [];
        var list = new List<PdfFormField>();
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        foreach (var o in fields.Items) Field(f, o, "", null, null, 0, list, seen, 0);
        return list;
    }

    static void Field(PdfFile f, PdfObject? o, string prefix, string? ft, PdfObject? val, int ff, List<PdfFormField> acc, HashSet<PdfDictionary> seen, int depth)
    {
        if (depth > MaxDepth || f.Resolve(o) is not PdfDictionary d || !seen.Add(d)) return;
        var partial = f.Resolve(d.Get("T")) is PdfString t ? t.Text : null;
        var name = partial is null ? prefix : prefix.Length == 0 ? partial : prefix + "." + partial;
        ft = f.Resolve(d.Get("FT")).AsName() ?? ft;
        val = d.Get("V") ?? val;
        ff = d.Get("Ff") is { } fo ? (int)(f.Resolve(fo).AsNumber() ?? 0) : ff;
        if (f.Resolve(d.Get("Kids")) is PdfArray kids && kids.Items.Any(k => f.Resolve(k) is PdfDictionary kd && kd.Get("T") is not null))
        {
            foreach (var k in kids.Items) Field(f, k, name, ft, val, ff, acc, seen, depth + 1);
            return;
        }
        var rv = f.Resolve(val);
        acc.Add(new PdfFormField(name, ft, rv is PdfString s ? s.Text : rv is PdfName n ? n.Value : null, ff, d));
    }

    /// <summary>Files from the Names/EmbeddedFiles tree.</summary>
    public static IReadOnlyList<PdfEmbeddedFile> GetEmbeddedFiles(this PdfDocument doc)
    {
        var f = doc.File;
        var list = new List<PdfEmbeddedFile>();
        if (f.Resolve(f.Catalog.Get("Names")) is PdfDictionary names && f.Resolve(names.Get("EmbeddedFiles")) is PdfDictionary tree)
            Collect(f, tree, list, 0);
        return list;
    }

    static void Collect(PdfFile f, PdfDictionary node, List<PdfEmbeddedFile> acc, int depth)
    {
        if (depth > MaxDepth) return;
        if (f.Resolve(node.Get("Names")) is PdfArray pairs)
            for (var i = 0; i + 1 < pairs.Count; i += 2)
            {
                if (f.Resolve(pairs[i]) is not PdfString nm || f.Resolve(pairs[i + 1]) is not PdfDictionary spec) continue;
                if (f.Resolve(spec.Get("EF")) is not PdfDictionary ef) continue;
                var stream = f.Resolve(ef.Get("UF") ?? ef.Get("F")) as PdfStream;
                if (stream is null) continue;
                var p = f.Resolve(stream.Dict.Get("Params")) as PdfDictionary;
                acc.Add(new PdfEmbeddedFile(nm.Text, f.Resolve(stream.Dict.Get("Subtype")).AsName(),
                    p is null ? null : (long?)f.Resolve(p.Get("Size")).AsNumber(), f.DecodeStream(stream)));
            }
        if (f.Resolve(node.Get("Kids")) is PdfArray kids)
            foreach (var k in kids.Items) if (f.Resolve(k) is PdfDictionary kd) Collect(f, kd, acc, depth + 1);
    }

    /// <summary>Page label for a zero-based page index per the Catalog /PageLabels number tree.</summary>
    public static string GetPageLabel(this PdfDocument doc, int index)
    {
        var f = doc.File;
        var ranges = new SortedList<int, PdfDictionary>();
        if (f.Resolve(f.Catalog.Get("PageLabels")) is PdfDictionary tree) CollectNums(f, tree, ranges, 0);
        if (ranges.Count == 0) return (index + 1).ToString();
        var start = ranges.Keys.Where(k => k <= index).DefaultIfEmpty(-1).Max();
        if (start < 0) return (index + 1).ToString();
        var d = ranges[start];
        var style = f.Resolve(d.Get("S")).AsName();
        var prefix = f.Resolve(d.Get("P")) is PdfString ps ? ps.Text : "";
        var n = (int)(f.Resolve(d.Get("St")).AsNumber() ?? 1) + (index - start);
        return prefix + style switch
        {
            "D" => n.ToString(),
            "R" => Roman(n),
            "r" => Roman(n).ToLowerInvariant(),
            "A" => Alpha(n, 'A'),
            "a" => Alpha(n, 'a'),
            _ => "",
        };
    }

    static void CollectNums(PdfFile f, PdfDictionary node, SortedList<int, PdfDictionary> acc, int depth)
    {
        if (depth > MaxDepth) return;
        if (f.Resolve(node.Get("Nums")) is PdfArray nums)
            for (var i = 0; i + 1 < nums.Count; i += 2)
                if (f.Resolve(nums[i]).AsNumber() is { } k && f.Resolve(nums[i + 1]) is PdfDictionary v) acc[(int)k] = v;
        if (f.Resolve(node.Get("Kids")) is PdfArray kids)
            foreach (var k in kids.Items) if (f.Resolve(k) is PdfDictionary kd) CollectNums(f, kd, acc, depth + 1);
    }

    static string Roman(int n)
    {
        if (n <= 0 || n >= 4000) return n.ToString();
        ReadOnlySpan<int> v = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        string[] s = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];
        var sb = new StringBuilder();
        for (var i = 0; i < v.Length; i++) while (n >= v[i]) { sb.Append(s[i]); n -= v[i]; }
        return sb.ToString();
    }

    static string Alpha(int n, char a)
    {
        if (n <= 0) return n.ToString();
        var letter = (char)(a + (n - 1) % 26);
        return new string(letter, (n - 1) / 26 + 1);
    }
}
