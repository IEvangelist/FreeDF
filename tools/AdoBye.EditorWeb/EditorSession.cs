using System.Globalization;
using System.IO.Compression;
using System.Diagnostics;
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
    public sealed record RenderMetrics(double SnapshotMs, double RasterMs, double PngEncodeMs, double TotalMs,
        long AllocatedBytes, int Gen0Collections, bool CacheHit);
    public RenderMetrics? LastRenderMetrics { get; private set; }
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    PdfEditor editor = PdfEditor.Create();
    int version, savedVersion;
    PdfDocument? snapshot, baseSnapshot, pristine;
    byte[]? snapshotBytes;
    string fileName = "document.pdf";
    string? savePath;
    bool prepared;
    bool recoverySupported = true;
    readonly Dictionary<int, List<Item>> items = [];
    readonly Dictionary<string, Asset> assets = [];
    readonly Dictionary<string, DateTimeOffset> unclaimedAssets = [];
    readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    readonly Dictionary<int, HashSet<string>> historyAssets = [];
    HashSet<string> clipboardAssets = [];
    const int RenderCacheLimit = 8 * 1024 * 1024;
    readonly Dictionary<(int Page, int Dpi, bool Bare), byte[]> renderedPages = [];
    readonly Queue<(int Page, int Dpi, bool Bare)> renderOrder = [];
    int renderCacheBytes;
    OverlayBuilder? overlayBuilder;
    readonly Dictionary<int, (byte[] Content, List<ContentInterpreter.TextSpan> Spans, List<ContentInterpreter.PageObject> Objects)> texts = [];
    readonly Dictionary<int, Reply> selectionReplies = [];
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
        var doc = PdfDocument.Load(File.ReadAllBytes(path));
        OpenDocument(doc);
        fileName = Path.GetFileName(path);
        savePath = Path.GetFullPath(path);
    }

    void OpenDocument(PdfDocument doc)
    {
        Reset(PdfEditor.Open(doc), !doc.File.IsEncrypted);
        pristine = doc;
        // PdfEditor.Open retains source object numbers until a rewrite is needed.
        pristineBack = doc.File.Xref.Keys.ToDictionary(number => number, number => number);
    }

    void Reset(PdfEditor e, bool allowRecovery = true)
    {
        editor = e;
        recoverySupported = allowRecovery;
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

    sealed record TextDetails(long Index, string Text, double X, double Y, double Width, double Size, string Font, string Color, double Spacing);
    sealed record ObjectDetails(int Index, string Kind, bool InForm, double X0, double Y0, double X1, double Y1,
        bool HasFill, bool HasStroke, double LineWidth, string? Fill, string? Stroke, bool FormContentsShared, ObjEdit? Edit);

    IEnumerable<TextDetails> TextDetailsFor(int i) =>
        EditableSpans(PageText(i).Spans).Where(s => !s.Vertical && Math.Abs(s.Angle) < 1 && s.Size > 1 && !string.IsNullOrWhiteSpace(s.Text))
            .Select(s => new TextDetails(SpanKey(s), s.Text, s.X, s.Y, s.Width, s.Size, s.FontName, Hex(s.Color), s.CharacterSpacing));

    IEnumerable<ObjectDetails> ObjectDetailsFor(int i)
    {
        objEdits.TryGetValue(editor.PageId(i), out var edits);
        var objects = PageText(i).Objects;
        var ambiguous = objects.Any(o => o.Form != 0) ? AmbiguousFormObjects(i) : [];
        foreach (var o in objects.Where(o => FormObjectEditPolicy.IsEditable(o, ambiguous) && (o.Form == 0 || pristineBack.ContainsKey(o.Form))))
        {
            ObjEdit? edit = null;
            if (o.Form == 0) edits?.TryGetValue(o.Index, out edit);
            else if (pristineBack.TryGetValue(o.Form, out var form)) formObjEdits.TryGetValue((form, o.Start, o.End), out edit);
            yield return new(o.Index, o.Kind, o.Form != 0, o.X0, o.Y0, o.X1, o.Y1, o.HasFill, o.HasStroke, o.LineWidth,
                o.HasFill ? Hex(o.Fill) : null, o.HasStroke ? Hex(o.Stroke) : null,
                o.Kind == "form" && (SharedFormReferences().Contains(o.Ref) || ambiguous.Any(range => range.Form == o.Ref)), edit);
        }
    }

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
    static bool AffectsBase(Item item) => item.Orig is not null || item.Type == "redact";
    void CheckPageMutation()
    {
        if (formObjEdits.Count > 0) throw new EditorConflictException("Save and reopen the document, or reset Form child edits, before changing pages.");
    }
    bool HasGraphicGroupEdits() => Enumerable.Range(0, editor.PageCount).Any(page =>
        objEdits.TryGetValue(editor.PageId(page), out var edits) &&
        edits.Keys.Any(index => PageText(page).Objects[index].Kind == "form"));

    void Prepare(bool originalTextOnly = false)
    {
        if (prepared && !originalTextOnly) return;
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
            var list = allItems?.Where(x => x.Type != "redact" && (!originalTextOnly || x.Orig is not null)).ToList();
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
                    if (span?.Font is null) return null;
                    var fontRef = span.FontRef != 0 && pristineBack.TryGetValue(span.FontRef, out var fnum) ? new PdfRef(fnum, 0) : null;
                    var m = span.RenderingMatrix;
                    var transform = span.Size > 0 ? new Matrix(m.A / span.Size, -m.B / span.Size, m.C / span.Size, -m.D / span.Size, 0, 0) : Matrix.Identity;
                    return new OverlayBuilder.OrigFont(span.Font, fontRef, transform, span.WordSpacing);
                });
                editor.SetOverlay(i, content, res);
            }
            else editor.SetOverlay(i, []);
        }
        prepared = !originalTextOnly;
    }

    static Reply OutlinesJson(IReadOnlyList<PdfOutlineItem> items)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            void Write(IReadOnlyList<PdfOutlineItem> list)
            {
                w.WriteStartArray();
                foreach (var item in list)
                {
                    w.WriteStartObject();
                    w.WriteString("title", item.Title);
                    if (item.PageIndex is { } page) w.WriteNumber("page", page);
                    w.WriteBoolean("open", item.Open);
                    if (item.Children.Count > 0) { w.WritePropertyName("children"); Write(item.Children); }
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }
            Write(items);
        }
        return new(stream.ToArray());
    }

    PdfDocument Snap()
    {
        if (snapshot is null) { Prepare(); snapshotBytes = editor.Save(); snapshot = PdfDocument.Load(snapshotBytes); }
        return snapshot;
    }
    PdfDocument BaseSnap()
    {
        if (baseSnapshot is null)
        {
            if (!items.Values.Any(page => page.Any(AffectsBase)) &&
                !objEdits.Values.Any(page => page.Count > 0) && formObjEdits.Count == 0)
                baseSnapshot = Pristine();
            else
            {
                Prepare(originalTextOnly: true);
                baseSnapshot = PdfDocument.Load(editor.Save());
            }
        }
        return baseSnapshot;
    }
    void Changed(bool baseChanged = true, int? changedPage = null)
    {
        selectionReplies.Clear();
        version++; snapshot = null; snapshotBytes = null; prepared = false; findCache = null; findVersion = -1;
        if (baseChanged)
        {
            baseSnapshot = null; pristine = null; texts.Clear(); sharedFormReferences = null;
            renderedPages.Clear(); renderOrder.Clear(); renderCacheBytes = 0;
            return;
        }

        foreach (var key in renderedPages.Keys.Where(key => !key.Bare && (changedPage is null || key.Page == changedPage)).ToArray())
        {
            renderCacheBytes -= renderedPages[key].Length;
            renderedPages.Remove(key);
        }
        var retained = renderOrder.Where(renderedPages.ContainsKey).Distinct().ToArray();
        renderOrder.Clear();
        foreach (var key in retained) renderOrder.Enqueue(key);
    }

    byte[] PagePng(int index, int dpi, bool bare)
    {
        var key = (editor.PageId(index), dpi, bare);
        if (renderedPages.TryGetValue(key, out var cached))
        {
            LastRenderMetrics = new(0, 0, 0, 0, 0, 0, true);
            return cached;
        }
        var total = Stopwatch.StartNew();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var collections = GC.CollectionCount(0);
        var snapshot = Stopwatch.StartNew();
        var doc = bare ? BaseSnap() : Snap();
        var snapshotMs = snapshot.Elapsed.TotalMilliseconds;
        var png = RenderProfiled(doc.Pages[index], dpi, snapshotMs, total, allocated, collections);
        if (png.Length <= RenderCacheLimit)
        {
            while (renderCacheBytes + png.Length > RenderCacheLimit)
            {
                var oldest = renderOrder.Dequeue();
                renderCacheBytes -= renderedPages[oldest].Length;
                renderedPages.Remove(oldest);
            }
            renderedPages[key] = png;
            renderOrder.Enqueue(key);
            renderCacheBytes += png.Length;
        }
        return png;
    }
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
    byte[] Render(PdfPage page, double dpi) =>
        DocumentRenderer.RenderPage(page, new PdfRenderOptions { FontFallback = fontFallback }, dpi).ToPng();

    byte[] RenderProfiled(PdfPage page, double dpi, double snapshotMs, Stopwatch total, long allocated, int collections)
    {
        var raster = Stopwatch.StartNew();
        var rendered = DocumentRenderer.RenderPage(page, new PdfRenderOptions { FontFallback = fontFallback }, dpi);
        var rasterMs = raster.Elapsed.TotalMilliseconds;
        var encode = Stopwatch.StartNew();
        var png = rendered.ToPng();
        LastRenderMetrics = new(snapshotMs, rasterMs, encode.Elapsed.TotalMilliseconds, total.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocated, GC.CollectionCount(0) - collections, false);
        return png;
    }

    sealed record RecoveryPage(int Id, List<Item> Items, Dictionary<int, ObjEdit> Objects, List<string> History);
    sealed record RecoveryForm(int Form, int Start, int End, ObjEdit Edit);
    sealed record RecoveryData(int Format, byte[] Document, string FileName, bool Dirty, List<RecoveryPage> Pages,
        Dictionary<string, Asset> Assets, List<string> Clipboard, List<RecoveryForm> Forms, Dictionary<int, int> OriginalForms);

    static long? RemapOriginal(long? key, Func<int, int> map) =>
        key is >= FormSpan ? checked(map((int)(key.Value / FormSpan)) * FormSpan + key.Value % FormSpan) : key;

    Reply CaptureRecovery()
    {
        RequirePages();
        if (!recoverySupported) throw new NotSupportedException("Local recovery is disabled for documents opened from or importing encrypted PDFs. Download a protected copy using the server tools; decrypted bytes are never stored for recovery.");
        Pristine();
        var map = new Dictionary<int, int>();
        var document = editor.Save(false, false, new PdfWriteOptions { ObjectMapOut = map });
        var pages = Enumerable.Range(0, editor.PageCount).Select(i =>
        {
            var id = editor.PageId(i);
            var pageItems = (items.GetValueOrDefault(id) ?? []).Select(it =>
                it with { Orig = RemapOriginal(it.Orig, form => map[pristineBack[form]]) }).ToList();
            return new RecoveryPage(map[id], pageItems, objEdits.GetValueOrDefault(id) ?? [],
                historyAssets.GetValueOrDefault(id)?.ToList() ?? []);
        }).ToList();
        var retained = pages.SelectMany(p => p.Items.Select(it => it.Asset).OfType<string>().Concat(p.History)).Concat(clipboardAssets).ToHashSet();
        var retainedAssets = assets.Where(kv => retained.Contains(kv.Key)).ToDictionary();
        var forms = formObjEdits.Select(kv => new RecoveryForm(map[kv.Key.Form], kv.Key.Start, kv.Key.End, kv.Value)).ToList();
        var originalForms = pristineBack.Where(kv => Pristine().File.GetObject(kv.Key) is PdfStream stream && Pristine().File.Resolve(stream.Dict.Get("Subtype")).AsName() == "Form")
            .ToDictionary(kv => kv.Key, kv => map[kv.Value]);
        return AsJson(new RecoveryData(1, document, fileName, version != savedVersion, pages, retainedAssets, clipboardAssets.ToList(), forms, originalForms));
    }

    Dictionary<int, int> RestoreRecovery(RecoveryData data, List<int>? historyForms)
    {
        if (data.Format != 1 || data.Document is not { Length: > 0 } || data.Pages is null || data.Assets is null ||
            data.Clipboard is null || data.Forms is null || data.OriginalForms is null || string.IsNullOrWhiteSpace(data.FileName))
            throw new InvalidDataException("The local recovery record is incomplete or uses an unsupported format.");
        var doc = PdfDocument.Load(data.Document);
        if (doc.File.IsEncrypted) throw new NotSupportedException("Encrypted recovery is not supported. The retained record has not been changed.");
        var restored = new EditorSession(catalog, fontFallback, clock);
        restored.Reset(PdfEditor.Open(doc));
        if (data.Pages.Count != restored.editor.PageCount) throw new InvalidDataException("Recovery page count does not match the retained PDF.");
        restored.Pristine();
        var forward = restored.pristineBack.ToDictionary(kv => kv.Value, kv => kv.Key);
        foreach (var (assetId, asset) in data.Assets)
        {
            if (string.IsNullOrWhiteSpace(assetId) || asset?.Data is not { Length: > 0 } || asset.Mime is not ("image/png" or "image/jpeg"))
                throw new InvalidDataException("A recovery image asset is invalid.");
            if (asset.Mime == "image/png")
            {
                var (width, height, rgba) = PngWriter.Decode(asset.Data);
                if (width <= 0 || height <= 0 || rgba.LongLength != (long)width * height * 4) throw new InvalidDataException("A recovery PNG is invalid.");
            }
            else if (JpegDecoder.Decode(asset.Data, out _, out _, out _) is null) throw new InvalidDataException("A recovery JPEG is invalid.");
            restored.assets[assetId] = asset;
        }
        for (var i = 0; i < data.Pages.Count; i++)
        {
            var page = data.Pages[i];
            if (page is null || page.Id != restored.editor.PageId(i) || page.Items is null || page.Objects is null || page.History is null)
                throw new InvalidDataException("A recovery page is invalid.");
            foreach (var (index, edit) in page.Objects)
                restored.Handle("PUT", $"/api/page/{i}/objects/{index}", new Dictionary<string, string>(), JsonSerializer.SerializeToUtf8Bytes(edit, Json));
        }
        foreach (var form in data.Forms)
        {
            if (form is null || !forward.TryGetValue(form.Form, out var pristineForm)) throw new InvalidDataException("A recovery Form reference is invalid.");
            var matches = Enumerable.Range(0, restored.editor.PageCount).SelectMany(page => restored.PageText(page).Objects.Select((obj, index) => (page, obj, index)))
                .Where(x => x.obj.Form == pristineForm && x.obj.Start == form.Start && x.obj.End == form.End).ToList();
            if (matches.Count != 1) throw new InvalidDataException("A recovery Form edit is ambiguous or missing.");
            var match = matches[0];
            restored.Handle("PUT", $"/api/page/{match.page}/objects/{match.index}", new Dictionary<string, string>(), JsonSerializer.SerializeToUtf8Bytes(form.Edit, Json));
        }
        for (var i = 0; i < data.Pages.Count; i++)
        {
            var page = data.Pages[i];
            var list = page.Items.Select(it => it is null ? throw new InvalidDataException("A recovery item is invalid.") :
                it with { Orig = RemapOriginal(it.Orig, form => forward[form]) }).ToList();
            if (list.Select(it => it.Asset).OfType<string>().Where(id => id.Length > 0).Concat(page.History).Any(id => !restored.assets.ContainsKey(id)))
                throw new InvalidDataException("A recovery image reference has no retained bytes.");
            restored.items[page.Id] = list;
            restored.historyAssets[page.Id] = page.History.ToHashSet();
        }
        if (data.Clipboard.Any(id => !restored.assets.ContainsKey(id))) throw new InvalidDataException("A recovery clipboard image is missing.");
        restored.clipboardAssets = data.Clipboard.ToHashSet();
        var originalForms = data.OriginalForms.ToDictionary(kv => kv.Key, kv => forward[kv.Value]);
        if (historyForms?.Any(form => !originalForms.ContainsKey(form)) == true) throw new InvalidDataException("A recovery history Form reference is missing.");
        Reset(restored.editor);
        fileName = Path.GetFileName(data.FileName);
        foreach (var kv in restored.assets) assets[kv.Key] = kv.Value;
        foreach (var kv in restored.items) items[kv.Key] = kv.Value;
        foreach (var kv in restored.historyAssets) historyAssets[kv.Key] = kv.Value;
        foreach (var kv in restored.objEdits) objEdits[kv.Key] = kv.Value;
        foreach (var kv in restored.formObjEdits) formObjEdits[kv.Key] = kv.Value;
        clipboardAssets = restored.clipboardAssets;
        pristine = restored.pristine; pristineBack = restored.pristineBack;
        foreach (var kv in restored.texts) texts[kv.Key] = kv.Value;
        if (data.Dirty) savedVersion = version - 1;
        return originalForms;
    }

    /// <summary>Dispatches a local command. Neither this method nor the WASM host performs network I/O.</summary>
    public Reply Handle(string method, string path, IReadOnlyDictionary<string, string> query, byte[]? body = null, string? uploadName = null)
    {
        LastRenderMetrics = null;
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
                    return AsJson(new { version, fileName, pageCount = editor.PageCount, pages, dirty = version != savedVersion, canSave = savePath is not null, recoverySupported });
                case "recovery": return CaptureRecovery();
                case "outlines": return editor.PageCount == 0 ? AsJson(Array.Empty<int>()) : OutlinesJson(BaseSnap().GetOutlines());
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
                return new(PagePng(i, Math.Clamp(Int("dpi", 48), 16, 300), Q("bare") == "true"), "image/png");
            var id = editor.PageId(i);
            switch (p.ElementAtOrDefault(3))
            {
                case "items":
                    if (method == "GET") return AsJson(items.GetValueOrDefault(id) ?? []);
                    if (method == "PUT")
                    {
                        var oldBaseItems = items.GetValueOrDefault(id)?.Where(AffectsBase).ToArray() ?? [];
                        using var payload = JsonDocument.Parse(body ?? []);
                        List<Item> updatedItems;
                        if (payload.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            updatedItems = Read<List<Item>>() ?? [];
                            items[id] = updatedItems;
                            historyAssets.Remove(id);
                        }
                        else
                        {
                            var update = Read<ItemUpdate>() ?? throw new ArgumentException("Missing item update.");
                            updatedItems = update.Items;
                            items[id] = updatedItems;
                            historyAssets[id] = update.HistoryAssets.ToHashSet();
                            if (update.ClipboardAssets is { } clipboard) clipboardAssets = clipboard.ToHashSet();
                        }
                        var newBaseItems = updatedItems.Where(AffectsBase).ToArray();
                        var baseChanged = !oldBaseItems.SequenceEqual(newBaseItems);
                        PruneAssets(); Changed(baseChanged, id); return Ok;
                    }
                    break;
                case "text" when method == "GET":
                    return AsJson(TextDetailsFor(i));
                case "selection" when method == "GET":
                    if (!selectionReplies.TryGetValue(id, out var selection))
                        selectionReplies[id] = selection = AsJson(new { spans = TextDetailsFor(i), objects = ObjectDetailsFor(i) });
                    return selection;
                case "objects":
                    if (method == "GET")
                        return AsJson(ObjectDetailsFor(i));
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
                    var opened = PdfDocument.Load(body ?? throw new ArgumentException("Choose a PDF."));
                    OpenDocument(opened);
                    fileName = uploadName ?? "document.pdf"; return Ok;
                case "recovery":
                    return AsJson(new { originalForms = RestoreRecovery(Read<RecoveryData>() ?? throw new InvalidDataException("Missing local recovery record."),
                        Q("forms") is { } historyForms ? JsonSerializer.Deserialize<List<int>>(historyForms) : null) });
                case "import":
                    CheckPageMutation();
                    var imported = LoadImport(body ?? throw new ArgumentException("Choose a PDF."), uploadName ?? "PDF");
                    var chosen = ImportRange(Q("pages"), imported.Pages.Count, uploadName ?? "PDF");
                    var firstNew = ImportPosition(Q("at"));
                    editor.ImportPages(imported, chosen, firstNew, PdfImportOptions.Merge(BookmarkTitle(uploadName)));
                    recoverySupported &= !imported.File.IsEncrypted;
                    PruneAssets(); Changed(); return AsJson(new { first = firstNew, count = chosen.Count });
                case "import-batch":
                    {
                        CheckPageMutation();
                        var frames = ReadFrames(body ?? throw new ArgumentException("Choose at least one PDF."));
                        var names = Q("names") is { } namesJson ? JsonSerializer.Deserialize<List<string>>(namesJson, Json) ?? [] : [];
                        var ranges = (Q("ranges") ?? "").Split('|');
                        var batch = new List<(PdfDocument, List<int>, PdfImportOptions)>();
                        var encrypted = false;
                        for (var k = 0; k < frames.Count; k++)
                        {
                            var label = k < names.Count && names[k].Length > 0 ? names[k] : $"File {k + 1}";
                            var doc = LoadImport(frames[k], label);
                            encrypted |= doc.File.IsEncrypted;
                            batch.Add((doc, ImportRange(k < ranges.Length ? ranges[k] : null, doc.Pages.Count, label), PdfImportOptions.Merge(BookmarkTitle(label))));
                        }
                        var start = ImportPosition(Q("at"));
                        editor.ImportBatch(batch, start);
                        recoverySupported &= !encrypted;
                        PruneAssets(); Changed(); return AsJson(new { first = start, count = batch.Sum(b => b.Item2.Count), files = batch.Count });
                    }
                case "inspect":
                    var probe = LoadImport(body ?? throw new ArgumentException("Choose a PDF."), uploadName ?? "This PDF");
                    return AsJson(new { pageCount = probe.Pages.Count, encrypted = probe.File.IsEncrypted });
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

    static PdfDocument LoadImport(byte[] bytes, string label)
    {
        try { return PdfDocument.Load(bytes); }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or PlatformNotSupportedException)
        {
            throw new NotSupportedException($"{label} is password-protected or uses encryption this editor cannot open. Passwords are never requested or stored.", e);
        }
        catch (Exception e) when (e is not ArgumentException)
        {
            throw new ArgumentException($"{label} is not a readable PDF ({e.Message}).", e);
        }
    }

    static List<int> ImportRange(string? range, int count, string label)
    {
        if (range is not null && range.Any(c => !char.IsAsciiDigit(c) && c is not (',' or '-' or ' ')))
            throw new ArgumentException($"{label}: page ranges use numbers, commas and dashes, such as 1-3, 5.");
        var chosen = ParseRange(range, count);
        if (chosen.Count == 0) throw new ArgumentException($"{label}: no pages in range. The PDF has {count} page(s).");
        return chosen;
    }

    int ImportPosition(string? atText) => atText is not null ? Math.Clamp(int.Parse(atText, CultureInfo.InvariantCulture), 0, editor.PageCount) : editor.PageCount;

    static string? BookmarkTitle(string? fileName) => string.IsNullOrWhiteSpace(fileName) ? null : Path.GetFileNameWithoutExtension(fileName);

    // Framing: uint32 count, then per file a uint32 length followed by that many bytes (little endian).
    static List<byte[]> ReadFrames(byte[] body)
    {
        var frames = new List<byte[]>();
        var span = body.AsSpan();
        if (span.Length < 4) throw new ArgumentException("Choose at least one PDF.");
        var count = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span);
        var pos = 4;
        for (uint i = 0; i < count; i++)
        {
            if (pos + 4 > span.Length) throw new ArgumentException("The merge request is truncated.");
            var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span[pos..]); pos += 4;
            if (length > span.Length - pos) throw new ArgumentException("The merge request is truncated.");
            frames.Add(span.Slice(pos, (int)length).ToArray()); pos += (int)length;
        }
        if (frames.Count == 0) throw new ArgumentException("Choose at least one PDF.");
        return frames;
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
