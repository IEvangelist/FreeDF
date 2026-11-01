using System.Text.Json;
using PortableDocument.Document;
using PortableDocument.Render;

public class EditorPageHistoryTests
{
    static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    static EditorSession.Reply Call(EditorSession session, string method, string path, object? body = null, params (string, string)[] query)
        => session.Handle(method, path, query.ToDictionary(x => x.Item1, x => x.Item2),
            body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body, Web));

    static JsonElement State(EditorSession session) => JsonDocument.Parse(Call(session, "GET", "/api/state").Data).RootElement.Clone();
    static int[] PageIds(EditorSession session) => State(session).GetProperty("pages").EnumerateArray().Select(p => p.GetProperty("id").GetInt32()).ToArray();
    static int[] Rotations(EditorSession session) => State(session).GetProperty("pages").EnumerateArray().Select(p => p.GetProperty("rotation").GetInt32()).ToArray();

    static string Upload(EditorSession session)
    {
        using var reply = JsonDocument.Parse(session.Handle("POST", "/api/asset", new Dictionary<string, string>(), PngWriter.Encode(1, 1, [255, 0, 0, 255])).Data);
        return reply.RootElement.GetProperty("id").GetString()!;
    }

    static EditorSession ThreePages()
    {
        var session = new EditorSession(new FontCatalog());
        Call(session, "POST", "/api/new");
        Call(session, "POST", "/api/blank/1");
        Call(session, "POST", "/api/blank/2");
        return session;
    }

    [Fact]
    public void DeleteMoveRotateAndInsertUndoAndRedoInOrder()
    {
        var session = ThreePages();
        var original = PageIds(session);
        Call(session, "POST", "/api/rotate/0/90");
        Call(session, "POST", "/api/move/0/2");
        Call(session, "POST", "/api/delete/1");
        Call(session, "POST", "/api/duplicate/0");
        Assert.Equal(6, State(session).GetProperty("pageUndo").GetInt32());
        var afterAll = PageIds(session);
        Assert.Equal(3, afterAll.Length);

        for (var step = 0; step < 4; step++) Call(session, "POST", "/api/page-undo");
        Assert.Equal(original, PageIds(session));
        Assert.Equal([0, 0, 0], Rotations(session));
        for (var step = 0; step < 4; step++) Call(session, "POST", "/api/page-redo");
        Assert.Equal(afterAll, PageIds(session));
        Assert.Equal(0, State(session).GetProperty("pageRedo").GetInt32());
    }

    [Fact]
    public void UndoingDeleteRestoresPageItemsObjectEditsAndPixels()
    {
        var session = ThreePages();
        var asset = Upload(session);
        var item = new Item("img", "image", 20, 20, 40, 40, null, null, 0, false, false, null, null, null, 0, 1, asset, 0, 0);
        Call(session, "PUT", "/api/page/1/items", new ItemUpdate([item], []));
        var pixels = Call(session, "GET", "/api/page/1.png", query: [("dpi", "40")]).Data;
        var doomed = PageIds(session)[1];

        Call(session, "POST", "/api/delete/1");
        Assert.Equal(2, State(session).GetProperty("pageCount").GetInt32());
        Call(session, "POST", "/api/page-undo");

        Assert.Equal(doomed, PageIds(session)[1]);
        using var restored = JsonDocument.Parse(Call(session, "GET", "/api/page/1/items").Data);
        Assert.Equal("img", restored.RootElement.EnumerateArray().Single().GetProperty("id").GetString());
        Assert.Equal(pixels, Call(session, "GET", "/api/page/1.png", query: [("dpi", "40")]).Data);
        Assert.NotNull(Call(session, "GET", "/api/asset/" + asset).Data);
    }

    [Fact]
    public void UndoAndRedoKeepImagesUsedOnlyByTheStateBeingLeft()
    {
        var session = ThreePages();
        Call(session, "POST", "/api/rotate/0/90");
        var asset = Upload(session);
        var item = new Item("img", "image", 20, 20, 40, 40, null, null, 0, false, false, null, null, null, 0, 1, asset, 0, 0);
        Call(session, "PUT", "/api/page/0/items", new ItemUpdate([item], []));
        var pixels = Call(session, "GET", "/api/page/0.png", query: [("dpi", "40")]).Data;

        Call(session, "POST", "/api/page-undo");
        using (var none = JsonDocument.Parse(Call(session, "GET", "/api/page/0/items").Data))
            Assert.Empty(none.RootElement.EnumerateArray());
        Assert.NotEmpty(Call(session, "GET", "/api/asset/" + asset).Data);

        Call(session, "POST", "/api/page-redo");
        using var back = JsonDocument.Parse(Call(session, "GET", "/api/page/0/items").Data);
        Assert.Equal("img", back.RootElement.EnumerateArray().Single().GetProperty("id").GetString());
        Assert.Equal(pixels, Call(session, "GET", "/api/page/0.png", query: [("dpi", "40")]).Data);

        var saved = PdfDocument.Load(Call(session, "GET", "/api/download").Data);
        Assert.Equal(3, saved.Pages.Count);
        var reopened = new EditorSession(new FontCatalog());
        reopened.Handle("POST", "/api/open", new Dictionary<string, string>(), Call(session, "GET", "/api/download").Data, "r.pdf");
        Assert.Equal(3, JsonDocument.Parse(Call(reopened, "GET", "/api/state").Data).RootElement.GetProperty("pageCount").GetInt32());
    }

    [Fact]
    public void DeletedPageAssetsStayAvailableUntilHistoryIsDropped()
    {
        var session = ThreePages();
        var asset = Upload(session);
        var item = new Item("img", "image", 0, 0, 10, 10, null, null, 0, false, false, null, null, null, 0, 1, asset, 0, 0);
        Call(session, "PUT", "/api/page/2/items", new ItemUpdate([item], []));
        Call(session, "POST", "/api/delete/2");
        Assert.NotEmpty(Call(session, "GET", "/api/asset/" + asset).Data);

        Call(session, "POST", "/api/new");
        Assert.Throws<KeyNotFoundException>(() => Call(session, "GET", "/api/asset/" + asset));
        Assert.Equal(0, State(session).GetProperty("pageUndo").GetInt32());
    }

    [Fact]
    public void NewPageOperationClearsRedoAndFailedOperationsRecordNothing()
    {
        var session = ThreePages();
        var baseline = State(session).GetProperty("pageUndo").GetInt32();
        Call(session, "POST", "/api/delete/2");
        Call(session, "POST", "/api/page-undo");
        Assert.Equal(1, State(session).GetProperty("pageRedo").GetInt32());
        Call(session, "POST", "/api/rotate/0/90");
        Assert.Equal(0, State(session).GetProperty("pageRedo").GetInt32());

        Assert.ThrowsAny<Exception>(() => Call(session, "POST", "/api/delete/99"));
        Assert.Equal(baseline + 1, State(session).GetProperty("pageUndo").GetInt32());
        Assert.Throws<EditorConflictException>(() => Call(session, "POST", "/api/page-redo"));
    }

    [Fact]
    public void ImportUndoRemovesPagesAndRedoRestoresThemWithoutReusingStaleIds()
    {
        var session = ThreePages();
        var source = FreeDF.Tests.TestPdf.Build("BT /F1 18 Tf 30 90 Td (Imported) Tj ET", "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>");
        session.Handle("POST", "/api/import", new Dictionary<string, string>(), source, "imported.pdf");
        Assert.Equal(4, PageIds(session).Length);
        var withImport = PageIds(session);
        var exported = Call(session, "GET", "/api/export/pdf").Data;

        Call(session, "POST", "/api/page-undo");
        Assert.Equal(3, PageIds(session).Length);
        Call(session, "POST", "/api/page-redo");
        Assert.Equal(withImport, PageIds(session));
        Assert.Equal(PdfDocument.Load(exported).Pages.Count, PdfDocument.Load(Call(session, "GET", "/api/export/pdf").Data).Pages.Count);
    }
}
