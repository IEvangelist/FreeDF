using System.Text.Json;
using PortableDocument.Document;

public class EditorMergeTests
{
    static readonly Dictionary<string, string> NoQuery = [];

    static Dictionary<string, string> Q(params (string, string)[] q) => q.ToDictionary(x => x.Item1, x => x.Item2);

    static string Sample(string prefix)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var s = Path.Combine(d.FullName, "samples");
            if (Directory.Exists(s) && Directory.GetFiles(s, prefix + "*.pdf") is [var file, ..]) return file;
        }
        throw new FileNotFoundException(prefix);
    }

    static EditorSession Open(string prefix)
    {
        var session = new EditorSession(new FontCatalog());
        session.Handle("POST", "/api/open", NoQuery, File.ReadAllBytes(Sample(prefix)), "base.pdf");
        return session;
    }

    static JsonDocument Json(EditorSession s, string method, string path, byte[]? body = null, params (string, string)[] query)
        => JsonDocument.Parse(s.Handle(method, path, query.ToDictionary(q => q.Item1, q => q.Item2), body).Data);

    static int Pages(EditorSession s) { using var state = Json(s, "GET", "/api/state"); return state.RootElement.GetProperty("pageCount").GetInt32(); }
    static int PageCount(string prefix) => PdfDocument.Load(File.ReadAllBytes(Sample(prefix))).Pages.Count;

    [Fact]
    public void InspectReportsPageCountWithoutChangingTheDocument()
    {
        var session = Open("01-");
        var before = Pages(session);
        using var info = Json(session, "POST", "/api/inspect", File.ReadAllBytes(Sample("20-")));
        Assert.Equal(PageCount("20-"), info.RootElement.GetProperty("pageCount").GetInt32());
        Assert.Equal(before, Pages(session));
        using var state = Json(session, "GET", "/api/state");
        Assert.False(state.RootElement.GetProperty("dirty").GetBoolean());
    }

    [Fact]
    public void ImportAppendsByDefaultAndReportsWhereThePagesLanded()
    {
        var session = Open("01-");
        var before = Pages(session);
        using var result = Json(session, "POST", "/api/import", File.ReadAllBytes(Sample("20-")));
        Assert.Equal(before, result.RootElement.GetProperty("first").GetInt32());
        Assert.Equal(PageCount("20-"), result.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(before + PageCount("20-"), Pages(session));
    }

    [Fact]
    public void ImportHonoursInsertPositionAndPageRangeOrder()
    {
        var session = Open("01-");
        var before = Pages(session);
        var second = File.ReadAllBytes(Sample("20-"));
        using var result = Json(session, "POST", "/api/import", second, ("at", "1"), ("pages", "2,1"));
        Assert.Equal(1, result.RootElement.GetProperty("first").GetInt32());
        Assert.Equal(2, result.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(before + 2, Pages(session));
        var merged = PdfDocument.Load(session.Handle("GET", "/api/download", NoQuery, null).Data);
        var source = PdfDocument.Load(second);
        Assert.Equal(source.Pages[1].MediaBox.Width, merged.Pages[1].MediaBox.Width, 3);
        Assert.Equal(source.Pages[0].MediaBox.Height, merged.Pages[2].MediaBox.Height, 3);
    }

    [Fact]
    public void OutOfRangeInsertPositionIsClampedToTheDocument()
    {
        var session = Open("01-");
        var before = Pages(session);
        using var result = Json(session, "POST", "/api/import", File.ReadAllBytes(Sample("20-")), ("at", "9999"), ("pages", "1"));
        Assert.Equal(before, result.RootElement.GetProperty("first").GetInt32());
        using var negative = Json(session, "POST", "/api/import", File.ReadAllBytes(Sample("20-")), ("at", "-5"), ("pages", "1"));
        Assert.Equal(0, negative.RootElement.GetProperty("first").GetInt32());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("999")]
    [InlineData("abc")]
    [InlineData("1,x")]
    public void InvalidPageRangeIsRejectedAndLeavesTheDocumentUntouched(string range)
    {
        var session = Open("01-");
        var before = Pages(session);
        Assert.Throws<ArgumentException>(() => session.Handle("POST", "/api/import", Q(("pages", range)), File.ReadAllBytes(Sample("20-"))));
        Assert.Equal(before, Pages(session));
    }

    [Fact]
    public void InvalidAndMissingInputsAreRejectedWithoutChangingTheDocument()
    {
        var session = Open("01-");
        var before = Pages(session);
        Assert.ThrowsAny<Exception>(() => session.Handle("POST", "/api/import", NoQuery, [1, 2, 3]));
        Assert.ThrowsAny<Exception>(() => session.Handle("POST", "/api/inspect", NoQuery, [1, 2, 3]));
        Assert.Throws<ArgumentException>(() => session.Handle("POST", "/api/import", NoQuery, null));
        Assert.Equal(before, Pages(session));
    }

    [Fact]
    public void PasswordProtectedInputsFailWithoutPersistingAnything()
    {
        var session = Open("01-");
        var before = Pages(session);
        Assert.ThrowsAny<Exception>(() => session.Handle("POST", "/api/inspect", NoQuery, File.ReadAllBytes(Sample("17-"))));
        Assert.ThrowsAny<Exception>(() => session.Handle("POST", "/api/import", NoQuery, File.ReadAllBytes(Sample("17-"))));
        Assert.Equal(before, Pages(session));
        using var state = Json(session, "GET", "/api/state");
        Assert.True(state.RootElement.GetProperty("recoverySupported").GetBoolean());
    }

    [Fact]
    public void ImportingAnEncryptedPdfThatOpensDisablesLocalRecovery()
    {
        var session = Open("01-");
        using var info = Json(session, "POST", "/api/inspect", File.ReadAllBytes(Sample("18-")));
        Assert.True(info.RootElement.GetProperty("encrypted").GetBoolean());
        session.Handle("POST", "/api/import", NoQuery, File.ReadAllBytes(Sample("18-")));
        using var state = Json(session, "GET", "/api/state");
        Assert.False(state.RootElement.GetProperty("recoverySupported").GetBoolean());
    }

    [Fact]
    public void MergedPagesKeepOverlayEditsOnTheirOwnPagesAndStayRenderable()
    {
        var session = Open("20-");
        var item = new Item("note", "text", 40, 40, 120, 20, "kept", "Helvetica", 12, false, false, "#000000", null, null, 0, 1, null, 0, 0);
        session.Handle("PUT", "/api/page/0/items", NoQuery, JsonSerializer.SerializeToUtf8Bytes(new[] { item }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        session.Handle("POST", "/api/import", Q(("at", "0"), ("pages", "1")), File.ReadAllBytes(Sample("01-")));
        using var moved = Json(session, "GET", "/api/page/1/items");
        Assert.Equal("note", Assert.Single(moved.RootElement.EnumerateArray()).GetProperty("id").GetString());
        using var inserted = Json(session, "GET", "/api/page/0/items");
        Assert.Empty(inserted.RootElement.EnumerateArray());
        Assert.NotEmpty(session.Handle("GET", "/api/page/0.png", Q(("dpi", "40")), null).Data);
        Assert.Equal(PageCount("20-") + 1, PdfDocument.Load(session.Handle("GET", "/api/download", NoQuery, null).Data).Pages.Count);
    }
}
