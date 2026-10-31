using System.Globalization;
using System.Text;
using PortableDocument.Core;

namespace PortableDocument.Document;

public sealed partial class PdfEditor
{
    static readonly HashSet<string> AuthorableSubtypes = ["Text", "FreeText", "Line", "Square", "Circle", "Polygon", "PolyLine", "Highlight", "Underline", "Squiggly", "StrikeOut",
        "Stamp", "Caret", "Ink", "FileAttachment", "Redact"];
    static readonly HashSet<string> NoPopupSubtypes = ["FreeText", "Redact", "Popup"];

    /// <summary>Annotation subtypes <see cref="AddAnnotation"/> can create and draw an appearance for.</summary>
    public static IReadOnlyCollection<string> AuthorableAnnotationSubtypes => AuthorableSubtypes;

    static PdfString TextString(string s) => new(s.All(ch => ch < 128) ? Encoding.Latin1.GetBytes(s) : Utf16(s));

    static string AnnotDate(DateTimeOffset t) => "D:" + t.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "Z";

    static DateTimeOffset? ParsePdfDate(string? s)
    {
        if (s is null || !s.StartsWith("D:", StringComparison.Ordinal) || s.Length < 6) return null;
        int N(int at, int len, int fallback) => s.Length >= at + len && int.TryParse(s.AsSpan(at, len), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        try
        {
            var t = new DateTime(N(2, 4, 1970), N(6, 2, 1), N(8, 2, 1), N(10, 2, 0), N(12, 2, 0), N(14, 2, 0), DateTimeKind.Utc);
            var rest = s.Length > 16 ? s[16..] : "";
            if (rest.Length >= 3 && rest[0] is '+' or '-' && int.TryParse(rest.AsSpan(1, 2), out var oh))
            {
                var om = rest.Length >= 6 && int.TryParse(rest.AsSpan(4, 2), out var m) ? m : 0;
                var offset = TimeSpan.FromMinutes(oh * 60 + om) * (rest[0] == '-' ? -1 : 1);
                return new DateTimeOffset(t.Add(-offset), TimeSpan.Zero);
            }
            return new DateTimeOffset(t, TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    static PdfArray Numbers(IEnumerable<double> v) => new([.. v.Select(x => (PdfObject)new PdfReal(Math.Round(x, 4)))]);

    HashSet<string>? _batchNames;
    Dictionary<int, List<PdfObject>>? _batchItems;

    sealed class AnnotationBatch(PdfEditor owner) : IDisposable
    {
        public void Dispose() => owner.EndAnnotationBatch();
    }

    /// <summary>Caches the used /NM set and each page's /Annots list so bulk creation is linear; the page arrays are written when the batch is disposed.</summary>
    internal IDisposable BeginAnnotationBatch()
    {
        _batchNames = UsedAnnotationNames();
        _batchItems = [];
        return new AnnotationBatch(this);
    }

    void EndAnnotationBatch()
    {
        var pending = _batchItems; _batchItems = null; _batchNames = null;
        if (pending is not null) foreach (var (page, items) in pending) if (page < PageCount) SetPageAnnotItems(page, items);
    }

    List<PdfObject> PageAnnotItems(int page)
    {
        Check(page);
        if (_batchItems is null) return LoadAnnotItems(page);
        if (!_batchItems.TryGetValue(page, out var cached)) _batchItems[page] = cached = LoadAnnotItems(page);
        return cached;
    }

    List<PdfObject> LoadAnnotItems(int page) =>
        Res(((PdfDictionary)_objs[_pages[page]]).Get("Annots")) is PdfArray arr ? [.. arr.Items] : [];

    void SetPageAnnotItems(int page, List<PdfObject> items)
    {
        if (_batchItems is not null) { _batchItems[page] = items; return; }
        var pd = (PdfDictionary)_objs[_pages[page]];
        if (pd.Get("Annots") is PdfRef ar) { _objs[ar.Number] = new PdfArray(items); return; }
        var e = new Dictionary<string, PdfObject>(pd.Entries);
        if (items.Count == 0) e.Remove("Annots"); else e["Annots"] = new PdfArray(items);
        _objs[_pages[page]] = new PdfDictionary(e);
    }

    PdfDictionary? AnnotDict(PdfObject? item) => Res(item) as PdfDictionary;

    HashSet<string> UsedAnnotationNames() => [.. Enumerable.Range(0, PageCount).SelectMany(PageAnnotItems)
        .Select(AnnotDict).Select(d => Res(d?.Get("NM")) as PdfString).Where(s => s is not null).Select(s => s!.Text)];

    string NewAnnotationName(string? wanted)
    {
        var used = _batchNames ?? UsedAnnotationNames();
        string name;
        if (!string.IsNullOrEmpty(wanted) && !used.Contains(wanted)) name = wanted;
        else
        {
            var n = used.Count + 1;
            var baseName = string.IsNullOrEmpty(wanted) ? "annot" : wanted;
            while (used.Contains($"{baseName}-{n}")) n++;
            name = $"{baseName}-{n}";
        }
        _batchNames?.Add(name);
        return name;
    }

    double[] PageBox(int page)
    {
        var pd = (PdfDictionary)_objs[_pages[page]];
        return Res(pd.Get("CropBox") ?? pd.Get("MediaBox")) is PdfArray { Count: 4 } a ? [.. a.Items.Select(x => Res(x).AsNumber() ?? 0)] : [0, 0, 612, 792];
    }

    /// <summary>Applies the non-null members of <paramref name="s"/> to <paramref name="d"/>. Geometry changes also refresh /Rect.</summary>
    void ApplySpec(Dictionary<string, PdfObject> d, string subtype, PdfAnnotationSpec s, double borderWidth)
    {
        if (s.Color is { } c) { if (c.Length == 0) d.Remove("C"); else d["C"] = Numbers(c); }
        if (s.InteriorColor is { } ic) { if (ic.Length == 0) d.Remove("IC"); else d["IC"] = Numbers(ic); }
        if (s.Opacity is { } ca) d["CA"] = new PdfReal(Math.Clamp(ca, 0, 1));
        if (s.BorderWidth is not null || s.BorderStyle is not null || s.DashPattern is not null)
        {
            var bs = Res(d.GetValueOrDefault("BS")) is PdfDictionary old ? new Dictionary<string, PdfObject>(old.Entries) : [];
            bs["Type"] = new PdfName("Border");
            bs["W"] = new PdfReal(borderWidth);
            if (s.BorderStyle is { Length: > 0 } style) bs["S"] = new PdfName(style);
            if (s.DashPattern is { Length: > 0 } dash) bs["D"] = Numbers(dash);
            d["BS"] = new PdfDictionary(bs);
        }
        if (s.Contents is { } contents) { if (contents.Length == 0) d.Remove("Contents"); else d["Contents"] = TextString(contents); }
        if (s.RichContents is { } rc) { if (rc.Length == 0) d.Remove("RC"); else d["RC"] = TextString(rc); }
        if (s.Author is { } t) d["T"] = TextString(t);
        if (s.Subject is { } subj) d["Subj"] = TextString(subj);
        if (s.QuadPoints is { } q) d["QuadPoints"] = Numbers(q);
        if (s.Vertices is { } v) d["Vertices"] = Numbers(v);
        if (s.Line is { } l) d["L"] = Numbers(l);
        if (s.InkList is { } ink) d["InkList"] = new PdfArray([.. ink.Select(p => (PdfObject)Numbers(p))]);
        if (s.LineStart is not null || s.LineEnd is not null)
        {
            var cur = Res(d.GetValueOrDefault("LE")) is PdfArray { Count: >= 2 } le ? (Res(le[0]).AsName() ?? "None", Res(le[1]).AsName() ?? "None") : ("None", "None");
            d["LE"] = new PdfArray([new PdfName(s.LineStart ?? cur.Item1), new PdfName(s.LineEnd ?? cur.Item2)]);
        }
        if (s.Cloud is { } cloud)
        {
            if (cloud) d["BE"] = new PdfDictionary(new Dictionary<string, PdfObject> { ["S"] = new PdfName("C"), ["I"] = new PdfInteger(1) });
            else d.Remove("BE");
        }
        if (s.Icon is { Length: > 0 } icon) d["Name"] = new PdfName(icon);
        if (s.StampName is { Length: > 0 } stamp) d["Name"] = new PdfName(stamp);
        if (s.Quadding is { } quad) d["Q"] = new PdfInteger(quad);
        if (subtype == "FreeText" && (s.FontSize is not null || s.TextColor is not null || !d.ContainsKey("DA")))
        {
            var oldDa = Res(d.GetValueOrDefault("DA")) is PdfString ds ? ds.Text : "";
            var m = System.Text.RegularExpressions.Regex.Match(oldDa, @"([\d.]+)\s+Tf");
            var size = s.FontSize ?? (m.Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 12);
            var colorOps = s.TextColor is { Length: > 0 } tc ? ColorOperator(tc) : System.Text.RegularExpressions.Regex.Replace(oldDa, @"/\S+\s+[\d.]+\s+Tf", "").Trim();
            if (colorOps.Length == 0) colorOps = "0 g";
            d["DA"] = new PdfString(Encoding.Latin1.GetBytes($"{colorOps} /Helv {size.ToString("0.##", CultureInfo.InvariantCulture)} Tf"));
        }
        if (s.State is { } state) d["State"] = TextString(state);
        if (s.StateModel is { } model) d["StateModel"] = TextString(model);
        if (s.Open is { } open) d["Open"] = new PdfBoolean(open);
        if (s.OverlayText is { } ot) d["OverlayText"] = TextString(ot);
        if (s.Symbol is { } sy) d["Sy"] = new PdfName(sy);
    }

    static string ColorOperator(double[] c) => c.Length switch
    {
        1 => $"{c[0].ToString("0.###", CultureInfo.InvariantCulture)} g",
        4 => string.Join(' ', c.Select(x => x.ToString("0.###", CultureInfo.InvariantCulture))) + " k",
        _ => string.Join(' ', c.Take(3).Select(x => x.ToString("0.###", CultureInfo.InvariantCulture))) + " rg",
    };

    static double[] Defaults(string subtype) => subtype switch
    {
        "Highlight" => [1, 1, 0],
        "Underline" => [0, 0.6, 0],
        "Squiggly" => [1, 0, 0],
        "StrikeOut" => [1, 0, 0],
        "Text" => [1, 0.9, 0.2],
        "Caret" => [0, 0, 1],
        "Redact" => [1, 0, 0],
        "FreeText" => [0],
        _ => [1, 0, 0],
    };

    /// <summary>
    /// Creates an annotation with a generated /AP /N, unique /NM, /P, /T, /Subj, dates and (for ordinary markup) a linked /Popup, and appends it to the page's /Annots.
    /// Replies are created by setting <see cref="PdfAnnotationSpec.InReplyTo"/>. Returns the new annotation's reference.
    /// </summary>
    public PdfRef AddAnnotation(int page, PdfAnnotationSpec spec, DateTimeOffset? now = null)
    {
        Require(PdfPermissions.Annotate);
        Check(page);
        var sub = spec.Subtype ?? throw new ArgumentException("Annotation subtype is required.", nameof(spec));
        if (!AuthorableSubtypes.Contains(sub)) throw new ArgumentException($"Annotation subtype {sub} cannot be authored.", nameof(spec));
        var time = now ?? DateTimeOffset.UtcNow;
        var created = spec.Created ?? time;
        var bw = spec.BorderWidth ?? (sub == "Ink" ? 2 : sub == "FreeText" ? 0 : 1);
        var isReply = spec.InReplyTo is not null;
        if (isReply && sub != "Text") throw new ArgumentException("Replies are Text annotations.", nameof(spec));
        var d = new Dictionary<string, PdfObject>
        {
            ["Type"] = new PdfName("Annot"), ["Subtype"] = new PdfName(sub), ["P"] = new PdfRef(_pages[page], 0),
            ["NM"] = TextString(NewAnnotationName(spec.Name)), ["M"] = new PdfString(Encoding.ASCII.GetBytes(AnnotDate(spec.Modified ?? time))),
            ["CreationDate"] = new PdfString(Encoding.ASCII.GetBytes(AnnotDate(created))),
            ["F"] = new PdfInteger(isReply ? 32 : 4),
        };
        var seeded = sub == "FreeText" ? spec : spec with { Color = spec.Color ?? Defaults(sub) };
        var bordered = sub is "Square" or "Circle" or "Polygon" or "PolyLine" or "Line" or "Ink" or "FreeText" or "Underline" or "StrikeOut" or "Squiggly";
        seeded = bordered ? seeded with { BorderWidth = bw, BorderStyle = spec.BorderStyle ?? "S" } : seeded with { BorderWidth = null };
        if (sub == "Redact") seeded = seeded with { InteriorColor = spec.InteriorColor ?? [0] };
        ApplySpec(d, sub, seeded, bw);
        if (sub == "FreeText" && !d.ContainsKey("Contents")) d["Contents"] = TextString("");
        if (sub == "Text")
        {
            d["Name"] = new PdfName(spec.Icon is { Length: > 0 } ? spec.Icon : "Note");
            if (spec.State is not null) d["StateModel"] = TextString(spec.StateModel ?? (spec.State is "Marked" or "Unmarked" ? "Marked" : "Review"));
            d["Open"] = new PdfBoolean(spec.Open ?? false);
        }
        if (sub == "Stamp" && !d.ContainsKey("Name")) d["Name"] = new PdfName(spec.Image is null ? "Draft" : "Custom");
        if (sub == "FileAttachment") d["Name"] = new PdfName(spec.Icon is { Length: > 0 } ? spec.Icon : "PushPin");
        if (sub == "Caret") d["Sy"] = new PdfName(spec.Symbol ?? "None");

        if (isReply)
        {
            var parentNumber = spec.InReplyTo!.Value;
            var parent = _objs.GetValueOrDefault(parentNumber) as PdfDictionary ?? throw new ArgumentException("The replied-to annotation does not exist.", nameof(spec));
            d["IRT"] = new PdfRef(parentNumber, 0);
            d["RT"] = new PdfName(spec.ReplyType is "Group" ? "Group" : "R");
            if (spec.Rect is null && Res(parent.Get("Rect")) is PdfArray { Count: 4 } pr)
            {
                var r = pr.Items.Select(x => Res(x).AsNumber() ?? 0).ToArray();
                d["Rect"] = Numbers([Math.Min(r[0], r[2]), Math.Max(r[1], r[3]) - 20, Math.Min(r[0], r[2]) + 20, Math.Max(r[1], r[3])]);
            }
        }
        if (!d.ContainsKey("Rect"))
        {
            var rect = spec.Rect ?? AnnotationAppearance.RectFromGeometry(sub, seeded, bw)
                ?? throw new ArgumentException("A rectangle or geometry is required.", nameof(spec));
            if (sub == "Text" && spec.Rect is { Length: 2 or 4 } && rect.Length == 4 && rect[2] - rect[0] < 1) rect = [rect[0], rect[1], rect[0] + 20, rect[1] + 20];
            d["Rect"] = Numbers(rect);
        }
        else if (spec.Rect is not null) d["Rect"] = Numbers(spec.Rect);
        if (sub is "Highlight" or "Underline" or "Squiggly" or "StrikeOut" or "Redact" && spec.QuadPoints is null) throw new ArgumentException("QuadPoints are required.", nameof(spec));

        if (sub == "FileAttachment" && spec.Attachment is { } att) d["FS"] = AddFileSpec(att);
        var annot = new PdfDictionary(d);
        var ap = AnnotationAppearance.Build(annot, Res, AddObject, null, spec.Image);
        if (ap is not null) d["AP"] = new PdfDictionary(new Dictionary<string, PdfObject> { ["N"] = AddObject(ap) });
        var number = _next++;
        var self = new PdfRef(number, 0);
        if (!isReply && !NoPopupSubtypes.Contains(sub))
        {
            var popup = AddObject(PopupDictionary(self, page, d["Rect"], spec.Open ?? false));
            d["Popup"] = popup;
        }
        _objs[number] = new PdfDictionary(d);
        var items = PageAnnotItems(page);
        items.Add(self);
        if (d.TryGetValue("Popup", out var pu)) items.Add(pu);
        SetPageAnnotItems(page, items);
        return self;
    }

    PdfDictionary PopupDictionary(PdfRef parent, int page, PdfObject rect, bool open)
    {
        var r = (Res(rect) as PdfArray)!.Items.Select(x => Res(x).AsNumber() ?? 0).ToArray();
        var box = PageBox(page);
        double w = 180, h = 110;
        var x = Math.Max(r[0], r[2]);
        var y = Math.Max(r[1], r[3]);
        if (x + w > box[2]) x = Math.Max(box[0], box[2] - w);
        if (y - h < box[1]) y = box[1] + h;
        return new PdfDictionary(new Dictionary<string, PdfObject>
        {
            ["Type"] = new PdfName("Annot"), ["Subtype"] = new PdfName("Popup"), ["Parent"] = parent, ["P"] = new PdfRef(_pages[page], 0),
            ["Rect"] = Numbers([x, y - h, x + w, y]), ["Open"] = new PdfBoolean(open), ["F"] = new PdfInteger(28),
        });
    }

    /// <summary>Largest file a FileAttachment annotation may embed.</summary>
    public const int MaxAttachmentBytes = 25 * 1024 * 1024;

    PdfObject AddFileSpec(PdfAnnotationAttachment att)
    {
        if (att.Data.Length > MaxAttachmentBytes) throw new ArgumentException($"Attachments are limited to {MaxAttachmentBytes / (1024 * 1024)} MB.", nameof(att));
        var data = AnnotationAppearance.Deflate(att.Data);
        var ef = AddObject(new PdfStream(new PdfDictionary(new Dictionary<string, PdfObject>
        {
            ["Type"] = new PdfName("EmbeddedFile"), ["Filter"] = new PdfName("FlateDecode"), ["Length"] = new PdfInteger(data.Length),
            ["Params"] = new PdfDictionary(new Dictionary<string, PdfObject> { ["Size"] = new PdfInteger(att.Data.Length) }),
            ["Subtype"] = new PdfName(att.MimeType is { Length: > 0 } mt ? mt : "application/octet-stream"),
        }), data));
        return new PdfDictionary(new Dictionary<string, PdfObject>
        {
            ["Type"] = new PdfName("Filespec"), ["F"] = TextString(att.FileName), ["UF"] = TextString(att.FileName),
            ["EF"] = new PdfDictionary(new Dictionary<string, PdfObject> { ["F"] = ef }),
        });
    }

    (int Page, PdfDictionary Dict)? FindAnnotation(int page, int number)
    {
        foreach (var item in PageAnnotItems(page))
            if (item is PdfRef r && r.Number == number && _objs.GetValueOrDefault(number) is PdfDictionary d) return (page, d);
        return null;
    }

    /// <summary>
    /// Applies a patch to an annotation and regenerates its appearance and /M. Geometry members (QuadPoints, Vertices, L, InkList) update /Rect
    /// automatically; for other subtypes pass <see cref="PdfAnnotationSpec.Rect"/> to move or resize.
    /// </summary>
    public void UpdateAnnotation(int page, int number, PdfAnnotationSpec patch, DateTimeOffset? now = null)
    {
        Require(PdfPermissions.Annotate);
        Check(page);
        var (_, old) = FindAnnotation(page, number) ?? throw new ArgumentException("Annotation not found on that page.", nameof(number));
        var sub = Res(old.Get("Subtype")).AsName() ?? "";
        var d = new Dictionary<string, PdfObject>(old.Entries);
        var current = ReadSpec(old);
        var bw = patch.BorderWidth ?? current.BorderWidth ?? 1;
        ApplySpec(d, sub, patch, bw);
        if (patch.Rect is { } r) d["Rect"] = Numbers(r);
        else if (patch.QuadPoints is not null || patch.Vertices is not null || patch.Line is not null || patch.InkList is not null || patch.Cloud is not null || patch.BorderWidth is not null || patch.LineStart is not null || patch.LineEnd is not null)
        {
            var merged = current with
            {
                QuadPoints = patch.QuadPoints ?? current.QuadPoints, Vertices = patch.Vertices ?? current.Vertices, Line = patch.Line ?? current.Line,
                InkList = patch.InkList ?? current.InkList, Cloud = patch.Cloud ?? current.Cloud, LineStart = patch.LineStart ?? current.LineStart, LineEnd = patch.LineEnd ?? current.LineEnd,
            };
            if (AnnotationAppearance.RectFromGeometry(sub, merged, bw) is { } geo) d["Rect"] = Numbers(geo);
        }
        d["M"] = new PdfString(Encoding.ASCII.GetBytes(AnnotDate(patch.Modified ?? now ?? DateTimeOffset.UtcNow)));
        if (sub == "Text" && patch.State is not null && !d.ContainsKey("StateModel"))
            d["StateModel"] = TextString(patch.State is "Marked" or "Unmarked" ? "Marked" : "Review");
        var updated = new PdfDictionary(d);
        PdfStream? previous = null;
        PdfObject? apRef = null;
        if (Res(old.Get("AP")) is PdfDictionary apd)
        {
            apRef = apd.Get("N");
            previous = Res(apRef) as PdfStream;
        }
        var ap = AnnotationAppearance.Build(updated, Res, AddObject, previous, patch.Image);
        if (ap is not null)
        {
            if (apRef is PdfRef existing) _objs[existing.Number] = ap;
            else d["AP"] = new PdfDictionary(new Dictionary<string, PdfObject> { ["N"] = AddObject(ap) });
        }
        else d.Remove("AP");
        _objs[number] = new PdfDictionary(d);
    }

    /// <summary>Applies x' = x*sx + tx, y' = y*sy + ty to every geometry member (Rect, QuadPoints, Vertices, L, InkList) and regenerates the appearance, i.e. move and resize.</summary>
    public void TransformAnnotation(int page, int number, double sx, double sy, double tx, double ty, DateTimeOffset? now = null)
    {
        var (_, d) = FindAnnotation(page, number) ?? throw new ArgumentException("Annotation not found on that page.", nameof(number));
        var s = ReadSpec(d);
        double[] Map(double[]? v)
        {
            if (v is null) return [];
            var o = new double[v.Length];
            for (var i = 0; i < v.Length; i += 2) { o[i] = v[i] * sx + tx; o[i + 1] = v[i + 1] * sy + ty; }
            return o;
        }
        var rect = Map(s.Rect);
        if (rect.Length == 4) rect = [Math.Min(rect[0], rect[2]), Math.Min(rect[1], rect[3]), Math.Max(rect[0], rect[2]), Math.Max(rect[1], rect[3])];
        UpdateAnnotation(page, number, new PdfAnnotationSpec
        {
            Rect = rect.Length == 4 ? rect : null,
            QuadPoints = s.QuadPoints is null ? null : Map(s.QuadPoints), Vertices = s.Vertices is null ? null : Map(s.Vertices),
            Line = s.Line is null ? null : Map(s.Line), InkList = s.InkList?.Select(Map).ToList(),
        }, now);
    }

    /// <summary>Removes an annotation together with its popup, its replies (recursively) and their appearance objects.</summary>
    public int DeleteAnnotation(int page, int number) => DeleteAnnotations(page, [number], true);

    /// <summary>Single-pass removal of several annotations (and their popups and replies) from one page.</summary>
    int DeleteAnnotations(int page, IReadOnlyCollection<int> roots, bool dropAppearance)
    {
        Require(PdfPermissions.Annotate);
        Check(page);
        var items = PageAnnotItems(page);
        var present = new HashSet<int>();
        var replies = new Dictionary<int, List<int>>();
        foreach (var item in items)
        {
            if (item is not PdfRef r || AnnotDict(item) is not { } dict) continue;
            present.Add(r.Number);
            if (dict.Get("IRT") is PdfRef irt)
            {
                if (!replies.TryGetValue(irt.Number, out var l)) replies[irt.Number] = l = [];
                l.Add(r.Number);
            }
        }
        var doomed = new HashSet<int>();
        var stack = new Stack<int>(roots.Where(present.Contains));
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (!doomed.Add(n)) continue;
            if (_objs.GetValueOrDefault(n) is PdfDictionary dd && dd.Get("Popup") is PdfRef pu) doomed.Add(pu.Number);
            if (replies.TryGetValue(n, out var kids)) foreach (var k in kids) stack.Push(k);
        }
        if (doomed.Count == 0) return 0;
        foreach (var n in doomed)
        {
            if (dropAppearance && _objs.GetValueOrDefault(n) is PdfDictionary dd) DeleteAppearanceObjects(dd);
            _deleted.Add(n);
        }
        SetPageAnnotItems(page, [.. items.Where(i => !(i is PdfRef r && doomed.Contains(r.Number)))]);
        return doomed.Count;
    }

    void DeleteAppearanceObjects(PdfDictionary annot)
    {
        if (Res(annot.Get("AP")) is not PdfDictionary ap) return;
        if (ap.Get("N") is PdfRef n)
        {
            if (_objs.GetValueOrDefault(n.Number) is PdfStream st && Res(st.Dict.Get("Resources")) is PdfDictionary rs && Res(rs.Get("XObject")) is PdfDictionary xo)
                foreach (var v in xo.Entries.Values.OfType<PdfRef>())
                {
                    if (_objs.GetValueOrDefault(v.Number) is PdfStream im && im.Dict.Get("SMask") is PdfRef sm) _deleted.Add(sm.Number);
                    _deleted.Add(v.Number);
                }
            _deleted.Add(n.Number);
        }
        if (Res(annot.Get("FS")) is PdfDictionary fs && Res(fs.Get("EF")) is PdfDictionary ef && ef.Get("F") is PdfRef f) _deleted.Add(f.Number);
    }

    /// <summary>Replies to an annotation (Text annotation with /IRT and /RT) and optionally sets a review or marked state.</summary>
    public PdfRef ReplyToAnnotation(int page, int parent, string author, string text, string? state = null, string? stateModel = null, DateTimeOffset? now = null)
    {
        if (FindAnnotation(page, parent) is null) throw new ArgumentException("Annotation not found on that page.", nameof(parent));
        return AddAnnotation(page, new PdfAnnotationSpec
        {
            Subtype = "Text", InReplyTo = parent, Author = author, Contents = text, State = state,
            StateModel = state is null ? null : stateModel ?? (state is "Marked" or "Unmarked" ? "Marked" : "Review"),
        }, now);
    }

    /// <summary>Replace-text markup: a StrikeOut over the quads plus a Caret whose contents are the replacement, linked as a group reply.</summary>
    public (PdfRef StrikeOut, PdfRef Caret) AddReplaceText(int page, double[] quadPoints, string replacement, string author, DateTimeOffset? now = null)
    {
        var strike = AddAnnotation(page, new PdfAnnotationSpec { Subtype = "StrikeOut", QuadPoints = quadPoints, Author = author, Contents = "", Color = [1, 0, 0] }, now);
        var b = AnnotationAppearance.Bounds(quadPoints);
        var caret = AddAnnotation(page, new PdfAnnotationSpec
        {
            Subtype = "Caret", Rect = [b[2] - 6, b[1] - 8, b[2] + 6, b[1]], Symbol = "None", Author = author, Contents = replacement, Subject = "Replace Text",
        }, now);
        var items = _objs[caret.Number] as PdfDictionary;
        var e = new Dictionary<string, PdfObject>(items!.Entries) { ["IRT"] = strike, ["RT"] = new PdfName("Group") };
        _objs[caret.Number] = new PdfDictionary(e);
        return (strike, caret);
    }

    /// <summary>Reads the authored properties of every annotation on a page (popups, links and widgets are not annotations to review).</summary>
    public IReadOnlyList<PdfAnnotationInfo> ListAnnotations(int page)
    {
        Check(page);
        var list = new List<PdfAnnotationInfo>();
        foreach (var item in PageAnnotItems(page))
        {
            if (item is not PdfRef r || AnnotDict(item) is not { } d) continue;
            var sub = Res(d.Get("Subtype")).AsName();
            if (sub is "Popup" or "Link" or "Widget" or null) continue;
            list.Add(new PdfAnnotationInfo(r.Number, page, (d.Get("IRT") as PdfRef)?.Number, (d.Get("Popup") as PdfRef)?.Number, ReadSpec(d)));
        }
        return list;
    }

    PdfAnnotationSpec ReadSpec(PdfDictionary d)
    {
        double[]? Nums(string k) => Res(d.Get(k)) is PdfArray a ? [.. a.Items.Select(x => Res(x).AsNumber() ?? 0)] : null;
        string? Str(string k) => Res(d.Get(k)) is PdfString s ? s.Text : null;
        var sub = Res(d.Get("Subtype")).AsName();
        double? bw = null; string? style = null; double[]? dash = null;
        if (Res(d.Get("BS")) is PdfDictionary bs)
        {
            bw = Res(bs.Get("W")).AsNumber(); style = Res(bs.Get("S")).AsName();
            dash = Res(bs.Get("D")) is PdfArray da ? [.. da.Items.Select(x => Res(x).AsNumber() ?? 0)] : null;
        }
        else if (Nums("Border") is { Length: >= 3 } border) bw = border[2];
        string? ls = null, le = null;
        if (Res(d.Get("LE")) is PdfArray { Count: >= 2 } lea) { ls = Res(lea[0]).AsName(); le = Res(lea[1]).AsName(); }
        double? size = null;
        if (Str("DA") is { } da2 && System.Text.RegularExpressions.Regex.Match(da2, @"([\d.]+)\s+Tf") is { Success: true } m) size = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var ink = Res(d.Get("InkList")) is PdfArray ia ? ia.Items.Select(p => Res(p) is PdfArray pa ? pa.Items.Select(x => Res(x).AsNumber() ?? 0).ToArray() : []).ToList() : null;
        var name = Res(d.Get("Name")).AsName();
        return new PdfAnnotationSpec
        {
            Subtype = sub, Rect = Nums("Rect"), Color = Nums("C"), InteriorColor = Nums("IC"), Opacity = Res(d.Get("CA")).AsNumber(),
            BorderWidth = bw, BorderStyle = style, DashPattern = dash, Contents = Str("Contents"), RichContents = Str("RC"), Author = Str("T"), Subject = Str("Subj"), Name = Str("NM"),
            QuadPoints = Nums("QuadPoints"), Vertices = Nums("Vertices"), Line = Nums("L"), InkList = ink, LineStart = ls, LineEnd = le,
            Cloud = Res(d.Get("BE")) is PdfDictionary be && Res(be.Get("S")).AsName() == "C",
            Icon = sub is "Text" or "FileAttachment" ? name : null, StampName = sub == "Stamp" ? name : null, FontSize = size,
            Quadding = (int?)Res(d.Get("Q")).AsNumber(), State = Str("State"), StateModel = Str("StateModel"),
            InReplyTo = (d.Get("IRT") as PdfRef)?.Number, ReplyType = Res(d.Get("RT")).AsName(),
            Open = Res(d.Get("Open")) is PdfBoolean ob ? ob.Value : null, OverlayText = Str("OverlayText"), Symbol = Res(d.Get("Sy")).AsName(),
            Created = ParsePdfDate(Str("CreationDate")), Modified = ParsePdfDate(Str("M")),
        };
    }

    /// <summary>
    /// Permanently draws appearances into the page content and removes the annotations (and their popups and replies). Annotations that are hidden,
    /// links, widgets, popups or redaction marks stay. Returns the number of flattened annotations.
    /// </summary>
    public int FlattenAnnotations(int page, Func<PdfAnnotationInfo, bool>? include = null)
    {
        Require(PdfPermissions.Annotate);
        Check(page);
        var infos = ListAnnotations(page).Where(i => i.Spec.Subtype != "Redact" && (include?.Invoke(i) ?? true)).ToList();
        var flat = new List<(PdfAnnotationInfo Info, PdfDictionary Annot, PdfObject Form, double[] Matrix)>();
        foreach (var info in infos)
        {
            var d = (PdfDictionary)_objs[info.Number];
            var flags = (int)(Res(d.Get("F")).AsNumber() ?? 0);
            if ((flags & (2 | 32)) != 0 || info.ParentNumber is not null) continue;
            var form = AppearanceForm(d);
            if (form is null) continue;
            var (obj, stream) = form.Value;
            if (Res(d.Get("Rect")) is not PdfArray { Count: 4 } ra) continue;
            var r = ra.Items.Select(x => Res(x).AsNumber() ?? 0).ToArray();
            var bb = Res(stream.Dict.Get("BBox")) is PdfArray { Count: 4 } ba ? ba.Items.Select(x => Res(x).AsNumber() ?? 0).ToArray() : [0, 0, 1, 1];
            var mt = Res(stream.Dict.Get("Matrix")) is PdfArray { Count: 6 } ma ? ma.Items.Select(x => Res(x).AsNumber() ?? 0).ToArray() : [1, 0, 0, 1, 0, 0];
            var corners = new[] { (bb[0], bb[1]), (bb[2], bb[1]), (bb[0], bb[3]), (bb[2], bb[3]) }.Select(p => (X: mt[0] * p.Item1 + mt[2] * p.Item2 + mt[4], Y: mt[1] * p.Item1 + mt[3] * p.Item2 + mt[5])).ToArray();
            double tx0 = corners.Min(p => p.X), tx1 = corners.Max(p => p.X), ty0 = corners.Min(p => p.Y), ty1 = corners.Max(p => p.Y);
            double rx0 = Math.Min(r[0], r[2]), rx1 = Math.Max(r[0], r[2]), ry0 = Math.Min(r[1], r[3]), ry1 = Math.Max(r[1], r[3]);
            var sx = tx1 - tx0 > 0 ? (rx1 - rx0) / (tx1 - tx0) : 1;
            var sy = ty1 - ty0 > 0 ? (ry1 - ry0) / (ty1 - ty0) : 1;
            flat.Add((info, d, obj, [sx, 0, 0, sy, rx0 - tx0 * sx, ry0 - ty0 * sy]));
        }
        if (flat.Count == 0) return 0;

        var pageNumber = _pages[page];
        var pd = (PdfDictionary)_objs[pageNumber];
        var xo = new Dictionary<string, PdfObject>();
        var resources = Res(pd.Get("Resources")) is PdfDictionary pres ? new Dictionary<string, PdfObject>(pres.Entries) : [];
        if (Res(resources.GetValueOrDefault("XObject")) is PdfDictionary oldXo) foreach (var kv in oldXo.Entries) xo[kv.Key] = kv.Value;
        var content = new StringBuilder();
        var k = 0;
        foreach (var (_, _, form, m) in flat)
        {
            string name;
            do name = $"AnnFlat{++k}"; while (xo.ContainsKey(name));
            xo[name] = form;
            content.Append($"q {string.Join(' ', m.Select(v => v.ToString("0.#####", CultureInfo.InvariantCulture)))} cm /{name} Do Q\n");
        }
        resources["XObject"] = new PdfDictionary(xo);
        var existing = new List<PdfObject>();
        if (pd.Get("Contents") is { } contents)
            if (Res(contents) is PdfArray ca) existing.AddRange(ca.Items); else existing.Add(contents);
        PdfRef Stream(string s) { var b = Encoding.Latin1.GetBytes(s); return AddObject(new PdfStream(new PdfDictionary(new Dictionary<string, PdfObject> { ["Length"] = new PdfInteger(b.Length) }), b)); }
        var newContents = new List<PdfObject> { Stream("q\n") };
        newContents.AddRange(existing);
        newContents.Add(Stream("Q\n"));
        newContents.Add(Stream(content.ToString()));
        var pe = new Dictionary<string, PdfObject>(pd.Entries) { ["Resources"] = new PdfDictionary(resources), ["Contents"] = new PdfArray(newContents) };
        _objs[pageNumber] = new PdfDictionary(pe);
        DeleteAnnotations(page, flat.Select(f => f.Info.Number).ToList(), false);
        return flat.Count;
    }

    (PdfObject Ref, PdfStream Stream)? AppearanceForm(PdfDictionary d)
    {
        if (Res(d.Get("AP")) is PdfDictionary ap && ap.Get("N") is { } n)
        {
            var target = Res(n);
            if (target is PdfDictionary states)
            {
                var state = Res(d.Get("AS")).AsName();
                if (state is null || states.Get(state) is not { } chosen) return null;
                n = chosen; target = Res(chosen);
            }
            if (target is PdfStream s) return (n is PdfRef ? n : AddObject(s), s);
        }
        var built = AnnotationAppearance.Build(d, Res, AddObject);
        return built is null ? null : (AddObject(built), built);
    }
}
