using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;

namespace AdoBye.Tests;

public class AnnotationAuthoringTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    const string Font = "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>";

    static PdfEditor TextPage() => PdfEditor.Open(TestPdf.Build("BT /F1 24 Tf 20 100 Td (Hello World) Tj ET", Font));

    static (PdfEditor Editor, PdfRef Ref) Add(PdfAnnotationSpec spec)
    {
        var e = TextPage();
        return (e, e.AddAnnotation(0, spec, Now));
    }

    static PdfEditor Reload(PdfEditor e) => PdfEditor.Open(e.Save());

    static readonly (string Name, PdfAnnotationSpec Spec)[] AllKinds =
    [
        ("Highlight", new() { Subtype = "Highlight", QuadPoints = [20, 120, 100, 120, 20, 95, 100, 95] }),
        ("Underline", new() { Subtype = "Underline", QuadPoints = [20, 120, 100, 120, 20, 95, 100, 95] }),
        ("StrikeOut", new() { Subtype = "StrikeOut", QuadPoints = [20, 120, 100, 120, 20, 95, 100, 95] }),
        ("Squiggly", new() { Subtype = "Squiggly", QuadPoints = [20, 120, 100, 120, 20, 95, 100, 95] }),
        ("Text", new() { Subtype = "Text", Rect = [150, 150, 170, 170], Icon = "Comment", Contents = "note" }),
        ("FreeText", new() { Subtype = "FreeText", Rect = [10, 10, 120, 50], Contents = "Free text that wraps across more than one line", FontSize = 10, Color = [1, 0, 0], TextColor = [0, 0, 1] }),
        ("Ink", new() { Subtype = "Ink", InkList = [[10, 10, 30, 40, 50, 20, 70, 60]], Color = [0, 0, 1] }),
        ("Line", new() { Subtype = "Line", Line = [10, 10, 100, 60], LineEnd = "ClosedArrow", Color = [0, 0.5, 0] }),
        ("Square", new() { Subtype = "Square", Rect = [20, 20, 80, 60], InteriorColor = [0.9, 0.9, 1] }),
        ("Circle", new() { Subtype = "Circle", Rect = [20, 20, 80, 60], Cloud = true }),
        ("Polygon", new() { Subtype = "Polygon", Vertices = [20, 20, 80, 20, 50, 70], Cloud = true }),
        ("PolyLine", new() { Subtype = "PolyLine", Vertices = [20, 20, 80, 20, 50, 70], LineStart = "OpenArrow" }),
        ("Stamp", new() { Subtype = "Stamp", Rect = [10, 150, 110, 190], StampName = "Approved" }),
        ("ImageStamp", new() { Subtype = "Stamp", Rect = [10, 150, 60, 190], Image = new(2, 2, [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 0], [255, 255, 128, 255]) }),
        ("Caret", new() { Subtype = "Caret", Rect = [50, 90, 62, 98] }),
        ("FileAttachment", new() { Subtype = "FileAttachment", Rect = [150, 10, 170, 30], Attachment = new("a.txt", "hello"u8.ToArray(), "text/plain") }),
        ("Redact", new() { Subtype = "Redact", QuadPoints = [20, 120, 100, 120, 20, 95, 100, 95], OverlayText = "X" }),
    ];

    public static IEnumerable<object[]> Kinds() => AllKinds.Select((k, i) => new object[] { i });

    [Theory, MemberData(nameof(Kinds))]
    public void EverySubtypeGetsConsistentAppearanceAndSurvivesSave(int index)
    {
        var (name, spec) = AllKinds[index];
        var (e, r) = Add(spec with { Author = "Ann", Subject = "S", Contents = spec.Contents ?? "c" });
        var reopened = Reload(e);
        var info = Assert.Single(reopened.ListAnnotations(0));
        Assert.Equal(spec.Subtype, info.Spec.Subtype);
        Assert.Equal("Ann", info.Spec.Author);
        Assert.Equal(Now, info.Spec.Created);

        var doc = PdfDocument.Load(e.Save());
        var f = doc.File;
        var annots = Assert.IsType<PdfArray>(f.Resolve(doc.Pages[0].Dict.Get("Annots")));
        var dict = annots.Items.Select(f.Resolve).OfType<PdfDictionary>().First(d => f.Resolve(d.Get("Subtype")).AsName() == spec.Subtype);
        var rect = ((PdfArray)f.Resolve(dict.Get("Rect"))!).Items.Select(x => f.Resolve(x).AsNumber()!.Value).ToArray();
        var ap = Assert.IsType<PdfDictionary>(f.Resolve(dict.Get("AP")));
        var form = Assert.IsType<PdfStream>(f.Resolve(ap.Get("N")));
        var bbox = ((PdfArray)f.Resolve(form.Dict.Get("BBox"))!).Items.Select(x => f.Resolve(x).AsNumber()!.Value).ToArray();
        for (var k = 0; k < 4; k++) Assert.Equal(rect[k], bbox[k], 3);
        Assert.True(rect[2] > rect[0] && rect[3] > rect[1], name);
        Assert.NotEmpty(f.DecodeStream(form));
        Assert.False(string.IsNullOrEmpty(info.Spec.Name));
    }


    [Fact]
    public void PopupLinksNamesAndPageArrayAreConsistent()
    {
        var e = TextPage();
        var a = e.AddAnnotation(0, new() { Subtype = "Highlight", QuadPoints = [20, 120, 100, 120, 20, 95, 100, 95], Contents = "one", Name = "dup" }, Now);
        var b = e.AddAnnotation(0, new() { Subtype = "Square", Rect = [1, 1, 20, 20], Name = "dup" }, Now);
        var doc = PdfDocument.Load(e.Save());
        var f = doc.File;
        var items = ((PdfArray)f.Resolve(doc.Pages[0].Dict.Get("Annots"))!).Items;
        var dicts = items.Select(f.Resolve).Cast<PdfDictionary>().ToList();
        Assert.Equal(4, dicts.Count);
        var names = dicts.Where(d => d.Get("NM") is not null).Select(d => ((PdfString)f.Resolve(d.Get("NM"))!).Text).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        foreach (var d in dicts.Where(d => f.Resolve(d.Get("Subtype")).AsName() != "Popup"))
        {
            var popup = Assert.IsType<PdfDictionary>(f.Resolve(d.Get("Popup")));
            Assert.Contains(d.Get("Popup")!, items, new RefEq());
            Assert.Same(d, f.Resolve(popup.Get("Parent")));
            Assert.Equal("Popup", f.Resolve(popup.Get("Subtype")).AsName());
            Assert.Same(doc.Pages[0].Dict, f.Resolve(d.Get("P")));
        }
        Assert.NotEqual(a.Number, b.Number);
    }

    sealed class RefEq : IEqualityComparer<PdfObject>
    {
        public bool Equals(PdfObject? x, PdfObject? y) => x is PdfRef a && y is PdfRef b && a.Number == b.Number;
        public int GetHashCode(PdfObject o) => (o as PdfRef)?.Number ?? 0;
    }

    [Fact]
    public void RepliesCarryStateAndDeleteCascades()
    {
        var e = TextPage();
        var note = e.AddAnnotation(0, new() { Subtype = "Text", Rect = [100, 100, 120, 120], Contents = "parent", Author = "A" }, Now);
        var reply = e.ReplyToAnnotation(0, note.Number, "B", "agreed", "Accepted", now: Now);
        var nested = e.ReplyToAnnotation(0, reply.Number, "C", "thanks", now: Now);
        var list = e.ListAnnotations(0);
        Assert.Contains(Reload(e).ListAnnotations(0), i => i.Spec.Contents == "agreed" && i.Spec.State == "Accepted");
        var r = list.Single(i => i.Number == reply.Number);
        Assert.Equal(note.Number, r.ParentNumber);
        Assert.Equal("Accepted", r.Spec.State);
        Assert.Equal("Review", r.Spec.StateModel);
        Assert.Equal("R", r.Spec.ReplyType);
        Assert.Equal(reply.Number, list.Single(i => i.Number == nested.Number).ParentNumber);
        Assert.Equal(4, e.DeleteAnnotation(0, note.Number)); // parent, popup, reply, nested
        Assert.Empty(Reload(e).ListAnnotations(0));
    }

    [Fact]
    public void UpdateAndTransformRegenerateAppearanceAndRect()
    {
        var (e, r) = Add(new() { Subtype = "Square", Rect = [20, 20, 80, 60], Color = [1, 0, 0] });
        e.UpdateAnnotation(0, r.Number, new() { Color = [0, 0, 1], BorderWidth = 3, Contents = "x" }, Now.AddHours(1));
        e.TransformAnnotation(0, r.Number, 2, 2, 10, 5, Now.AddHours(2));
        var info = Reload(e).ListAnnotations(0).Single();
        Assert.Equal([50, 45, 170, 125], info.Spec.Rect!.Select(v => Math.Round(v)).ToArray());
        Assert.Equal([0, 0, 1], info.Spec.Color!);
        Assert.Equal(3, info.Spec.BorderWidth);
        Assert.Equal(Now.AddHours(2), info.Spec.Modified);
        var doc = PdfDocument.Load(e.Save());
        var f = doc.File;
        var annot = (PdfDictionary)f.Resolve(((PdfArray)f.Resolve(doc.Pages[0].Dict.Get("Annots"))!).Items[0])!;
        var form = (PdfStream)f.Resolve(((PdfDictionary)f.Resolve(annot.Get("AP"))!).Get("N"))!;
        var bbox = ((PdfArray)f.Resolve(form.Dict.Get("BBox"))!).Items.Select(x => Math.Round(f.Resolve(x).AsNumber()!.Value)).ToArray();
        Assert.Equal([50, 45, 170, 125], bbox);
        Assert.Contains("0 0 1 RG", System.Text.Encoding.Latin1.GetString(f.DecodeStream(form)));
    }

    [Fact]
    public void HighlightQuadsCoverSelectedGlyphBoxes()
    {
        var e = TextPage();
        var doc = PdfDocument.Load(e.Save());
        var page = doc.Pages[0];
        var span = PortableDocument.Render.PortableDocument.ExtractText(page).Single();
        var region = new Redactor.BoxRect(span.X - 1, span.Y - span.Size, span.Width + 2, span.Size * 1.5);
        var runs = AnnotationTools.SelectText(page, region);
        var run = Assert.Single(runs);
        Assert.Equal("Hello World", run.Text);
        var glyphs = span.Pieces!.SelectMany(p => p.Glyphs ?? []).ToList();
        var quads = AnnotationTools.QuadsFromRuns(page, runs);
        var b = AnnotationAppearance.Bounds(quads);
        var top = page.CropBox.Top;
        foreach (var g in glyphs.Where(g => g.X1 > g.X0))
        {
            Assert.True(b[0] <= g.X0 + 0.01 && b[2] >= g.X1 - 0.01, "x coverage");
            Assert.True(b[1] <= top - Math.Max(g.Y0, g.Y1) + 0.01 && b[3] >= top - Math.Min(g.Y0, g.Y1) - 0.01, "y coverage");
        }
        // A narrower selection covers only the first word.
        var first = AnnotationTools.SelectText(page, new Redactor.BoxRect(span.X, span.Y - span.Size, span.Width * 0.4, span.Size * 1.5));
        Assert.StartsWith("Hello", Assert.Single(first).Text);
        Assert.True(first[0].X1 < run.X1);
    }

    static (byte R, byte G, byte B) Pixel(PdfDocument doc, int x, int y)
    {
        var img = PortableDocument.Render.PortableDocument.RenderPage(doc.Pages[0], 72);
        var k = img.Width / 200.0;
        var o = ((int)(y * k) * img.Width + (int)(x * k)) * 4;
        return (img.Rgba[o], img.Rgba[o + 1], img.Rgba[o + 2]);
    }

    [Fact]
    public void GeneratedAppearancesRenderAndFlatteningKeepsThePixels()
    {
        var e = PdfEditor.Open(TestPdf.Build("", Font));
        e.AddAnnotation(0, new() { Subtype = "Square", Rect = [20, 20, 80, 60], InteriorColor = [0, 1, 0], BorderWidth = 2 }, Now);
        e.AddAnnotation(0, new() { Subtype = "Highlight", QuadPoints = [100, 150, 180, 150, 100, 120, 180, 120] }, Now);
        e.AddAnnotation(0, new() { Subtype = "Text", Rect = [150, 20, 170, 40], Contents = "n" }, Now);
        var withAnnots = PdfDocument.Load(e.Save());
        Assert.Equal((0, 255, 0), Pixel(withAnnots, 50, 200 - 40));
        Assert.Equal((255, 255, 0), Pixel(withAnnots, 140, 200 - 135));
        var before = Pixel(withAnnots, 160, 200 - 30);
        Assert.NotEqual((255, 255, 255), before);

        Assert.Equal(3, e.FlattenAnnotations(0));
        Assert.Empty(e.ListAnnotations(0));
        var flat = PdfDocument.Load(e.Save());
        Assert.Null(flat.File.Resolve(flat.Pages[0].Dict.Get("Annots")));
        Assert.Equal((0, 255, 0), Pixel(flat, 50, 200 - 40));
        Assert.Equal((255, 255, 0), Pixel(flat, 140, 200 - 135));
        Assert.Equal(before, Pixel(flat, 160, 200 - 30));
    }

    [Fact]
    public void RedactionAnnotationsRemoveTextWhenApplied()
    {
        var e = TextPage();
        e.AddAnnotation(0, new() { Subtype = "Redact", QuadPoints = [18, 130, 150, 130, 18, 95, 150, 95] }, Now);
        Assert.Contains("Hello", string.Concat(PortableDocument.Render.PortableDocument.ExtractText(PdfDocument.Load(e.Save()).Pages[0]).Select(s => s.Text)));
        Assert.Equal(1, AnnotationTools.ApplyRedactionAnnotations(e, 0));
        var after = PdfDocument.Load(e.Save());
        Assert.DoesNotContain("Hello", string.Concat(PortableDocument.Render.PortableDocument.ExtractText(after.Pages[0]).Select(s => s.Text)));
        Assert.Empty(PdfEditor.Open(after).ListAnnotations(0));
    }

    [Fact]
    public void XfdfRoundTripsAnnotationsAndRepliesAndRejectsDtds()
    {
        var e = TextPage();
        var h = e.AddAnnotation(0, new() { Subtype = "Highlight", QuadPoints = [20, 120, 100, 120, 20, 95, 100, 95], Contents = "a & <b>", Author = "Ann", Color = [1, 0.5, 0] }, Now);
        e.ReplyToAnnotation(0, h.Number, "Bob", "ok", "Completed", now: Now);
        e.AddAnnotation(0, new() { Subtype = "Ink", InkList = [[1, 1, 5, 5, 9, 2]] }, Now);
        e.AddAnnotation(0, new() { Subtype = "Line", Line = [1, 2, 30, 40], LineEnd = "OpenArrow" }, Now);
        var xml = PdfXfdf.Export(e);
        var target = TextPage();
        Assert.Equal(4, PdfXfdf.Import(target, xml, now: Now));
        var list = target.ListAnnotations(0);
        var hi = list.Single(i => i.Spec.Subtype == "Highlight");
        Assert.Equal("a & <b>", hi.Spec.Contents);
        Assert.Equal("Ann", hi.Spec.Author);
        Assert.Equal(hi.Number, list.Single(i => i.Spec.State == "Completed").ParentNumber);
        Assert.Single(list, i => i.Spec.Subtype == "Ink");
        Assert.Equal("OpenArrow", list.Single(i => i.Spec.Subtype == "Line").Spec.LineEnd);
        Assert.ThrowsAny<System.Xml.XmlException>(() => PdfXfdf.Import(TextPage(), "<!DOCTYPE x [<!ENTITY e 'x'>]><xfdf xmlns=\"http://ns.adobe.com/xfdf/\"><annots/></xfdf>"));
    }

    [Fact]
    public void EverySubtypeDocumentCanBeDumpedForExternalValidators()
    {
        var e = TextPage();
        foreach (var (_, spec) in AllKinds) e.AddAnnotation(0, spec with { Author = "Ann", Contents = spec.Contents ?? "c" }, Now);
        var first = e.ListAnnotations(0).First(i => i.Spec.Subtype == "Text");
        e.ReplyToAnnotation(0, first.Number, "Bob", "reply", "Accepted", now: Now);
        var bytes = e.Save();
        if (Environment.GetEnvironmentVariable("ANNOT_DUMP") is { Length: > 0 } path) File.WriteAllBytes(path, bytes);
        Assert.Equal(AllKinds.Length + 1, PdfEditor.Open(bytes).ListAnnotations(0).Count);
    }

    [Fact]
    public void AttachmentMimeTypeSurvivesSaveAndOversizeIsRejected()
    {
        var (e, _) = Add(new() { Subtype = "FileAttachment", Rect = [150, 10, 170, 30], Attachment = new("a.txt", "hello"u8.ToArray(), "text/plain") });
        e.AddAnnotation(0, new() { Subtype = "FileAttachment", Rect = [100, 10, 120, 30], Attachment = new("b.bin", [1, 2, 3]) }, Now);
        var doc = PdfDocument.Load(e.Save());
        var f = doc.File;
        var types = ((PdfArray)f.Resolve(doc.Pages[0].Dict.Get("Annots"))!).Items.Select(f.Resolve).OfType<PdfDictionary>()
            .Where(d => f.Resolve(d.Get("Subtype")).AsName() == "FileAttachment")
            .Select(d => f.Resolve(((PdfDictionary)f.Resolve(((PdfDictionary)f.Resolve(d.Get("FS"))!).Get("EF"))!).Get("F")) as PdfStream)
            .Select(s => f.Resolve(s!.Dict.Get("Subtype")).AsName()).ToList();
        Assert.Contains("text/plain", types);
        Assert.Contains("application/octet-stream", types);
        Assert.Throws<ArgumentException>(() => TextPage().AddAnnotation(0, new() { Subtype = "FileAttachment", Rect = [1, 1, 21, 21],
            Attachment = new("big.bin", new byte[PdfEditor.MaxAttachmentBytes + 1]) }, Now));
    }

    [Fact]
    public void LargeXfdfImportAndFlattenAreLinear()
    {
        const int count = 20000;
        var sb = new System.Text.StringBuilder("<xfdf xmlns=\"http://ns.adobe.com/xfdf/\"><annots>");
        for (var i = 0; i < count; i++) sb.Append($"<square page=\"0\" name=\"n{i % 5000}\" rect=\"{i % 150},{i % 150},{i % 150 + 10},{i % 150 + 10}\"/>");
        sb.Append("</annots></xfdf>");
        var e = TextPage();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(count, PdfXfdf.Import(e, sb.ToString(), now: Now));
        var list = e.ListAnnotations(0);
        Assert.Equal(count, list.Count);
        Assert.Equal(count, list.Select(i => i.Spec.Name).Distinct().Count());
        Assert.Equal(count, e.FlattenAnnotations(0));
        Assert.Empty(e.ListAnnotations(0));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(60), $"took {sw.Elapsed}");
    }

    [Fact]
    public void ReplaceTextCreatesStrikeOutAndGroupedCaret()
    {
        var e = TextPage();
        var (s, c) = e.AddReplaceText(0, [20, 120, 100, 120, 20, 95, 100, 95], "Bye", "Ann", Now);
        var list = e.ListAnnotations(0);
        Assert.Contains(Reload(e).ListAnnotations(0), i => i.Spec.Contents == "Bye" && i.Spec.ReplyType == "Group");
        var caret = list.Single(i => i.Number == c.Number);
        Assert.Equal(s.Number, caret.ParentNumber);
        Assert.Equal("Group", caret.Spec.ReplyType);
        Assert.Equal("Bye", caret.Spec.Contents);
    }
}
