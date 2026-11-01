using System.Buffers.Binary;
using System.Text.Json;
using Xunit;

namespace FreeDF.Tests;

/// <summary>Browser-supplied (Local Font Access) fonts: OS/2 fsType licensing, registration and the /api/font command.</summary>
public class EditorLocalFontTests
{
    static byte[] Font(int? fsType = null, byte[]? panose = null)
    {
        var data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "fonts", "SourceCodePro-Regular.otf"));
        var os2 = FontSubsetter.TableOffset(data, "OS/2");
        if (fsType is { } fs) BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(os2 + 8), (ushort)fs);
        panose?.CopyTo(data, os2 + 32);
        return data;
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(FontLicense.PreviewPrint, false)]
    [InlineData(FontLicense.Editable, true)]
    [InlineData(FontLicense.Editable | FontLicense.NoSubsetting, false)]
    [InlineData(FontLicense.Restricted, false)]
    [InlineData(FontLicense.PreviewPrint | FontLicense.BitmapOnly, false)]
    [InlineData(FontLicense.NoSubsetting, false)]
    public void FsTypeBitsDecideWhetherSubsetEmbeddingIsAllowed(int fsType, bool allowed)
    {
        var result = FontLicense.Check(Font(fsType));
        Assert.Equal(allowed, result.Allowed);
        Assert.Equal(fsType, result.FsType);
        Assert.Equal(allowed, result.Reason is null);
    }

    [Fact]
    public void RestrictedFontIsRefusedWithAReasonAndNeverEntersTheCatalog()
    {
        var catalog = new FontCatalog();
        var result = catalog.Register(Font(FontLicense.Restricted));
        Assert.False(result.Accepted);
        Assert.Contains("restricts embedding", result.Reason);
        Assert.False(catalog.Has(result.Family));
    }

    [Theory]
    [InlineData(new byte[] { 2, 11, 5, 9, 0, 0, 0, 0, 0, 0 }, "Courier")]
    [InlineData(new byte[] { 2, 2, 6, 3, 5, 4, 5, 2, 3, 4 }, "Times")]
    [InlineData(new byte[] { 2, 11, 6, 4, 2, 2, 2, 2, 2, 4 }, "Helvetica")]
    [InlineData(new byte[] { 5, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, "Helvetica")]
    public void RefusedFontsOfferAStandardSubstituteFromPanose(byte[] panose, string expected) =>
        Assert.Equal(expected, FontLicense.Substitute(Font(FontLicense.Restricted, panose)));

    [Fact]
    public void AcceptedFontIsEmbeddedAsAnInMemorySubset()
    {
        var catalog = new FontCatalog();
        var result = catalog.Register(Font(FontLicense.Editable));
        Assert.True(result.Accepted);
        Assert.Equal("Source Code Pro", result.Family);
        var face = catalog.Resolve("Source Code Pro", false, false);
        Assert.StartsWith("local:", face!.Path);
        Assert.True(catalog.Load(face).Font.LookupAny('A') > 0);

        var pdf = TestPdf.Build("", "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>");
        var editor = PortableDocument.Document.PdfEditor.Open(pdf);
        var box = PortableDocument.Document.PdfDocument.Load(pdf).Pages[0].CropBox;
        var item = new Item("t", "text", 20, 20, 0, 0, "Local", "Source Code Pro", 24, false, false, "#000000", "", "", 0, 1, "", 0, 0, null);
        var (content, resources) = new OverlayBuilder(editor, [], catalog).Build([item], box);
        editor.SetOverlay(0, content, resources);
        Assert.True(editor.Save().Length > pdf.Length + 1000);
    }

    [Fact]
    public void RegisteringAgainReplacesTheSameStyleAndKeepsOtherFamiliesListed()
    {
        var catalog = new FontCatalog();
        catalog.Register(Font(0));
        catalog.Register(Font(FontLicense.Editable));
        Assert.Single(catalog.List(), f => f.ToString()!.Contains("Source Code Pro"));
    }

    [Fact]
    public void TruncatedOrUnsupportedBytesAreRejectedClearly()
    {
        var catalog = new FontCatalog();
        Assert.Throws<ArgumentException>(() => catalog.Register([1, 2, 3]));
        Assert.Throws<ArgumentException>(() => catalog.Register(new byte[256]));
    }

    [Fact]
    public void FontCommandReportsAcceptanceRefusalAndSubstitute()
    {
        var session = new EditorSession(new FontCatalog());
        JsonElement Post(byte[] bytes) => JsonDocument.Parse(session.Handle("POST", "/api/font", new Dictionary<string, string> { ["ps"] = "SourceCodePro-Regular" }, bytes).Data).RootElement.Clone();
        var ok = Post(Font());
        Assert.True(ok.GetProperty("accepted").GetBoolean());
        Assert.Equal("Source Code Pro", ok.GetProperty("family").GetString());
        var refused = Post(Font(FontLicense.Restricted));
        Assert.False(refused.GetProperty("accepted").GetBoolean());
        Assert.Equal("Courier", refused.GetProperty("substitute").GetString());
        Assert.Contains("restricts embedding", refused.GetProperty("reason").GetString());
        var state = JsonDocument.Parse(session.Handle("GET", "/api/state", new Dictionary<string, string>()).Data).RootElement;
        Assert.False(state.GetProperty("dirty").GetBoolean());
    }

    [Fact]
    public void MissingLocalFontBlocksSavingUntilReloadedOrReplacedWithConsent()
    {
        var session = new EditorSession(new FontCatalog());
        var empty = new Dictionary<string, string>();
        session.Handle("POST", "/api/new", empty);
        var item = new Item("t", "text", 20, 30, 70, 0, "Local text", "Source Code Pro", 12, false, false, "#000000", null, null, 0, 1, null, 0, 0, null);
        session.Handle("PUT", "/api/page/0/items", empty, JsonSerializer.SerializeToUtf8Bytes(new { items = new[] { item }, historyAssets = Array.Empty<string>() }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        JsonElement Post(string path, byte[]? body = null, params (string, string)[] q) =>
            JsonDocument.Parse(session.Handle("POST", path, q.ToDictionary(x => x.Item1, x => x.Item2), body).Data).RootElement.Clone();
        var missing = Post("/api/require-fonts", JsonSerializer.SerializeToUtf8Bytes(new[] { "Source Code Pro" }));
        Assert.Equal("Source Code Pro", missing.GetProperty("missing")[0].GetString());
        var error = Assert.Throws<EditorConflictException>(() => session.Handle("GET", "/api/export/pdf", empty));
        Assert.Contains("Source Code Pro", error.Message);
        Assert.Throws<EditorConflictException>(() => session.Handle("GET", "/api/download", empty));

        Post("/api/font", Font(FontLicense.Editable), ("ps", "SourceCodePro-Regular"));
        Assert.True(session.Handle("GET", "/api/export/pdf", empty).Data.Length > 1000);

        var other = new EditorSession(new FontCatalog());
        other.Handle("POST", "/api/new", empty);
        other.Handle("PUT", "/api/page/0/items", empty, JsonSerializer.SerializeToUtf8Bytes(new { items = new[] { item }, historyAssets = Array.Empty<string>() }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        other.Handle("POST", "/api/require-fonts", empty, JsonSerializer.SerializeToUtf8Bytes(new[] { "Source Code Pro" }));
        var replaced = JsonDocument.Parse(other.Handle("POST", "/api/replace-font", new Dictionary<string, string> { ["from"] = "Source Code Pro", ["to"] = "Helvetica" }).Data).RootElement;
        Assert.Equal(1, replaced.GetProperty("changed").GetInt32());
        Assert.Empty(replaced.GetProperty("missing").EnumerateArray());
        Assert.NotEmpty(other.Handle("GET", "/api/export/pdf", empty).Data);
    }

    [Fact]
    public void SavedWithAnOlderVersionKeepsLaterEditsUnsaved()
    {
        var session = new EditorSession(new FontCatalog());
        var empty = new Dictionary<string, string>();
        session.Handle("POST", "/api/new", empty);
        int Version() => JsonDocument.Parse(session.Handle("GET", "/api/state", empty).Data).RootElement.GetProperty("version").GetInt32();
        bool Dirty() => JsonDocument.Parse(session.Handle("GET", "/api/state", empty).Data).RootElement.GetProperty("dirty").GetBoolean();
        var exported = Version();
        session.Handle("POST", "/api/blank/1", empty);
        session.Handle("POST", "/api/saved", new Dictionary<string, string> { ["version"] = exported.ToString() });
        Assert.True(Dirty());
        session.Handle("POST", "/api/saved", empty);
        Assert.False(Dirty());
    }
}
