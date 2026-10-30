using System.Text.Json;
using System.Text.Json.Nodes;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;

namespace AdoBye.Tests;

public class EditorRecoveryTests
{
    static EditorSession Session() => new(new FontCatalog());
    static EditorSession.Reply Call(EditorSession s, string method, string path, object? body = null) =>
        s.Handle(method, path, new Dictionary<string, string>(), body is null ? null : JsonSerializer.SerializeToUtf8Bytes(body));
    static JsonNode Json(EditorSession.Reply reply) => JsonNode.Parse(reply.Data)!;
    static byte[] Raster(EditorSession s, int page = 0) => s.Handle("GET", $"/api/page/{page}.png",
        new Dictionary<string, string> { ["dpi"] = "40" }).Data;
    static Item Text(string text, long? orig = null) => new("text", "text", 20, 30, 70, 0, text, "Helvetica", 12,
        false, false, "#000000", null, null, 0, 1, null, 0, 0, orig);
    static Item Image(string id) => new("image", "image", 50, 60, 10, 10, null, null, 0,
        false, false, null, null, null, 0, 1, id, 0, 0);

    [Fact]
    public void RecoveryRestoresEditableItemsPagesMetadataImagesUndoAndClipboard()
    {
        var source = Session();
        Call(source, "POST", "/api/new");
        var png = PngWriter.Encode(1, 1, [255, 0, 0, 255]);
        var asset = Json(source.Handle("POST", "/api/asset", new Dictionary<string, string>(), png))["id"]!.GetValue<string>();
        Call(source, "PUT", "/api/page/0/items", new[] { Text("Pending text"), Image(asset) });
        Call(source, "POST", "/api/blank/1");
        Call(source, "POST", "/api/rotate/1/90");
        Call(source, "POST", "/api/info", new { title = "Recovered title", author = "Local author" });
        Call(source, "PUT", "/api/page/0/items", new ItemUpdate([Text("Pending text") with { Asset = "" }], [asset], [asset]));
        var before = Raster(source);
        var checkpoint = Call(source, "GET", "/api/recovery").Data;
        var restored = Session();
        var response = restored.Handle("POST", "/api/recovery", new Dictionary<string, string>(), checkpoint);
        Assert.NotNull(Json(response)["originalForms"]);
        Assert.Equal(before, Raster(restored));
        Assert.Equal(png, Call(restored, "GET", "/api/asset/" + asset).Data);
        Assert.Equal(2, Json(Call(restored, "GET", "/api/state"))["pageCount"]!.GetValue<int>());
        Assert.True(Json(Call(restored, "GET", "/api/state"))["dirty"]!.GetValue<bool>());
        var pdf = PdfDocument.Load(Call(restored, "GET", "/api/download").Data);
        Assert.Equal(90, pdf.Pages[1].Rotate);
        Assert.Equal("Recovered title", pdf.Info("Title"));
        Assert.Single(Json(Call(restored, "GET", "/api/page/0/items")).AsArray());
        Call(restored, "POST", "/api/asset-history");
        Assert.Equal(png, Call(restored, "GET", "/api/asset/" + asset).Data);
        Call(restored, "PUT", "/api/page/0/items", new[] { Text("Edited after resume"), Image(asset) });
        Assert.NotEqual(before, Raster(restored));
        Call(restored, "PUT", "/api/page/0/items", Array.Empty<Item>());
        Assert.Equal(png, Call(restored, "GET", "/api/asset/" + asset).Data);
        Call(restored, "POST", "/api/asset-clipboard", Array.Empty<string>());
        Assert.Throws<KeyNotFoundException>(() => Call(restored, "GET", "/api/asset/" + asset));
    }

