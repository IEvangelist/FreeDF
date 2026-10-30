using System.Text;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;
using DocumentRenderer = global::PortableDocument.Render.PortableDocument;

namespace AdoBye.Tests;

public class VectorRedactionTests
{
    [Fact]
    public void Clips_simple_rectangle_fill_and_preserves_unaffected_paths_after_save()
    {
        const string source = "1 0 0 rg 20 20 80 40 re f 0 0 1 rg 120 20 20 20 re f";
        var editor = PdfEditor.Open(TestPdf.Build(source));
        Redactor.Apply(editor, 0, [new(50, 130, 20, 60)]);

        Assert.False(editor.CanSaveIncremental);
        Assert.Throws<InvalidOperationException>(() => editor.SaveIncremental());
        var saved = editor.Save(options: new PdfWriteOptions { Compress = true, ObjectStreams = true });
        var page = PdfDocument.Load(saved).Pages[0];
        var content = Encoding.Latin1.GetString(page.GetContents());
        Assert.DoesNotContain("20 20 80 40 re", content);
        Assert.Contains("20 20 30 40 re", content);
        Assert.Contains("70 20 30 40 re", content);
        Assert.Contains("120 20 20 20 re f", content);

        var red = DocumentRenderer.Analyze(page).Objects.Where(o => o.Fill == new RgbColor(1, 0, 0)).ToArray();
        var redObject = Assert.Single(red);
        Assert.Equal((20d, 100d), (redObject.X0, redObject.X1));

        var rendered = DocumentRenderer.RenderPage(page, 72);
        Assert.Equal((255, 0, 0), Pixel(rendered, 30, 150));
        Assert.Equal((0, 0, 0), Pixel(rendered, 60, 150));
        Assert.Equal((255, 0, 0), Pixel(rendered, 80, 150));
        Assert.Equal((0, 0, 255), Pixel(rendered, 130, 170));
    }

    [Fact]
    public void Removes_fully_covered_rectangle_and_clips_the_stroke_outline()
    {
        const string source = "1 0 0 rg 10 10 20 20 re f 1 0 0 RG 4 w 20 20 m 100 20 l S 0 1 0 rg 20 20 m 40 20 80 20 100 20 c 100 60 l 20 60 l h f";
        var editor = PdfEditor.Open(TestPdf.Build(source));
        Redactor.Apply(editor, 0, [new(0, 160, 50, 50), new(50, 175, 20, 10)]);

        var page = PdfDocument.Load(editor.Save()).Pages[0];
        var content = Encoding.Latin1.GetString(page.GetContents());
        Assert.DoesNotContain("10 10 20 20 re f", content);
        Assert.DoesNotContain("20 20 m 100 20 l S", content);
        Assert.DoesNotContain("20 20 m 40 20 80 20 100 20 c", content);
        var red = DocumentRenderer.Analyze(page).Objects.Where(o => o.Fill == new RgbColor(1, 0, 0)).ToArray();
        Assert.NotEmpty(red);
        Assert.All(red, o => Assert.True(o.X0 >= 70, $"red survivor {o.X0}..{o.X1}"));
        Assert.DoesNotContain(DocumentRenderer.Analyze(page).Objects, o => o.Stroke == new RgbColor(1, 0, 0) && o.HasStroke);
    }

    [Fact]
    public void Clips_axis_aligned_rectangle_after_a_page_transform()
    {
        const string source = "q 2 0 0 2 0 0 cm 1 0 0 rg 10 10 40 20 re f Q";
        var editor = PdfEditor.Open(TestPdf.Build(source));
        Redactor.Apply(editor, 0, [new(50, 130, 20, 60)]);

        var page = PdfDocument.Load(editor.Save()).Pages[0];
        var content = Encoding.Latin1.GetString(page.GetContents());
        Assert.DoesNotContain("10 10 40 20 re", content);
        Assert.Contains("10 10 15 20 re", content);
        Assert.Contains("35 10 15 20 re", content);
        var rendered = DocumentRenderer.RenderPage(page, 72);
        Assert.Equal((255, 0, 0), Pixel(rendered, 30, 150));
        Assert.Equal((0, 0, 0), Pixel(rendered, 60, 150));
        Assert.Equal((255, 0, 0), Pixel(rendered, 80, 150));
    }

