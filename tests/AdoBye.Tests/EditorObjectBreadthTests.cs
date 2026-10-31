using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PortableDocument.Document;
using PortableDocument.Render;
using Pdf = PortableDocument.Render.PortableDocument;

public class EditorObjectBreadthTests
{
    static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    const string Fonts = "";

    static EditorSession Open(string content)
    {
        var session = new EditorSession(new FontCatalog());
        session.Handle("POST", "/api/open", new Dictionary<string, string>(), AdoBye.Tests.TestPdf.Build(content, Fonts), "o.pdf");
        return session;
    }

    static JsonArray Objects(EditorSession session) =>
        JsonNode.Parse(session.Handle("GET", "/api/page/0/objects", new Dictionary<string, string>()).Data)!.AsArray();

    static int FindFill(EditorSession session, string hex) =>
        Objects(session).Single(o => o!["fill"]?.GetValue<string>() == hex)!["index"]!.GetValue<int>();

    static void Edit(EditorSession session, int index, object edit) =>
        session.Handle("PUT", $"/api/page/0/objects/{index}", new Dictionary<string, string>(), JsonSerializer.SerializeToUtf8Bytes(edit, Web));

    static byte[] Download(EditorSession session) => session.Handle("GET", "/api/download", new Dictionary<string, string>()).Data;
    static string Content(byte[] pdf) => Encoding.Latin1.GetString(PdfDocument.Load(pdf).Pages[0].GetContents());

    static (int W, int H, byte[] Rgba) Pixels(byte[] pdf) =>
        PngWriter.Decode(Pdf.RenderPage(PdfDocument.Load(pdf).Pages[0], 72).ToPng());

    static void AssertSameRender(byte[] actual, string referenceContent, int tolerance = 12)
    {
        var a = Pixels(actual);
        var b = Pixels(AdoBye.Tests.TestPdf.Build(referenceContent, Fonts));
        Assert.Equal((a.W, a.H), (b.W, b.H));
        var differing = 0;
        for (var i = 0; i < a.Rgba.Length; i++) if (Math.Abs(a.Rgba[i] - b.Rgba[i]) > tolerance) differing++;
        Assert.True(differing <= a.Rgba.Length / 400, $"{differing} channel values differ from the reference render. Content: {Content(actual)}");
    }

    [Fact]
    public void RotationAboutTheEditedCentreMatchesAnalyticGeometry()
    {
        var session = Open("1 0 0 rg 20 20 60 20 re f");
        Edit(session, FindFill(session, "#ff0000"), new { rot = 90 });
        AssertSameRender(Download(session), "1 0 0 rg 60 0 m 60 60 l 40 60 l 40 0 l h f");
    }

    [Fact]
    public void HorizontalSkewMatchesAnalyticGeometry()
    {
        var session = Open("0 0 1 rg 20 20 60 20 re f");
        Edit(session, FindFill(session, "#0000ff"), new { skx = 45 });
        AssertSameRender(Download(session), "0 0 1 rg 30 20 m 90 20 l 70 40 l 10 40 l h f");
    }

    [Fact]
    public void RotationComposesWithScaleAndTranslationAndKeepsBookkeepingBalanced()
    {
        var session = Open("q 1 0 0 1 10 10 cm 0 1 0 rg 20 20 60 20 re f Q 0 0 0 rg 150 150 10 10 re f");
        var index = FindFill(session, "#00ff00");
        Edit(session, index, new { tx = 5, ty = 5, rot = 90 });
        var saved = Download(session);
        var content = Content(saved);
        Assert.Equal(CountOps(content, "q"), CountOps(content, "Q"));
        // Edits use top-left page space: moved by (5,5) the 60x20 rectangle is centred at (65,35) PDF-space; rotated 90 it is 20 wide and 60 tall.
        AssertSameRender(saved, "0 1 0 rg 55 5 20 60 re f 0 0 0 rg 150 150 10 10 re f");
    }

    [Fact]
    public void StrokeWidthAndDashAreAbsoluteEvenWhenTheObjectIsScaled()
    {
        var session = Open("1 0 0 RG 2 w 20 20 30 30 re S");
        var stroked = Assert.Single(Objects(session), o => o!["hasStroke"]!.GetValue<bool>())!["index"]!.GetValue<int>();
        Edit(session, stroked, new { sx = 2, sy = 2, ty = -200, width = 6, dash = new[] { 8.0, 4.0 } });
        var saved = Download(session);
        Assert.Contains("[", Content(saved));
        AssertSameRender(saved, "1 0 0 RG 6 w [8 4] 0 d 40 40 60 60 re S");
    }

    [Theory]
    [InlineData("front")]
    [InlineData("back")]
    public void ZOrderSwapsPaintOrderAndRestatesTheColoursItRelied(string direction)
    {
        var session = Open("1 0 0 rg 20 20 80 80 re f 0 0 1 rg 60 60 80 80 re f");
        var red = FindFill(session, "#ff0000"); var blue = FindFill(session, "#0000ff");
        Edit(session, direction == "front" ? red : blue, new { z = direction });
        var saved = Download(session);
        var content = Content(saved);
        Assert.Equal(CountOps(content, "q"), CountOps(content, "Q"));
        AssertSameRender(saved, direction == "front" ? "0 0 1 rg 60 60 80 80 re f 1 0 0 rg 20 20 80 80 re f" : "0 0 1 rg 60 60 80 80 re f 1 0 0 rg 20 20 80 80 re f");
        // Round trip: the saved file reopens with both objects and renders identically.
        var reopened = Open(content);
        Assert.Equal(2, Objects(reopened).Count(o => o!["hasFill"]!.GetValue<bool>()));
    }

    [Fact]
    public void ZOrderRefusesToCrossClipScopesOrChangedTransforms()
    {
        var content = "q 0 0 100 100 re W n 1 0 0 rg 0 0 200 200 re f Q 1 0 0 1 20 0 cm 0 0 1 rg 50 50 60 60 re f";
        var session = Open(content);
        var before = Download(session);
        Edit(session, FindFill(session, "#ff0000"), new { z = "front" });
        AssertSameRender(Download(session), content, 0);
        Edit(session, FindFill(session, "#0000ff"), new { z = "back" });
        AssertSameRender(Download(session), content, 0);
        Assert.Equal(CountOps(Content(Download(session)), "q"), CountOps(Content(Download(session)), "Q"));
        _ = before;
    }

    [Fact]
    public void InvalidRestyleRequestsAreRejected()
    {
        var session = Open("1 0 0 rg 20 20 60 20 re f");
        var index = FindFill(session, "#ff0000");
        Assert.Throws<ArgumentException>(() => Edit(session, index, new { z = "sideways" }));
        Assert.Throws<ArgumentException>(() => Edit(session, index, new { skx = 89 }));
        Assert.Throws<ArgumentException>(() => Edit(session, index, new { width = -1 }));
        Assert.Throws<ArgumentException>(() => Edit(session, index, new { dash = new[] { 0.0, 0.0 } }));
    }

    static int CountOps(string content, string op) =>
        System.Text.RegularExpressions.Regex.Matches(content, $@"(?<![A-Za-z0-9*]){System.Text.RegularExpressions.Regex.Escape(op)}(?![A-Za-z0-9*])").Count;
}
