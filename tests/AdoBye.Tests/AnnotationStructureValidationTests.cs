using PortableDocument.Core;
using PortableDocument.Document;

namespace AdoBye.Tests;

public class AnnotationStructureValidationTests
{
    const string Annotation = "<< /Type /Annot /Subtype /Stamp /Rect [0 0 10 10] /Contents (Note) /StructParent 1 >>";
    const string Element = "<< /Type /StructElem /S /Annot /P 7 0 R /Pg 3 0 R /K 11 0 R >>";

    static PdfDocument Load(int part, params (int Number, string Body)[] replacements)
    {
        var bodies = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R /StructTreeRoot 5 0 R /MarkInfo << /Marked true >> /Lang (en) /ViewerPreferences << /DisplayDocTitle true >> /Metadata 6 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Annots [9 0 R] /Tabs /S >>",
            ObjPdf.Stream(""),
            "<< /Type /StructTreeRoot /K 7 0 R /ParentTree 10 0 R >>",
            ObjPdf.Xmp($"<rdf:Description rdf:about='' xmlns:pdfuaid='http://www.aiim.org/pdfua/ns/id/' xmlns:dc='http://purl.org/dc/elements/1.1/' pdfuaid:part='{part}'><dc:title><rdf:Alt><rdf:li xml:lang='x-default'>Doc</rdf:li></rdf:Alt></dc:title></rdf:Description>"),
            "<< /Type /StructElem /S /Document /P 5 0 R /K 8 0 R >>",
            Element,
            Annotation,
            "<< /Nums [1 8 0 R] >>",
            "<< /Type /OBJR /Obj 9 0 R /Pg 3 0 R >>",
            Annotation,
            "<< /Type /StructElem /S /Span /P 8 0 R /K 11 0 R >>",
        };
        foreach (var (number, body) in replacements) bodies[number - 1] = body;
        return PdfDocument.Load(ObjPdf.Build(bodies));
    }

    static ValidationReport Validate(PdfDocument doc, int part)
    {
        var report = PdfValidator.ValidatePdfUA(doc, part);
        Assert.False(report.Has("VALIDATION-INTERNAL"), string.Join("\n", report.Findings));
        return report;
    }

    static ValidationFinding OnlyAnnotationFinding(ValidationReport report, string rule)
    {
        var finding = Assert.Single(report.Findings.Where(f => f.RuleId.StartsWith("PDFUA-ANNOT-")));
        Assert.Equal(rule, finding.RuleId);
        Assert.Equal(ValidationSeverity.Error, finding.Severity);
        Assert.Equal(0, finding.PageIndex);
        return finding;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ParentTreeAndDirectObjrIdentifyTheAnnotation(int part)
    {
        foreach (var kids in new[] { "11 0 R", "[0 11 0 R]", "<< /Type /OBJR /Obj 9 0 R >>" })
        {
            var doc = Load(part, (8, Element.Replace("11 0 R", kids)));
            var element = doc.Structure!.ElementFor(Assert.IsType<PdfDictionary>(doc.File.GetObject(8)))!;
            Assert.Same(doc.File.GetObject(9), Assert.Single(element.Children.OfType<StructObjectRef>()).Object);
            Assert.True(Validate(doc, part).IsCompliant);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void StructParentMustBeAnInRangeNonNegativeIntegerWithAMapping(int part)
    {
        foreach (var key in new[] { "/One", "(1)", "true", "[]", "<< >>", "null", "1.0", "1.5", "-1", "2147483648", "9223372036854775807", "2" })
        {
            var doc = Load(part, (9, Annotation.Replace("/StructParent 1", $"/StructParent {key}")));
            var raw = Assert.IsType<PdfDictionary>(doc.File.GetObject(9)).Get("StructParent");
            Assert.NotNull(raw);
            var finding = OnlyAnnotationFinding(Validate(doc, part), "PDFUA-ANNOT-PARENTTREE");
            Assert.Equal("ISO 32000-2 14.7.5.4", finding.Clause);
            Assert.Same(raw, Assert.IsType<PdfDictionary>(doc.File.GetObject(9)).Get("StructParent"));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void MissingStructParentKeepsTheExistingDiagnosticWithoutDuplicates(int part)
    {
        var doc = Load(part, (9, Annotation.Replace(" /StructParent 1", "")));
        var finding = OnlyAnnotationFinding(Validate(doc, part), "PDFUA-ANNOT-UNTAGGED");
        Assert.Equal("/Stamp annotation has no /StructParent (not tagged)", finding.Message);
        Assert.Equal(part == 1 ? "ISO 14289-1 7.18.1" : "ISO 14289-2 8.9", finding.Clause);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void UnresolvedStructParentReportsInvalidMappingAndKeepsRawReference(int part)
    {
        var doc = Load(part, (9, Annotation.Replace("/StructParent 1", "/StructParent 99 0 R")));
        OnlyAnnotationFinding(Validate(doc, part), "PDFUA-ANNOT-PARENTTREE");
        Assert.IsType<PdfRef>(Assert.IsType<PdfDictionary>(doc.File.GetObject(9)).Get("StructParent"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void StructParentAcceptsBoundaryKeysAndResolvedIntegers(int part)
    {
        foreach (var key in new[] { "0", "2147483647" })
        {
            var doc = Load(part,
                (9, Annotation.Replace("/StructParent 1", $"/StructParent {key}")),
                (10, $"<< /Nums [{key} 8 0 R] >>"));
            Assert.True(Validate(doc, part).IsCompliant);
        }

        var indirectKey = Load(part,
            (9, Annotation.Replace("/StructParent 1", "/StructParent 13 0 R")),
            (13, "1"));
        Assert.True(Validate(indirectKey, part).IsCompliant);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ParentTreeLookupSupportsBoundedKidsAndCycles(int part)
    {
        var doc = Load(part,
            (10, "<< /Kids [10 0 R 13 0 R] >>"),
            (13, "<< /Limits [1 1] /Nums [1 8 0 R] >>"));
        Assert.True(Validate(doc, part).IsCompliant);

        var cycle = Load(part, (10, "<< /Kids [10 0 R] >>"));
        OnlyAnnotationFinding(Validate(cycle, part), "PDFUA-ANNOT-PARENTTREE");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ParentTreeMustMapTheKeyToAParsedElement(int part)
    {
        foreach (var nums in new[] { "", "2 8 0 R", "1 [8 0 R]", "1 << /Type /StructElem /S /Annot >>", "1 12 0 R", "1 5 0 R", "1 99 0 R", "1 true", "1 null" })
        {
            var doc = Load(part, (10, $"<< /Nums [{nums}] >>"));
            OnlyAnnotationFinding(Validate(doc, part), "PDFUA-ANNOT-PARENTTREE");
        }

        var missingTree = Load(part, (5, "<< /Type /StructTreeRoot /K 7 0 R >>"));
        OnlyAnnotationFinding(Validate(missingTree, part), "PDFUA-ANNOT-PARENTTREE");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ParentElementMustHaveAMatchingDirectObjr(int part)
    {
        foreach (var kids in new[] { "", "/K []", "/K 0", "/K 13 0 R" })
        {
            var doc = Load(part, (8, Element.Replace("/K 11 0 R", kids)));
            var finding = OnlyAnnotationFinding(Validate(doc, part), "PDFUA-ANNOT-OBJR");
            Assert.Equal("ISO 32000-2 14.7.5.3", finding.Clause);
        }

        var wrongParent = Load(part, (10, "<< /Nums [1 7 0 R] >>"));
        OnlyAnnotationFinding(Validate(wrongParent, part), "PDFUA-ANNOT-OBJR");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ObjrMustIdentifyTheSameResolvedAnnotationByIdentity(int part)
    {
        var doc = Load(part, (11, "<< /Type /OBJR /Obj 12 0 R >>"));
        var annotation = Assert.IsType<PdfDictionary>(doc.File.GetObject(9));
        var other = Assert.IsType<PdfDictionary>(doc.File.GetObject(12));
        Assert.NotSame(annotation, other);
        Assert.Equal(annotation.Entries.Keys.Order(), other.Entries.Keys.Order());
        Assert.Equal(Assert.IsType<PdfString>(annotation.Get("Contents")).Text, Assert.IsType<PdfString>(other.Get("Contents")).Text);
        OnlyAnnotationFinding(Validate(doc, part), "PDFUA-ANNOT-OBJR");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ObjrMustHaveAnIndirectObjEvenWhenItsResolvedIdentityMatches(int part)
    {
        var doc = Load(part);
        var annotation = Assert.IsType<PdfDictionary>(doc.File.GetObject(9));
        var objr = Assert.IsType<PdfDictionary>(doc.File.GetObject(11));
        objr.Entries["Obj"] = annotation;
        var element = doc.Structure!.ElementFor(Assert.IsType<PdfDictionary>(doc.File.GetObject(8)))!;
        Assert.Same(annotation, Assert.Single(element.Children.OfType<StructObjectRef>()).Object);
        OnlyAnnotationFinding(Validate(doc, part), "PDFUA-ANNOT-OBJR");
        Assert.Same(annotation, objr.Get("Obj"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void MalformedObjrRemainsObservableAndDoesNotSatisfyTheCheck(int part)
    {
        foreach (var entries in new[] { "", "/Obj null", "/Obj 99 0 R", "/Obj [9 0 R]", "/Obj << /Type /Annot >>" })
        {
            var doc = Load(part, (11, $"<< /Type /OBJR {entries} >>"));
            var raw = Assert.IsType<PdfDictionary>(doc.File.GetObject(11));
            OnlyAnnotationFinding(Validate(doc, part), "PDFUA-ANNOT-OBJR");
            Assert.Same(raw, doc.File.GetObject(11));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SharedParentChecksEachAnnotationAndCachesOnlyDirectReferences(int part)
    {
        var doc = Load(part, (3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Annots [9 0 R 12 0 R] /Tabs /S >>"));
        OnlyAnnotationFinding(Validate(doc, part), "PDFUA-ANNOT-OBJR");

        var both = Load(part,
            (3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Annots [9 0 R 12 0 R] /Tabs /S >>"),
            (8, Element.Replace("11 0 R", "[11 0 R << /Type /OBJR /Obj 12 0 R >>]")));
        Assert.True(Validate(both, part).IsCompliant);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void LinkRetainsContentsOrMappedAltRequirement(int part)
    {
        foreach (var (contents, alt, described) in new[]
        {
            ("/Contents (Link description)", "", true),
            ("", "/Alt (Link description)", true),
            ("/Contents ()", "/Alt (Link description)", true),
            ("", "", false),
            ("/Contents ()", "/Alt ()", false),
            ("/Contents /Invalid", "/Alt /Invalid", false),
        })
        {
            var doc = Load(part,
                (8, $"<< /Type /StructElem /S /Link /P 7 0 R /K 11 0 R {alt} >>"),
                (9, $"<< /Type /Annot /Subtype /Link /Rect [0 0 10 10] /StructParent 1 {contents} >>"));
            var report = Validate(doc, part);
            Assert.DoesNotContain(report.Findings, f => f.RuleId.StartsWith("PDFUA-ANNOT-"));
            Assert.Equal(!described, report.Has("PDFUA-LINK-DESCRIPTION"));
            Assert.Equal(described, report.IsCompliant);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void InvalidLinkMappingCannotBorrowAltFromAnotherKey(int part)
    {
        var doc = Load(part,
            (8, Element.Replace("/S /Annot", "/S /Link /Alt (Description)")),
            (9, "<< /Type /Annot /Subtype /Link /StructParent 1.5 >>"));
        var report = Validate(doc, part);
        OnlyAnnotationFinding(report, "PDFUA-ANNOT-PARENTTREE");
        Assert.True(report.Has("PDFUA-LINK-DESCRIPTION"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ExistingExemptAnnotationSubtypesRemainExempt(int part)
    {
        foreach (var subtype in new[] { "Popup", "PrinterMark", "TrapNet" })
        {
            var doc = Load(part, (9, $"<< /Type /Annot /Subtype /{subtype} >>"));
            Assert.True(Validate(doc, part).IsCompliant);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void DocumentWithoutAnnotationsRemainsClean(int part)
    {
        var doc = Load(part,
            (3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>"),
            (8, "<< /Type /StructElem /S /P /P 7 0 R >>"));
        Assert.Empty(Validate(doc, part).Findings);
    }
}
