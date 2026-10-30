using System.Text;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;
using DocumentRenderer = global::PortableDocument.Render.PortableDocument;

namespace AdoBye.Tests;

/// <summary>Arbitrary-geometry, shared-form, image and hidden-content redaction, proven by inspecting what the saved file still contains.</summary>
public class RedactionCoverageTests
{
    const string Font = "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>";

    static string AllStreamText(byte[] saved)
    {
        var sb = new StringBuilder(Encoding.Latin1.GetString(saved));
        var file = PdfDocument.Load(saved).File;
        foreach (var n in file.Xref.Keys.ToArray())
            if (file.Xref[n].InUse && file.GetObject(n) is PdfStream s) sb.Append('\n').Append(Encoding.Latin1.GetString(file.DecodeStream(s)));
        return sb.ToString();
    }

    // Renders the redacted page content without the final opaque box, so only surviving geometry shows.
    static RenderedPage RenderGeometry(byte[] saved)
    {
        var content = Encoding.Latin1.GetString(PdfDocument.Load(saved).Pages[0].GetContents());
        var end = content.LastIndexOf("\nQ\nn\nq 0 g\n", StringComparison.Ordinal);
        Assert.True(end > 0);
        return DocumentRenderer.RenderPage(PdfDocument.Load(TestPdf.Build(content[..end] + "\nQ")).Pages[0], 72);
    }

    static (byte R, byte G, byte B) Pixel(RenderedPage page, int x, int y)
    {
        var i = (y * page.Width + x) * 4;
        return (page.Rgba[i], page.Rgba[i + 1], page.Rgba[i + 2]);
    }

    static byte[] Redact(byte[] pdf, Redactor.BoxRect rect)
    {
        var editor = PdfEditor.Open(pdf);
        Redactor.Apply(editor, 0, [rect]);
        Assert.False(editor.CanSaveIncremental);
        return editor.Save(options: new PdfWriteOptions { Compress = true, ObjectStreams = true });
    }

    [Fact]
    public void Rotated_fill_is_cut_exactly_and_nothing_remains_under_the_rectangle()
    {
        var saved = Redact(TestPdf.Build("q 0.7071 0.7071 -0.7071 0.7071 100 20 cm 1 0 0 rg 0 0 60 60 re f Q"), new(90, 110, 20, 40));
        var geometry = RenderGeometry(saved);
        for (var y = 112; y < 148; y += 4)
            for (var x = 92; x < 108; x += 4) Assert.Equal((255, 255, 255), Pixel(geometry, x, y));
        Assert.Equal((255, 0, 0), Pixel(geometry, 100, 170));
        Assert.Equal((255, 0, 0), Pixel(geometry, 100, 100));
        Assert.Equal((255, 0, 0), Pixel(geometry, 70, 138));
        Assert.Equal((255, 0, 0), Pixel(geometry, 130, 138));
        Assert.DoesNotContain("0 0 60 60 re", AllStreamText(saved));
    }

    [Fact]
    public void Curved_even_odd_ring_keeps_its_hole_and_loses_only_the_rectangle()
    {
        var saved = Redact(TestPdf.Build("1 0 0 rg 20 20 m 20 100 100 100 100 20 c h 40 40 m 40 70 80 70 80 40 c h f*"), new(50, 110, 20, 80));
        var geometry = RenderGeometry(saved);
        Assert.Equal((255, 255, 255), Pixel(geometry, 60, 150));
        Assert.Equal((255, 255, 255), Pixel(geometry, 60, 125));
        Assert.Equal((255, 0, 0), Pixel(geometry, 25, 175));
        Assert.Equal((255, 0, 0), Pixel(geometry, 45, 142));
        Assert.Equal((255, 255, 255), Pixel(geometry, 45, 155));
        Assert.Equal((255, 0, 0), Pixel(geometry, 90, 175));
    }

    [Fact]
    public void Dashed_rotated_stroke_is_converted_and_cut_without_leaving_stroked_geometry_under_the_rectangle()
    {
        var source = "q 0.7071 0.7071 -0.7071 0.7071 50 20 cm 0 0 1 RG 6 w [10 5] 0 d 0 0 m 150 0 l S Q";
        var saved = Redact(TestPdf.Build(source), new(80, 110, 30, 50));
        var text = AllStreamText(saved);
        Assert.DoesNotContain("0 0 m 150 0 l", text);
        var geometry = RenderGeometry(saved);
        for (var y = 112; y < 158; y += 3)
            for (var x = 82; x < 108; x += 3) Assert.Equal((255, 255, 255), Pixel(geometry, x, y));
        Assert.Equal((0, 0, 255), Pixel(geometry, 64, 166));
        Assert.Equal((0, 0, 255), Pixel(geometry, 119, 111));
    }

