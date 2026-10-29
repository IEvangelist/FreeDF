using AdoBye.Content;
using AdoBye.Core;
using AdoBye.Document;
using AdoBye.Render;

// Usage: AdoBye.EditorWeb [input.pdf] [--port 5080]
var port = args.SkipWhile(a => a != "--port").Skip(1).FirstOrDefault() ?? "5080";
var initial = args.FirstOrDefault(a => a.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));

var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls($"http://localhost:{port}");
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

var gate = new object();
var editor = PdfEditor.Create();
var version = 0;
PdfDocument? snapshot = null, baseSnapshot = null; byte[]? snapshotBytes = null;
string fileName = "document.pdf";
var items = new Dictionary<int, List<Item>>();
var assets = new Dictionary<string, Asset>();
var catalog = FontCatalog.Scan();
var overlayBuilder = new OverlayBuilder(editor, assets, catalog);
var jsonOpts = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);

PdfDocument? pristine = null; bool prepared = false;
var texts = new Dictionary<int, (byte[] Content, List<ContentInterpreter.TextSpan> Spans, List<ContentInterpreter.PageObject> Objects)>();
var objEdits = new Dictionary<int, Dictionary<int, ObjEdit>>();

string? savePath = null; int savedVersion = 0;
void MarkSaved() => savedVersion = version;
void Reset(PdfEditor e)
{
    editor = e; items.Clear(); texts.Clear(); objEdits.Clear(); overlayBuilder = new OverlayBuilder(editor, assets, catalog); Changed(); savePath = null; MarkSaved();
}
if (initial is not null) { Reset(PdfEditor.Open(File.ReadAllBytes(initial))); fileName = Path.GetFileName(initial); savePath = Path.GetFullPath(initial); MarkSaved(); }

// Callers hold the lock. Pristine is the document with no overlays and no text edits; text spans are extracted from it so operator indices stay stable.
// Its object numbers differ from the editor's (a full save renumbers), so PristineBack maps them back.
Dictionary<int, int> pristineBack = [];
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

const long FormSpan = 10_000_000;
static long SpanKey(ContentInterpreter.TextSpan s) => s.Form == 0 ? s.Index : s.Form * FormSpan + s.Index;

// Spans of form XObjects can be edited only when each (form, index) is seen once on the page, because editing rewrites the shared form stream.
IEnumerable<ContentInterpreter.TextSpan> EditableSpans(List<ContentInterpreter.TextSpan> spans)
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
        var (spans, objects) = PdfRenderer.Analyze(page);
        texts[id] = t = (page.GetContents(), spans, objects);
    }
    return t;
}

static double[]? Rgb(string? hex) => hex is { Length: 7 } && hex[0] == '#'
    ? [Convert.ToInt32(hex[1..3], 16) / 255.0, Convert.ToInt32(hex[3..5], 16) / 255.0, Convert.ToInt32(hex[5..7], 16) / 255.0] : null;

