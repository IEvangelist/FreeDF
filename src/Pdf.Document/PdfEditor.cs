using Pdf.Core;

namespace Pdf.Document;

/// <summary>
/// Mutable document model: page delete/reorder/rotate, merge/split via page import, Info edits, and full save.
/// Saving writes a fresh unencrypted file (encryption is dropped; see spec matrix).
/// </summary>
public sealed class PdfEditor
{
    static readonly string[] Inheritable = ["Resources", "MediaBox", "CropBox", "Rotate"];

    readonly Dictionary<int, PdfObject> _objs = [];
    readonly HashSet<int> _deleted = [];
    readonly List<int> _pages = [];
    readonly int _catalog, _pagesRoot;
    int _info;
    int _next = 1;
    string _version = "1.7";

    public int PageCount => _pages.Count;

    PdfEditor()
    {
        _pagesRoot = _next++;
        _catalog = _next++;
        _objs[_pagesRoot] = new PdfDictionary();
        _objs[_catalog] = new PdfDictionary(new Dictionary<string, PdfObject> { ["Type"] = new PdfName("Catalog"), ["Pages"] = new PdfRef(_pagesRoot, 0) });
    }

    PdfEditor(PdfDocument doc)
    {
        var f = doc.File;
        _version = string.Compare(f.Version, "1.7", StringComparison.Ordinal) < 0 ? "1.7" : f.Version;
        foreach (var (num, e) in f.Xref)
        {
            if (!e.InUse) continue;
            var o = f.GetObject(num);
            if (o is PdfNull) continue;
            _objs[num] = o;
            if (num >= _next) _next = num + 1;
        }
        var rootRef = f.Trailer.Get("Root") as PdfRef ?? throw new PdfParseException("no catalog reference");
        _catalog = rootRef.Number;
        _pagesRoot = _next++;
        _objs[_pagesRoot] = new PdfDictionary();
        if (f.Trailer.Get("Info") is PdfRef ir) _info = ir.Number;

        var byInstance = new Dictionary<PdfDictionary, int>(ReferenceEqualityComparer.Instance);
        foreach (var (n, o) in _objs) if (o is PdfDictionary d) byInstance[d] = n;
        foreach (var p in doc.Pages)
        {
            if (!byInstance.TryGetValue(p.Dict, out var num)) { num = _next++; }
            var merged = new Dictionary<string, PdfObject>(p.Dict.Entries);
            foreach (var k in Inheritable) if (!merged.ContainsKey(k) && p.Inherited.TryGetValue(k, out var v)) merged[k] = v;
            merged["Parent"] = new PdfRef(_pagesRoot, 0);
            _objs[num] = new PdfDictionary(merged);
            _pages.Add(num);
        }
        var cat = new Dictionary<string, PdfObject>(((PdfDictionary)_objs[_catalog]).Entries) { ["Pages"] = new PdfRef(_pagesRoot, 0) };
        _objs[_catalog] = new PdfDictionary(cat);
    }

    public static PdfEditor Create() => new();
    public static PdfEditor Open(PdfDocument doc) => new(doc);
    public static PdfEditor Open(byte[] data, string password = "") => new(PdfDocument.Load(data, password));

    void Check(int i) { if ((uint)i >= (uint)_pages.Count) throw new ArgumentOutOfRangeException(nameof(i)); }

    public void DeletePage(int index)
    {
        Check(index);
        _deleted.Add(_pages[index]);
        _pages.RemoveAt(index);
    }

    public void MovePage(int from, int to)
    {
        Check(from); Check(to);
        var n = _pages[from];
        _pages.RemoveAt(from);
        _pages.Insert(to, n);
    }

    public void SetRotation(int index, int degrees)
    {
        Check(index);
        if (degrees % 90 != 0) throw new ArgumentException("rotation must be a multiple of 90", nameof(degrees));
        var d = (PdfDictionary)_objs[_pages[index]];
        var e = new Dictionary<string, PdfObject>(d.Entries) { ["Rotate"] = new PdfInteger(((degrees % 360) + 360) % 360) };
        _objs[_pages[index]] = new PdfDictionary(e);
    }

