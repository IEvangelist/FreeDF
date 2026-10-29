using Pdf.Document;
using Pdf.Render;
using Xunit;

namespace Pdf.Tests;

public class EditorTests
{
    static string Fx(params string[] p) => Path.Combine([AppContext.BaseDirectory, "fixtures", .. p]);
    static PdfDocument Features() => PdfDocument.Open(Fx("features", "features.pdf"));
    static PdfDocument Cff() => PdfDocument.Open(Fx("fonts", "cff.pdf"));
    static PdfDocument Reload(byte[] b) => PdfDocument.Load(b);

    static byte[] Render(PdfDocument d, int i) => PdfRenderer.RenderPage(d.Pages[i], 72).Rgba;

    [Fact]
    public void Roundtrip_preserves_structure_and_rendering()
    {
        var src = Cff();
        var saved = Reload(PdfEditor.Open(src).Save());
        Assert.Single(saved.Pages);
        Assert.Equal(Render(src, 0), Render(saved, 0));

        var f = Reload(PdfEditor.Open(Features()).Save());
        Assert.Equal(3, f.Pages.Count);
        Assert.Equal(1, f.GetOutlines()[1].PageIndex);
        Assert.Contains(f.GetFormFields(), x => x.FullName == "firstName" && x.Value == "Ada");
        Assert.Equal("A-5", f.GetPageLabel(1));
        Assert.Single(f.GetEmbeddedFiles());
        Assert.Equal("Feature Doc", f.Info("Title"));
    }

    [Fact]
    public void Delete_move_rotate()
    {
        var e = PdfEditor.Open(Features());
        e.DeletePage(0);
        e.MovePage(0, 1);
        e.SetRotation(0, 90);
        var d = Reload(e.Save());
        Assert.Equal(2, d.Pages.Count);
        Assert.Equal(90, d.Pages[0].Rotate);
        var o = d.GetOutlines();
        Assert.Null(o[0].PageIndex);          // chapter 1 pointed at the deleted page
        Assert.Equal(0, o[2].PageIndex);      // chapter 3 is now first (moved)
        Assert.Equal(1, o[1].PageIndex);
    }

    [Fact]
    public void Merge_and_split()
    {
        var m = PdfEditor.Create();
        m.ImportPages(Features(), [2, 0]);
        m.ImportPages(Cff());
        var d = Reload(m.Save());
        Assert.Equal(3, d.Pages.Count);
        Assert.Equal(Render(Cff(), 0), Render(d, 2));
        Assert.Equal(Render(Features(), 2), Render(d, 0));

        var one = Reload(PdfEditor.Extract(Features(), [1]).Save());
        Assert.Single(one.Pages);
        Assert.Equal(Render(Features(), 1), Render(one, 0));
    }

    [Fact]
    public void Saves_encrypted_source_decrypted_and_sets_info()
    {
        var path = Directory.GetFiles(Fx("encrypted"), "*-empty.pdf").First();
        var e = PdfEditor.Open(PdfDocument.Open(path));
        e.SetInfo("Title", "Caf\u00E9");
        var d = Reload(e.Save());
        Assert.False(d.File.IsEncrypted);
        Assert.Equal("Caf\u00E9", d.Info("Title"));
        Assert.Equal((255, 0, 0), (Render(d, 0)[(150 * 200 + 50) * 4], Render(d, 0)[(150 * 200 + 50) * 4 + 1], Render(d, 0)[(150 * 200 + 50) * 4 + 2]));
    }

