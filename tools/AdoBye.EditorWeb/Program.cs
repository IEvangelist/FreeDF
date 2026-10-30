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
var session = new EditorSession(FontCatalog.Scan());
if (initial is not null) session.OpenFile(initial);
app.MapMethods("/api/{**path}", ["GET", "POST", "PUT"], async (HttpRequest request) =>
{
    byte[]? body = null;
    string? uploadName = null;
    if (request.HasFormContentType && request.Path.Value is "/api/open" or "/api/import" or "/api/asset")
    {
        var form = await request.ReadFormAsync();
        var file = form.Files.FirstOrDefault() ?? throw new BadHttpRequestException("Choose a file.");
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        body = ms.ToArray(); uploadName = file.FileName;
    }
    else if (request.Method is "POST" or "PUT")
    {
        using var ms = new MemoryStream();
        await request.Body.CopyToAsync(ms); body = ms.ToArray();
    }
    try
    {
        lock (gate)
        {
            var reply = session.Handle(request.Method, request.Path.Value!, request.Query.ToDictionary(k => k.Key, k => k.Value.ToString()), body, uploadName);
            return Results.File(reply.Data, reply.ContentType, reply.FileName);
        }
    }
    catch (Exception e) when (e is ArgumentException or InvalidDataException or NotSupportedException or PortableDocument.Core.PdfParseException)
    {
        app.Logger.LogWarning(e, "Editor command failed: {Path}", request.Path);
        return Results.BadRequest(e.Message);
    }
});
Console.WriteLine($"AdoBye editor running at http://localhost:{port}");
app.Run();
