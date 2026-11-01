using System.Text.Json;
using PortableDocument.Document;
using Pdf = PortableDocument.Render.PortableDocument;

/// <summary>Render replicas open GET /api/snapshot; it must reproduce exactly what /api/page/N.png renders.</summary>
public class SnapshotRouteTests
{
    static EditorSession.Reply Call(EditorSession s, string method, string path, object? body = null, params (string, string)[] query)
        => s.Handle(method, path, query.ToDictionary(x => x.Item1, x => x.Item2),
            body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void SnapshotRendersIdenticallyToPageImagesAndCarriesTheVersion(string bare)
    {
        var source = FreeDF.Tests.TestPdf.Build("BT /F1 18 Tf 30 90 Td (Original text) Tj ET 1 0 0 rg 20 20 60 40 re f",
            "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>");
        var session = new EditorSession(new FontCatalog());
        session.Handle("POST", "/api/open", new Dictionary<string, string>(), source, "doc.pdf");
        Call(session, "PUT", "/api/page/0/items", new[] { new Item("n", "text", 40, 20, 80, 0, "Added", "Helvetica", 12, false, false, "#0000ff", "", "", 0, 1, null, 0, 0, null) });

        var snapshot = Call(session, "GET", "/api/snapshot", query: ("bare", bare));
        using var state = JsonDocument.Parse(Call(session, "GET", "/api/state").Data);
        Assert.Equal("v" + state.RootElement.GetProperty("version").GetInt32(), snapshot.FileName);
        var expected = Call(session, "GET", "/api/page/0.png", query: [("dpi", "96"), ("bare", bare)]).Data;
        Assert.Equal(expected, Pdf.RenderPage(PdfDocument.Load(snapshot.Data).Pages[0], 96).ToPng());
    }
}
