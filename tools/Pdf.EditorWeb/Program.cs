using Pdf.Content;
using Pdf.Document;
using Pdf.Render;

// Usage: Pdf.EditorWeb [input.pdf] [--port 5080]
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
var overlayBuilder = new OverlayBuilder(editor, assets);
var jsonOpts = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);

PdfDocument? pristine = null; bool prepared = false;
var texts = new Dictionary<int, (byte[] Content, List<ContentInterpreter.TextSpan> Spans, List<ContentInterpreter.PageObject> Objects)>();
var objEdits = new Dictionary<int, Dictionary<int, ObjEdit>>();

void Reset(PdfEditor e)
{
    editor = e; items.Clear(); texts.Clear(); objEdits.Clear(); overlayBuilder = new OverlayBuilder(editor, assets); Changed();
}
if (initial is not null) { Reset(PdfEditor.Open(File.ReadAllBytes(initial))); fileName = Path.GetFileName(initial); }

// Callers hold the lock. Pristine is the document with no overlays and no text edits; text spans are extracted from it so operator indices stay stable.
PdfDocument Pristine() => pristine ??= PdfDocument.Load(editor.Save(false, false));

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
            var removals = spans.Where(s => origs.Contains(s.Index)).ToDictionary(s => s.Index, s => s.Advance);
            editor.SetContentOverride(i, ContentRewriter.RemoveText(content, removals));
        }
        else editor.SetContentOverride(i, null);
        if (redacts.Count > 0)
        {
            // Redact the page as edited so far; overlay items that touch a redaction are dropped too.
            var edited = PdfDocument.Load(editor.Save(false)).Pages[i];
            editor.SetContentOverride(i, Redactor.Redact(edited, redacts));
            list = list?.Where(x => !redacts.Any(r => x.X < r.X + r.W && x.X + Math.Max(x.W, x.Type == "text" ? x.Size * Math.Max(x.Text.Length, 1) * 0.5 : 0) > r.X && x.Y < r.Y + r.H && x.Y + Math.Max(x.H, x.Type == "text" ? x.Size : 0) > r.Y)).ToList();
        }
        if (list is { Count: > 0 })
        {
            var (content, res) = overlayBuilder.Build(list, Pristine().Pages[i].CropBox);
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
void Changed() { version++; snapshot = null; baseSnapshot = null; snapshotBytes = null; pristine = null; prepared = false; }

app.MapGet("/api/page/{i:int}/text", (int i) =>
{
    lock (gate)
    {
        var spans = PageText(i).Spans
            .Where(s => !s.Vertical && Math.Abs(s.Angle) < 1 && s.Size > 1 && !string.IsNullOrWhiteSpace(s.Text))
            .Select(s => new
            {
                index = s.Index, text = s.Text, x = s.X, y = s.Y, width = s.Width, size = s.Size, font = s.FontName,
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
        var list = PageText(i).Objects.Select(o => new
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
        if (n < 0 || n >= PageText(i).Objects.Count) return Results.NotFound();
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
        return Results.Json(new { version, fileName, pageCount = editor.PageCount, pages });
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
    if (mime == "image/jpeg") { if (Pdf.Content.JpegDecoder.Decode(bytes, out w, out h, out _) is null) return Results.BadRequest(); }
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

Console.WriteLine($"PDF editor running at http://localhost:{port}");
app.Run();

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
