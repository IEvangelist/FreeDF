using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;
using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using AdoBye.EditorWeb;

/// <summary>The editor's in-memory operations, shared by the HTTP and browser hosts.</summary>
public sealed class EditorSession(FontCatalog catalog, IFontFallback? fontFallback = null, TimeProvider? timeProvider = null)
{
    public sealed record Reply(byte[] Data, string ContentType = "application/json", string? FileName = null);
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    PdfEditor editor = PdfEditor.Create();
    int version, savedVersion;
    PdfDocument? snapshot, baseSnapshot, pristine;
    byte[]? snapshotBytes;
    string fileName = "document.pdf";
    string? savePath;
    bool prepared;
    readonly Dictionary<int, List<Item>> items = [];
    readonly Dictionary<string, Asset> assets = [];
    readonly Dictionary<string, DateTimeOffset> unclaimedAssets = [];
    readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    readonly Dictionary<int, HashSet<string>> historyAssets = [];
    HashSet<string> clipboardAssets = [];
    OverlayBuilder? overlayBuilder;
    readonly Dictionary<int, (byte[] Content, List<ContentInterpreter.TextSpan> Spans, List<ContentInterpreter.PageObject> Objects)> texts = [];
    readonly Dictionary<int, Dictionary<int, ObjEdit>> objEdits = [];
    readonly Dictionary<(int Form, int Start, int End), ObjEdit> formObjEdits = [];
    HashSet<int>? sharedFormReferences;
    Dictionary<int, int> pristineBack = [];
    List<(int Page, ContentInterpreter.TextSpan Span)>? findCache;
    int findVersion = -1;
    const long FormSpan = 10_000_000;
    static readonly string[] InfoKeys = ["Title", "Author", "Subject", "Keywords", "Creator", "Producer"];
    static long SpanKey(ContentInterpreter.TextSpan s) => s.Form == 0 ? s.Index : s.Form * FormSpan + s.Index;
    static Reply AsJson<T>(T value) => new(JsonSerializer.SerializeToUtf8Bytes(value, Json));
    static readonly Reply Ok = AsJson(new { });

    public void OpenFile(string path)
    {
        Reset(PdfEditor.Open(File.ReadAllBytes(path)));
        fileName = Path.GetFileName(path);
        savePath = Path.GetFullPath(path);
    }

    void Reset(PdfEditor e)
    {
        editor = e;
        items.Clear(); texts.Clear(); objEdits.Clear(); formObjEdits.Clear(); assets.Clear(); unclaimedAssets.Clear(); historyAssets.Clear(); clipboardAssets.Clear();
        overlayBuilder = new(editor, assets, catalog);
        Changed(); savePath = null; savedVersion = version;
    }

    PdfDocument Pristine()
    {
        if (pristine is null)
        {
            var map = new Dictionary<int, int>();
            pristine = PdfDocument.Load(editor.Save(false, false, new PdfWriteOptions { ObjectMapOut = map }));
            pristineBack = map.ToDictionary(kv => kv.Value, kv => kv.Key);
        }
        return pristine;
    }

    static IEnumerable<ContentInterpreter.TextSpan> EditableSpans(List<ContentInterpreter.TextSpan> spans)
    {
        var dup = spans.Where(s => s.Form != 0).GroupBy(s => (s.Form, s.Index)).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        return spans.Where(s => s.Form == 0 || !dup.Contains((s.Form, s.Index)));
    }

    (byte[] Content, List<ContentInterpreter.TextSpan> Spans, List<ContentInterpreter.PageObject> Objects) PageText(int i)
    {
        var id = editor.PageId(i);
        if (!texts.TryGetValue(id, out var t))
        {
            var page = Pristine().Pages[i];
            var (spans, objects) = DocumentRenderer.Analyze(page);
            texts[id] = t = (page.GetContents(), spans, objects);
        }
        return t;
    }

    static double[]? Rgb(string? hex) => hex is { Length: 7 } && hex[0] == '#'
        ? [Convert.ToInt32(hex[1..3], 16) / 255.0, Convert.ToInt32(hex[3..5], 16) / 255.0, Convert.ToInt32(hex[5..7], 16) / 255.0] : null;
    static string Hex(RgbColor c) => $"#{(int)Math.Round(c.R * 255):x2}{(int)Math.Round(c.G * 255):x2}{(int)Math.Round(c.B * 255):x2}";

