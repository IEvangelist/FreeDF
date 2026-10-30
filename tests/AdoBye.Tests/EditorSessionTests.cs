using System.Text;
using System.Text.Json;
using PortableDocument.Document;
using PortableDocument.Render;
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

    static string Upload(EditorSession session)
    {
        var png = PngWriter.Encode(1, 1, [255, 0, 0, 255]);
        using var reply = JsonDocument.Parse(session.Handle("POST", "/api/asset", new Dictionary<string, string>(), png).Data);
        return reply.RootElement.GetProperty("id").GetString()!;
    }

    static Item Image(string id, string asset) =>
        new(id, "image", 0, 0, 10, 10, null, null, 0, false, false, null, null, null, 0, 1, asset, 0, 0);

    [Fact]
    public void ReplacingAndDeletingItemsReleasesUnreferencedAssetBytes()
    {
        var session = new EditorSession(new FontCatalog());
        Call(session, "POST", "/api/new");
        var first = Upload(session);
        Call(session, "PUT", "/api/page/0/items", new[] { Image("image", first) });
        var second = Upload(session);
        Call(session, "PUT", "/api/page/0/items", new[] { Image("image", second) });
        Assert.Throws<KeyNotFoundException>(() => Call(session, "GET", "/api/asset/" + first));
        Assert.NotEmpty(Call(session, "GET", "/api/asset/" + second).Data);
        Call(session, "PUT", "/api/page/0/items", Array.Empty<Item>());
        Assert.Throws<KeyNotFoundException>(() => Call(session, "GET", "/api/asset/" + second));
    }

    [Fact]
    public void SharedAssetSurvivesWhileAnotherItemOrPageReferencesIt()
    {
        var session = new EditorSession(new FontCatalog());
        Call(session, "POST", "/api/new");
        Call(session, "POST", "/api/blank/1");
        var asset = Upload(session);
        Call(session, "PUT", "/api/page/0/items", new[] { Image("a", asset), Image("b", asset) });
        Call(session, "PUT", "/api/page/1/items", new[] { Image("c", asset) });
        Call(session, "PUT", "/api/page/0/items", new[] { Image("b", asset) });
        Assert.NotEmpty(Call(session, "GET", "/api/asset/" + asset).Data);
        Call(session, "POST", "/api/delete/0");
        Assert.NotEmpty(Call(session, "GET", "/api/asset/" + asset).Data);
        using var items = JsonDocument.Parse(Call(session, "GET", "/api/page/0/items").Data);
        Assert.Equal("c", Assert.Single(items.RootElement.EnumerateArray()).GetProperty("id").GetString());
        Call(session, "POST", "/api/delete/0");
        Assert.Throws<KeyNotFoundException>(() => Call(session, "GET", "/api/asset/" + asset));
    }

    [Fact]
    public void RemovedPageItemsCannotRetainAssetsAfterImport()
    {
        var session = new EditorSession(new FontCatalog());
        Call(session, "POST", "/api/new");
        var asset = Upload(session);
        Call(session, "PUT", "/api/page/0/items", new[] { Image("removed", asset) });
        var original = Call(session, "GET", "/api/download").Data;
        Call(session, "POST", "/api/delete/0");
        session.Handle("POST", "/api/import", new Dictionary<string, string>(), original);
        using var items = JsonDocument.Parse(Call(session, "GET", "/api/page/0/items").Data);
        Assert.Empty(items.RootElement.EnumerateArray());
        Assert.Throws<KeyNotFoundException>(() => Call(session, "GET", "/api/asset/" + asset));
    }

    [Fact]
    public void UndoReferencesKeepDeletedImagesUntilHistoryIsReleased()
    {
        var session = new EditorSession(new FontCatalog());
        Call(session, "POST", "/api/new");
        var asset = Upload(session);
        var image = Image("undo", asset);
        Call(session, "PUT", "/api/page/0/items", new ItemUpdate([image], []));
        Call(session, "PUT", "/api/page/0/items", new ItemUpdate([], [asset]));
        Assert.NotEmpty(Call(session, "GET", "/api/asset/" + asset).Data);
        Call(session, "PUT", "/api/page/0/items", new ItemUpdate([image], []));
        Assert.NotEmpty(Call(session, "GET", "/api/asset/" + asset).Data);
        Call(session, "PUT", "/api/page/0/items", new ItemUpdate([], [asset]));
        Call(session, "POST", "/api/asset-history");
        Assert.Throws<KeyNotFoundException>(() => Call(session, "GET", "/api/asset/" + asset));
    }

    [Fact]
    public void UnclaimedUploadSurvivesConcurrentItemUpdatesUntilItIsAttached()
    {
        var session = new EditorSession(new FontCatalog());
        Call(session, "POST", "/api/new");
        var asset = Upload(session);
        Call(session, "PUT", "/api/page/0/items", new ItemUpdate([], []));
        Call(session, "POST", "/api/asset-history");
        Assert.NotEmpty(Call(session, "GET", "/api/asset/" + asset).Data);
        Call(session, "PUT", "/api/page/0/items", new ItemUpdate([Image("pending", asset)], []));
        Assert.NotEmpty(Call(session, "GET", "/api/asset/" + asset).Data);
        Call(session, "PUT", "/api/page/0/items", new ItemUpdate([], []));
        Assert.Throws<KeyNotFoundException>(() => Call(session, "GET", "/api/asset/" + asset));
    }
}
