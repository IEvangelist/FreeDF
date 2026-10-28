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
var texts = new Dictionary<int, (byte[] Content, List<ContentInterpreter.TextSpan> Spans)>();

void Reset(PdfEditor e)
{
    editor = e; items.Clear(); texts.Clear(); overlayBuilder = new OverlayBuilder(editor, assets); Changed();
}
if (initial is not null) { Reset(PdfEditor.Open(File.ReadAllBytes(initial))); fileName = Path.GetFileName(initial); }

// Callers hold the lock. Pristine is the document with no overlays and no text edits; text spans are extracted from it so operator indices stay stable.
PdfDocument Pristine() => pristine ??= PdfDocument.Load(editor.Save(false, false));

(byte[] Content, List<ContentInterpreter.TextSpan> Spans) PageText(int i)
{
    var id = editor.PageId(i);
    if (!texts.TryGetValue(id, out var t))
    {
        var page = Pristine().Pages[i];
        texts[id] = t = (page.GetContents(), PdfRenderer.ExtractText(page));
    }
    return t;
}

// Pushes the current items to the editor: overlays for new objects, content rewrites to remove text that was converted into editable objects.
void Prepare()
{
    if (prepared) return;
    for (var i = 0; i < editor.PageCount; i++)
    {
        items.TryGetValue(editor.PageId(i), out var list);
        var origs = list?.Where(x => x.Orig is not null).Select(x => x.Orig!.Value).ToHashSet() ?? [];
        if (origs.Count > 0)
        {
            var (content, spans) = PageText(i);
            var removals = spans.Where(s => origs.Contains(s.Index)).ToDictionary(s => s.Index, s => s.Advance);
            editor.SetContentOverride(i, ContentRewriter.RemoveText(content, removals));
        }
        else editor.SetContentOverride(i, null);
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

static async Task<(string, byte[])> ReadUpload(HttpRequest r)
{
    var form = await r.ReadFormAsync();
    var f = form.Files.FirstOrDefault() ?? throw new BadHttpRequestException("no file");
    using var ms = new MemoryStream();
    await f.CopyToAsync(ms);
    return (f.FileName, ms.ToArray());
}