    HashSet<FormObjectRange> AmbiguousFormObjects(int i) =>
        FormObjectEditPolicy.FindAmbiguous(PageText(i).Objects, SharedFormReferences());
    HashSet<int> SharedFormReferences() => sharedFormReferences ??= FormObjectEditPolicy.FindSharedForms(Pristine());
    static double[]? ObjectMatrix(ContentInterpreter.PageObject o, ObjEdit e)
    {
        if (e.Sx == 1 && e.Sy == 1 && e.Tx == 0 && e.Ty == 0) return null;
        var m = o.Ctm.Then(new Matrix(e.Sx, 0, 0, e.Sy, e.Tx, e.Ty)).Then(o.Ctm.Invert());
        return [m.A, m.B, m.C, m.D, m.E, m.F];
    }
    static bool IsReset(ObjEdit? e) => e is null || e == new ObjEdit();
    void CheckPageMutation()
    {
        if (formObjEdits.Count > 0) throw new EditorConflictException("Save and reopen the document, or reset Form child edits, before changing pages.");
    }
    bool HasGraphicGroupEdits() => Enumerable.Range(0, editor.PageCount).Any(page =>
        objEdits.TryGetValue(editor.PageId(page), out var edits) &&
        edits.Keys.Any(index => PageText(page).Objects[index].Kind == "form"));

    void Prepare()
    {
        if (prepared) return;
        overlayBuilder ??= new(editor, assets, catalog);
        editor.ClearStreamOverrides(); editor.ClearPageXObjects();
        var formStreamContents = new Dictionary<int, byte[]>();
        if (formObjEdits.Count > 0)
        {
            var doc = Pristine();
            var pageObjects = Enumerable.Range(0, editor.PageCount).SelectMany(i => PageText(i).Objects).ToList();
            foreach (var group in formObjEdits.GroupBy(kv => kv.Key.Form))
            {
                var pair = pristineBack.FirstOrDefault(kv => kv.Value == group.Key);
                if (pair.Key == 0 || doc.File.GetObject(pair.Key) is not PdfStream stream)
                    throw new InvalidOperationException("The edited Form XObject is no longer available.");
                var edits = group.Select(kv =>
                {
                    var obj = pageObjects.SingleOrDefault(o => o.Form == pair.Key && o.Start == kv.Key.Start && o.End == kv.Key.End)
                        ?? throw new InvalidOperationException("The edited Form XObject object is no longer available.");
                    return new ContentRewriter.ObjectEdit(obj.Start, obj.End, kv.Value.Delete, ObjectMatrix(obj, kv.Value), Rgb(kv.Value.Fill), Rgb(kv.Value.Stroke));
                });
                var content = ContentRewriter.EditObjects(doc.File.DecodeStream(stream), edits);
                formStreamContents[group.Key] = content;
                editor.SetStreamOverride(group.Key, content);
            }
        }
        for (var i = 0; i < editor.PageCount; i++)
        {
            items.TryGetValue(editor.PageId(i), out var allItems);
            var list = allItems?.Where(x => x.Type != "redact").ToList();
            var redacts = allItems?.Where(x => x.Type == "redact").Select(x => new Redactor.BoxRect(x.X, x.Y, x.W, x.H)).ToList() ?? [];
            var origs = list?.Where(x => x.Orig is not null).Select(x => x.Orig!.Value).ToHashSet() ?? [];
            objEdits.TryGetValue(editor.PageId(i), out var oe);
            if (origs.Count > 0 || oe is { Count: > 0 })
            {
                var (content, spans, objects) = PageText(i);
                if (oe is { Count: > 0 })
                    content = ContentRewriter.EditObjects(content, oe.Select(kv =>
                    {
                        var o = objects[kv.Key]; var e = kv.Value;
                        return new ContentRewriter.ObjectEdit(o.Start, o.End, e.Delete, ObjectMatrix(o, e), Rgb(e.Fill), Rgb(e.Stroke));
                    }));
                var removals = spans.Where(s => s.Form == 0 && origs.Contains(s.Index)).ToDictionary(s => s.Index, s => s.Advance);
                editor.SetContentOverride(i, ContentRewriter.RemoveText(content, removals));
                foreach (var g in EditableSpans(spans).Where(s => s.Form != 0 && origs.Contains(SpanKey(s))).GroupBy(s => s.Form))
                {
                    if (Pristine().File.GetObject(g.Key) is not PdfStream fs || !pristineBack.TryGetValue(g.Key, out var formNum)) continue;
                    var formContent = formStreamContents.TryGetValue(formNum, out var edited) ? edited : Pristine().File.DecodeStream(fs);
                    formContent = ContentRewriter.RemoveText(formContent, g.ToDictionary(s => s.Index, s => s.Advance));
                    formStreamContents[formNum] = formContent;
                    editor.SetStreamOverride(formNum, formContent);
                }
            }
            else editor.SetContentOverride(i, null);
            if (redacts.Count > 0)
            {
                Redactor.Apply(editor, i, redacts);
                list = list?.Where(x => !redacts.Any(r => x.X < r.X + r.W && x.X + Math.Max(x.W, x.Type == "text" ? x.Size * Math.Max(x.Text?.Length ?? 0, 1) * 0.5 : 0) > r.X && x.Y < r.Y + r.H && x.Y + Math.Max(x.H, x.Type == "text" ? x.Size : 0) > r.Y)).ToList();
            }
            if (list is { Count: > 0 })
            {
                var page = i;
                var (content, res) = overlayBuilder.Build(list, Pristine().Pages[i].CropBox, it =>
                {
                    if (it.Orig is not { } key) return null;
                    var span = PageText(page).Spans.FirstOrDefault(s => SpanKey(s) == key);
                    if (span?.Font is null || span.FontRef == 0 || !pristineBack.TryGetValue(span.FontRef, out var fnum)) return null;
                    return new OverlayBuilder.OrigFont(span.Font, new PdfRef(fnum, 0));
                });
                editor.SetOverlay(i, content, res);
            }
            else editor.SetOverlay(i, []);
        }
        prepared = true;
    }

