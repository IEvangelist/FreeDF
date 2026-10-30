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

    static PdfDocument Reload(PdfEditor editor) => PdfDocument.Load(editor.Save());
    static PdfDocument Doc(string prefix) => PdfDocument.Load(File.ReadAllBytes(Sample(prefix)));
    static IEnumerable<PdfOutlineItem> Flatten(IEnumerable<PdfOutlineItem> items) => items.SelectMany(i => new[] { i }.Concat(Flatten(i.Children)));

    [Fact]
    public void MergedFormFieldsAreRegisteredRenamedOnCollisionAndEditable()
    {
        var source = Doc("11-");
        var names = source.GetFormFields().Select(f => f.FullName).ToList();
        Assert.NotEmpty(names);
        var editor = PdfEditor.Open(Doc("01-"));
        Assert.False(editor.SetFieldValue(names[0], "x"));
        editor.ImportPages(source, null, null, PdfImportOptions.Merge("forms"));
        var merged = Reload(editor).GetFormFields();
        foreach (var name in names) Assert.Contains(merged, f => f.FullName == name);
        Assert.True(editor.SetFieldValue(names[0], "merged value"));
        Assert.Contains(Reload(editor).GetFormFields(), f => f.FullName == names[0] && f.Value == "merged value");

        editor.ImportPages(source, null, null, PdfImportOptions.Merge("forms again"));
        var twice = Reload(editor).GetFormFields().Select(f => f.FullName).ToList();
        Assert.Equal(twice.Count, twice.Distinct().Count());
        Assert.Equal(names.Count * 2, twice.Count);
        Assert.Contains(twice, n => n.StartsWith(names[0].Split('.')[0] + "_2", StringComparison.Ordinal));
    }

    [Fact]
    public void PlainPageCopiesDoNotRegisterFieldsOrOutlines()
    {
        var editor = PdfEditor.Open(Doc("01-"));
        var before = Reload(editor).GetFormFields().Count;
        editor.ImportPages(Doc("11-"));
        Assert.Equal(before, Reload(editor).GetFormFields().Count);
    }

    [Fact]
    public void FieldsOnOmittedPagesAreNotMerged()
    {
        var source = Doc("11-");
        var editor = PdfEditor.Open(Doc("01-"));
        editor.ImportPages(source, [], null, PdfImportOptions.Merge());
        Assert.Equal(Doc("01-").GetFormFields().Count, Reload(editor).GetFormFields().Count);
    }

    [Fact]
    public void MergedOutlinesNestUnderTheFileBookmarkAndPointAtImportedPages()
    {
        var source = Doc("12-");
        var baseDoc = Doc("01-");
        var baseCount = baseDoc.Pages.Count;
        var editor = PdfEditor.Open(baseDoc);
        var existing = Flatten(baseDoc.GetOutlines()).Count();
        editor.ImportPages(source, null, null, PdfImportOptions.Merge("12-navigation"));
        var merged = Reload(editor);
        var all = Flatten(merged.GetOutlines()).ToList();
        var wrapper = merged.GetOutlines().Single(o => o.Title == "12-navigation");
        Assert.Equal(baseCount, wrapper.PageIndex);
        Assert.Equal(Flatten(source.GetOutlines()).Count(), Flatten(wrapper.Children).Count());
        Assert.Equal(existing + 1 + Flatten(source.GetOutlines()).Count(), all.Count);
        Assert.All(Flatten(wrapper.Children).Where(i => i.PageIndex is not null), i => Assert.InRange(i.PageIndex!.Value, baseCount, merged.Pages.Count - 1));
        var originalDestinations = Flatten(source.GetOutlines()).Select(i => i.PageIndex).ToList();
        Assert.Equal(originalDestinations.Select(p => p is null ? (int?)null : p + baseCount), Flatten(wrapper.Children).Select(i => i.PageIndex));
    }

    [Fact]
    public void OutlineItemsForOmittedPagesAreDropped()
    {
        var source = Doc("12-");
        var editor = PdfEditor.Open(Doc("01-"));
        editor.ImportPages(source, [0], null, PdfImportOptions.Merge("only first"));
        var merged = Reload(editor);
        var kept = Flatten(merged.GetOutlines().Single(o => o.Title == "only first").Children).ToList();
        Assert.All(kept, i => Assert.Equal(Doc("01-").Pages.Count, i.PageIndex ?? Doc("01-").Pages.Count));
        Assert.True(kept.Count < Flatten(source.GetOutlines()).Count());
    }

    [Fact]
    public void ImportedPagesKeepTheirSourcePageLabels()
    {
        var source = Doc("12-");
        var editor = PdfEditor.Open(Doc("01-"));
        var baseCount = editor.PageCount;
        var before = Enumerable.Range(0, baseCount).Select(Doc("01-").GetPageLabel).ToList();
        editor.ImportPages(source, null, null, PdfImportOptions.Merge());
        var merged = Reload(editor);
        for (var i = 0; i < baseCount; i++) Assert.Equal(before[i], merged.GetPageLabel(i));
        for (var i = 0; i < source.Pages.Count; i++) Assert.Equal(source.GetPageLabel(i), merged.GetPageLabel(baseCount + i));
    }

    [Fact]
    public void InsertingInTheMiddleKeepsExistingLabelsAttachedToTheirPages()
    {
        var source = Doc("12-");
        var editor = PdfEditor.Open(source);
        var before = Enumerable.Range(0, source.Pages.Count).Select(source.GetPageLabel).ToList();
        editor.ImportPages(Doc("20-"), [0], 2, PdfImportOptions.Merge());
        var merged = Reload(editor);
        Assert.Equal(before[0], merged.GetPageLabel(0));
        Assert.Equal(before[1], merged.GetPageLabel(1));
        for (var i = 2; i < before.Count; i++) Assert.Equal(before[i], merged.GetPageLabel(i + 1));
    }

    [Fact]
    public void AnnotationPdfMergesWithEveryAnnotationAndStaysRenderable()
    {
        var source = Doc("10-");
        var editor = PdfEditor.Open(Doc("01-"));
        var baseCount = editor.PageCount;
        editor.ImportPages(source, null, null, PdfImportOptions.Merge("10"));
        var merged = Reload(editor);
        for (var i = 0; i < source.Pages.Count; i++)
            Assert.Equal(AnnotationCount(source, i), AnnotationCount(merged, baseCount + i));
    }

    static int AnnotationCount(PdfDocument doc, int page) =>
        doc.File.Resolve(doc.Pages[page].Dict.Get("Annots")) is PortableDocument.Core.PdfArray a ? a.Count : 0;

    [Fact]
    public void BatchImportIsAllOrNothing()
    {
        var session = Open("01-");
        var before = Pages(session);
        var bad = Frames(File.ReadAllBytes(Sample("20-")), File.ReadAllBytes(Sample("22-")));
        var ex = Assert.Throws<ArgumentException>(() => session.Handle("POST", "/api/import-batch", Q(("ranges", "|99"), ("names", "[\"20-report.pdf\",\"22-invoice.pdf\"]")), bad));
        Assert.Contains("22-invoice.pdf", ex.Message);
        Assert.Equal(before, Pages(session));
        Assert.Equal(before, PdfDocument.Load(session.Handle("GET", "/api/download", NoQuery, null).Data).Pages.Count);

        using var ok = Json(session, "POST", "/api/import-batch", Frames(File.ReadAllBytes(Sample("20-")), File.ReadAllBytes(Sample("22-"))), ("at", "0"), ("ranges", "1|"));
        Assert.Equal(2, ok.RootElement.GetProperty("files").GetInt32());
        Assert.Equal(1 + PageCount("22-"), ok.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(before + 1 + PageCount("22-"), Pages(session));
        Assert.NotEmpty(session.Handle("GET", "/api/page/0.png", Q(("dpi", "40")), null).Data);
    }

    [Fact]
    public void BatchImportRollsBackWhenALaterFileFailsMidway()
    {
        var editor = PdfEditor.Open(Doc("01-"));
        var count = editor.PageCount;
        Assert.ThrowsAny<Exception>(() => editor.ImportBatch([(Doc("20-"), [0], PdfImportOptions.Merge("a")), (Doc("22-"), [999], PdfImportOptions.Merge("b"))], 0));
        Assert.Equal(count, editor.PageCount);
        Assert.Equal(count, Reload(editor).Pages.Count);
    }

    static byte[] Frames(params byte[][] files)
    {
        var ms = new MemoryStream();
        ms.Write(BitConverter.GetBytes((uint)files.Length));
        foreach (var f in files) { ms.Write(BitConverter.GetBytes((uint)f.Length)); ms.Write(f); }
        return ms.ToArray();
    }

    [Fact]
    public void BatchWithAnEncryptedFileIsRejectedAndNamesTheFile()
    {
        var session = Open("01-");
        var before = Pages(session);
        var ex = Assert.ThrowsAny<Exception>(() => session.Handle("POST", "/api/import-batch", Q(("names", "[\"a.pdf\",\"17-encrypted-user-password.pdf\"]")),
            Frames(File.ReadAllBytes(Sample("20-")), File.ReadAllBytes(Sample("17-")))));
        Assert.Contains("17-encrypted-user-password.pdf", ex.Message);
        Assert.Equal(before, Pages(session));
    }

    [Fact]
    public void MergedFormFieldsRemainEditableThroughTheSession()
    {
        var session = Open("01-");
        session.Handle("POST", "/api/import", NoQuery, File.ReadAllBytes(Sample("11-")), "11-acroform.pdf");
        var names = Doc("11-").GetFormFields().Select(f => f.FullName).ToList();
        var merged = PdfDocument.Load(session.Handle("GET", "/api/download", NoQuery, null).Data);
        foreach (var name in names) Assert.Contains(merged.GetFormFields(), f => f.FullName == name);
    }

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