    [Fact]
    public void Stroke_that_cannot_be_converted_faithfully_is_removed_whole()
    {
        var pdf = TestPdf.Build("/G1 gs 0 0 1 RG 4 w 20 100 m 180 100 l S", "/ExtGState << /G1 << /CA 0.5 /ca 1 >> >>");
        var saved = Redact(pdf, new(80, 90, 20, 20));
        var geometry = RenderGeometry(saved);
        Assert.Equal((255, 255, 255), Pixel(geometry, 30, 100));
        Assert.Equal((255, 255, 255), Pixel(geometry, 150, 100));
        Assert.DoesNotContain("20 100 m 180 100 l", AllStreamText(saved));
    }

    static byte[] SharedFormPdf(string pageContent)
    {
        const string form = "BT /F1 20 Tf 10 10 Td (SECRETFORM) Tj ET";
        var objs = $"5 0 obj\n<< /Type /XObject /Subtype /Form /BBox [0 0 200 50] /Resources << {Font} >> /Length {form.Length} >>\nstream\n{form}\nendstream\nendobj\n";
        return TestPdf.Build(pageContent, $"{Font} /XObject << /Fm1 5 0 R >>", objs, brokenXref: true);
    }

    [Fact]
    public void Shared_form_is_redacted_only_where_the_invocation_meets_the_rectangle()
    {
        var pdf = SharedFormPdf("q 1 0 0 1 0 100 cm /Fm1 Do Q q 1 0 0 1 0 0 cm /Fm1 Do Q");
        var saved = Redact(pdf, new(0, 50, 200, 50));
        var spans = DocumentRenderer.ExtractText(PdfDocument.Load(saved).Pages[0]);
        var survivor = Assert.Single(spans, s => s.Text.Contains("SECRETFORM"));
        Assert.True(survivor.Y > 150, "the untouched lower invocation keeps its text");
        Assert.Equal(1, AllStreamText(saved).Split("(SECRETFORM)").Length - 1);
    }

    [Fact]
    public void Form_used_only_inside_the_rectangle_leaves_no_copy_in_the_file()
    {
        var saved = Redact(SharedFormPdf("q 1 0 0 1 0 100 cm /Fm1 Do Q"), new(0, 50, 200, 50));
        Assert.DoesNotContain("SECRETFORM", AllStreamText(saved));
    }

    [Fact]
    public void Nested_shared_forms_are_copied_through_their_parent()
    {
        const string inner = "BT /F1 20 Tf 10 10 Td (SECRETINNER) Tj ET";
        const string outer = "/In Do";
        var objs = $"5 0 obj\n<< /Type /XObject /Subtype /Form /BBox [0 0 200 50] /Resources << /XObject << /In 6 0 R >> >> /Length {outer.Length} >>\nstream\n{outer}\nendstream\nendobj\n"
            + $"6 0 obj\n<< /Type /XObject /Subtype /Form /BBox [0 0 200 50] /Resources << {Font} >> /Length {inner.Length} >>\nstream\n{inner}\nendstream\nendobj\n";
        var pdf = TestPdf.Build("q 1 0 0 1 0 100 cm /Out Do Q q 1 0 0 1 0 0 cm /Out Do Q", "/XObject << /Out 5 0 R >>", objs, brokenXref: true);
        var saved = Redact(pdf, new(0, 50, 200, 50));
        var spans = DocumentRenderer.ExtractText(PdfDocument.Load(saved).Pages[0]);
        var survivor = Assert.Single(spans, s => s.Text.Contains("SECRETINNER"));
        Assert.True(survivor.Y > 150);
        Assert.Equal(1, AllStreamText(saved).Split("(SECRETINNER)").Length - 1);
    }

    static byte[] ImagePdf(string content, string image, string extra = "") =>
        TestPdf.Build(content, "/XObject << /Im1 7 0 R >>", $"7 0 obj\n<< /Type /XObject /Subtype /Image {extra} >>\nstream\n{image}\nendstream\nendobj\n", brokenXref: true);

