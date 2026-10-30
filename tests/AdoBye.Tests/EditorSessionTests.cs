using System.Text;
using System.Text.Json;
using PortableDocument.Document;
using Pdf = PortableDocument.Render.PortableDocument;

public class EditorSessionTests
{
    static EditorSession.Reply Call(EditorSession session, string method, string path, object? body = null, params (string, string)[] query)
        => session.Handle(method, path, query.ToDictionary(x => x.Item1, x => x.Item2),
            body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    [Fact]
    public void LocalEditSaveRoundTripAndPageOperations()
    {
        var session = new EditorSession(new FontCatalog());
        Call(session, "POST", "/api/new");
        var item = new Item("text", "text", 40, 50, 0, 0, "Local WASM round trip", "Helvetica", 20, false, false, "#000000", "", "", 0, 1, "", 0, 0);
        Call(session, "PUT", "/api/page/0/items", new[] { item });
        Call(session, "POST", "/api/title", query: [("t", "Browser document")]);
        var png = Call(session, "GET", "/api/page/0.png", query: [("dpi", "96")]);
        Assert.Equal("image/png", png.ContentType);
        Assert.Equal(new byte[] { 137, 80, 78, 71 }, png.Data[..4]);
        var reply = Call(session, "GET", "/api/export/pdf");
        var pdf = PdfDocument.Load(reply.Data);
        Assert.Equal("Browser document", pdf.Info("Title"));
        Assert.Contains("Local WASM round trip", string.Join("", Pdf.ExtractText(pdf.Pages[0]).Select(s => s.Text)));
        Call(session, "POST", "/api/duplicate/0");
        Call(session, "POST", "/api/rotate/1/90");
        Call(session, "POST", "/api/move/1/0");
        pdf = PdfDocument.Load(Call(session, "GET", "/api/download").Data);
        Assert.Equal(2, pdf.Pages.Count);
        Assert.Equal(90, pdf.Pages[0].Rotate);
        Call(session, "POST", "/api/delete/0");
        Assert.Single(PdfDocument.Load(Call(session, "GET", "/api/download").Data).Pages);
        var compressed = Call(session, "GET", "/api/export/secure");
        Assert.Single(PdfDocument.Load(compressed.Data).Pages);
        Assert.Contains("Local WASM round trip", Encoding.UTF8.GetString(Call(session, "GET", "/api/export/text").Data));
        using var hits = JsonDocument.Parse(Call(session, "GET", "/api/find", query: [("q", "round")]).Data);
        Assert.NotEmpty(hits.RootElement.EnumerateArray());
    }

    [Fact]
    public void FailedOpenPreservesDocumentAndSessionsAreIsolated()
    {
        var a = new EditorSession(new FontCatalog());
        var b = new EditorSession(new FontCatalog());
        Call(a, "POST", "/api/new");
        Assert.ThrowsAny<Exception>(() => a.Handle("POST", "/api/open", new Dictionary<string, string>(), [1, 2, 3], "bad.pdf"));
        Assert.Single(PdfDocument.Load(Call(a, "GET", "/api/download").Data).Pages);
        Assert.Throws<ArgumentException>(() => Call(b, "GET", "/api/download"));
        using var state = JsonDocument.Parse(Call(a, "GET", "/api/state").Data);
        Assert.False(state.RootElement.GetProperty("canSave").GetBoolean());
        Assert.Throws<ArgumentException>(() => Call(a, "GET", "/api/export/pdf", query: [("pages", "999")]));
    }
}
