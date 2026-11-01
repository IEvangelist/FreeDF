using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;
using DocumentRenderer = global::PortableDocument.Render.PortableDocument;

namespace FreeDF.Tests;

public class ExistingObjectTransformTests
{
    [Theory]
    [InlineData("image")]
    [InlineData("path")]
    [InlineData("inline")]
    public void AnalysisWithoutPaintingMatchesPaintedObjectAndTextMetadata(string kind)
    {
        var page = Fixture(Target(kind) + " " + Rest, 0).Pages[0];
        using var backend = new SoftwareRasterizer(1, 1);
        var painted = new ContentInterpreter(page.Document.File, backend) { TextSpans = [], Objects = [], DrawAnnotations = false };
        var box = page.CropBox;
        painted.Render(page, new Matrix(1, 0, 0, -1, -box.Left, box.Top));
        var (spans, objects) = DocumentRenderer.Analyze(page);
        Assert.Equal(painted.Objects, objects);
        Assert.Equal(painted.TextSpans.Select(s => (s.Text, s.X, s.Y, s.Width, s.Advance, s.Form, s.Index)),
            spans.Select(s => (s.Text, s.X, s.Y, s.Width, s.Advance, s.Form, s.Index)));
    }

    const string Rest = "0 0 1 rg 140 30 25 15 re f BT /F1 12 Tf 100 150 Td (Keep) Tj ET";
    const double Sx = 1.5, Sy = 2, Tx = 15, Ty = 20;

    static string Target(string kind) => kind switch
    {
        "image" => "q 30 0 0 20 30 100 cm /Im Do Q",
        "inline" => "q 30 0 0 20 30 100 cm BI /W 1 /H 1 /BPC 8 /CS /RGB ID \xff\0\0 EI Q",
        _ => "q 2 0 0 2 30 100 cm 1 0 0 rg 0 0 10 8 re f Q",
    };

    static PdfDocument Fixture(string content, int rotation)
    {
        var editor = PdfEditor.Open(TestPdf.Build(content,
            "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>",
            mediaBox: "[10 20 210 220]"));
        var image = editor.AddObject(new PdfStream(new PdfDictionary(new Dictionary<string, PdfObject>
        {
            ["Type"] = new PdfName("XObject"), ["Subtype"] = new PdfName("Image"),
            ["Width"] = new PdfInteger(1), ["Height"] = new PdfInteger(1),
            ["BitsPerComponent"] = new PdfInteger(8), ["ColorSpace"] = new PdfName("DeviceRGB"),
            ["Length"] = new PdfInteger(3),
        }), [255, 0, 0]));
        editor.AddPageXObject(0, "Im", image);
        editor.SetRotation(0, rotation);
        return PdfDocument.Load(editor.Save());
    }

    [Theory]
    [InlineData("image", 0)]
    [InlineData("image", 90)]
    [InlineData("inline", 0)]
    [InlineData("path", 0)]
    [InlineData("path", 90)]
    public void Move_and_resize_survive_save_reopen_without_changing_other_paint(string kind, int rotation)
    {
        var target = Target(kind);
        // A second invocation of the same image must not change with the selected invocation.
        var rest = kind == "image" ? "q 20 0 0 15 150 60 cm /Im Do Q " + Rest : Rest;
        var source = Fixture(target + " " + rest, rotation);
        var (spans, objects) = DocumentRenderer.Analyze(source.Pages[0]);
        var selected = objects[0];
        var transform = selected.Ctm.Then(new Matrix(Sx, 0, 0, Sy, Tx, Ty)).Then(selected.Ctm.Invert());
        var rewritten = ContentRewriter.EditObjects(source.Pages[0].GetContents(),
            [new(selected.Start, selected.End, false,
                [transform.A, transform.B, transform.C, transform.D, transform.E, transform.F], null, null)]);
        var editor = PdfEditor.Open(source);
        editor.SetContentOverride(0, rewritten);

        var box = source.Pages[0].CropBox;
        var expectedContent = FormattableString.Invariant(
            $"q {Sx} 0 0 {Sy} {box.Left * (1 - Sx) + Tx} {box.Top * (1 - Sy) - Ty} cm {target} Q {rest}");
        var expected = Fixture(expectedContent, rotation);
        foreach (var packed in new[] { false, true })
        {
            var saved = editor.Save(options: new PdfWriteOptions { Compress = packed, ObjectStreams = packed });
            var reopened = PdfDocument.Load(saved);
            var (afterSpans, after) = DocumentRenderer.Analyze(reopened.Pages[0]);
            Assert.Equal(objects.Count, after.Count);
            Assert.Equal(Sx * selected.X0 + Tx, after[0].X0, 4);
            Assert.Equal(Sy * selected.Y0 + Ty, after[0].Y0, 4);
            Assert.Equal(Sx * selected.X1 + Tx, after[0].X1, 4);
            Assert.Equal(Sy * selected.Y1 + Ty, after[0].Y1, 4);
            Assert.Equal(objects.Skip(1).Select(o => (o.Kind, o.X0, o.Y0, o.X1, o.Y1)),
                after.Skip(1).Select(o => (o.Kind, o.X0, o.Y0, o.X1, o.Y1)));
            Assert.Equal(spans.Select(s => (s.Text, s.X, s.Y)), afterSpans.Select(s => (s.Text, s.X, s.Y)));
            Assert.Equal(DocumentRenderer.RenderPage(expected.Pages[0], 72).Rgba,
                DocumentRenderer.RenderPage(reopened.Pages[0], 72).Rgba);
            Assert.NotEqual(DocumentRenderer.RenderPage(source.Pages[0], 72).Rgba,
                DocumentRenderer.RenderPage(reopened.Pages[0], 72).Rgba);
            if (kind == "image")
            {
                Assert.Equal(after[0].Ref, after[1].Ref);
                Assert.Equal(new byte[] { 255, 0, 0 }, reopened.File.DecodeStream(after[0].Stream!));
            }
        }

        editor.SetContentOverride(0, null);
        Assert.Equal(DocumentRenderer.RenderPage(source.Pages[0], 72).Rgba,
            DocumentRenderer.RenderPage(PdfDocument.Load(editor.Save()).Pages[0], 72).Rgba);
    }
}
