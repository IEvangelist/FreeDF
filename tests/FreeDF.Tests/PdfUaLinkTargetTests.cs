using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfUaLinkTargetTests
{
    static PdfDocument Load(string first, string second, string role = "Link", bool separate = false, string catalog = "", int part = 2, string[]? extraObjects = null)
    {
        var bodies = new[]
        {
            $"<< /Type /Catalog /Version /2.0 /Pages 2 0 R /StructTreeRoot 5 0 R /MarkInfo << /Marked true >> /Lang (en) /ViewerPreferences << /DisplayDocTitle true >> /Metadata 6 0 R {catalog} >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Annots [9 0 R 12 0 R] /Tabs /S >>",
            ObjPdf.Stream(""),
            "<< /Type /StructTreeRoot /K 7 0 R /ParentTree 10 0 R >>",
            ObjPdf.Xmp($"<rdf:Description rdf:about='' xmlns:pdfuaid='http://www.aiim.org/pdfua/ns/id/' xmlns:dc='http://purl.org/dc/elements/1.1/' pdfuaid:part='{part}' pdfuaid:rev='2024'><dc:title><rdf:Alt><rdf:li xml:lang='x-default'>Doc</rdf:li></rdf:Alt></dc:title></rdf:Description>"),
            "<< /Type /StructElem /S /Document /NS 14 0 R /P 5 0 R /K [13 0 R 16 0 R] >>",
            $"<< /Type /StructElem /S /{role} /P 13 0 R /Pg 3 0 R /K {(separate ? "11 0 R" : "[11 0 R 15 0 R]")} >>",
            $"<< /Type /Annot /Subtype /Link /Rect [0 0 10 10] /Contents (First) /StructParent 1 {first} >>",
            $"<< /Nums [1 8 0 R 2 {(separate ? 16 : 8)} 0 R] >>",
            "<< /Type /OBJR /Obj 9 0 R /Pg 3 0 R >>",
            $"<< /Type /Annot /Subtype /Link /Rect [10 10 20 20] /Contents (Second) /StructParent 2 {second} >>",
            "<< /Type /StructElem /S /P /P 7 0 R /Pg 3 0 R /K 8 0 R >>",
            "<< /Type /Namespace /NS (http://iso.org/pdf2/ssn) >>",
            "<< /Type /OBJR /Obj 12 0 R /Pg 3 0 R >>",
            $"<< /Type /StructElem /S /Link /P 7 0 R /Pg 3 0 R {(separate ? "/K 15 0 R" : "")} >>",
        };
        return PdfDocument.Load(ObjPdf.Build([.. bodies, .. extraObjects ?? []]));
    }

    static ValidationReport Validate(PdfDocument doc, int part = 2)
    {
        var report = PdfValidator.ValidatePdfUA(doc, part);
        Assert.False(report.Has("VALIDATION-INTERNAL"), string.Join("\n", report.Findings));
        return report;
    }

    [Theory]
    [InlineData("Link", false)]
    [InlineData("Link", true)]
    [InlineData("Reference", false)]
    [InlineData("Reference", true)]
    public void LinksInOneElementMustShareTheirStructureTarget(string role, bool action)
    {
        string Destination(int target) => action
            ? $"/A << /S /GoTo /D [3 0 R /Fit] /SD [{target} 0 R /Fit] >>"
            : $"/Dest [{target} 0 R /Fit]";

        var invalid = Validate(Load(Destination(7), Destination(13), role));
        var finding = Assert.Single(invalid.Findings, f => f.RuleId == "PDFUA2-LINK-TARGETS");
        Assert.Equal(ValidationSeverity.Error, finding.Severity);
        Assert.Equal("ISO 14289-2 8.2.5.20", finding.Clause);
        Assert.Equal(0, finding.PageIndex);
        Assert.True(Validate(Load(Destination(7), Destination(7), role)).IsCompliant);
        Assert.True(Validate(Load(Destination(7), Destination(13), role, separate: true)).IsCompliant);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NamedTargetsResolveToStructureElementIdentity(bool nameTree, bool different)
    {
        var secondTarget = different ? 13 : 7;
        var catalog = nameTree
            ? $"/Names << /Dests << /Names [(first) << /SD [7 0 R /Fit] >> (second) << /SD [{secondTarget} 0 R /Fit] >>] >> >>"
            : $"/Dests << /first << /SD [7 0 R /Fit] >> /second << /SD [{secondTarget} 0 R /Fit] >> >>";
        var first = nameTree ? "/Dest (first)" : "/Dest /first";
        var second = nameTree ? "/Dest (second)" : "/Dest /second";

        var report = Validate(Load(first, second, catalog: catalog));
        Assert.Equal(different, report.Has("PDFUA2-LINK-TARGETS"));
        Assert.False(report.Has("PDFUA2-LINK-TARGET-UNRESOLVED"));
    }

    [Fact]
    public void GoToStructureDestinationTakesPrecedenceOverItsPageFallback()
    {
        var doc = Load(
            "/A << /S /GoTo /D [3 0 R /Fit] /SD [7 0 R /Fit] >>",
            "/Dest [7 0 R /XYZ 0 0 1]");

        Assert.True(Validate(doc).IsCompliant);
    }

    [Fact]
    public void RemoteAndUriActionsAreNotTreatedAsLocalStructureDestinations()
    {
        var doc = Load(
            "/A << /S /GoToR /F (external.pdf) /D [0 /Fit] >>",
            "/A << /S /URI /URI (https://example.invalid/) >>");

        var report = Validate(doc);
        Assert.False(report.Has("PDFUA2-LINK-TARGETS"));
        Assert.False(report.Has("PDFUA2-LINK-TARGET-UNRESOLVED"));
    }

    [Fact]
    public void CyclicNamedTargetsProduceAnExplicitIncompleteCheckError()
    {
        var report = Validate(Load("/Dest /loop", "/Dest [7 0 R /Fit]", catalog: "/Dests << /loop /loop >>"));

        var finding = Assert.Single(report.Findings, f => f.RuleId == "PDFUA2-LINK-TARGET-UNRESOLVED");
        Assert.Equal(ValidationSeverity.Error, finding.Severity);
    }

    [Theory]
    [InlineData("/Dest 17 0 R")]
    [InlineData("/Dest [17 0 R /Fit]")]
    [InlineData("/A 17 0 R")]
    public void CyclicIndirectDestinationsProduceAnExplicitIncompleteCheckError(string destination)
    {
        var report = Validate(Load(destination, "/Dest [7 0 R /Fit]", extraObjects: ["18 0 R", "17 0 R"]));

        Assert.Single(report.Findings, f => f.RuleId == "PDFUA2-LINK-TARGET-UNRESOLVED");
    }

    [Theory]
    [InlineData(32, false)]
    [InlineData(33, true)]
    public void IndirectDestinationDepthBoundaryIsExplicit(int length, bool incomplete)
    {
        var objects = Enumerable.Range(17, length).Select(n => n == 16 + length ? "[7 0 R /Fit]" : $"{n + 1} 0 R").ToArray();
        var report = Validate(Load("/Dest 17 0 R", "/Dest [7 0 R /Fit]", extraObjects: objects));

        Assert.Equal(incomplete, report.Has("PDFUA2-LINK-TARGET-UNRESOLVED"));
        Assert.False(report.Has("PDFUA2-LINK-TARGETS"));
    }

    [Fact]
    public void NamedTargetTreeCycleReportsIncompleteWithoutHidingOtherTargets()
    {
        var report = Validate(Load("/Dest (first)", "/Dest [13 0 R /Fit]",
            catalog: "/Names << /Dests 17 0 R >>",
            extraObjects: ["<< /Kids [17 0 R 18 0 R] >>", "<< /Names [(first) [7 0 R /Fit]] >>"]));

        Assert.True(report.Has("PDFUA2-LINK-TARGET-UNRESOLVED"));
        Assert.True(report.Has("PDFUA2-LINK-TARGETS"));
    }

    [Fact]
    public void Ua1DoesNotApplyTheUa2TargetGroupingRule()
    {
        var report = Validate(Load("/Dest [7 0 R /Fit]", "/Dest [13 0 R /Fit]", part: 1), 1);

        Assert.False(report.Has("PDFUA2-LINK-TARGETS"));
    }
}