    [Fact]
    public void Filled_field_appearance_is_rendered()
    {
        var doc = Features();
        var pageIdx = Enumerable.Range(0, doc.Pages.Count).First(i => doc.Pages[i].GetAnnotations().Any(a => a.Subtype == "Widget"));
        var e = PdfEditor.Open(doc);
        var before = PdfRenderer.RenderPage(Reload(e.Save()).Pages[pageIdx], 72).Rgba;
        e.SetFieldValue("firstName", "Grace Hopper");
        var after = PdfRenderer.RenderPage(Reload(e.Save()).Pages[pageIdx], 72).Rgba;
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Set_field_value_roundtrips_and_generates_appearance()
    {
        var e = PdfEditor.Open(Features());
        Assert.True(e.SetFieldValue("firstName", "Grace (Hopper)"));
        Assert.True(e.SetFieldValue("agree", "Yes"));
        Assert.False(e.SetFieldValue("nope", "x"));
        var f = Reload(e.Save());
        var fields = f.GetFormFields();
        Assert.Contains(fields, x => x.FullName == "firstName" && x.Value == "Grace (Hopper)");
        Assert.Contains(fields, x => x.FullName == "agree" && x.Value == "Yes");
    }

    [Fact]
    public void Edit_objects_moves_resizes_recolours_and_deletes_painted_objects()
    {
        const string content = "q 2 0 0 2 10 10 cm 1 0 0 rg 0 0 20 10 re f Q 0 0 1 rg 100 100 30 30 re f BT /F1 12 Tf 5 5 Td (Hi) Tj ET";
        var bytes = TestPdf.Build(content, "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>");
        var page = PdfDocument.Load(bytes).Pages[0];
        var (spans, objs) = PdfRenderer.Analyze(page);
        Assert.Equal(2, objs.Count);
        Assert.Equal((10, 50), (objs[0].X0, objs[0].X1));
        Assert.Equal((170, 190), (objs[0].Y0, objs[0].Y1));

        // Move +30 right and scale x by 2 about the origin (x' = 2x + 5); box space is y-down but the matrix is applied through the object's CTM.
        var c = objs[0].Ctm; var m = c.Then(new Pdf.Content.Matrix(2, 0, 0, 1, 5, 0)).Then(c.Invert());
        var edited = ContentRewriter.EditObjects(page.GetContents(), [
            new ContentRewriter.ObjectEdit(objs[0].Start, objs[0].End, false, [m.A, m.B, m.C, m.D, m.E, m.F], [0, 1, 0], null),
            new ContentRewriter.ObjectEdit(objs[1].Start, objs[1].End, true, null, null, null)]);
        var e = PdfEditor.Open(bytes);
        e.SetContentOverride(0, edited);
        var (spans2, objs2) = PdfRenderer.Analyze(PdfDocument.Load(e.Save()).Pages[0]);
        Assert.Single(objs2);
        Assert.Equal(25, objs2[0].X0, 3);
        Assert.Equal(105, objs2[0].X1, 3);
        Assert.Equal(170, objs2[0].Y0, 3);
        Assert.Equal(new Pdf.Content.RgbColor(0, 1, 0), objs2[0].Fill);
        Assert.Equal(spans.Select(s => s.Text), spans2.Select(s => s.Text));
    }

    [Fact]
    public void Redact_removes_text_and_objects_under_the_rect_and_keeps_the_rest()
    {
        const string content = "BT /F1 12 Tf 10 100 Td (Secret) Tj ET BT /F1 12 Tf 10 20 Td (Keep) Tj ET 1 0 0 rg 150 100 20 20 re f 0 0 1 rg 150 20 20 20 re f";
        var bytes = TestPdf.Build(content, "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>");
        var doc = PdfDocument.Load(bytes);
        var red = Redactor.Redact(doc.Pages[0], [new Redactor.BoxRect(0, 70, 200, 50)]);
        var text = System.Text.Encoding.Latin1.GetString(red);
        Assert.DoesNotContain("Secret", text);
        Assert.Contains("Keep", text);

        var e = PdfEditor.Open(bytes);
        e.SetContentOverride(0, red);
        var (spans, objs) = PdfRenderer.Analyze(PdfDocument.Load(e.Save()).Pages[0]);
        Assert.Equal(["Keep"], spans.Select(s => s.Text.Trim()).Where(t => t.Length > 0).ToArray());
        Assert.Contains(objs, o => o.Fill == new Pdf.Content.RgbColor(0, 0, 1));
        Assert.DoesNotContain(objs, o => o.Fill == new Pdf.Content.RgbColor(1, 0, 0));
        Assert.Contains(objs, o => o.Fill == new Pdf.Content.RgbColor(0, 0, 0) && o.Y0 >= 69 && o.Y1 <= 121);
    }

    [Fact]
    public void Extract_text_and_remove_operator_keeps_following_text_in_place()
    {
        const string content = "BT /F1 20 Tf 10 100 Td (Hello) Tj 0 -30 Td [(Wor) -100 (ld)] TJ ( Again) Tj ET 1 0 0 rg 0 0 5 5 re f";
        var bytes = TestPdf.Build(content, "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>");
        var page = PdfDocument.Load(bytes).Pages[0];
        var spans = PdfRenderer.ExtractText(page);
        Assert.Equal(["Hello", "World", " Again"], spans.Select(s => s.Text));
        Assert.Equal(10, spans[0].X, 3);
        Assert.Equal(200 - 100, spans[0].Y, 3);
        Assert.Equal(20, spans[0].Size, 3);

        var edited = ContentRewriter.RemoveText(page.GetContents(), new Dictionary<int, double> { [1] = spans[1].Advance });
        var text = System.Text.Encoding.Latin1.GetString(edited);
        Assert.DoesNotContain("Wor", text);
        Assert.Contains("(Hello) Tj", text);
        Assert.Contains("1 0 0 rg", text);

        var e = PdfEditor.Open(bytes);
        e.SetContentOverride(0, edited);
        var after = PdfRenderer.ExtractText(PdfDocument.Load(e.Save()).Pages[0]);
        Assert.Equal(3, after.Count);
        Assert.Equal("Hello", after[0].Text);
        Assert.True(string.IsNullOrWhiteSpace(after[1].Text));
        Assert.Equal(" Again", after[2].Text);
        Assert.Equal(spans[2].X, after[2].X, 2);
    }

    [Fact]
    public void Overlay_draws_on_top_and_base_save_excludes_it()
    {
        var e = PdfEditor.Open(Cff());
        var box = Reload(e.Save(false)).Pages[0].MediaBox;
        var rect = System.Text.Encoding.ASCII.GetBytes($"1 0 0 rg {box.Left} {box.Bottom} 20 20 re f");
        e.SetOverlay(0, rect);
        var with = Render(Reload(e.Save()), 0);
        var without = Render(Reload(e.Save(false)), 0);
        var h = (int)box.Height; var w = (int)box.Width;
        var px = ((h - 5) * w + 5) * 4;
        Assert.Equal((255, 0, 0), (with[px], with[px + 1], with[px + 2]));
        Assert.NotEqual((255, 0, 0), (without[px], without[px + 1], without[px + 2]));
    }
}
