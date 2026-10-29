using Pdf.Core;
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

    static byte[] FormPdf()
    {
        const string s = "%PDF-2.0\n" +
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R 5 0 R 6 0 R] >> >>\nendobj\n" +
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 300] /Annots [4 0 R 5 0 R 6 0 R] >>\nendobj\n" +
            "4 0 obj\n<< /Type /Annot /Subtype /Widget /FT /Tx /T (ml) /Ff 4096 /Rect [10 150 150 290] /MK << /BG [1 1 0.8] /BC [0 0 0] >> /P 3 0 R >>\nendobj\n" +
            "5 0 obj\n<< /Type /Annot /Subtype /Widget /FT /Tx /T (comb) /Ff 16777216 /MaxLen 8 /Rect [10 100 150 130] /MK << /BC [0 0 1] >> /P 3 0 R >>\nendobj\n" +
            "6 0 obj\n<< /Type /Annot /Subtype /Widget /FT /Ch /Ff 131072 /T (combo) /Opt [[(a) (Alpha)] [(b) (Beta)]] /Rect [10 50 150 80] /P 3 0 R >>\nendobj\n" +
            "trailer\n<< /Root 1 0 R /Size 7 >>\n%%EOF\n";
        return System.Text.Encoding.ASCII.GetBytes(s);
    }

    [Fact]
    public void Object_streams_compression_and_encryption_roundtrip()
    {
        var src = Features();
        var e = PdfEditor.Open(src);
        var plain = e.Save();
        var packed = e.Save(options: new PdfWriteOptions { Compress = true, ObjectStreams = true });
        Assert.True(packed.Length < plain.Length);
        var d = Reload(packed);
        Assert.Equal(src.Pages.Count, d.Pages.Count);
        for (var i = 0; i < d.Pages.Count; i++) Assert.Equal(Render(Reload(plain), i), Render(d, i));

        var locked = e.Save(options: new PdfWriteOptions { Compress = true, ObjectStreams = true, Encryption = new PdfEncryption { UserPassword = "u", OwnerPassword = "o", Permissions = -3904 } });
        Assert.Throws<PdfPasswordException>(() => PdfDocument.Load(locked, "wrong"));
        var un = PdfDocument.Load(locked, "u");
        Assert.True(un.File.IsEncrypted);
        Assert.Equal(Render(Reload(plain), 0), Render(un, 0));
        Assert.Equal(Render(Reload(plain), 0), Render(PdfDocument.Load(locked, "o"), 0));

        var classic = e.Save(options: new PdfWriteOptions { Encryption = new PdfEncryption { UserPassword = "x" } });
        Assert.Equal(Render(Reload(plain), 0), Render(PdfDocument.Load(classic, "x"), 0));
    }

    [Theory]
    [InlineData("cff")]
    [InlineData("features")]
    public void Incremental_save_preserves_original_bytes_and_applies_edits(string which)
    {
        var path = which == "cff" ? Fx("fonts", "cff.pdf") : Fx("features", "features.pdf");
        var original = File.ReadAllBytes(path);
        var e = PdfEditor.Open(PdfDocument.Load(original));
        Assert.True(e.CanSaveIncremental);
        e.SetInfo("Title", "Incremental");
        e.SetRotation(0, 90);
        var upd = e.SaveIncremental();
        Assert.True(upd.Length > original.Length);
        Assert.True(upd.AsSpan(0, original.Length).SequenceEqual(original));
        var d = Reload(upd);
        Assert.Equal("Incremental", d.Info("Title"));
        Assert.Equal(90, d.Pages[0].Rotate);
        Assert.Equal(PdfDocument.Load(original).Pages.Count, d.Pages.Count);
    }

    [Fact]
    public void Multiline_comb_and_choice_fields_get_appearances_with_borders()
    {
        var e = PdfEditor.Open(PdfDocument.Load(FormPdf()));
        var before = Render(Reload(e.Save()), 0);
        Assert.True(e.SetFieldValue("ml", "The quick brown fox jumps over the lazy dog again and again"));
        Assert.True(e.SetFieldValue("comb", "ABC"));
        Assert.True(e.SetFieldValue("combo", "b"));
        var d = Reload(e.Save());
        var after = Render(d, 0);
        Assert.NotEqual(before, after);
        // MK background (light yellow) is drawn inside the multiline box: pixel at (20, 300-160)
        var o = (140 * 300 + 20) * 4;
        Assert.True(after[o] > 240 && after[o + 1] > 240 && after[o + 2] < 230);
        Assert.Contains(d.GetFormFields(), x => x.FullName == "combo" && x.Value == "b");
    }

    [Fact]
    public void Annotations_without_AP_get_default_appearances()
    {
        const string s = "%PDF-2.0\n" +
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n" +
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Annots [4 0 R 5 0 R] >>\nendobj\n" +
            "4 0 obj\n<< /Type /Annot /Subtype /Highlight /Rect [20 150 120 170] /QuadPoints [20 170 120 170 20 150 120 150] /C [1 0 0] >>\nendobj\n" +
            "5 0 obj\n<< /Type /Annot /Subtype /Square /Rect [20 20 100 80] /C [0 0 1] /IC [0 1 0] >>\nendobj\n" +
            "trailer\n<< /Root 1 0 R /Size 6 >>\n%%EOF\n";
        var px = Render(PdfDocument.Load(System.Text.Encoding.ASCII.GetBytes(s)), 0);
        int At(int x, int y) => (y * 200 + x) * 4;
        Assert.True(px[At(60, 40)] > 200 && px[At(60, 40) + 1] < 60);   // highlight (red) over white (multiply)
        Assert.True(px[At(60, 140)] < 60 && px[At(60, 140) + 1] > 200); // square interior (green)
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
    public void Redact_removes_caption_through_editor_save()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "showcase.pdf");
        var e = PdfEditor.Open(File.ReadAllBytes(path));
        var page = PdfDocument.Load(e.Save(false)).Pages[0];
        e.SetContentOverride(0, Redactor.Redact(page, [new Redactor.BoxRect(140, 700, 340, 30)]));
        e.SetOverlay(0, []);
        var text = System.Text.Encoding.Latin1.GetString(PdfDocument.Load(e.Save()).Pages[0].GetContents());
        Assert.DoesNotContain("Vector", text);
    }

    [Fact]
    public void Redact_removes_caption_from_showcase()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "showcase.pdf");
        var page = PdfDocument.Load(File.ReadAllBytes(path)).Pages[0];
        var (spans, _) = PdfRenderer.Analyze(page);
        var cap = spans.First(s => s.Text.StartsWith("Vector"));
        var outp = Redactor.Redact(page, [new Redactor.BoxRect(140, 700, 340, 30)]);
        var text = System.Text.Encoding.Latin1.GetString(outp);
        Assert.True(!text.Contains("Vector"), $"span y={cap.Y} size={cap.Size} x={cap.X}");
    }

    [Fact]
    public void Redact_survives_a_second_pass_through_the_editor()
    {
        var bytes = TestPdf.Build("BT /F1 12 Tf 10 100 Td (Secret) Tj ET", "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>");
        var e = PdfEditor.Open(bytes);
        e.SetContentOverride(0, null);
        var page = PdfDocument.Load(e.Save(false)).Pages[0];
        e.SetContentOverride(0, Redactor.Redact(page, [new Redactor.BoxRect(0, 70, 200, 50)]));
        e.SetOverlay(0, []);
        var text = System.Text.Encoding.Latin1.GetString(PdfDocument.Load(e.Save()).Pages[0].GetContents());
        Assert.DoesNotContain("Secret", text);
        Assert.Contains(" re\nf", text.Replace(" f\n", " re\nf"));
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