    PdfDocument Snap()
    {
        if (snapshot is null) { Prepare(); snapshotBytes = editor.Save(); snapshot = PdfDocument.Load(snapshotBytes); }
        return snapshot;
    }
    PdfDocument BaseSnap() { Prepare(); return baseSnapshot ??= PdfDocument.Load(editor.Save(false)); }
    void Changed() { version++; snapshot = null; baseSnapshot = null; snapshotBytes = null; pristine = null; texts.Clear(); sharedFormReferences = null; prepared = false; }
    void PruneAssets()
    {
        var livePages = Enumerable.Range(0, editor.PageCount).Select(editor.PageId).ToHashSet();
        foreach (var id in items.Keys.Where(id => !livePages.Contains(id)).ToArray()) items.Remove(id);
        foreach (var id in historyAssets.Keys.Where(id => !livePages.Contains(id)).ToArray()) historyAssets.Remove(id);
        var referenced = items.Values.SelectMany(page => page).Select(item => item.Asset).OfType<string>().ToHashSet();
        referenced.UnionWith(historyAssets.Values.SelectMany(ids => ids));
        referenced.UnionWith(clipboardAssets);
        foreach (var id in unclaimedAssets.Keys.Where(id => referenced.Contains(id) || clock.GetUtcNow() - unclaimedAssets[id] >= TimeSpan.FromMinutes(10)).ToArray())
            unclaimedAssets.Remove(id);
        foreach (var id in assets.Keys.Where(id => !referenced.Contains(id) && !unclaimedAssets.ContainsKey(id)).ToArray()) assets.Remove(id);
    }
    byte[] Render(PdfPage page, double dpi) => DocumentRenderer.RenderPage(page, new PdfRenderOptions { FontFallback = fontFallback }, dpi).ToPng();

