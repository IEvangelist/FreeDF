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
        _src = f;
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
            if (ft is "Tx" or "Ch")
                foreach (var w in kids is null ? [r] : kids.Items)
                    if (w is PdfRef wr && _objs.TryGetValue(wr.Number, out var wo) && wo is PdfDictionary wd && Res(wd.Get("Rect")) is PdfArray ra && ra.Count >= 4)
                    {
                        double N(int i) => Res(ra[i]).AsNumber() ?? 0;
                        var xo = BuildTextAppearance(d, wd, Math.Abs(N(2) - N(0)), Math.Abs(N(3) - N(1)), DisplayText(d, value), ft == "Ch");
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

    string DisplayText(PdfDictionary field, string value)
    {
        if (Res(field.Get("Opt")) is PdfArray opt)
            foreach (var o in opt.Items)
                if (Res(o) is PdfArray pair && pair.Count >= 2 && Res(pair[0]) is PdfString ex && ex.Text == value && Res(pair[1]) is PdfString disp) return disp.Text;
        return value;
    }

    static string PdfEsc(string s) => new string(s.Select(ch => ch is >= ' ' and <= '~' ? ch : ch is >= (char)0xA0 and <= (char)0xFF ? ch : '?').ToArray())
        .Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    static string Rgb(PdfArray? a)
    {
        if (a is null) return "";
        var v = a.Items.Select(x => x.AsNumber() ?? 0).ToArray();
        return v.Length switch { 1 => $"{v[0]:0.###} ", 3 => $"{v[0]:0.###} {v[1]:0.###} {v[2]:0.###} ", 4 => $"{v[0]:0.###} {v[1]:0.###} {v[2]:0.###} {v[3]:0.###} ", _ => "" };
    }

    /// <summary>Builds a form-field appearance stream (12.7.4.3): MK background/border, single-line, multiline (wrapped) or comb text, and list/combo display.</summary>
    PdfRef BuildTextAppearance(PdfDictionary field, PdfDictionary widget, double width, double height, string text, bool choice)
    {
        var ff = (int)(Res(field.Get("Ff")).AsNumber() ?? 0);
        var multiline = !choice && (ff & 4096) != 0;
        var comb = !choice && (ff & (1 << 24)) != 0 && Res(field.Get("MaxLen")).AsNumber() is > 0;
        var q = (int)(Res(field.Get("Q")).AsNumber() ?? 0);
        var mk = Res(widget.Get("MK")) as PdfDictionary;
        var sb = new System.Text.StringBuilder();
        var bg = Rgb(Res(mk?.Get("BG")) as PdfArray);
        var bc = Rgb(Res(mk?.Get("BC")) as PdfArray);
        var bw = 1.0;
        if (Res(widget.Get("BS")) is PdfDictionary bs && Res(bs.Get("W")).AsNumber() is { } w0) bw = w0;
        if (bg.Length > 0) sb.Append($"q {bg}{(bg.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length switch { 1 => "g", 3 => "rg", _ => "k" })} 0 0 {width:0.##} {height:0.##} re f Q\n");
        if (bc.Length > 0 && bw > 0)
            sb.Append($"q {bc}{(bc.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length switch { 1 => "G", 3 => "RG", _ => "K" })} {bw:0.##} w {bw / 2:0.##} {bw / 2:0.##} {width - bw:0.##} {height - bw:0.##} re S Q\n");
        var pad = Math.Max(2, bw + 1);
        var size = multiline ? 12.0 : Math.Clamp(height - 2 * pad, 6, 12);
        var lineH = size * 1.15;
        var inner = Math.Max(1, width - 2 * pad);
        List<string> lines = [];
        if (multiline)
            foreach (var para in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var cur = "";
                foreach (var word in para.Split(' '))
                {
                    var cand = cur.Length == 0 ? word : cur + " " + word;
                    if (cand.Length * size * 0.5 > inner && cur.Length > 0) { lines.Add(cur); cur = word; } else cur = cand;
                }
                lines.Add(cur);
            }
        else lines.Add(text.Replace("\r", " ").Replace("\n", " "));
        sb.Append($"/Tx BMC q {pad:0.##} {pad:0.##} {inner:0.##} {Math.Max(1, height - 2 * pad):0.##} re W n BT /Helv {size:0.##} Tf 0 g\n");
        if (comb)
        {
            var max = (int)Res(field.Get("MaxLen")).AsNumber()!;
            var cell = width / max;
            var shown = text.Length > max ? text[..max] : text;
            var start = q == 1 ? (max - shown.Length) / 2 : q == 2 ? max - shown.Length : 0;
            for (var i = 0; i < shown.Length; i++)
                sb.Append($"1 0 0 1 {(start + i) * cell + cell / 2 - size * 0.25:0.##} {(height - size) / 2 + size * 0.22:0.##} Tm ({PdfEsc(shown[i].ToString())}) Tj\n");
        }
        else
        {
            var y = multiline ? height - pad - size : (height - size) / 2 + size * 0.22;
            foreach (var ln in lines)
            {
                var tw = ln.Length * size * 0.5;
                var x = q == 1 ? Math.Max(pad, (width - tw) / 2) : q == 2 ? Math.Max(pad, width - pad - tw) : pad;
                sb.Append($"1 0 0 1 {x:0.##} {y:0.##} Tm ({PdfEsc(ln)}) Tj\n");
                y -= lineH;
            }
        }
        sb.Append("ET Q EMC");
        var body = System.Text.Encoding.Latin1.GetBytes(sb.ToString());
        var font = AddObject(new PdfDictionary(new Dictionary<string, PdfObject>
        { ["Type"] = new PdfName("Font"), ["Subtype"] = new PdfName("Type1"), ["BaseFont"] = new PdfName("Helvetica"), ["Encoding"] = new PdfName("WinAnsiEncoding") }));
        return AddObject(new PdfStream(new PdfDictionary(new Dictionary<string, PdfObject>
        {
            ["Type"] = new PdfName("XObject"), ["Subtype"] = new PdfName("Form"),
            ["BBox"] = new PdfArray([new PdfInteger(0), new PdfInteger(0), new PdfReal(width), new PdfReal(height)]),
            ["Resources"] = new PdfDictionary(new Dictionary<string, PdfObject> { ["Font"] = new PdfDictionary(new Dictionary<string, PdfObject> { ["Helv"] = font }) }),
            ["Length"] = new PdfInteger(body.Length),
        }), body));
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

    PdfFile? _src;

    /// <summary>True when the editor was opened from an intact, unencrypted file, so <see cref="SaveIncremental"/> can append an update.</summary>
    public bool CanSaveIncremental => _src is { IsEncrypted: false, Repaired: false };

    Dictionary<int, PdfObject> BuildObjects(bool applyOverlays, bool applyContentEdits, out int next)
    {
        var kids = new PdfArray(_pages.Select(n => (PdfObject)new PdfRef(n, 0)).ToList());
        var objs = new Dictionary<int, PdfObject>(_objs)
        {
            [_pagesRoot] = new PdfDictionary(new Dictionary<string, PdfObject>
            {
                ["Type"] = new PdfName("Pages"), ["Kids"] = kids, ["Count"] = new PdfInteger(_pages.Count),
            }),
        };
        next = _next;
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
        return objs;
    }

    /// <summary>Full rewrite. <paramref name="options"/> enables compression, object/xref streams and AES-256 encryption.</summary>
    public byte[] Save(bool applyOverlays = true, bool applyContentEdits = true, PdfWriteOptions? options = null)
    {
        var objs = BuildObjects(applyOverlays, applyContentEdits, out _);
        return PdfWriter.Write(objs, _catalog, _info == 0 ? null : _info, _version, _deleted, null, options);
    }

    /// <summary>Appends changed and new objects to the original bytes (7.5.6), leaving the original content intact.</summary>
    public byte[] SaveIncremental(bool applyOverlays = true, bool applyContentEdits = true)
    {
        if (!CanSaveIncremental) throw new InvalidOperationException("Incremental save needs an intact, unencrypted source file.");
        var f = _src!;
        var objs = BuildObjects(applyOverlays, applyContentEdits, out var next);
        var changed = new Dictionary<int, PdfObject>();
        foreach (var (n, o) in objs)
        {
            if (_deleted.Contains(n)) continue;
            var orig = f.Xref.TryGetValue(n, out var xe) && xe.InUse ? f.GetObject(n) : null;
            if (!ReferenceEquals(orig, o)) changed[n] = o;
        }
        var data = f.Data;
        var sx = System.Text.Encoding.Latin1.GetString(data, Math.Max(0, data.Length - 1024), Math.Min(1024, data.Length)).LastIndexOf("startxref", StringComparison.Ordinal);
        var tailText = System.Text.Encoding.Latin1.GetString(data, Math.Max(0, data.Length - 1024), Math.Min(1024, data.Length))[(sx + 9)..].Trim();
        var prev = long.Parse(new string(tailText.TakeWhile(char.IsDigit).ToArray()));
        var isStream = !(prev < data.Length && System.Text.Encoding.Latin1.GetString(data, (int)prev, Math.Min(4, data.Length - (int)prev)) == "xref");
        return PdfWriter.WriteIncremental(data, prev, changed, next, _catalog, _info == 0 ? null : _info, f.Resolve(f.Trailer.Get("ID")), isStream);
    }
}
