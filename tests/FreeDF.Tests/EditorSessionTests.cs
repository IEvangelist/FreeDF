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

    [Theory]
    [InlineData("1 0 0 1 0 0", "100", "0", "0")]
    [InlineData("1.2 0 0.18 0.85 12 20", "73", "0.4", "1.5")]
    public void OriginalTextReplacementRetainsTransformSpacingAndPdfPreview(string matrix, string scale, string characterSpace, string wordSpace)
    {
        var source = FreeDF.Tests.TestPdf.Build($"q {matrix} cm BT /F1 18 Tf {scale} Tz {characterSpace} Tc {wordSpace} Tw 30 90 Td (Original text) Tj ET Q",
            "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>");
        var session = new EditorSession(new FontCatalog());
        session.Handle("POST", "/api/open", new Dictionary<string, string>(), source, "transformed.pdf");
        var before = Pdf.ExtractText(PdfDocument.Load(source).Pages[0]).Single();
        using var response = JsonDocument.Parse(Call(session, "GET", "/api/page/0/text").Data);
        var text = response.RootElement.EnumerateArray().Single();
        var item = new Item("original", "text", text.GetProperty("x").GetDouble(), text.GetProperty("y").GetDouble() - .95 * before.Size,
            before.Width, 0, before.Text, "Helvetica", before.Size, false, false, "#000000", "", "", 0, 1, null, 0, 0,
            before.Index, Spacing: text.GetProperty("spacing").GetDouble(), OStyle: "Helvetica|false|false");
        Call(session, "PUT", "/api/page/0/items", new[] { item });
        var replacement = PdfDocument.Load(Call(session, "GET", "/api/export/pdf").Data);
        var after = Pdf.ExtractText(replacement.Pages[0]).Single(s => !string.IsNullOrWhiteSpace(s.Text));
        Assert.Equal(before.FontName, after.FontName);
        Assert.Equal(before.Text, after.Text);
        Assert.InRange(Math.Abs(before.X - after.X), 0, .02);
        Assert.InRange(Math.Abs(before.Y - after.Y), 0, .02);
        Assert.True(Math.Abs(before.Width - after.Width) < .04,
            $"Width {before.Width} -> {after.Width}; matrices {before.RenderingMatrix} -> {after.RenderingMatrix}; spacing {before.CharacterSpacing}/{before.WordSpacing} -> {after.CharacterSpacing}/{after.WordSpacing}");
        Assert.InRange(Math.Abs(before.Size - after.Size), 0, .02);
        Assert.InRange(Math.Abs(before.RenderingMatrix.C - after.RenderingMatrix.C), 0, .02);
        Assert.Equal(Pdf.RenderPage(replacement.Pages[0], 72).ToPng(),
            Call(session, "GET", "/api/page/0.png", query: [("dpi", "72"), ("bare", "true")]).Data);
        var inserted = item with { Id = "inserted", Orig = null, Text = "Inserted", Y = 140 };
        Call(session, "PUT", "/api/page/0/items", new[] { item, inserted });
        Assert.Equal(Pdf.RenderPage(replacement.Pages[0], 72).ToPng(),
            Call(session, "GET", "/api/page/0.png", query: [("dpi", "72"), ("bare", "true")]).Data);
        Assert.NotEqual(Call(session, "GET", "/api/page/0.png", query: [("dpi", "72"), ("bare", "true")]).Data,
            Call(session, "GET", "/api/page/0.png", query: [("dpi", "72")]).Data);
    }

    [Fact]
    public void SelectionReplyMatchesLegacyEndpointsAndInvalidatesAfterEditsAndReplacement()
    {
        var source = FreeDF.Tests.TestPdf.Build("0 0 1 rg 20 30 80 50 re f BT /F1 18 Tf 30 90 Td (Original) Tj ET",
            "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>");
        var session = new EditorSession(new FontCatalog());
        session.Handle("POST", "/api/open", new Dictionary<string, string>(), source, "selection.pdf");
        var first = Call(session, "GET", "/api/page/0/selection");
        Assert.Same(first, Call(session, "GET", "/api/page/0/selection"));
        using var selection = JsonDocument.Parse(first.Data);
        Assert.Equal(Encoding.UTF8.GetString(Call(session, "GET", "/api/page/0/text").Data),
            selection.RootElement.GetProperty("spans").GetRawText());
        Assert.Equal(Encoding.UTF8.GetString(Call(session, "GET", "/api/page/0/objects").Data),
            selection.RootElement.GetProperty("objects").GetRawText());
        var index = selection.RootElement.GetProperty("objects")[0].GetProperty("index").GetInt32();
        Call(session, "PUT", $"/api/page/0/objects/{index}", new { sx = 1, sy = 1, tx = 10, ty = 0, delete = false });
        var edited = Call(session, "GET", "/api/page/0/selection");
        Assert.NotSame(first, edited);
        using var changed = JsonDocument.Parse(edited.Data);
        Assert.Equal(10, changed.RootElement.GetProperty("objects")[0].GetProperty("edit").GetProperty("tx").GetDouble());
        Call(session, "POST", "/api/new");
        using var empty = JsonDocument.Parse(Call(session, "GET", "/api/page/0/selection").Data);
        Assert.Empty(empty.RootElement.GetProperty("spans").EnumerateArray());
        Assert.Empty(empty.RootElement.GetProperty("objects").EnumerateArray());
    }

    [Fact]
    public void RepeatedPageRendersReuseExactPngBytesUntilContentChanges()
    {
        var session = new EditorSession(new FontCatalog());
        Call(session, "POST", "/api/new");
        var first = Call(session, "GET", "/api/page/0.png", query: [("dpi", "40")]).Data;
        var cached = Call(session, "GET", "/api/page/0.png", query: [("dpi", "40"), ("t", "ignored-cache-buster")]).Data;
        Assert.Same(first, cached);
        Assert.NotSame(first, Call(session, "GET", "/api/page/0.png", query: [("dpi", "72")]).Data);
        Assert.NotSame(first, Call(session, "GET", "/api/page/0.png", query: [("dpi", "40"), ("bare", "true")]).Data);
        var asset = Upload(session);
        Call(session, "PUT", "/api/page/0/items", new ItemUpdate([Image("new", asset)], []));
        var edited = Call(session, "GET", "/api/page/0.png", query: [("dpi", "40")]).Data;
        Assert.NotEqual(first, edited);
        Assert.Same(edited, Call(session, "GET", "/api/page/0.png", query: [("dpi", "40")]).Data);
        Call(session, "POST", "/api/new");
        Assert.Equal(first, Call(session, "GET", "/api/page/0.png", query: [("dpi", "40")]).Data);
    }

    [Fact]
    public void PristineBackgroundTracksDocumentMutationsButExcludesNewOverlays()
    {
        var source = FreeDF.Tests.TestPdf.Build("0 0 1 rg 20 30 80 50 re f BT /F1 18 Tf 30 90 Td (Original) Tj ET",
            "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>");
        var session = new EditorSession(new FontCatalog());
        session.Handle("POST", "/api/open", new Dictionary<string, string>(), source, "background.pdf");
        Call(session, "GET", "/api/state");
        Call(session, "GET", "/api/page/0/text");
        var original = Call(session, "GET", "/api/page/0.png", query: [("dpi", "72"), ("bare", "true")]).Data;
        Assert.Equal(Pdf.RenderPage(PdfDocument.Load(source).Pages[0], 72).ToPng(), original);

        var rectangle = new Item("overlay", "rect", 10, 10, 30, 30, null, null, 0, false, false,
            null, "#ff0000", "", 0, 1, null, 0, 0);
        Call(session, "PUT", "/api/page/0/items", new[] { rectangle });
        Call(session, "POST", "/api/title", query: [("t", "Changed")]);
        Call(session, "GET", "/api/state");
        Assert.Equal(original, Call(session, "GET", "/api/page/0.png", query: [("dpi", "72"), ("bare", "true")]).Data);
        Assert.NotEqual(original, Call(session, "GET", "/api/page/0.png", query: [("dpi", "72")]).Data);

        Call(session, "POST", "/api/rotate/0/90");
        Call(session, "GET", "/api/state");
        var rotated = PdfDocument.Load(source);
        var editor = PdfEditor.Open(rotated);
        editor.SetRotation(0, 90);
        var expected = PdfDocument.Load(editor.Save());
        Assert.Equal(Pdf.RenderPage(expected.Pages[0], 72).ToPng(),
            Call(session, "GET", "/api/page/0.png", query: [("dpi", "72"), ("bare", "true")]).Data);
    }

    [Fact]
    public void OverlayOnlyChangesRetainBarePageRasterCache()
    {
        var session = new EditorSession(new FontCatalog());
        Call(session, "POST", "/api/new");
        var bare = Call(session, "GET", "/api/page/0.png", query: [("dpi", "40"), ("bare", "true")]).Data;
        var full = Call(session, "GET", "/api/page/0.png", query: [("dpi", "40")]).Data;
        var rectangle = new Item("rectangle", "rect", 20, 30, 80, 60, null, null, 0, false, false,
            null, "#ff0000", "#000000", 1, 1, null, 0, 0);

        Call(session, "PUT", "/api/page/0/items", new[] { rectangle });

        Assert.Same(bare, Call(session, "GET", "/api/page/0.png", query: [("dpi", "40"), ("bare", "true")]).Data);
        var overlay = Call(session, "GET", "/api/page/0.png", query: [("dpi", "40")]).Data;
        Assert.NotSame(full, overlay);
        var saved = PdfDocument.Load(Call(session, "GET", "/api/export/pdf").Data);
        Assert.Equal(Pdf.RenderPage(saved.Pages[0], 40).ToPng(), overlay);
        Assert.Same(bare, Call(session, "GET", "/api/page/0.png", query: [("dpi", "40"), ("bare", "true")]).Data);
    }

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
        Call(session, "POST", "/api/page-history-clear");
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
        Call(session, "POST", "/api/page-history-clear");
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

    sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void UnclaimedUploadsExpireAtTenMinutesAndCanBeRetriedWithTheSameId()
    {
        var clock = new Clock();
        var session = new EditorSession(new FontCatalog(), timeProvider: clock);
        Call(session, "POST", "/api/new");
        var asset = Upload(session);
        clock.Now += TimeSpan.FromMinutes(10) - TimeSpan.FromTicks(1);
        Assert.NotEmpty(Call(session, "GET", "/api/asset/" + asset).Data);
        clock.Now += TimeSpan.FromTicks(1);
        Assert.Throws<KeyNotFoundException>(() => Call(session, "GET", "/api/asset/" + asset));
        var png = PngWriter.Encode(1, 1, [255, 0, 0, 255]);
        session.Handle("POST", "/api/asset", new Dictionary<string, string> { ["id"] = asset }, png);
        Call(session, "PUT", "/api/page/0/items", new ItemUpdate([Image("retry", asset)], []));
        clock.Now += TimeSpan.FromHours(1);
        Assert.NotEmpty(Call(session, "GET", "/api/asset/" + asset).Data);
    }

    [Fact]
    public void SuccessfulUnclaimedRetryRenewsLeaseAcrossOriginalDeadline()
    {
        var clock = new Clock();
        var session = new EditorSession(new FontCatalog(), timeProvider: clock);
        Call(session, "POST", "/api/new");
        var asset = Upload(session);
        clock.Now += TimeSpan.FromMinutes(9);
        var png = PngWriter.Encode(1, 1, [255, 0, 0, 255]);
        session.Handle("POST", "/api/asset", new Dictionary<string, string> { ["id"] = asset }, png);
        clock.Now += TimeSpan.FromMinutes(2);
        Call(session, "PUT", "/api/page/0/items", new ItemUpdate([Image("retry", asset)], []));
        clock.Now += TimeSpan.FromHours(1);
        Assert.NotEmpty(Call(session, "GET", "/api/asset/" + asset).Data);
        Call(session, "PUT", "/api/page/0/items", new ItemUpdate([], []));
        Assert.Throws<KeyNotFoundException>(() => Call(session, "GET", "/api/asset/" + asset));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClipboardSurvivesHistoryReleaseAndSourceRemovalUntilReplaced(bool removePage)
    {
        var clock = new Clock();
        var session = new EditorSession(new FontCatalog(), timeProvider: clock);
        Call(session, "POST", "/api/new");
        Call(session, "POST", "/api/blank/1");
        var asset = Upload(session);
        var image = Image("copy", asset);
        Call(session, "PUT", "/api/page/0/items", new ItemUpdate([image], [], [asset]));
        if (removePage) Call(session, "POST", "/api/delete/0");
        else Call(session, "PUT", "/api/page/0/items", new ItemUpdate([], [asset]));
        Call(session, "POST", "/api/asset-history");
        Call(session, "POST", "/api/asset-cancel", query: [("id", asset)]);
        clock.Now += TimeSpan.FromHours(1);
        Assert.NotEmpty(Call(session, "GET", "/api/asset/" + asset).Data);
        Call(session, "PUT", "/api/page/0/items", new ItemUpdate([image], []));
        Call(session, "POST", "/api/asset-clipboard", Array.Empty<string>());
        Assert.NotEmpty(Call(session, "GET", "/api/asset/" + asset).Data);
        Call(session, "PUT", "/api/page/0/items", new ItemUpdate([], []));
        Call(session, "POST", "/api/page-history-clear");
        Assert.Throws<KeyNotFoundException>(() => Call(session, "GET", "/api/asset/" + asset));
    }

    [Fact]
    public void ReplacingClipboardOrDocumentReleasesClipboardOnlyAssets()
    {
        var session = new EditorSession(new FontCatalog());
        Call(session, "POST", "/api/new");
        var first = Upload(session);
        Call(session, "POST", "/api/asset-clipboard", new[] { first });
        var second = Upload(session);
        Call(session, "POST", "/api/asset-clipboard", new[] { second });
        Assert.Throws<KeyNotFoundException>(() => Call(session, "GET", "/api/asset/" + first));
        Assert.NotEmpty(Call(session, "GET", "/api/asset/" + second).Data);
        Call(session, "POST", "/api/new");
        Assert.Throws<KeyNotFoundException>(() => Call(session, "GET", "/api/asset/" + second));
    }

    [Fact]
    public void ExpiryAndCancelPreserveLiveAndUndoReferences()
    {
        var clock = new Clock();
        var session = new EditorSession(new FontCatalog(), timeProvider: clock);
        Call(session, "POST", "/api/new");
        var live = Upload(session);
        Call(session, "PUT", "/api/page/0/items", new ItemUpdate([Image("live", live)], []));
        var history = Upload(session);
        Call(session, "PUT", "/api/page/0/items", new ItemUpdate([Image("live", live)], [history]));
        var abandoned = Upload(session);
        Call(session, "POST", "/api/asset-cancel", query: [("id", abandoned)]);
        Assert.Throws<KeyNotFoundException>(() => Call(session, "GET", "/api/asset/" + abandoned));
        foreach (var id in new[] { live, history }) Call(session, "POST", "/api/asset-cancel", query: [("id", id)]);
        clock.Now += TimeSpan.FromHours(1);
        foreach (var id in new[] { live, history }) Assert.NotEmpty(Call(session, "GET", "/api/asset/" + id).Data);
        Call(session, "POST", "/api/asset-history");
        Assert.Throws<KeyNotFoundException>(() => Call(session, "GET", "/api/asset/" + history));
        Assert.NotEmpty(Call(session, "GET", "/api/asset/" + live).Data);
    }
}
