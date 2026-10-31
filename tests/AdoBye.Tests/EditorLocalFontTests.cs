using System.Buffers.Binary;
using System.Text.Json;
using Xunit;

namespace AdoBye.Tests;

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
    [InlineData(FontLicense.PreviewPrint, true)]
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
        var result = catalog.Register(Font(FontLicense.PreviewPrint));
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
