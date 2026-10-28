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
PdfDocument? snapshot = null; byte[]? snapshotBytes = null;
string fileName = "document.pdf";

if (initial is not null) { editor = PdfEditor.Open(File.ReadAllBytes(initial)); fileName = Path.GetFileName(initial); }

// Callers hold the lock. The snapshot is the saved-and-reloaded document used for rendering and download.
PdfDocument Snap()
{
    if (snapshot is null) { snapshotBytes = editor.Save(); snapshot = PdfDocument.Load(snapshotBytes); }
    return snapshot;
}
void Changed() { version++; snapshot = null; snapshotBytes = null; }

app.MapGet("/api/state", () =>
{
    lock (gate)
    {
        var doc = editor.PageCount == 0 ? null : Snap();
        var pages = doc?.Pages.Select((p, i) => new { index = i, rotation = p.Rotate, width = p.MediaBox.Width, height = p.MediaBox.Height }) ?? [];
        return Results.Json(new { version, fileName, pageCount = editor.PageCount, pages });
    }
});

app.MapGet("/api/page/{i:int}.png", (int i, int? dpi, int? v) =>
{
    lock (gate)
    {
        if (editor.PageCount == 0 || i < 0 || i >= editor.PageCount) return Results.NotFound();
        var page = Snap().Pages[i];
        return Results.File(PdfRenderer.RenderPage(page, Math.Clamp(dpi ?? 48, 16, 300)).ToPng(), "image/png");
    }
});

app.MapPost("/api/open", async (HttpRequest r) =>
{
    var (name, bytes) = await ReadUpload(r);
    lock (gate) { editor = PdfEditor.Open(bytes); fileName = name; Changed(); }
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
