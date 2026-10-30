using AdoBye.EditorWeb;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;
using DocumentRenderer = global::PortableDocument.Render.PortableDocument;

namespace AdoBye.Tests;

public class FormObjectTransformTests
{
    const string Font = "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>";
    const string FormContent = "1 0 0 rg 10 10 10 8 re f q 20 0 0 20 25 25 cm /Im Do Q 0 1 0 rg 40 10 10 8 re f BT /F1 10 Tf 10 50 Td (Form text) Tj ET";
    const string PageContent = "0 0 1 rg 140 30 10 10 re f BT /F1 12 Tf 100 150 Td (Keep) Tj ET q 2 0 0 2 30 100 cm /Fm Do Q";
    const double Sx = 1.5, Sy = 1.25, Tx = 4, Ty = 7;

    static string Page(int contents, int form) =>
        $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents {contents} 0 R /Resources << {Font} /XObject << /Fm {form} 0 R >> >> >>";

    static string Form(int image = 6) => ObjPdf.Stream(FormContent,
        $"/Type /XObject /Subtype /Form /BBox [0 0 100 100] /Resources << {Font} /XObject << /Im {image} 0 R >> >>");

    static PdfDocument SingleForm(string pageContent = PageContent) => PdfDocument.Load(ObjPdf.Build(
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        Page(4, 5),
        ObjPdf.Stream(pageContent),
        Form(),
        ObjPdf.Stream("\xff\0\0", "/Type /XObject /Subtype /Image /Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceRGB")));

    static PdfDocument SharedAcrossPages() => PdfDocument.Load(ObjPdf.Build(
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R 5 0 R] /Count 2 >>",
        Page(4, 7),
        ObjPdf.Stream("/Fm Do"),
        Page(6, 7),
        ObjPdf.Stream("/Fm Do"),
        Form(8),
        ObjPdf.Stream("\xff\0\0", "/Type /XObject /Subtype /Image /Width 1 /Height 1 /BitsPerComponent 8 /ColorSpace /DeviceRGB")));

    [Theory]
    [InlineData("path")]
    [InlineData("image")]
    public void Child_transform_survives_save_reopen_without_changing_siblings_or_text(string kind)
    {
        var source = SingleForm();
        var (spans, objects) = DocumentRenderer.Analyze(source.Pages[0]);
        var children = objects.Where(o => o.Form == 5).ToList();
        Assert.Equal(3, children.Count);
        var ambiguous = FormObjectEditPolicy.FindAmbiguous(objects, FormObjectEditPolicy.FindSharedForms(source));
        var selected = Assert.Single(children, o => o.Kind == kind && o.X0 < 100);
        Assert.True(FormObjectEditPolicy.IsEditable(selected, ambiguous));
        var matrix = selected.Ctm.Then(new Matrix(Sx, 0, 0, Sy, Tx, Ty)).Then(selected.Ctm.Invert());
        var stream = Assert.IsType<PdfStream>(source.File.GetObject(selected.Form));
        var content = ContentRewriter.EditObjects(source.File.DecodeStream(stream),
        [
            new(selected.Start, selected.End, false,
                [matrix.A, matrix.B, matrix.C, matrix.D, matrix.E, matrix.F], null, null),
        ]);
        var editor = PdfEditor.Open(source);
        editor.SetStreamOverride(selected.Form, content);

        var reopened = PdfDocument.Load(editor.Save());
        var (afterSpans, after) = DocumentRenderer.Analyze(reopened.Pages[0]);
        var transformed = after[selected.Index];
        Assert.Equal(Sx * selected.X0 + Tx, transformed.X0, 4);
        Assert.Equal(Sy * selected.Y0 + Ty, transformed.Y0, 4);
        Assert.Equal(Sx * selected.X1 + Tx, transformed.X1, 4);
        Assert.Equal(Sy * selected.Y1 + Ty, transformed.Y1, 4);
        Assert.Equal(objects.Count, after.Count);
        Assert.Equal(objects.Where(o => o.Index != selected.Index).Select(o => (o.Kind, o.Form, o.X0, o.Y0, o.X1, o.Y1)),
            after.Where(o => o.Index != selected.Index).Select(o => (o.Kind, o.Form, o.X0, o.Y0, o.X1, o.Y1)));
        Assert.Equal(spans.Select(s => (s.Text, s.X, s.Y)), afterSpans.Select(s => (s.Text, s.X, s.Y)));
        Assert.NotEqual(DocumentRenderer.RenderPage(source.Pages[0], 72).Rgba,
            DocumentRenderer.RenderPage(reopened.Pages[0], 72).Rgba);
    }

    [Fact]
    public void Repeated_invocations_of_a_form_do_not_expose_ambiguous_children()
    {
        var source = SingleForm("/Fm Do /Fm Do");
        var objects = DocumentRenderer.Analyze(source.Pages[0]).Objects;
        var ambiguous = FormObjectEditPolicy.FindAmbiguous(objects, FormObjectEditPolicy.FindSharedForms(source));
        var children = objects.Where(o => o.Form == 5).ToList();

        Assert.NotEmpty(children);
        Assert.All(children, child => Assert.False(FormObjectEditPolicy.IsEditable(child, ambiguous)));
        Assert.All(objects.Where(o => o.Form == 0), parent => Assert.True(FormObjectEditPolicy.IsEditable(parent, ambiguous)));
    }

    [Fact]
    public void A_form_shared_between_pages_does_not_expose_ambiguous_children()
    {
        var source = SharedAcrossPages();
        var pageObjects = source.Pages.Select(page => DocumentRenderer.Analyze(page).Objects).ToList();
        var sharedForms = FormObjectEditPolicy.FindSharedForms(source);
        var ambiguous = FormObjectEditPolicy.FindAmbiguous(pageObjects[0], sharedForms);
        var children = pageObjects.SelectMany(objects => objects).Where(o => o.Form == 7).ToList();

        Assert.NotEmpty(children);
        Assert.All(children, child => Assert.False(FormObjectEditPolicy.IsEditable(child, ambiguous)));
    }
}