    /// <summary>Dispatches a local command. Neither this method nor the WASM host performs network I/O.</summary>
    public Reply Handle(string method, string path, IReadOnlyDictionary<string, string> query, byte[]? body = null, string? uploadName = null)
    {
        PruneAssets();
        string? Q(string key) => query.GetValueOrDefault(key);
        int Int(string key, int fallback) => int.TryParse(Q(key), out var n) ? n : fallback;
        double? Number(string key) => double.TryParse(Q(key), CultureInfo.InvariantCulture, out var n) ? n : null;
        T? Read<T>() => JsonSerializer.Deserialize<T>(body ?? [], Json);
        Reply Pdf(byte[] bytes, string name) => new(bytes, "application/pdf", name);
        var p = path.Trim('/').Split('/');
        if (p[0] != "api") throw new ArgumentException("Unknown editor command.");
        var action = p.ElementAtOrDefault(1);
        if (method == "GET")
        {
            switch (action)
            {
                case "state":
                    var doc = editor.PageCount == 0 ? null : BaseSnap();
                    var pages = doc?.Pages.Select((pg, i) => new { index = i, id = editor.PageId(i), rotation = pg.Rotate, x0 = pg.CropBox.Left, top = pg.CropBox.Top, width = pg.CropBox.Width, height = pg.CropBox.Height }) ?? [];
                    return AsJson(new { version, fileName, pageCount = editor.PageCount, pages, dirty = version != savedVersion, canSave = savePath is not null });
                case "fonts": return AsJson(catalog.List());
                case "fields": return AsJson(editor.PageCount == 0 ? [] : Pristine().GetFormFields().Where(f => f.FieldType is "Tx" or "Btn" or "Ch").Select(f => new { name = f.FullName, type = f.FieldType, value = f.Value ?? "" }));
                case "asset":
                    var a = assets[p[2]];
                    return new(a.Data, a.Mime);
                case "info":
                    if (editor.PageCount == 0) return Ok;
                    var d = BaseSnap();
                    var info = InfoKeys.ToDictionary(k => k.ToLowerInvariant(), k => d.Info(k) ?? "");
                    info["pages"] = editor.PageCount.ToString(); info["file"] = fileName;
                    return AsJson(info);
                case "download":
                    RequirePages(); Snap(); return Pdf(snapshotBytes!, "edited-" + fileName);
                case "find": return Find(Q("q") ?? "", Q("cs") == "true");
                case "export": return Export(p[2], query);
            }
        }
        if (action == "page")
        {
            var i = int.Parse(p[2].Replace(".png", ""), CultureInfo.InvariantCulture);
            if (p[2].EndsWith(".png", StringComparison.Ordinal) && method == "GET")
                return new(Render((Q("bare") == "true" ? BaseSnap() : Snap()).Pages[i], Math.Clamp(Int("dpi", 48), 16, 300)), "image/png");
            var id = editor.PageId(i);
            switch (p.ElementAtOrDefault(3))
            {
                case "items":
                    if (method == "GET") return AsJson(items.GetValueOrDefault(id) ?? []);
                    if (method == "PUT")
                    {
                        using var payload = JsonDocument.Parse(body ?? []);
                        if (payload.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            items[id] = Read<List<Item>>() ?? [];
                            historyAssets.Remove(id);
                        }
                        else
                        {
                            var update = Read<ItemUpdate>() ?? throw new ArgumentException("Missing item update.");
                            items[id] = update.Items;
                            historyAssets[id] = update.HistoryAssets.ToHashSet();
                            if (update.ClipboardAssets is { } clipboard) clipboardAssets = clipboard.ToHashSet();
                        }
                        PruneAssets(); Changed(); return Ok;
                    }
                    break;
                case "text" when method == "GET":
                    return AsJson(EditableSpans(PageText(i).Spans).Where(s => !s.Vertical && Math.Abs(s.Angle) < 1 && s.Size > 1 && !string.IsNullOrWhiteSpace(s.Text))
                        .Select(s => new { index = SpanKey(s), text = s.Text, x = s.X, y = s.Y, width = s.Width, size = s.Size, font = s.FontName, color = Hex(s.Color) }));
                case "objects":
                    objEdits.TryGetValue(id, out var oe);
                    if (method == "GET")
                    {
                        var ambiguous = AmbiguousFormObjects(i);
                        return AsJson(PageText(i).Objects.Where(o => FormObjectEditPolicy.IsEditable(o, ambiguous) && (o.Form == 0 || pristineBack.ContainsKey(o.Form))).Select(o =>
                        {
                            ObjEdit? edit = null;
                            if (o.Form == 0) oe?.TryGetValue(o.Index, out edit);
                            else if (pristineBack.TryGetValue(o.Form, out var formNum)) formObjEdits.TryGetValue((formNum, o.Start, o.End), out edit);
                            return new
                            {
                                index = o.Index, kind = o.Kind, inForm = o.Form != 0, x0 = o.X0, y0 = o.Y0, x1 = o.X1, y1 = o.Y1,
                                hasFill = o.HasFill, hasStroke = o.HasStroke, lineWidth = o.LineWidth,
                                fill = o.HasFill ? Hex(o.Fill) : null, stroke = o.HasStroke ? Hex(o.Stroke) : null,
                                formContentsShared = o.Kind == "form" && (SharedFormReferences().Contains(o.Ref) || ambiguous.Any(range => range.Form == o.Ref)),
                                edit,
                            };
                        }));
                    }
                    if (method == "PUT")
                    {
                        var n = int.Parse(p[4], CultureInfo.InvariantCulture);
                        var objects = PageText(i).Objects;
                        if (n < 0 || n >= objects.Count) throw new ArgumentException("Object is not editable.");
                        var o = objects[n];
                        var e = Read<ObjEdit>();
                        if (o.Form != 0)
                        {
                            if (!IsReset(e) && HasGraphicGroupEdits())
                                throw new EditorConflictException("Reset graphic-group edits before editing Form children, or save and reopen the document.");
                            if (!FormObjectEditPolicy.IsEditable(o, AmbiguousFormObjects(i)) || !pristineBack.TryGetValue(o.Form, out var formNum))
                                throw new EditorConflictException("This Form is shared, repeated, or cannot be proven unique (including hidden invocations). Transform its graphic group instead.");
                            var key = (formNum, o.Start, o.End);
                            if (IsReset(e)) formObjEdits.Remove(key); else if (e is not null) formObjEdits[key] = e;
                        }
                        else
                        {
                            if (!IsReset(e) && o.Kind == "form" && formObjEdits.Count > 0)
                                throw new EditorConflictException("Reset Form child edits before editing graphic groups, or save and reopen the document.");
                            if (!objEdits.TryGetValue(id, out var edits)) objEdits[id] = edits = [];
                            if (IsReset(e)) edits.Remove(n); else if (e is not null) edits[n] = e;
                        }
                        Changed(); return Ok;
                    }
                    break;
            }
        }
        if (method == "POST")
        {
            switch (action)
            {
                case "open":
                    Reset(PdfEditor.Open(body ?? throw new ArgumentException("Choose a PDF.")));
                    fileName = uploadName ?? "document.pdf"; return Ok;
                case "import": CheckPageMutation(); editor.ImportPages(PdfDocument.Load(body ?? [])); PruneAssets(); Changed(); return Ok;
                case "asset":
                    var bytes = body ?? [];
                    var mime = bytes.Length > 2 && bytes[0] == 0xff && bytes[1] == 0xd8 ? "image/jpeg" : "image/png";
                    int w, h;
                    if (mime == "image/jpeg") { if (JpegDecoder.Decode(bytes, out w, out h, out _) is null) throw new ArgumentException("Unsupported JPEG."); }
                    else (w, h, _) = PngWriter.Decode(bytes);
                    var key = Q("id") ?? Guid.NewGuid().ToString("N");
                    if (assets.TryGetValue(key, out var existing))
                    {
                        if (!existing.Data.AsSpan().SequenceEqual(bytes)) throw new ArgumentException("Asset ID already contains a different image.");
                    }
                    else
                    {
                        assets[key] = new(bytes, mime);
                    }
                    unclaimedAssets[key] = clock.GetUtcNow();
                    return AsJson(new { id = key, w, h });
                case "asset-clipboard":
                    clipboardAssets = (Read<List<string>>() ?? throw new ArgumentException("Missing clipboard asset references.")).ToHashSet();
                    PruneAssets(); return Ok;
                case "asset-cancel":
                    if (Q("id") is not { } cancelled) throw new ArgumentException("Missing asset ID.");
                    unclaimedAssets.Remove(cancelled); PruneAssets(); return Ok;
                case "asset-history":
                    historyAssets.Clear(); PruneAssets(); return Ok;
                case "delete": CheckPageMutation(); editor.DeletePage(int.Parse(p[2], CultureInfo.InvariantCulture)); PruneAssets(); break;
                case "move": CheckPageMutation(); editor.MovePage(int.Parse(p[2], CultureInfo.InvariantCulture), int.Parse(p[3], CultureInfo.InvariantCulture)); break;
                case "rotate":
                    CheckPageMutation();
                    var i = int.Parse(p[2], CultureInfo.InvariantCulture);
                    editor.SetRotation(i, ((Snap().Pages[i].Rotate + int.Parse(p[3], CultureInfo.InvariantCulture)) % 360 + 360) % 360); break;
                case "title": editor.SetInfo("Title", Q("t") ?? ""); break;
                case "field":
                    if (!editor.SetFieldValue(Q("n") ?? "", Q("v") ?? "")) throw new ArgumentException("Form field not found.");
                    break;
                case "info":
                    var info = Read<Dictionary<string, string>>() ?? [];
                    foreach (var k in InfoKeys) if (info.TryGetValue(k.ToLowerInvariant(), out var v)) editor.SetInfo(k, v);
                    break;
                case "close": Reset(PdfEditor.Create()); fileName = "document.pdf"; return Ok;
                case "new":
                    Reset(PdfEditor.Open(Blank(Number("w") ?? 595.276, Number("h") ?? 841.89)));
                    fileName = "Untitled.pdf"; return Ok;
                case "blank":
                    CheckPageMutation();
                    var at = int.Parse(p[2], CultureInfo.InvariantCulture);
                    var width = Number("w"); var height = Number("h");
                    if (editor.PageCount > 0 && width is null) { var pg = Snap().Pages[Math.Clamp(at - 1, 0, editor.PageCount - 1)]; width = pg.CropBox.Width; height = pg.CropBox.Height; }
                    editor.ImportPages(PdfDocument.Load(Blank(width ?? 595.276, height ?? 841.89)), null, Math.Clamp(at, 0, editor.PageCount)); break;
                case "duplicate":
                    CheckPageMutation();
                    var index = int.Parse(p[2], CultureInfo.InvariantCulture);
                    editor.ImportPages(Snap(), [index], index + 1); break;
                case "save":
                    RequirePages();
                    if (savePath is null) return AsJson(new { saved = false });
                    Snap(); File.WriteAllBytes(savePath, snapshotBytes!); savedVersion = version;
                    return AsJson(new { saved = true, path = savePath });
                case "saved":
                    savedVersion = version;
                    if (!string.IsNullOrWhiteSpace(Q("name"))) fileName = Q("name")!;
                    return Ok;
                default: throw new ArgumentException("Unknown editor command.");
            }
            Changed(); return Ok;
        }
        throw new ArgumentException("Unknown editor command.");
    }