    public void SetInfo(string key, string value)
    {
        var entries = _info != 0 && _objs.TryGetValue(_info, out var o) && o is PdfDictionary d
            ? new Dictionary<string, PdfObject>(d.Entries) : [];
        if (_info == 0) _info = _next++;
        entries[key] = new PdfString(Utf16(value));
        _objs[_info] = new PdfDictionary(entries);
    }

    static byte[] Utf16(string s)
    {
        var b = new byte[2 + s.Length * 2];
        b[0] = 0xFE; b[1] = 0xFF;
        System.Text.Encoding.BigEndianUnicode.GetBytes(s, 0, s.Length, b, 2);
        return b;
    }

    /// <summary>Copies pages (with everything they reference) from another document, inserting at <paramref name="insertAt"/> (default: end).</summary>
    public void ImportPages(PdfDocument src, IEnumerable<int>? indices = null, int? insertAt = null)
    {
        var f = src.File;
        var chosen = (indices ?? Enumerable.Range(0, src.Pages.Count)).ToList();
        var srcNum = new Dictionary<PdfDictionary, int>(ReferenceEqualityComparer.Instance);
        foreach (var (n, e) in f.Xref) if (e.InUse && f.GetObject(n) is PdfDictionary d) srcNum[d] = n;

        var allPageNums = new HashSet<int>();
        foreach (var p in src.Pages) if (srcNum.TryGetValue(p.Dict, out var n)) allPageNums.Add(n);

        var map = new Dictionary<int, int>();
        var work = new Queue<int>();
        var newPages = new List<int>();
        foreach (var i in chosen)
        {
            var p = src.Pages[i];
            var n = srcNum.TryGetValue(p.Dict, out var sn) ? sn : -1 - newPages.Count;
            var dst = _next++;
            if (n >= 0) map[n] = dst;
            newPages.Add(dst);
            var merged = new Dictionary<string, PdfObject>(p.Dict.Entries);
            foreach (var k in Inheritable) if (!merged.ContainsKey(k) && p.Inherited.TryGetValue(k, out var v)) merged[k] = v;
            merged.Remove("Parent");
            _objs[dst] = new PdfDictionary(merged.ToDictionary(kv => kv.Key, kv => Copy(kv.Value)));
        }
        while (work.Count > 0)
        {
            var n = work.Dequeue();
            _objs[map[n]] = Copy(f.GetObject(n));
        }
        foreach (var dst in newPages)
        {
            var d = (PdfDictionary)_objs[dst];
            var e = new Dictionary<string, PdfObject>(d.Entries) { ["Parent"] = new PdfRef(_pagesRoot, 0) };
            _objs[dst] = new PdfDictionary(e);
        }
        _pages.InsertRange(insertAt ?? _pages.Count, newPages);

        PdfObject Copy(PdfObject? o) => o switch
        {
            PdfRef r => Remap(r),
            PdfArray a => new PdfArray(a.Items.Select(Copy).ToList()),
            PdfStream s => new PdfStream((PdfDictionary)Copy(s.Dict), s.RawData),
            PdfDictionary d => new PdfDictionary(d.Entries.ToDictionary(kv => kv.Key, kv => Copy(kv.Value))),
            null => PdfNull.Instance,
            _ => o,
        };

        PdfObject Remap(PdfRef r)
        {
            if (map.TryGetValue(r.Number, out var m)) return new PdfRef(m, 0);
            if (allPageNums.Contains(r.Number)) return PdfNull.Instance; // page not being imported
            var n = _next++;
            map[r.Number] = n; work.Enqueue(r.Number);
            return new PdfRef(n, 0);
        }
    }

    /// <summary>New document containing only the given pages of <paramref name="src"/>.</summary>
    public static PdfEditor Extract(PdfDocument src, IEnumerable<int> indices)
    {
        var e = Create();
        e.ImportPages(src, indices);
        return e;
    }