    [Fact]
    public void Clips_simple_rectangle_used_as_a_standalone_clipping_path()
    {
        const string source = "10 10 100 100 re W n 0 1 0 rg 20 20 80 40 re f";
        var editor = PdfEditor.Open(TestPdf.Build(source));
        Redactor.Apply(editor, 0, [new(50, 130, 20, 60)]);

        var page = PdfDocument.Load(editor.Save()).Pages[0];
        var content = Encoding.Latin1.GetString(page.GetContents());
        Assert.DoesNotContain("10 10 100 100 re", content);
        Assert.DoesNotContain("20 20 80 40 re", content);
        Assert.Contains("W n", content);
        var rendered = DocumentRenderer.RenderPage(page, 72);
        Assert.Equal((0, 255, 0), Pixel(rendered, 30, 150));
        Assert.Equal((0, 0, 0), Pixel(rendered, 60, 150));
        Assert.Equal((0, 255, 0), Pixel(rendered, 80, 150));
    }

    [Fact]
    public void Unsupported_clip_path_fails_closed_without_retaining_its_geometry()
    {
        const string source = "q 20 20 m 100 20 l W S Q 0 0 1 rg 120 20 20 20 re f 0 1 0 rg 20 20 80 40 re f";
        var editor = PdfEditor.Open(TestPdf.Build(source));
        Redactor.Apply(editor, 0, [new(50, 130, 20, 60)]);

        var page = PdfDocument.Load(editor.Save()).Pages[0];
        var content = Encoding.Latin1.GetString(page.GetContents());
        Assert.DoesNotContain("20 20 m 100 20 l W S", content);
        Assert.Contains("0 0 m 0 0 l W n", content);
        var rendered = DocumentRenderer.RenderPage(page, 72);
        Assert.Equal((0, 0, 255), Pixel(rendered, 130, 170));
    }

    [Fact]
    public void Clips_a_polygon_clip_path_and_the_content_it_clips()
    {
        const string source = "q 10 10 m 110 10 l 110 110 l 10 110 l h W n 0 1 0 rg 0 0 200 200 re f Q";
        var editor = PdfEditor.Open(TestPdf.Build(source));
        Redactor.Apply(editor, 0, [new(50, 130, 20, 60)]);

        var page = PdfDocument.Load(editor.Save()).Pages[0];
        var rendered = DocumentRenderer.RenderPage(page, 72);
        Assert.Equal((0, 255, 0), Pixel(rendered, 30, 150));
        Assert.Equal((0, 0, 0), Pixel(rendered, 60, 150));
        Assert.Equal((0, 255, 0), Pixel(rendered, 80, 150));
        Assert.Equal((255, 255, 255), Pixel(rendered, 5, 150));
        Assert.Equal((255, 255, 255), Pixel(rendered, 115, 150));
    }

    [Fact]
    public void Clipped_fill_keeps_clip_paint_order_and_drops_redacted_geometry()
    {
        const string source = "1 0 0 rg 20 20 80 40 re W f 0 0 1 rg 10 10 120 80 re f";
        var editor = PdfEditor.Open(TestPdf.Build(source));
        Redactor.Apply(editor, 0, [new(50, 130, 20, 60)]);

        var page = PdfDocument.Load(editor.Save()).Pages[0];
        var content = Encoding.Latin1.GetString(page.GetContents());
        Assert.DoesNotContain("20 20 80 40 re", content);
        Assert.Contains("W f", content);
        var rendered = DocumentRenderer.RenderPage(page, 72);
        Assert.Equal((0, 0, 255), Pixel(rendered, 30, 150));
        Assert.Equal((0, 0, 0), Pixel(rendered, 60, 150));
        Assert.Equal((0, 0, 255), Pixel(rendered, 80, 150));
        Assert.Equal((255, 255, 255), Pixel(rendered, 15, 150));
        Assert.Equal((255, 255, 255), Pixel(rendered, 105, 150));
    }

    static (byte R, byte G, byte B) Pixel(RenderedPage page, int x, int y)
    {
        var i = (y * page.Width + x) * 4;
        return (page.Rgba[i], page.Rgba[i + 1], page.Rgba[i + 2]);
    }
}