    void RequirePages()
    {
        if (editor.PageCount == 0) throw new ArgumentException("Open or create a PDF first.");
    }

    Reply Find(string q, bool matchCase)
    {
        if (editor.PageCount == 0 || q.Length == 0) return AsJson(Array.Empty<object>());
        if (findCache is null || findVersion != version)
        {
            var doc = Snap(); findCache = [];
            for (var page = 0; page < editor.PageCount; page++)
                foreach (var span in DocumentRenderer.ExtractText(doc.Pages[page]).Where(s => !s.Vertical && !string.IsNullOrEmpty(s.Text)))
                    findCache.Add((page, span));
            findVersion = version;
        }
        var hits = new List<object>();
        var cmp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        foreach (var (page, s) in findCache)
            for (var at = s.Text.IndexOf(q, cmp); at >= 0 && hits.Count < 2000; at = s.Text.IndexOf(q, at + q.Length, cmp))
            {
                var len = Math.Max(s.Text.Length, 1);
                hits.Add(new { page, x = s.X + s.Width * at / len, y = s.Y, w = s.Width * q.Length / len, size = s.Size, angle = s.Angle });
            }
        return AsJson(hits);
    }

    Reply Export(string kind, IReadOnlyDictionary<string, string> query)
    {
        RequirePages();
        string? Q(string k) => query.GetValueOrDefault(k);
        var name = Path.GetFileNameWithoutExtension(fileName);
        var dpi = Math.Clamp(int.TryParse(Q("dpi"), out var resolution) ? resolution : 150, 16, 600);
        switch (kind)
        {
            case "secure":
                Prepare();
                var user = Q("user"); var owner = Q("owner");
                var protect = !string.IsNullOrEmpty(user) || !string.IsNullOrEmpty(owner);
                if (protect && OperatingSystem.IsBrowser()) throw new NotSupportedException("Password-protected export requires the desktop/server editor; browser .NET does not support the required cryptography. Unprotected compressed export is available.");
                var enc = !protect ? null : new PdfEncryption
                {
                    UserPassword = user ?? "", OwnerPassword = string.IsNullOrEmpty(owner) ? user ?? "" : owner,
                    Method = Q("method")?.ToLowerInvariant() switch { "aes128" => PdfEncryptionMethod.Aes128, "rc4128" => PdfEncryptionMethod.Rc4_128, "rc440" => PdfEncryptionMethod.Rc4_40, _ => PdfEncryptionMethod.Aes256 },
                };
                return new(editor.Save(options: new PdfWriteOptions { Compress = true, ObjectStreams = true, Encryption = enc }), "application/pdf", Q("name") ?? (protect ? "protected-" : "compressed-") + fileName);
            case "pdf":
                var doc = Snap(); var idx = ParseRange(Q("pages"), editor.PageCount);
                if (idx.Count == 0) throw new ArgumentException("No pages in range.");
                return new(idx.SequenceEqual(Enumerable.Range(0, editor.PageCount)) ? snapshotBytes! : PdfEditor.Extract(doc, idx).Save(), "application/pdf", Q("name") ?? "export-" + fileName);
            case "png":
                var page = int.Parse(Q("page") ?? "0", CultureInfo.InvariantCulture);
                return new(Render(Snap().Pages[page], dpi), "image/png", $"{name}-{page + 1}.png");
            case "zip":
                var pages = Snap(); using (var ms = new MemoryStream())
                {
                    using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
                        for (var i = 0; i < editor.PageCount; i++)
                        {
                            using var s = z.CreateEntry($"page-{i + 1:000}.png", CompressionLevel.NoCompression).Open();
                            s.Write(Render(pages.Pages[i], dpi));
                        }
                    return new(ms.ToArray(), "application/zip", name + "-pages.zip");
                }
            case "text":
                var textDoc = Snap(); var sb = new StringBuilder();
                for (var i = 0; i < editor.PageCount; i++)
                {
                    if (i > 0) sb.Append('\f');
                    double lastY = double.NaN, lastEnd = 0;
                    foreach (var s in DocumentRenderer.ExtractText(textDoc.Pages[i]).OrderBy(s => Math.Round(s.Y / 3)).ThenBy(s => s.X))
                    {
                        if (!double.IsNaN(lastY) && Math.Abs(s.Y - lastY) > 3) { sb.AppendLine(); lastEnd = 0; }
                        else if (!double.IsNaN(lastY) && s.X - lastEnd > s.Size * 0.15) sb.Append(' ');
                        sb.Append(s.Text); lastY = s.Y; lastEnd = s.X + s.Width;
                    }
                    sb.AppendLine();
                }
                return new(new UTF8Encoding(false).GetBytes(sb.ToString()), "text/plain; charset=utf-8", name + ".txt");
            default: throw new ArgumentException("Unknown export format.");
        }
    }