    /// <summary>Stable object number of the page at <paramref name="index"/> (survives reordering).</summary>
    public int PageId(int index) { Check(index); return _pages[index]; }

    /// <summary>Adds an object (e.g. a font dictionary or image XObject) and returns a reference to it.</summary>
    public PdfRef AddObject(PdfObject o) { var n = _next++; _objs[n] = o; return new PdfRef(n, 0); }

    public sealed record Overlay(byte[] Content, IReadOnlyDictionary<string, IReadOnlyDictionary<string, PdfObject>> Resources);

    readonly Dictionary<int, Overlay> _overlays = [];

    /// <summary>
    /// Sets the overlay content drawn on top of a page. Overlays are kept separate from the page until <see cref="Save"/>, so they stay editable.
    /// Resources are grouped by category ("Font", "XObject", "ExtGState"); names must not collide with the page's own.
    /// </summary>
    public void SetOverlay(int index, byte[] content, IReadOnlyDictionary<string, IReadOnlyDictionary<string, PdfObject>>? resources = null)
    {
        Check(index);
        if (content.Length == 0) _overlays.Remove(_pages[index]);
        else _overlays[_pages[index]] = new Overlay(content, resources ?? new Dictionary<string, IReadOnlyDictionary<string, PdfObject>>());
    }

    PdfObject? Res(PdfObject? o) => o is PdfRef r && _objs.TryGetValue(r.Number, out var v) ? v : o;

    PdfDictionary ApplyOverlay(PdfDictionary page, Overlay ov, Dictionary<int, PdfObject> objs, ref int next)
    {
        var n0 = next;
        PdfRef Add(PdfObject o) { var n = n0++; objs[n] = o; return new PdfRef(n, 0); }
        PdfStream Stream(byte[] b) => new(new PdfDictionary(new Dictionary<string, PdfObject> { ["Length"] = new PdfInteger(b.Length) }), b);

        var parts = new List<PdfObject> { Add(Stream("q\n"u8.ToArray())) };
        switch (Res(page.Get("Contents")))
        {
            case PdfArray a: parts.AddRange(a.Items); break;
            case PdfStream: parts.Add(page.Get("Contents")!); break;
        }
        parts.Add(Add(Stream("\nQ\nq\n"u8.ToArray())));
        parts.Add(Add(Stream(ov.Content)));
        parts.Add(Add(Stream("\nQ\n"u8.ToArray())));

        var res = Res(page.Get("Resources")) is PdfDictionary rd ? new Dictionary<string, PdfObject>(rd.Entries) : [];
        foreach (var (cat, items) in ov.Resources)
        {
            var merged = res.TryGetValue(cat, out var c) && Res(c) is PdfDictionary cd ? new Dictionary<string, PdfObject>(cd.Entries) : [];
            foreach (var (k, v) in items) merged[k] = v;
            res[cat] = new PdfDictionary(merged);
        }
        next = n0;
        return new PdfDictionary(new Dictionary<string, PdfObject>(page.Entries) { ["Contents"] = new PdfArray(parts), ["Resources"] = new PdfDictionary(res) });
    }

    public byte[] Save(bool applyOverlays = true)
    {
        var kids = new PdfArray(_pages.Select(n => (PdfObject)new PdfRef(n, 0)).ToList());
        var objs = new Dictionary<int, PdfObject>(_objs)
        {
            [_pagesRoot] = new PdfDictionary(new Dictionary<string, PdfObject>
            {
                ["Type"] = new PdfName("Pages"), ["Kids"] = kids, ["Count"] = new PdfInteger(_pages.Count),
            }),
        };
        var next = _next;
        if (applyOverlays)
            foreach (var n in _pages)
                if (_overlays.TryGetValue(n, out var ov)) objs[n] = ApplyOverlay((PdfDictionary)objs[n], ov, objs, ref next);
        return PdfWriter.Write(objs, _catalog, _info == 0 ? null : _info, _version, _deleted);
    }
}
