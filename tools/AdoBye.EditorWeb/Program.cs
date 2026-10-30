// Usage: AdoBye.EditorWeb [input.pdf] [--port 5080]
var port = args.SkipWhile(a => a != "--port").Skip(1).FirstOrDefault() ?? "5080";
var initial = args.FirstOrDefault(a => a.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls($"http://localhost:{port}");
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

// Dev-time equivalent of the Pages `samples/` folder: serves the repo's samples/ (01-26 committed, 27 generated) plus a computed manifest.
var samplesDir = SamplesDirectory();
if (samplesDir is not null)
{
    string? manifestJson = null; DateTime manifestStamp = default;
    app.MapGet("/samples/manifest.json", () =>
    {
        var stamp = Directory.GetFiles(samplesDir, "*.pdf").Select(File.GetLastWriteTimeUtc).DefaultIfEmpty().Max();
        if (manifestJson is null || stamp != manifestStamp)
        {
            manifestJson = AdoBye.Samples.SampleManifest.ToJson(AdoBye.Samples.SampleManifest.Entries(samplesDir));
            manifestStamp = stamp;
        }
        return Results.Text(manifestJson, "application/json");
    });
    app.MapGet("/samples/{file}", (string file) =>
        System.Text.RegularExpressions.Regex.IsMatch(file, @"^\d\d-[a-z0-9-]+\.pdf$") && File.Exists(Path.Combine(samplesDir, file))
            ? Results.File(Path.Combine(samplesDir, file), "application/pdf")
            : Results.NotFound());
}

var gate = new object();
var session = new EditorSession(FontCatalog.Scan());
if (initial is not null) session.OpenFile(initial);
app.MapMethods("/api/{**path}", ["GET", "POST", "PUT"], async (HttpRequest request) =>
{
    byte[]? body = null;
    string? uploadName = null;
    if (request.HasFormContentType && request.Path.Value is "/api/open" or "/api/import" or "/api/import-batch" or "/api/inspect" or "/api/asset")
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
    catch (EditorConflictException e)
    {
        app.Logger.LogWarning(e, "Editor command conflicted: {Path}", request.Path);
        return Results.Text(e.Message, statusCode: StatusCodes.Status409Conflict);
    }
    catch (Exception e) when (e is ArgumentException or InvalidDataException or NotSupportedException or PortableDocument.Core.PdfParseException or System.Security.Cryptography.CryptographicException)
    {
        app.Logger.LogWarning(e, "Editor command failed: {Path}", request.Path);
        return Results.BadRequest(e.Message);
    }
});
Console.WriteLine($"AdoBye editor running at http://localhost:{port}");
app.Run();

static string? SamplesDirectory()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "samples", "01-spec-showcase.pdf"))) return Path.Combine(dir.FullName, "samples");
    return null;
}