// Pushes the current items to the editor: overlays for new objects, content rewrites to remove text that was converted into editable objects.
void Prepare()
{
    if (prepared) return;
    editor.ClearStreamOverrides(); editor.ClearPageXObjects();
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
                    double[]? m = null;
                    if (e.Sx != 1 || e.Sy != 1 || e.Tx != 0 || e.Ty != 0)
                    {
                        var c = o.Ctm; var mm = c.Then(new Matrix(e.Sx, 0, 0, e.Sy, e.Tx, e.Ty)).Then(c.Invert());
                        m = [mm.A, mm.B, mm.C, mm.D, mm.E, mm.F];
                    }
                    return new ContentRewriter.ObjectEdit(o.Start, o.End, e.Delete, m, Rgb(e.Fill), Rgb(e.Stroke));
                }));
            var removals = spans.Where(s => s.Form == 0 && origs.Contains(s.Index)).ToDictionary(s => s.Index, s => s.Advance);
            editor.SetContentOverride(i, ContentRewriter.RemoveText(content, removals));
            foreach (var g in EditableSpans(spans).Where(s => s.Form != 0 && origs.Contains(SpanKey(s))).GroupBy(s => s.Form))
            {
                if (Pristine().File.GetObject(g.Key) is not PdfStream fs || !pristineBack.TryGetValue(g.Key, out var formNum)) continue;
                var fcontent = Pristine().File.DecodeStream(fs);
                editor.SetStreamOverride(formNum, ContentRewriter.RemoveText(fcontent, g.ToDictionary(s => s.Index, s => s.Advance)));
            }
        }
        else editor.SetContentOverride(i, null);
        if (redacts.Count > 0)
        {
            // Redact the page as edited so far; overlay items that touch a redaction are dropped too.
            Redactor.Apply(editor, i, redacts);
            list = list?.Where(x => !redacts.Any(r => x.X < r.X + r.W && x.X + Math.Max(x.W, x.Type == "text" ? x.Size * Math.Max(x.Text.Length, 1) * 0.5 : 0) > r.X && x.Y < r.Y + r.H && x.Y + Math.Max(x.H, x.Type == "text" ? x.Size : 0) > r.Y)).ToList();
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

// Snap includes the editable overlays; BaseSnap is the page without them (what the editing canvas draws under the HTML objects).
PdfDocument Snap()
{
    if (snapshot is null) { Prepare(); snapshotBytes = editor.Save(); snapshot = PdfDocument.Load(snapshotBytes); }
    return snapshot;
}
PdfDocument BaseSnap() { Prepare(); return baseSnapshot ??= PdfDocument.Load(editor.Save(false)); }
void Changed() { version++; snapshot = null; baseSnapshot = null; snapshotBytes = null; pristine = null; texts.Clear(); prepared = false; }

app.MapGet("/api/page/{i:int}/text", (int i) =>
{
    lock (gate)
    {
        var spans = EditableSpans(PageText(i).Spans)
            .Where(s => !s.Vertical && Math.Abs(s.Angle) < 1 && s.Size > 1 && !string.IsNullOrWhiteSpace(s.Text))
            .Select(s => new
            {
                index = SpanKey(s), text = s.Text, x = s.X, y = s.Y, width = s.Width, size = s.Size, font = s.FontName,
                color = $"#{(int)Math.Round(s.Color.R * 255):x2}{(int)Math.Round(s.Color.G * 255):x2}{(int)Math.Round(s.Color.B * 255):x2}",
            });
        return Results.Json(spans, jsonOpts);
    }
});

app.MapGet("/api/page/{i:int}/objects", (int i) =>
{
    lock (gate)
    {
        var id = editor.PageId(i);
        objEdits.TryGetValue(id, out var oe);
        var list = PageText(i).Objects.Where(o => o.Form == 0).Select(o => new
        {
            index = o.Index, kind = o.Kind, x0 = o.X0, y0 = o.Y0, x1 = o.X1, y1 = o.Y1,
            hasFill = o.HasFill, hasStroke = o.HasStroke, lineWidth = o.LineWidth,
            fill = o.HasFill ? Hex(o.Fill) : null, stroke = o.HasStroke ? Hex(o.Stroke) : null,
            edit = oe is not null && oe.TryGetValue(o.Index, out var e) ? e : null,
        });
        return Results.Json(list, jsonOpts);
    }
});

app.MapPut("/api/page/{i:int}/objects/{n:int}", async (int i, int n, HttpRequest r) =>
{
    var e = await System.Text.Json.JsonSerializer.DeserializeAsync<ObjEdit>(r.Body, jsonOpts);
    lock (gate)
    {
        var id = editor.PageId(i);
        if (n < 0 || n >= PageText(i).Objects.Count || PageText(i).Objects[n].Form != 0) return Results.NotFound();
        if (!objEdits.TryGetValue(id, out var d)) objEdits[id] = d = [];
        if (e is null || (e.Sx == 1 && e.Sy == 1 && e.Tx == 0 && e.Ty == 0 && !e.Delete && e.Fill is null && e.Stroke is null)) d.Remove(n); else d[n] = e;
        Changed();
        return Results.Ok();
    }
});

app.MapGet("/api/state", () =>
{
    lock (gate)
    {
        var doc = editor.PageCount == 0 ? null : BaseSnap();
        var pages = doc?.Pages.Select((p, i) => new { index = i, id = editor.PageId(i), rotation = p.Rotate, x0 = p.CropBox.Left, top = p.CropBox.Top, width = p.CropBox.Width, height = p.CropBox.Height }) ?? [];
        return Results.Json(new { version, fileName, pageCount = editor.PageCount, pages, dirty = version != savedVersion, canSave = savePath is not null });
    }
});

app.MapGet("/api/page/{i:int}.png", (int i, int? dpi, int? v, bool? bare) =>
{
    lock (gate)
    {
        if (editor.PageCount == 0 || i < 0 || i >= editor.PageCount) return Results.NotFound();
        var page = (bare == true ? BaseSnap() : Snap()).Pages[i];
        return Results.File(PdfRenderer.RenderPage(page, Math.Clamp(dpi ?? 48, 16, 300)).ToPng(), "image/png");
    }
});

app.MapGet("/api/fonts", () => Results.Json(catalog.List(), jsonOpts));

app.MapGet("/api/page/{i:int}/items", (int i) =>
{
    lock (gate) return Results.Json(items.TryGetValue(editor.PageId(i), out var l) ? l : [], jsonOpts);
});

app.MapPut("/api/page/{i:int}/items", async (int i, HttpRequest r) =>
{
    var list = await System.Text.Json.JsonSerializer.DeserializeAsync<List<Item>>(r.Body, jsonOpts) ?? [];
    lock (gate) { items[editor.PageId(i)] = list; Changed(); }
    return Results.Ok();
});

app.MapPost("/api/asset", async (HttpRequest r) =>
{
    var (_, bytes) = await ReadUpload(r);
    var mime = bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xD8 ? "image/jpeg" : "image/png";
    var id = Guid.NewGuid().ToString("N");
    int w, h;
    if (mime == "image/jpeg") { if (AdoBye.Content.JpegDecoder.Decode(bytes, out w, out h, out _) is null) return Results.BadRequest(); }
    else { try { (w, h, _) = PngWriter.Decode(bytes); } catch (Exception) { return Results.BadRequest(); } }
    lock (gate) assets[id] = new Asset(bytes, mime);
    return Results.Json(new { id, w, h });
});

app.MapGet("/api/asset/{id}", (string id) =>
{
    lock (gate) return assets.TryGetValue(id, out var a) ? Results.File(a.Data, a.Mime) : Results.NotFound();
});

app.MapPost("/api/open", async (HttpRequest r) =>
{
    var (name, bytes) = await ReadUpload(r);
    lock (gate) { Reset(PdfEditor.Open(bytes)); fileName = name; }
    return Results.Ok();
});

app.MapPost("/api/import", async (HttpRequest r) =>
{
    var (_, bytes) = await ReadUpload(r);
    lock (gate) { editor.ImportPages(PdfDocument.Load(bytes)); Changed(); }
    return Results.Ok();
});

app.MapPost("/api/delete/{i:int}", (int i) => { lock (gate) { editor.DeletePage(i); Changed(); } return Results.Ok(); });
app.MapPost("/api/move/{from:int}/{to:int}", (int from, int to) => { lock (gate) { editor.MovePage(from, to); Changed(); } return Results.Ok(); });
app.MapPost("/api/rotate/{i:int}/{delta:int}", (int i, int delta) =>
{
    lock (gate)
    {
        var cur = Snap().Pages[i].Rotate;
        editor.SetRotation(i, ((cur + delta) % 360 + 360) % 360);
        Changed();
    }
    return Results.Ok();
});
app.MapPost("/api/title", (HttpRequest r, string t) => { lock (gate) { editor.SetInfo("Title", t); Changed(); } return Results.Ok(); });

app.MapGet("/api/fields", () =>
{
    lock (gate)
    {
        if (editor.PageCount == 0) return Results.Json(Array.Empty<object>(), jsonOpts);
        return Results.Json(Pristine().GetFormFields()
            .Where(f => f.FieldType is "Tx" or "Btn" or "Ch")
            .Select(f => new { name = f.FullName, type = f.FieldType, value = f.Value ?? "" }), jsonOpts);
    }
});
app.MapPost("/api/field", (string n, string v) => { lock (gate) { var ok = editor.SetFieldValue(n, v); if (ok) Changed(); return ok ? Results.Ok() : Results.NotFound(); } });

app.MapGet("/api/download", () =>
{
    lock (gate)
    {
        if (editor.PageCount == 0) return Results.NotFound();
        Snap();
        return Results.File(snapshotBytes!, "application/pdf", "edited-" + fileName);
    }
});

List<(int Page, ContentInterpreter.TextSpan S)>? findCache = null; int findVersion = -1;
app.MapGet("/api/find", (string q, bool? cs) =>
{
    lock (gate)
    {
        if (editor.PageCount == 0 || string.IsNullOrEmpty(q)) return Results.Json(Array.Empty<object>(), jsonOpts);
        if (findCache is null || findVersion != version)
        {
            var doc = Snap(); findCache = [];
            for (var i = 0; i < editor.PageCount; i++)
                foreach (var s in PdfRenderer.ExtractText(doc.Pages[i])) if (!s.Vertical && !string.IsNullOrEmpty(s.Text)) findCache.Add((i, s));
            findVersion = version;
        }
        var cmp = cs == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var hits = new List<object>();
        foreach (var (page, s) in findCache)
        {
            for (var at = s.Text.IndexOf(q, cmp); at >= 0 && hits.Count < 2000; at = s.Text.IndexOf(q, at + q.Length, cmp))
            {
                var len = Math.Max(s.Text.Length, 1);
                hits.Add(new { page, x = s.X + s.Width * at / len, y = s.Y, w = s.Width * q.Length / len, size = s.Size, angle = s.Angle });
            }
        }
        return Results.Json(hits, jsonOpts);
    }
});

app.MapPost("/api/close", () => { lock (gate) { Reset(PdfEditor.Create()); fileName = "document.pdf"; } return Results.Ok(); });

app.MapPost("/api/new", (double? w, double? h) =>
{
    lock (gate) { Reset(PdfEditor.Open(Blank(w ?? 595.276, h ?? 841.89))); fileName = "Untitled.pdf"; savePath = null; MarkSaved(); }
    return Results.Ok();
});
app.MapPost("/api/blank/{at:int}", (int at, double? w, double? h) =>
{
    lock (gate)
    {
        if (editor.PageCount > 0 && w is null) { var p = Snap().Pages[Math.Clamp(at - 1, 0, editor.PageCount - 1)]; w = p.CropBox.Width; h = p.CropBox.Height; }
        editor.ImportPages(PdfDocument.Load(Blank(w ?? 595.276, h ?? 841.89)), null, Math.Clamp(at, 0, editor.PageCount));
        Changed();
    }
    return Results.Ok();
});
app.MapPost("/api/duplicate/{i:int}", (int i) =>
{
    lock (gate) { editor.ImportPages(Snap(), [i], i + 1); Changed(); }
    return Results.Ok();
});

string[] infoKeys = ["Title", "Author", "Subject", "Keywords", "Creator", "Producer"];
app.MapGet("/api/info", () =>
{
    lock (gate)
    {
        if (editor.PageCount == 0) return Results.Json(new { }, jsonOpts);
        var d = BaseSnap();
        var o = infoKeys.ToDictionary(k => k.ToLowerInvariant(), k => d.Info(k) ?? "");
        o["pages"] = editor.PageCount.ToString(); o["file"] = fileName;
        return Results.Json(o, jsonOpts);
    }
});
app.MapPost("/api/info", async (HttpRequest r) =>
{
    var d = await System.Text.Json.JsonSerializer.DeserializeAsync<Dictionary<string, string>>(r.Body, jsonOpts) ?? [];
    lock (gate) { foreach (var k in infoKeys) if (d.TryGetValue(k.ToLowerInvariant(), out var v)) editor.SetInfo(k, v ?? ""); Changed(); }
    return Results.Ok();
});

// Writes the current document back to the file it was opened from; falls back to a download when there is none.
app.MapPost("/api/save", () =>
{
    lock (gate)
    {
        if (editor.PageCount == 0) return Results.NotFound();
        if (savePath is null) return Results.Json(new { saved = false }, jsonOpts);
        Snap(); File.WriteAllBytes(savePath, snapshotBytes!); MarkSaved();
        return Results.Json(new { saved = true, path = savePath }, jsonOpts);
    }
});
app.MapPost("/api/saved", (string? name) => { lock (gate) { MarkSaved(); if (!string.IsNullOrWhiteSpace(name)) fileName = name; } return Results.Ok(); });

// Compressed (object/xref streams) export, optionally AES-256 protected.
app.MapGet("/api/export/secure", (string? user, string? owner, string? name, string? method) =>
{
    lock (gate)
    {
        if (editor.PageCount == 0) return Results.NotFound();
        Prepare();
        var enc = string.IsNullOrEmpty(user) && string.IsNullOrEmpty(owner) ? null
            : new PdfEncryption { UserPassword = user ?? "", OwnerPassword = string.IsNullOrEmpty(owner) ? (user ?? "") : owner,
                Method = (method?.ToLowerInvariant()) switch { "aes128" => PdfEncryptionMethod.Aes128, "rc4128" => PdfEncryptionMethod.Rc4_128, "rc440" => PdfEncryptionMethod.Rc4_40, _ => PdfEncryptionMethod.Aes256 } };
        var bytes = editor.Save(options: new PdfWriteOptions { Compress = true, ObjectStreams = true, Encryption = enc });
        return Results.File(bytes, "application/pdf", name ?? (enc is null ? "compressed-" : "protected-") + fileName);
    }
});

app.MapGet("/api/export/pdf", (string? pages, string? name) =>
{
    lock (gate)
    {
        if (editor.PageCount == 0) return Results.NotFound();
        var doc = Snap();
        var idx = ParseRange(pages, editor.PageCount);
        if (idx.Count == 0) return Results.BadRequest("No pages in range");
        var bytes = idx.Count == editor.PageCount && idx.SequenceEqual(Enumerable.Range(0, idx.Count)) ? snapshotBytes! : PdfEditor.Extract(doc, idx).Save();
        return Results.File(bytes, "application/pdf", name ?? "export-" + fileName);
    }
});
app.MapGet("/api/export/png", (int page, int? dpi) =>
{
    lock (gate)
    {
        if (page < 0 || page >= editor.PageCount) return Results.NotFound();
        return Results.File(PdfRenderer.RenderPage(Snap().Pages[page], Math.Clamp(dpi ?? 150, 16, 600)).ToPng(), "image/png", $"{Path.GetFileNameWithoutExtension(fileName)}-{page + 1}.png");
    }
});
app.MapGet("/api/export/zip", (int? dpi) =>
{
    lock (gate)
    {
        if (editor.PageCount == 0) return Results.NotFound();
        var doc = Snap(); var ms = new MemoryStream();
        using (var z = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
            for (var i = 0; i < editor.PageCount; i++)
            {
                using var s = z.CreateEntry($"page-{i + 1:000}.png", System.IO.Compression.CompressionLevel.NoCompression).Open();
                s.Write(PdfRenderer.RenderPage(doc.Pages[i], Math.Clamp(dpi ?? 150, 16, 600)).ToPng());
            }
        return Results.File(ms.ToArray(), "application/zip", $"{Path.GetFileNameWithoutExtension(fileName)}-pages.zip");
    }
});
app.MapGet("/api/export/text", () =>
{
    lock (gate)
    {
        if (editor.PageCount == 0) return Results.NotFound();
        var doc = Snap(); var sb = new System.Text.StringBuilder();
        for (var i = 0; i < editor.PageCount; i++)
        {
            if (i > 0) sb.Append('\f');
            double lastY = double.NaN, lastEnd = 0;
            foreach (var s in PdfRenderer.ExtractText(doc.Pages[i]).OrderBy(s => Math.Round(s.Y / 3)).ThenBy(s => s.X))
            {
                if (!double.IsNaN(lastY) && Math.Abs(s.Y - lastY) > 3) { sb.AppendLine(); lastEnd = 0; }
                else if (!double.IsNaN(lastY) && s.X - lastEnd > s.Size * 0.15) sb.Append(' ');
                sb.Append(s.Text); lastY = s.Y; lastEnd = s.X + s.Width;
            }
            sb.AppendLine();
        }
        return Results.File(new System.Text.UTF8Encoding(false).GetBytes(sb.ToString()), "text/plain; charset=utf-8", Path.GetFileNameWithoutExtension(fileName) + ".txt");
    }
});

Console.WriteLine($"AdoBye editor running at http://localhost:{port}");
app.Run();

static byte[] Blank(double w, double h)
{
    var inv = System.Globalization.CultureInfo.InvariantCulture;
    string[] objs =
    [
        "<</Type/Catalog/Pages 2 0 R>>", "<</Type/Pages/Kids[3 0 R]/Count 1>>",
        string.Create(inv, $"<</Type/Page/Parent 2 0 R/MediaBox[0 0 {w:0.###} {h:0.###}]/Resources<<>>>>"),
    ];
    var sb = new System.Text.StringBuilder("%PDF-2.0\n"); var offs = new List<int>();
    for (var i = 0; i < objs.Length; i++) { offs.Add(sb.Length); sb.Append($"{i + 1} 0 obj\n{objs[i]}\nendobj\n"); }
    var xref = sb.Length;
    sb.Append($"xref\n0 {objs.Length + 1}\n0000000000 65535 f \n");
    foreach (var o in offs) sb.Append($"{o:0000000000} 00000 n \n");
    sb.Append($"trailer\n<</Size {objs.Length + 1}/Root 1 0 R>>\nstartxref\n{xref}\n%%EOF\n");
    return System.Text.Encoding.ASCII.GetBytes(sb.ToString());
}

// "1-3,5" -> zero-based indices; blank means all pages.
static List<int> ParseRange(string? s, int count)
{
    if (string.IsNullOrWhiteSpace(s)) return Enumerable.Range(0, count).ToList();
    var r = new List<int>();
    foreach (var part in s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var ab = part.Split('-', 2, StringSplitOptions.TrimEntries);
        if (!int.TryParse(ab[0], out var a)) continue;
        var b = ab.Length == 2 ? (int.TryParse(ab[1], out var bb) ? bb : count) : a;
        for (var p = Math.Min(a, b); p <= Math.Max(a, b); p++) if (p >= 1 && p <= count) r.Add(p - 1);
    }
    return r;
}

static string Hex(RgbColor c) => $"#{(int)Math.Round(c.R * 255):x2}{(int)Math.Round(c.G * 255):x2}{(int)Math.Round(c.B * 255):x2}";

static async Task<(string, byte[])> ReadUpload(HttpRequest r)
{
    var form = await r.ReadFormAsync();
    var f = form.Files.FirstOrDefault() ?? throw new BadHttpRequestException("no file");
    using var ms = new MemoryStream();
    await f.CopyToAsync(ms);
    return (f.FileName, ms.ToArray());
}

/// <summary>Edit of an existing page object: box-space scale/translate, colour overrides, or deletion.</summary>
record ObjEdit(bool Delete = false, double Sx = 1, double Sy = 1, double Tx = 0, double Ty = 0, string? Fill = null, string? Stroke = null);
