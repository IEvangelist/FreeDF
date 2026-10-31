using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;
using Xunit;

namespace AdoBye.Tests;

// Self-authored fixtures: expectations are derived from ISO 32000-2 8.11, not from another engine.
public class OptionalContentTests
{
    // Objects: 5 = OCG "A", 6 = OCG "B", 7 = annotation helpers via callers.
    static PdfDocument Doc(string config, string content, string objs = "", string res = "")
    {
        var ocgs = "5 0 obj\n<< /Type /OCG /Name (A) /Usage << /View << /ViewState /ON >> /Print << /PrintState /OFF >> >> >>\nendobj\n"
                 + "6 0 obj\n<< /Type /OCG /Name (B) >>\nendobj\n" + objs;
        var bytes = TestPdf.Build(content, "/Properties << /A 5 0 R /B 6 0 R >> " + res, ocgs, brokenXref: true);
        var s = System.Text.Encoding.Latin1.GetString(bytes).Replace("/Type /Catalog /Pages 2 0 R",
            $"/Type /Catalog /Pages 2 0 R /OCProperties << /OCGs [5 0 R 6 0 R] /D << {config} >> >>");
        return PdfDocument.Load(System.Text.Encoding.Latin1.GetBytes(s));
    }

    static (int R, int G, int B) Px(RenderedPage p, int x, int y) { var o = (y * p.Width + x) * 4; return (p.Rgba[o], p.Rgba[o + 1], p.Rgba[o + 2]); }

    const string Tagged = "/OC /A BDC 1 0 0 rg 0 0 100 200 re f EMC /OC /B BDC 0 0 1 rg 100 0 100 200 re f EMC";

    static (bool A, bool B) Vis(PdfDocument d)
    {
        var p = DocumentRenderer.RenderPage(d.Pages[0], 72);
        return (Px(p, 50, 100).R > 200 && Px(p, 50, 100).B < 50, Px(p, 150, 100).B > 200 && Px(p, 150, 100).R < 50);
    }

    [Fact]
    public void DefaultConfig_AllVisible() => Assert.Equal((true, true), Vis(Doc("", Tagged)));

    [Fact]
    public void BaseStateOff_WithOnArray() => Assert.Equal((true, false), Vis(Doc("/BaseState /OFF /ON [5 0 R]", Tagged)));

    [Fact]
    public void OffArray_HidesGroup() => Assert.Equal((true, false), Vis(Doc("/OFF [6 0 R]", Tagged)));

    [Fact]
    public void AutoState_PrintEvent_UsesPrintState()
    {
        const string cfg = "/AS [<< /Event /Print /OCGs [5 0 R] /Category [/Print] >>]";
        var d = Doc(cfg, Tagged);
        var ev = new OptionalContentEvaluator(d.File, OptionalContentEvent.Print);
        var view = new OptionalContentEvaluator(d.File, OptionalContentEvent.View);
        var ocg = (PdfDictionary)d.File.Resolve(new PdfRef(5, 0))!;
        Assert.True(ev.IsHidden(ocg));
        Assert.False(view.IsHidden(ocg));
    }

    static bool Hidden(string config, string ocmdBody, OptionalContentEvent ev = OptionalContentEvent.View)
    {
        var d = Doc(config, Tagged, $"8 0 obj\n{ocmdBody}\nendobj\n");
        return new OptionalContentEvaluator(d.File, ev).IsHidden((PdfDictionary)d.File.Resolve(new PdfRef(8, 0))!);
    }

    [Fact]
    public void OcmdPolicies()
    {
        static string O(string p) => $"<< /Type /OCMD /OCGs [5 0 R 6 0 R] /P /{p} >>";
        Assert.False(Hidden("/OFF [6 0 R]", O("AnyOn")));
        Assert.True(Hidden("/OFF [6 0 R]", O("AllOn")));
        Assert.False(Hidden("/OFF [6 0 R]", O("AnyOff")));
        Assert.True(Hidden("/OFF [6 0 R]", O("AllOff")));
    }

    [Fact]
    public void VisibilityExpression_AndOrNot()
    {
        static string V(string x) => $"<< /Type /OCMD /VE {x} >>";
        Assert.False(Hidden("/OFF [6 0 R]", V("[/And 5 0 R [/Not 6 0 R]]")));
        Assert.True(Hidden("/OFF [6 0 R]", V("[/And 5 0 R 6 0 R]")));
        Assert.False(Hidden("/OFF [6 0 R]", V("[/Or 6 0 R 5 0 R]")));
        Assert.True(Hidden("/OFF [6 0 R]", V("[/Not 5 0 R]")));
    }

    [Fact]
    public void VisibilityExpression_DeepNestingIsBounded()
    {
        var expr = string.Concat(Enumerable.Repeat("[/Not ", 100)) + "5 0 R" + new string(']', 100);
        _ = Hidden("", $"<< /Type /OCMD /VE {expr} >>");
    }
    [Fact]
    public void IntentMismatch_IsIgnored()
    {
        var d = Doc("/OFF [6 0 R] /Intent /Design", Tagged);
        Assert.Equal((true, true), Vis(d));
    }

    [Fact]
    public void Annotation_OcHidesAppearance()
    {
        var objs = "7 0 obj\n<< /Type /Annot /Subtype /Square /Rect [0 0 200 200] /OC 6 0 R /C [1 0 0] /IC [1 0 0] >>\nendobj\n";
        var bytes = System.Text.Encoding.Latin1.GetString(TestPdf.Build("", "", "6 0 obj\n<< /Type /OCG /Name (B) >>\nendobj\n" + objs, brokenXref: true))
            .Replace("/Resources <<", "/Annots [7 0 R] /Resources <<")
            .Replace("/Type /Catalog /Pages 2 0 R", "/Type /Catalog /Pages 2 0 R /OCProperties << /OCGs [6 0 R] /D << /OFF [6 0 R] >> >>");
        var hidden = DocumentRenderer.RenderPage(PdfDocument.Load(System.Text.Encoding.Latin1.GetBytes(bytes)).Pages[0], 72);
        Assert.True(Px(hidden, 100, 100).G > 240, "hidden " + Px(hidden, 100, 100));
        var shown = DocumentRenderer.RenderPage(PdfDocument.Load(System.Text.Encoding.Latin1.GetBytes(bytes.Replace("/D << /OFF [6 0 R] >>", "/D << >>"))).Pages[0], 72);
        Assert.True(Px(shown, 100, 100).G < 100, "shown " + Px(shown, 100, 100));
    }
}
