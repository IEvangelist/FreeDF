using System.Text.Json;
using PortableDocument.Document;

namespace AdoBye.Tests;

public class AnnotationSessionTests
{
    static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    const string Font = "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>";

    static EditorSession Open()
    {
        var s = new EditorSession(new FontCatalog());
        s.Handle("POST", "/api/open", new Dictionary<string, string>(), TestPdf.Build("BT /F1 24 Tf 20 100 Td (Hello World) Tj ET", Font), "a.pdf");
        return s;
    }

    static JsonElement Call(EditorSession s, string method, string path, object? body = null, params (string, string)[] q)
        => JsonDocument.Parse(s.Handle(method, path, q.ToDictionary(x => x.Item1, x => x.Item2), body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body, Web)).Data).RootElement.Clone();

    [Fact]
    public void AddSelectTextReplyUndoRedoFlattenAndExport()
    {
        var s = Open();
        var sel = Call(s, "GET", "/api/annot-text", null, ("page", "0"), ("x", "0"), ("y", "60"), ("w", "200"), ("h", "60"));
        Assert.Contains("Hello", sel.GetProperty("text").GetString());
        var quads = sel.GetProperty("quads").EnumerateArray().Select(v => v.GetDouble()).ToArray();
        var add = Call(s, "POST", "/api/annot-add", new { subtype = "Highlight", quadPoints = quads, author = "Ann", contents = "hi" }, ("page", "0"));
        var n = add.GetProperty("number").GetInt32();
        Call(s, "POST", "/api/annot-reply", new { author = "Bob", text = "ok", state = "Accepted" }, ("page", "0"), ("n", n.ToString()));
        Assert.Equal(2, Call(s, "GET", "/api/annots").GetArrayLength());
        Assert.Contains("<highlight", System.Text.Encoding.UTF8.GetString(s.Handle("GET", "/api/annot-export", new Dictionary<string, string>()).Data));

        Call(s, "POST", "/api/page-undo");
        Assert.Equal(1, Call(s, "GET", "/api/annots").GetArrayLength());
        Call(s, "POST", "/api/page-undo");
        Assert.Equal(0, Call(s, "GET", "/api/annots").GetArrayLength());
        Call(s, "POST", "/api/page-redo");
        Call(s, "POST", "/api/page-redo");
        Assert.Equal(2, Call(s, "GET", "/api/annots").GetArrayLength());

        Assert.Equal(1, Call(s, "POST", "/api/annot-flatten", null, ("page", "0")).GetProperty("flattened").GetInt32());
        Assert.Equal(0, Call(s, "GET", "/api/annots").GetArrayLength());
        var saved = PdfEditor.Open(s.Handle("GET", "/api/download", new Dictionary<string, string>()).Data);
        Assert.Empty(saved.ListAnnotations(0));
    }

    [Fact]
    public void RedactionMarksBecomeRedactionItemsAndRemoveTextOnSave()
    {
        var s = Open();
        Call(s, "POST", "/api/annot-add", new { subtype = "Redact", quadPoints = new[] { 18.0, 130, 150, 130, 18, 95, 150, 95 } }, ("page", "0"));
        Assert.Equal(1, Call(s, "POST", "/api/annot-redact-apply", null, ("page", "0")).GetProperty("applied").GetInt32());
        var pdf = s.Handle("GET", "/api/download", new Dictionary<string, string>()).Data;
        var doc = PdfDocument.Load(pdf);
        Assert.DoesNotContain("Hello", string.Concat(PortableDocument.Render.PortableDocument.ExtractText(doc.Pages[0]).Select(t => t.Text)));
        Assert.Equal(0, Call(s, "GET", "/api/annots").GetArrayLength());
    }
}
