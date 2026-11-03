using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfUaDestinationTests
{
    static PdfDocument Load(string catalog = "", string annotation = "", string outline = "", string[]? extraObjects = null, int part = 2)
    {
        return PdfDocument.Load(ObjPdf.Build([
            $"<< /Type /Catalog /Version /2.0 /Pages 2 0 R /StructTreeRoot 5 0 R /MarkInfo << /Marked true >> /Lang (en) /ViewerPreferences << /DisplayDocTitle true >> /Metadata 6 0 R /Outlines 11 0 R {catalog} >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Annots [9 0 R] /Tabs /S >>",
            ObjPdf.Stream(""),
            "<< /Type /StructTreeRoot /K 7 0 R /ParentTree 10 0 R >>",
            ObjPdf.Xmp($"<rdf:Description rdf:about='' xmlns:pdfuaid='http://www.aiim.org/pdfua/ns/id/' xmlns:dc='http://purl.org/dc/elements/1.1/' pdfuaid:part='{part}' pdfuaid:rev='2024'><dc:title><rdf:Alt><rdf:li xml:lang='x-default'>Doc</rdf:li></rdf:Alt></dc:title></rdf:Description>"),
            "<< /Type /StructElem /S /Document /NS 13 0 R /P 5 0 R /K 8 0 R >>",
            "<< /Type /StructElem /S /Link /P 7 0 R /Pg 3 0 R /K << /Type /OBJR /Obj 9 0 R /Pg 3 0 R >> >>",
            $"<< /Type /Annot /Subtype /Link /Rect [0 0 10 10] /Contents (Link) /StructParent 1 {annotation} >>",
            "<< /Nums [1 8 0 R] >>",
            "<< /Type /Outlines /First 12 0 R /Last 12 0 R >>",
            $"<< /Title (Entry) /Parent 11 0 R {outline} >>",
            "<< /Type /Namespace /NS (http://iso.org/pdf2/ssn) >>",
            .. extraObjects ?? [],
        ]));
    }

    static ValidationReport Validate(PdfDocument doc, int part = 2)
    {
        var report = PdfValidator.ValidatePdfUA(doc, part);
        Assert.False(report.Has("VALIDATION-INTERNAL"), string.Join("\n", report.Findings));
        return report;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NamedAnnotationAndOutlineDestinationsDetectPages(bool nameTree, bool structure)
    {
        var target = structure ? 7 : 3;
        var catalog = nameTree
            ? $"/Names << /Dests << /Names [(target) << /D [{target} 0 R /Fit] >>] >> >>"
            : $"/Dests << /target << /D [{target} 0 R /Fit] >> >>";
        var name = nameTree ? "(target)" : "/target";
        var report = Validate(Load(catalog, $"/Dest {name}", $"/Dest {name}"));

        Assert.Equal(!structure, report.Has("PDFUA2-DEST-NOT-STRUCTURE"));
        Assert.False(report.Has("PDFUA2-DEST-INCOMPLETE"));
    }

    [Theory]
    [InlineData("/OpenAction [3 0 R /Fit]")]
    [InlineData("/OpenAction << /S /GoTo /D [3 0 R /Fit] >>")]
    [InlineData("/AA << /WC << /S /GoTo /D [3 0 R /Fit] >> >>")]
    [InlineData("/OpenAction << /S /Named /N /NextPage /Next << /S /GoTo /D [3 0 R /Fit] >> >>")]
    [InlineData("/OpenAction /target /Dests << /target [3 0 R /Fit] >>")]
    public void InertCatalogTriggersAlsoRequireStructureDestinations(string catalog)
    {
        Assert.True(Validate(Load(catalog)).Has("PDFUA2-DEST-NOT-STRUCTURE"));
    }

    [Theory]
    [InlineData("/OpenAction [7 0 R /Fit]")]
    [InlineData("/OpenAction << /S /GoTo /D [3 0 R /Fit] /SD [7 0 R /Fit] >>")]
    [InlineData("/OpenAction << /S /GoToR /F (https://never.invalid/file.pdf) /D [0 /Fit] >>")]
    public void StructureFallbacksAndRemoteActionsDoNotBecomePageViolations(string catalog)
    {
        var report = Validate(Load(catalog));

        Assert.False(report.Has("PDFUA2-DEST-NOT-STRUCTURE"));
        Assert.False(report.Has("PDFUA2-DEST-INCOMPLETE"));
    }

    [Fact]
    public void CyclicNamedDestinationReportsIncomplete()
    {
        var report = Validate(Load("/OpenAction /loop /Dests << /loop /loop >>"));

        Assert.True(report.Has("PDFUA2-DEST-INCOMPLETE"));
    }

    [Fact]
    public void CyclicIndirectOpenActionReportsIncomplete()
    {
        var report = Validate(Load("/OpenAction 14 0 R", extraObjects: ["14 0 R"]));

        Assert.True(report.Has("PDFUA2-DEST-INCOMPLETE"));
    }

    [Theory]
    [InlineData("/A << /S /GoTo /D [3 0 R /Fit] >>")]
    [InlineData("/AA << /E << /S /GoTo /D [3 0 R /Fit] >> >>")]
    [InlineData("/PA << /S /GoTo /D [3 0 R /Fit] >>")]
    public void AnnotationActionScopesDetectPageDestinations(string annotation)
    {
        Assert.True(Validate(Load(annotation: annotation)).Has("PDFUA2-DEST-NOT-STRUCTURE"));
    }

    [Theory]
    [InlineData(32, false)]
    [InlineData(33, true)]
    public void DestinationReferenceBoundaryIsExplicit(int length, bool incomplete)
    {
        var objects = Enumerable.Range(14, length).Select(n => n == 13 + length ? "[7 0 R /Fit]" : $"{n + 1} 0 R").ToArray();
        var report = Validate(Load("/OpenAction << /S /GoTo /D 14 0 R >>", extraObjects: objects));

        Assert.Equal(incomplete, report.Has("PDFUA2-DEST-INCOMPLETE"));
        Assert.False(report.Has("PDFUA2-DEST-NOT-STRUCTURE"));
    }

    [Fact]
    public void CyclicNameTreeReportsIncompleteAndStillSearchesFollowingSiblings()
    {
        var report = Validate(Load("/OpenAction (target) /Names << /Dests 14 0 R >>",
            extraObjects: ["<< /Kids [14 0 R 15 0 R] >>", "<< /Names [(target) [3 0 R /Fit]] >>"]));

        Assert.True(report.Has("PDFUA2-DEST-INCOMPLETE"));
        Assert.True(report.Has("PDFUA2-DEST-NOT-STRUCTURE"));
    }

    [Theory]
    [InlineData(64, false)]
    [InlineData(65, true)]
    public void NamedDestinationTreeDepthBoundaryIsExplicit(int depth, bool incomplete)
    {
        var objects = Enumerable.Range(14, depth + 1)
            .Select(n => n == 14 + depth ? "<< /Names [(target) [3 0 R /Fit]] >>" : $"<< /Kids [{n + 1} 0 R] >>").ToArray();
        var report = Validate(Load("/OpenAction (target) /Names << /Dests 14 0 R >>", extraObjects: objects));

        Assert.Equal(incomplete, report.Has("PDFUA2-DEST-INCOMPLETE"));
        Assert.Equal(!incomplete, report.Has("PDFUA2-DEST-NOT-STRUCTURE"));
    }

    [Theory]
    [InlineData("/Names 14 0 R")]
    [InlineData("/Names << /Dests 14 0 R >>")]
    [InlineData("/Dests 14 0 R")]
    [InlineData("/Names << /Dests << /Kids [14 0 R] >> >>")]
    [InlineData("/Names << /Dests << /Names 14 0 R >> >>")]
    [InlineData("/Names << /Dests << /Names [14 0 R [3 0 R /Fit]] >> >>")]
    public void NameLookupIndirectCyclesReportIncomplete(string catalog)
    {
        var report = Validate(Load($"/OpenAction (target) {catalog}", extraObjects: ["14 0 R"]));

        Assert.True(report.Has("PDFUA2-DEST-INCOMPLETE"));
    }

    [Theory]
    [InlineData(32, false)]
    [InlineData(33, true)]
    public void NamedDestinationTreeReferenceBoundaryIsExplicit(int length, bool incomplete)
    {
        var objects = Enumerable.Range(14, length)
            .Select(n => n == 13 + length ? "<< /Names [(target) [3 0 R /Fit]] >>" : $"{n + 1} 0 R").ToArray();
        var report = Validate(Load("/OpenAction (target) /Names << /Dests 14 0 R >>", extraObjects: objects));

        Assert.Equal(incomplete, report.Has("PDFUA2-DEST-INCOMPLETE"));
        Assert.Equal(!incomplete, report.Has("PDFUA2-DEST-NOT-STRUCTURE"));
    }

    [Theory]
    [InlineData(9999, false)]
    [InlineData(10000, true)]
    public void NamedDestinationPairWorkBoundaryIsExplicit(int count, bool incomplete)
    {
        var pairs = string.Join(" ", Enumerable.Range(0, count - 1).Select(n => $"(other{n}) [7 0 R /Fit]"));
        var report = Validate(Load($"/OpenAction (target) /Names << /Dests << /Names [{pairs} (target) [3 0 R /Fit]] >> >>"));

        Assert.Equal(incomplete, report.Has("PDFUA2-DEST-INCOMPLETE"));
        Assert.Equal(!incomplete, report.Has("PDFUA2-DEST-NOT-STRUCTURE"));
    }

    [Fact]
    public void InspectionRetainsLookupBeyondValidationWorkLimit()
    {
        var pairs = string.Join(" ", Enumerable.Range(0, 10_000).Select(n => $"(other{n}) [7 0 R /Fit]"));
        var doc = Load($"/Names << /Dests << /Names [{pairs} (target) [3 0 R /Fit]] >> >>",
            outline: "/Dest (target)");

        Assert.Equal(0, Assert.Single(doc.GetOutlines()).PageIndex);
        Assert.True(Validate(doc).Has("PDFUA2-DEST-INCOMPLETE"));
    }

    [Fact]
    public void MissingNameAndCompletedSharedSubtreeDoNotReportIncomplete()
    {
        var report = Validate(Load("/OpenAction (missing) /Names << /Dests << /Kids [14 0 R 14 0 R] >> >>",
            extraObjects: ["<< /Names [(other) [3 0 R /Fit]] >>"]));

        Assert.False(report.Has("PDFUA2-DEST-INCOMPLETE"));
        Assert.False(report.Has("PDFUA2-DEST-NOT-STRUCTURE"));
    }

    [Fact]
    public void Ua1DoesNotApplyUa2DestinationRules()
    {
        var report = Validate(Load("/OpenAction [3 0 R /Fit]", part: 1), 1);

        Assert.DoesNotContain(report.Findings, f => f.RuleId.StartsWith("PDFUA2-DEST-", StringComparison.Ordinal));
    }
}