    [Fact]
    public void Image_pixels_under_the_rectangle_are_not_recoverable_from_the_saved_file()
    {
        const string pixels = "\u00ab\u00cd\u00ef\u00ab\u00cd\u00ef\u0010\u0020\u0030\u0010\u0020\u0030";
        var saved = Redact(ImagePdf("q 200 0 0 50 0 100 cm /Im1 Do Q", pixels,
            "/Width 4 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Length 12"), new(0, 50, 100, 50));
        var all = AllStreamText(saved);
        Assert.DoesNotContain("\u00ab\u00cd\u00ef", all);
        Assert.Contains("\u0010\u0020\u0030\u0010\u0020\u0030", all);
        var page = DocumentRenderer.RenderPage(PdfDocument.Load(saved).Pages[0], 72);
        Assert.Equal((0, 0, 0), Pixel(page, 40, 75));
        Assert.Equal((0x10, 0x20, 0x30), Pixel(page, 160, 75));
    }

    [Fact]
    public void Inline_image_is_partially_blanked_and_the_original_bytes_are_gone()
    {
        const string source = "q 200 0 0 50 0 100 cm BI /W 4 /H 1 /CS /RGB /BPC 8 ID \u00ab\u00cd\u00ef\u00ab\u00cd\u00ef\u0010\u0020\u0030\u0010\u0020\u0030\nEI Q";
        var saved = Redact(TestPdf.Build(source), new(0, 50, 100, 50));
        var all = AllStreamText(saved);
        Assert.DoesNotContain("\u00ab\u00cd\u00ef", all);
        Assert.DoesNotContain("BI /W", all);
        var page = DocumentRenderer.RenderPage(PdfDocument.Load(saved).Pages[0], 72);
        Assert.Equal((0, 0, 0), Pixel(page, 40, 75));
        Assert.Equal((0x10, 0x20, 0x30), Pixel(page, 160, 75));
    }

    [Fact]
    public void Stencil_mask_keeps_only_the_uncovered_painted_samples()
    {
        // 0x55 = 01010101: samples 0 paint, so columns 0,2,4,6 are painted.
        var pdf = TestPdf.Build("0 0 1 rg q 80 0 0 40 0 100 cm /Im1 Do Q", "/XObject << /Im1 7 0 R >>",
            "7 0 obj\n<< /Type /XObject /Subtype /Image /Width 8 /Height 1 /ImageMask true /BitsPerComponent 1 /Length 1 >>\nstream\nU\nendstream\nendobj\n", brokenXref: true);
        var saved = Redact(pdf, new(0, 60, 40, 40));
        var doc = PdfDocument.Load(saved);
        var mask = doc.File.Xref.Keys.Where(n => doc.File.Xref[n].InUse).Select(n => doc.File.GetObject(n)).OfType<PdfStream>()
            .Single(s => doc.File.Resolve(s.Dict.Get("ImageMask")) is PdfBoolean { Value: true });
        var data = ImageDecoder.Decode(doc.File, mask, new PdfDictionary(new Dictionary<string, PdfObject>()), out var isMask, out _)!;
        Assert.True(isMask);
        var painted = Enumerable.Range(0, 8).Select(x => data.Rgba[x * 4 + 3] != 0).ToArray();
        Assert.Equal([false, false, false, false, true, false, true, false], painted);
    }

    [Fact]
    public void Content_hidden_by_optional_content_is_redacted_too()
    {
        var objs = "5 0 obj\n<< /Type /OCG /Name (Hidden) >>\nendobj\n";
        var pdf = TestPdf.Build("/OC /Oc BDC BT /F1 12 Tf 10 100 Td (HIDDENSECRET) Tj ET EMC", $"{Font} /Properties << /Oc 5 0 R >>", objs, brokenXref: true,
            extraCatalog: "/OCProperties << /OCGs [5 0 R] /D << /BaseState /ON /OFF [5 0 R] >> >>");
        var saved = Redact(pdf, new(0, 75, 200, 40));
        var all = AllStreamText(saved);
        Assert.DoesNotContain("HIDDENSECRET", all);
        Assert.DoesNotContain(Convert.ToHexString(Encoding.ASCII.GetBytes("HIDDEN")), all, StringComparison.OrdinalIgnoreCase);
    }
}