    static byte[] Blank(double w, double h)
    {
        string[] objs = ["<</Type/Catalog/Pages 2 0 R>>", "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            string.Create(CultureInfo.InvariantCulture, $"<</Type/Page/Parent 2 0 R/MediaBox[0 0 {w:0.###} {h:0.###}]/Resources<<>>>>")];
        var sb = new StringBuilder("%PDF-2.0\n"); var offs = new List<int>();
        for (var i = 0; i < objs.Length; i++) { offs.Add(sb.Length); sb.Append($"{i + 1} 0 obj\n{objs[i]}\nendobj\n"); }
        var xref = sb.Length; sb.Append($"xref\n0 {objs.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offs) sb.Append($"{o:0000000000} 00000 n \n");
        sb.Append($"trailer\n<</Size {objs.Length + 1}/Root 1 0 R>>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    static List<int> ParseRange(string? s, int count)
    {
        if (string.IsNullOrWhiteSpace(s)) return Enumerable.Range(0, count).ToList();
        var result = new List<int>();
        foreach (var part in s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var ab = part.Split('-', 2, StringSplitOptions.TrimEntries);
            if (!int.TryParse(ab[0], out var a)) continue;
            var b = ab.Length == 2 ? int.TryParse(ab[1], out var bb) ? bb : count : a;
            for (var p = Math.Max(1, Math.Min(a, b)); p <= Math.Min(count, Math.Max(a, b)); p++) result.Add(p - 1);
        }
        return result;
    }
}

public sealed record ObjEdit(bool Delete = false, double Sx = 1, double Sy = 1, double Tx = 0, double Ty = 0, string? Fill = null, string? Stroke = null);

public sealed class EditorConflictException(string message) : InvalidOperationException(message);

public sealed record ItemUpdate(List<Item> Items, List<string> HistoryAssets, List<string>? ClipboardAssets = null);