    static byte[] FormFixture() => ObjPdf.Build(
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R /Resources << /XObject << /Fm 5 0 R >> >> >>",
        ObjPdf.Stream("0 0 1 rg 130 30 20 20 re f q 1.2 0 0.1 1 10 20 cm /Fm Do Q"),
        ObjPdf.Stream("1 0 0 rg 10 10 10 8 re f BT /F1 12 Tf 10 50 Td (Original) Tj ET",
            "/Type /XObject /Subtype /Form /BBox [0 0 100 100] /Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >>"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecoveryRemapsOriginalFormTextAndUniqueChildEditsWithoutFlattening(bool formChild)
    {
        var source = Session();
        source.Handle("POST", "/api/open", new Dictionary<string, string>(), FormFixture(), "form.pdf");
        var original = Json(Call(source, "GET", "/api/page/0/text")).AsArray().Single()!["index"]!.GetValue<long>();
        Call(source, "PUT", "/api/page/0/items", new[] { Text("Replacement", original) });
        var objects = Json(Call(source, "GET", "/api/page/0/objects")).AsArray();
        var selected = objects.First(o => o!["kind"]!.GetValue<string>() == "path" && o["inForm"]!.GetValue<bool>() == formChild)!;
        Call(source, "PUT", $"/api/page/0/objects/{selected["index"]}", new { sx = 1.2, sy = 1, tx = 6, ty = 2 });
        var before = Raster(source);
        var checkpoint = Call(source, "GET", "/api/recovery").Data;
        var restored = Session();
        var maps = Json(restored.Handle("POST", "/api/recovery", new Dictionary<string, string>(), checkpoint))["originalForms"]!;
        Assert.NotNull(maps[(original / 10_000_000).ToString()]);
        Assert.Equal(before, Raster(restored));
        Assert.NotNull(Json(Call(restored, "GET", "/api/page/0/items")).AsArray().Single()!["orig"]);
        Assert.Single(Json(Call(restored, "GET", "/api/page/0/objects")).AsArray(), o => o!["edit"] is not null);
        var again = Call(restored, "GET", "/api/recovery").Data;
        var twice = Session();
        twice.Handle("POST", "/api/recovery", new Dictionary<string, string>(), again);
        Assert.Equal(before, Raster(twice));
        Call(twice, "PUT", $"/api/page/0/objects/{selected["index"]}", new { });
        Call(twice, "PUT", "/api/page/0/items", Array.Empty<Item>());
        Assert.Equal(PortableDocument.Render.PortableDocument.RenderPage(PdfDocument.Load(FormFixture()).Pages[0], 40).ToPng(), Raster(twice));
    }

    [Theory]
    [InlineData("format")]
    [InlineData("page")]
    [InlineData("image")]
    [InlineData("missing")]
    [InlineData("form")]
    public void CorruptRecoveryDoesNotReplaceTheCurrentDocument(string corruption)
    {
        var session = Session();
        Call(session, "POST", "/api/new");
        Call(session, "PUT", "/api/page/0/items", new[] { Text("Keep current state") });
        var before = Raster(session);
        var record = Json(Call(session, "GET", "/api/recovery"));
        switch (corruption)
        {
            case "format": record["format"] = 99; break;
            case "page": record["pages"]![0]!["id"] = 99999; break;
            case "image": record["assets"]!["bad"] = new JsonObject { ["data"] = "AQID", ["mime"] = "image/png" }; break;
            case "missing": record["clipboard"]!.AsArray().Add("missing"); break;
            case "form": record["originalForms"]!["1"] = 999999; break;
        }
        Assert.ThrowsAny<Exception>(() => Call(session, "POST", "/api/recovery", record));
        Assert.Equal(before, Raster(session));
        Assert.Equal("Keep current state", Json(Call(session, "GET", "/api/page/0/items"))[0]!["text"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EncryptedInputOrImportExplicitlyDisablesPlaintextRecovery(bool import)
    {
        var bytes = PdfEditor.Open(FormFixture()).Save(options: new PdfWriteOptions {
            Encryption = new PdfEncryption { OwnerPassword = "not-persisted", Method = PdfEncryptionMethod.Rc4_40 } });
        var session = Session();
        if (import) Call(session, "POST", "/api/new");
        session.Handle("POST", import ? "/api/import" : "/api/open", new Dictionary<string, string>(), bytes);
        Assert.False(Json(Call(session, "GET", "/api/state"))["recoverySupported"]!.GetValue<bool>());
        Assert.Throws<NotSupportedException>(() => Call(session, "GET", "/api/recovery"));
        Call(session, "POST", "/api/new");
        Assert.NotEmpty(Call(session, "GET", "/api/recovery").Data);
    }

    [Fact]
    public void RecoveryPreservesFieldValuesAndCleanSaveState()
    {
        var fixture = ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [5 0 R] >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R /Annots [5 0 R] >>",
            ObjPdf.Stream(""),
            "<< /FT /Tx /T (Name) /V (Original) /Type /Annot /Subtype /Widget /Rect [10 10 100 30] /P 3 0 R >>");
        var source = Session();
        source.Handle("POST", "/api/open", new Dictionary<string, string>(), fixture);
        source.Handle("POST", "/api/field", new Dictionary<string, string> { ["n"] = "Name", ["v"] = "Recovered field" });
        Call(source, "POST", "/api/saved");
        var checkpoint = Call(source, "GET", "/api/recovery").Data;
        var restored = Session();
        restored.Handle("POST", "/api/recovery", new Dictionary<string, string>(), checkpoint);
        Assert.False(Json(Call(restored, "GET", "/api/state"))["dirty"]!.GetValue<bool>());
        Assert.Equal("Recovered field", Json(Call(restored, "GET", "/api/fields"))[0]!["value"]!.GetValue<string>());
        Assert.False(Json(Call(restored, "GET", "/api/state"))["canSave"]!.GetValue<bool>());
    }

    [Fact]
    public void MissingHistoryFormFailsBeforeReplacingCurrentState()
    {
        var session = Session();
        Call(session, "POST", "/api/new");
        var checkpoint = Call(session, "GET", "/api/recovery").Data;
        Call(session, "PUT", "/api/page/0/items", new[] { Text("Keep newer state") });
        var before = Raster(session);
        Assert.Throws<InvalidDataException>(() => session.Handle("POST", "/api/recovery",
            new Dictionary<string, string> { ["forms"] = "[99999]" }, checkpoint));
        Assert.Equal(before, Raster(session));
    }
}
