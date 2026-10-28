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
            case PdfStream: case PdfRef: parts.Add(page.Get("Contents")!); break;
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

    /// <summary>
    /// Sets an AcroForm field value by fully qualified name. Text/choice fields get /V as a text string plus a generated
    /// single-line appearance; buttons get /V and /AS (checked state name or Off). NeedAppearances is set so viewers refresh.
    /// Returns false if no such field exists.
    /// </summary>
    public bool SetFieldValue(string name, string value)
    {
        if (Res(((PdfDictionary)_objs[_catalog]).Get("AcroForm")) is not PdfDictionary form || Res(form.Get("Fields")) is not PdfArray fields) return false;
        var done = false;
        void Walk(PdfObject? o, string prefix, string? ft, int depth)
        {
            if (depth > 32 || o is not PdfRef r || !_objs.TryGetValue(r.Number, out var po) || po is not PdfDictionary d) return;
            var partial = Res(d.Get("T")) is PdfString t ? t.Text : null;
            var full = partial is null ? prefix : prefix.Length == 0 ? partial : prefix + "." + partial;
            ft = (Res(d.Get("FT")) as PdfName)?.Value ?? ft;
            var kids = Res(d.Get("Kids")) as PdfArray;
            var hasFieldKids = kids is not null && kids.Items.Any(k => Res(k) is PdfDictionary kd && kd.Get("T") is not null);
            if (hasFieldKids) { foreach (var k in kids!.Items) Walk(k, full, ft, depth + 1); return; }
            if (full != name) return;
            done = true;
            var e = new Dictionary<string, PdfObject>(d.Entries);
            if (ft == "Btn")
            {
                var on = value is not ("" or "Off");
                e["V"] = new PdfName(on ? value : "Off");
                _objs[r.Number] = new PdfDictionary(e);
                foreach (var w in kids is null ? [r] : kids.Items)
                    if (w is PdfRef wr && _objs.TryGetValue(wr.Number, out var wo) && wo is PdfDictionary wd)
                    {
                        var states = Res(wd.Get("AP")) is PdfDictionary ap && Res(ap.Get("N")) is PdfDictionary n ? n.Entries.Keys : (IEnumerable<string>)[];
                        var we = new Dictionary<string, PdfObject>(wd.Entries) { ["AS"] = new PdfName(on && states.Contains(value) ? value : "Off") };
                        _objs[wr.Number] = new PdfDictionary(we);
                    }
                return;
            }
            e["V"] = new PdfString(EncodeText(value));
            _objs[r.Number] = new PdfDictionary(e);
            if (ft == "Tx")
                foreach (var w in kids is null ? [r] : kids.Items)
                    if (w is PdfRef wr && _objs.TryGetValue(wr.Number, out var wo) && wo is PdfDictionary wd && Res(wd.Get("Rect")) is PdfArray ra && ra.Count >= 4)
                    {
                        double N(int i) => Res(ra[i]).AsNumber() ?? 0;
                        double width = Math.Abs(N(2) - N(0)), height = Math.Abs(N(3) - N(1));
                        var size = Math.Clamp(height - 4, 6, 12);
                        var shown = new string(value.Where(ch => ch is >= ' ' and <= '~').ToArray()).Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
                        var body = System.Text.Encoding.ASCII.GetBytes($"/Tx BMC q BT /Helv {size:0.##} Tf 0 g 2 {(height - size) / 2 + size * 0.22:0.##} Td ({shown}) Tj ET Q EMC");
                        var font = AddObject(new PdfDictionary(new Dictionary<string, PdfObject>
                        { ["Type"] = new PdfName("Font"), ["Subtype"] = new PdfName("Type1"), ["BaseFont"] = new PdfName("Helvetica"), ["Encoding"] = new PdfName("WinAnsiEncoding") }));
                        var xo = AddObject(new PdfStream(new PdfDictionary(new Dictionary<string, PdfObject>
                        {
                            ["Type"] = new PdfName("XObject"), ["Subtype"] = new PdfName("Form"),
                            ["BBox"] = new PdfArray([new PdfInteger(0), new PdfInteger(0), new PdfReal(width), new PdfReal(height)]),
                            ["Resources"] = new PdfDictionary(new Dictionary<string, PdfObject> { ["Font"] = new PdfDictionary(new Dictionary<string, PdfObject> { ["Helv"] = font }) }),
                            ["Length"] = new PdfInteger(body.Length),
                        }), body));
                        var we = new Dictionary<string, PdfObject>(wd.Entries)
                        { ["AP"] = new PdfDictionary(new Dictionary<string, PdfObject> { ["N"] = xo }) };
                        _objs[wr.Number] = new PdfDictionary(we);
                    }
        }
        foreach (var f in fields.Items) Walk(f, "", null, 0);
        if (done)
        {
            var af = ((PdfDictionary)_objs[_catalog]).Get("AcroForm");
            var fe = new Dictionary<string, PdfObject>(form.Entries) { ["NeedAppearances"] = new PdfBoolean(true) };
            if (af is PdfRef ar) _objs[ar.Number] = new PdfDictionary(fe);
            else _objs[_catalog] = new PdfDictionary(new Dictionary<string, PdfObject>(((PdfDictionary)_objs[_catalog]).Entries) { ["AcroForm"] = new PdfDictionary(fe) });
        }
        return done;
    }

    static byte[] EncodeText(string s) =>
        s.All(c => c < 0x80) ? System.Text.Encoding.ASCII.GetBytes(s) : [0xFE, 0xFF, .. System.Text.Encoding.BigEndianUnicode.GetBytes(s)];

    readonly Dictionary<int, byte[]> _contentOverrides = [];

    /// <summary>Replaces the page's content stream(s) at save time with <paramref name="content"/>; null clears the override.</summary>
    public void SetContentOverride(int index, byte[]? content)
    {
        Check(index);
        if (content is null) _contentOverrides.Remove(_pages[index]); else _contentOverrides[_pages[index]] = content;
    }

    public byte[] Save(bool applyOverlays = true, bool applyContentEdits = true)
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
        if (applyContentEdits)
            foreach (var n in _pages)
                if (_contentOverrides.TryGetValue(n, out var bytes))
                {
                    var sn = next++;
                    objs[sn] = new PdfStream(new PdfDictionary(new Dictionary<string, PdfObject> { ["Length"] = new PdfInteger(bytes.Length) }), bytes);
                    objs[n] = new PdfDictionary(new Dictionary<string, PdfObject>(((PdfDictionary)objs[n]).Entries) { ["Contents"] = new PdfRef(sn, 0) });
                }
        if (applyOverlays)
            foreach (var n in _pages)
                if (_overlays.TryGetValue(n, out var ov)) objs[n] = ApplyOverlay((PdfDictionary)objs[n], ov, objs, ref next);
        return PdfWriter.Write(objs, _catalog, _info == 0 ? null : _info, _version, _deleted);
    }
}
