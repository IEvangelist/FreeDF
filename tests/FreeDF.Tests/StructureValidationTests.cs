using DocumentRenderer = global::PortableDocument.Render.PortableDocument;
using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;
using PortableDocument.Render;

namespace FreeDF.Tests;

/// <summary>Builds PDFs from explicit object bodies (object n = bodies[n-1]); object 1 must be the catalog.</summary>
static class ObjPdf
{
    public static byte[] Build(params string[] bodies)
    {
        var sb = new StringBuilder("%PDF-1.7\n%\u00E2\u00E3\u00CF\u00D3\n");
        var offs = new List<int>();
        for (var i = 0; i < bodies.Length; i++)
        {
            offs.Add(sb.Length);
            sb.Append($"{i + 1} 0 obj\n{bodies[i]}\nendobj\n");
        }
        var xref = sb.Length;
        sb.Append($"xref\n0 {offs.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offs) sb.Append($"{o:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {offs.Count + 1} /Root 1 0 R /ID [<00112233445566778899AABBCCDDEEFF> <00112233445566778899AABBCCDDEEFF>] >>\nstartxref\n{xref}\n%%EOF\n");
        var bytes = Encoding.Latin1.GetBytes(sb.ToString());
        // Set PDF_VALIDATION_DUMP to a directory to export every built fixture for cross-checking with veraPDF/qpdf/pikepdf.
        if (Environment.GetEnvironmentVariable("PDF_VALIDATION_DUMP") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, $"obj-{Interlocked.Increment(ref _dumpCounter):D4}.pdf"), bytes);
        }
        return bytes;
    }

    static int _dumpCounter;

    public static string Stream(string content, string dict = "") => $"<< /Length {content.Length} {dict} >>\nstream\n{content}\nendstream";

    public static string Xmp(string body) =>
        Stream("<?xpacket begin='\uFEFF' id='W5M0MpCehiHzreSzNTczkc9d'?><x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'>"
            + body + "</rdf:RDF></x:xmpmeta><?xpacket end='w'?>", "/Type /Metadata /Subtype /XML");
}

public class StructureTests
{
    // Content order: paragraph (MCID 1) first, heading (MCID 0) second; structure order is the reverse.
    internal static byte[] Tagged(string extraKids = "", string extraObjects = "")
    {
        var objs = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /StructTreeRoot 5 0 R /MarkInfo << /Marked true >> /Lang (en-US) >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R /Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> /StructParents 0 >>",
            ObjPdf.Stream("/P <</MCID 1>> BDC BT /F1 12 Tf 10 100 Td (Second para) Tj ET EMC /H1 <</MCID 0>> BDC BT /F1 14 Tf 10 150 Td (Title) Tj ET EMC"),
            "<< /Type /StructTreeRoot /K 6 0 R /ParentTree 9 0 R /RoleMap << /Heading1 /H1 /Custom /Span >> >>",
            "<< /Type /StructElem /S /Document /K [7 0 R 8 0 R " + extraKids + "] >>",
            "<< /Type /StructElem /S /Heading1 /P 6 0 R /Pg 3 0 R /K 0 /T (Head) >>",
            "<< /Type /StructElem /S /P /P 6 0 R /Pg 3 0 R /Lang (fr) /K << /Type /MCR /Pg 3 0 R /MCID 1 >> >>",
            "<< /Nums [0 [7 0 R 8 0 R]] >>",
        };
        var b = ObjPdf.Build(objs.ToArray());
        return extraObjects.Length == 0 ? b : b;
    }

    [Fact]
    public void ParsesTreeRoleMapAndMcids()
    {
        var doc = PdfDocument.Load(Tagged());
        var tree = doc.Structure;
        Assert.NotNull(tree);
        var root = Assert.Single(tree!.Children);
        Assert.Equal("Document", root.Type);
        Assert.Equal(2, root.ChildElements.Count());
        var h = root.ChildElements.First();
        Assert.Equal("Heading1", h.Type);
        Assert.Equal("H1", h.StandardType);
        Assert.True(h.IsHeading);
        Assert.Equal("Head", h.Title);
        Assert.Equal(0, h.PageIndex);
        Assert.Equal(0, Assert.Single(h.MarkedContent).Mcid);
        var p = root.ChildElements.Last();
        Assert.Equal("fr", p.EffectiveLang);
        Assert.Equal("en-US", doc.GetLanguage());
        Assert.Same(p, tree.FindMarkedContent(0, 1)!.Parent);
        Assert.Same(h, tree.ElementFromParentTree(0, 0));
        Assert.Same(p, tree.ElementFromParentTree(0, 1));
        Assert.Equal("Span", tree.ResolveRole("Custom"));
    }

    [Fact]
    public void StructureOrderedTextDiffersFromContentOrder()
    {
        var doc = PdfDocument.Load(Tagged());
        var content = string.Concat(DocumentRenderer.ExtractText(doc.Pages[0]).Select(s => s.Text + "|"));
        Assert.StartsWith("Second para", content);
        Assert.Equal("Title\nSecond para", StructuredTextExtractor.Extract(doc));
    }

    [Fact]
    public void SpansCarryMcid()
    {
        var doc = PdfDocument.Load(Tagged());
        var spans = DocumentRenderer.ExtractText(doc.Pages[0]);
        Assert.Equal([1, 0], spans.Select(s => s.Mcid));
    }

    [Fact]
    public void ActualTextOverridesContent()
    {
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R /StructTreeRoot 5 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R /Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> /StructParents 0 >>",
            ObjPdf.Stream("/Span <</MCID 0>> BDC BT /F1 12 Tf 10 100 Td (f i) Tj ET EMC"),
            "<< /Type /StructTreeRoot /K 6 0 R >>",
            "<< /S /Span /Pg 3 0 R /ActualText (fi) /K 0 >>"));
        Assert.Equal("fi", StructuredTextExtractor.Extract(doc));
    }

    [Fact]
    public void CyclicAndHostileTreesTerminate()
    {
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R /StructTreeRoot 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 10 10] >>",
            "<< /Type /StructTreeRoot /K 5 0 R /RoleMap << /A /B /B /A >> >>",
            "<< /S /A /K [5 0 R 6 0 R] >>",
            "<< /S /B /K [5 0 R 4 0 R] >>"));
        var tree = doc.Structure!;
        Assert.Equal(2, tree.Elements().Count());
        Assert.Equal("A", tree.ResolveRole("B") is "A" or "B" ? "A" : "");
        Assert.NotNull(StructuredTextExtractor.Extract(doc));
    }

    [Fact]
    public void UntaggedDocumentHasNoStructure()
    {
        var doc = PdfDocument.Load(TestPdf.Build("0 0 10 10 re f"));
        Assert.Null(doc.Structure);
        Assert.Null(StructuredTextExtractor.Extract(doc));
    }

    [Theory]
    [InlineData("H", 0)]
    [InlineData("H1", 1)]
    [InlineData("H12", 12)]
    [InlineData("H0", null)]
    [InlineData("Hx", null)]
    [InlineData("P", null)]
    public void HeadingLevels(string type, int? level) => Assert.Equal(level, StructureTree.HeadingLevel(type));
}

public class StructureConsistencyTests
{
    static PdfDocument Load(string documentKids = "[7 0 R]", string paragraph = "<< /S /P /P 6 0 R /Pg 3 0 R /K 0 >>", string parentTree = "<< /Nums [0 [7 0 R]] >>", params string[] more) =>
        PdfDocument.Load(ObjPdf.Build(
            [
                "<< /Type /Catalog /Pages 2 0 R /StructTreeRoot 5 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> /StructParents 0 >>",
                ObjPdf.Stream("/P <</MCID 0>> BDC BT /F1 12 Tf 10 50 Td (Body) Tj ET EMC"),
                "<< /Type /StructTreeRoot /K 6 0 R /ParentTree 8 0 R >>",
                $"<< /S /Document /P 5 0 R /K {documentKids} >>",
                paragraph,
                parentTree,
                .. more,
            ]));

    static string[] Codes(PdfDocument doc) => doc.Structure!.CheckConsistency().Select(issue => issue.Code).Distinct().Order().ToArray();

    [Fact]
    public void ConsistentTreeHasNoIssues()
    {
        var doc = Load();
        Assert.Empty(doc.Structure!.CheckConsistency());
        Assert.True(doc.Structure.GetParentTreeDiagnostics()!.IsValid);
        Assert.Equal("Body", StructuredTextExtractor.Extract(doc));
    }

    [Fact]
    public void ParentAndParentTreeMismatchesAreReported()
    {
        Assert.Equal(["STRUCT-P-MISMATCH"], Codes(Load(paragraph: "<< /S /P /P 5 0 R /Pg 3 0 R /K 0 >>")));
        Assert.Equal(["STRUCT-P-MISSING"], Codes(Load(paragraph: "<< /S /P /Pg 3 0 R /K 0 >>")));
        Assert.Equal(["STRUCT-PARENTTREE-MISMATCH"], Codes(Load(parentTree: "<< /Nums [0 [6 0 R]] >>")));
    }

    [Fact]
    public void ParentTreeProblemsAreReported()
    {
        Assert.Contains("STRUCT-PARENTTREE-MISMATCH", Codes(Load(parentTree: "<< /Nums [1 [7 0 R]] >>")));
        Assert.Contains("STRUCT-PARENTTREE-MISSING", Codes(Load(parentTree: "null")));
    }

    [Fact]
    public void DuplicateMarkedContentClaimsKeepTheFirstOwner()
    {
        var doc = Load("[7 0 R 9 0 R]", more: "<< /S /P /P 6 0 R /Pg 3 0 R /K 0 >>");
        Assert.Contains("STRUCT-MCID-DUPLICATE", Codes(doc));
        Assert.Same(doc.Structure!.ElementFor((PdfDictionary)doc.File.GetObject(7)!), doc.Structure.FindMarkedContent(0, 0)!.Parent);
    }

    [Fact]
    public void CyclicAndSharedElementsAreReportedAsParseIssuesWithoutLoopingOrDuplicating()
    {
        var doc = Load("[7 0 R 6 0 R 7 0 R]");
        var issues = doc.Structure!.ParseIssues;
        Assert.Equal(2, issues.Count(issue => issue.Code == "STRUCT-ELEMENT-REVISITED"));
        Assert.Single(doc.Structure.Elements(), element => element.Type == "P");
        Assert.Equal("Body", StructuredTextExtractor.Extract(doc));
    }

    [Theory]
    [InlineData("<< /Type /MCR /Pg 3 0 R /MCID 0 /Stm << /F (other.pdf) >> >>", "STRUCT-MCR-STM-NONLOCAL")]
    [InlineData("<< /Type /MCR /Pg 3 0 R /MCID 0 /Stm 4 0 R /StmOwn << /Type /Annot >> >>", "STRUCT-MCR-STMOWN")]
    [InlineData("<< /Type /MCR /Pg 3 0 R >>", "STRUCT-MCR-MCID")]
    [InlineData("<< /Type /OBJR /Pg 3 0 R >>", "STRUCT-OBJR-OBJ")]
    public void MalformedOrExternalReferencesAreReportedAndInert(string kid, string code)
    {
        var doc = Load(paragraph: $"<< /S /P /P 6 0 R /Pg 3 0 R /K [{kid}] >>");
        Assert.Contains(doc.Structure!.ParseIssues, issue => issue.Code == code);
        Assert.Equal("", StructuredTextExtractor.Extract(doc));
        Assert.All(doc.Structure.ParseIssues, issue => Assert.StartsWith("ISO 32000-2 14.7.5", issue.Clause));
    }

    [Fact]
    public void PageLessMarkedContentIsAmbiguous()
    {
        var doc = Load(paragraph: "<< /S /P /P 6 0 R /K 0 >>");
        Assert.Contains("STRUCT-MCR-NOPAGE", Codes(doc));
    }

    [Fact]
    public void ObjectReferencesMustMapBackThroughTheParentTree()
    {
        const string annotation = "<< /Type /Annot /Subtype /Stamp /Rect [0 0 10 10] /StructParent 1 >>";
        const string figure = "<< /S /Figure /P 6 0 R /K << /Type /OBJR /Obj 7 0 R >> >>";
        var ok = Load("[9 0 R]", paragraph: annotation, parentTree: "<< /Nums [1 9 0 R] >>", more: figure);
        Assert.Empty(ok.Structure!.CheckConsistency());
        var bad = Load("[9 0 R]", paragraph: annotation, parentTree: "<< /Nums [1 [9 0 R]] >>", more: figure);
        Assert.Equal(["STRUCT-OBJR-PARENTTREE"], Codes(bad));
        var duplicate = Load("[9 0 R 10 0 R]", paragraph: annotation, parentTree: "<< /Nums [1 9 0 R] >>", more: [figure, figure]);
        Assert.Contains("STRUCT-OBJR-DUPLICATE", Codes(duplicate));
    }

    [Fact]
    public void ParentTreeDiagnosticsReportDuplicateOrderLimitsAndCycles()
    {
        var doc = Load(parentTree: "<< /Kids [9 0 R 10 0 R 8 0 R] >>",
            more: ["<< /Limits [0 2] /Nums [2 [7 0 R] 0 [7 0 R]] >>", "<< /Limits [5 6] /Nums [2 [7 0 R]] >>"]);
        var kinds = doc.Structure!.GetParentTreeDiagnostics()!.Issues.Select(issue => issue.Kind).Distinct().Order().ToArray();
        Assert.Equal(["Cycle", "DuplicateKey", "KeyOutOfOrder", "LimitsMismatch"], kinds);
    }

    [Fact]
    public void OrganizationalTreeWithoutContentItemsNeedsNoParentTree()
    {
        var doc = Load("[]", parentTree: "null");
        Assert.Empty(doc.Structure!.CheckConsistency());
    }

    [Fact]
    public void FlatParentTreeLookupsScaleLinearly()
    {
        const int count = 20_000;
        var nums = string.Concat(Enumerable.Range(0, count).Select(k => $"{k} [7 0 R] "));
        var doc = Load(parentTree: $"<< /Nums [{nums}] >>");
        var tree = doc.Structure!;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var k = 0; k < count; k++) Assert.IsType<PdfArray>(tree.ParentTreeLookup(k));
        Assert.Null(tree.ParentTreeLookup(count));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), watch.Elapsed.ToString());
        Assert.False(tree.ParentTreeIndexTruncated);
        Assert.Equal(count, tree.GetParentTreeDiagnostics()!.KeyCount);
    }

    [Fact]
    public void IssueListsAreBounded()
    {
        var doc = Load($"[7 0 R {string.Concat(Enumerable.Repeat("7 0 R ", 1200))}]");
        Assert.Equal(1000, doc.Structure!.ParseIssues.Count);
        Assert.True(doc.Structure.IssuesTruncated);
        Assert.Equal(1000, doc.Structure.CheckConsistency().Count);
    }
}

public class ValidationTests
{
    const string XmpUa = "<rdf:Description rdf:about='' xmlns:pdfuaid='http://www.aiim.org/pdfua/ns/id/' xmlns:dc='http://purl.org/dc/elements/1.1/' pdfuaid:part='1'><dc:title><rdf:Alt><rdf:li xml:lang='x-default'>Doc</rdf:li></rdf:Alt></dc:title></rdf:Description>";

    // Graphics-only tagged document; structXml elements are objects 7.. (K of the Document element listed in `kids`).
    static byte[] Ua(string kids, params string[] elems) => UaWithClassMap(kids, "", elems);

    static byte[] UaWithClassMap(string kids, string classMap, params string[] elems)
    {
        var classMapEntry = classMap.Length == 0 ? "" : $" /ClassMap << {classMap} >>";
        var objs = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /StructTreeRoot 5 0 R /MarkInfo << /Marked true >> /Lang (en) /ViewerPreferences << /DisplayDocTitle true >> /Metadata 6 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /StructParents 0 >>",
            ObjPdf.Stream(""),
            $"<< /Type /StructTreeRoot /K 7 0 R /ParentTree << /Nums [0 []] >>{classMapEntry} >>",
            ObjPdf.Xmp(XmpUa),
            $"<< /S /Document /K [{kids}] >>",
        };
        objs.AddRange(elems);
        return ObjPdf.Build(objs.ToArray());
    }

    static ValidationReport Ua1(byte[] pdf) => PdfValidator.Validate(PdfDocument.Load(pdf), PdfValidationProfile.PdfUA1);

    [Fact]
    public void UaCompliantMinimalDocument()
    {
        var r = Ua1(Ua("8 0 R", "<< /S /P >>"));
        Assert.True(r.IsCompliant, string.Join("\n", r.Findings));
    }

    [Fact]
    public void UaFlagsDocumentLevelProblems()
    {
        var r = Ua1(TestPdf.Build("0 0 10 10 re f"));
        foreach (var id in new[] { "PDFUA-MARKINFO", "PDFUA-STRUCTTREE", "PDFUA-LANG", "PDFUA-DISPLAYDOCTITLE", "PDFUA-XMP-MISSING" })
            Assert.True(r.Has(id), id);
        Assert.False(r.IsCompliant);
    }

    [Fact]
    public void UaFigureAltHeadingsTablesLists()
    {
        var r = Ua1(Ua("8 0 R 9 0 R 10 0 R 11 0 R 12 0 R 13 0 R",
            "<< /S /Figure >>",
            "<< /S /H3 >>",
            "<< /S /H1 >>",
            "<< /S /Table /K [14 0 R] >>",
            "<< /S /L /K [15 0 R] >>",
            "<< /S /Figure /Alt (a chart) >>",
            "<< /S /TR /K [16 0 R 16 0 R] >>",
            "<< /S /LI /K [17 0 R] >>",
            "<< /S /TD >>",
            "<< /S /TD >>"));
        Assert.Contains(r.Findings, f => f.RuleId == "PDFUA-ALT-MISSING");
        Assert.Single(r.Findings, f => f.RuleId == "PDFUA-ALT-MISSING");
        Assert.Contains(r.Findings, f => f.RuleId == "PDFUA-HEADING-NESTING" && f.Message.Contains("H3"));
        Assert.Contains(r.Findings, f => f.RuleId == "PDFUA-TABLE-HEADERS");
        Assert.Contains(r.Findings, f => f.RuleId == "PDFUA-LI-CHILD");
    }

    [Fact]
    public void UaTableWithHeaderIsClean()
    {
        var r = Ua1(Ua("8 0 R", "<< /S /Table /K [9 0 R 10 0 R] >>", "<< /S /TR /K [11 0 R 11 0 R] >>", "<< /S /TR /K [12 0 R 12 0 R] >>", "<< /S /TH /A << /O /Table /Scope /Column >> >>", "<< /S /TD >>"));
        Assert.DoesNotContain(r.Findings, f => f.RuleId.StartsWith("PDFUA-TABLE") || f.RuleId == "PDFUA-ROW-CHILD");
    }

    [Fact]
    public void UaTableHeaderAssociationsAcceptDirectAndClassAttributes()
    {
        var r = Ua1(UaWithClassMap("8 0 R", "/ColumnHeader << /O /Table /Scope /Column >> /HeaderRefs << /O /Table /Headers [(h1)] >>",
            "<< /S /Table /K [9 0 R 10 0 R] >>",
            "<< /S /TR /K [11 0 R 12 0 R] >>",
            "<< /S /TR /K [13 0 R 14 0 R] >>",
            "<< /S /TH /ID (h1) /C /ColumnHeader >>",
            "<< /S /TD /C /HeaderRefs >>",
            "<< /S /TD /C /HeaderRefs >>",
            "<< /S /TD /C /HeaderRefs >>"));

        Assert.DoesNotContain(r.Findings, f => f.RuleId.StartsWith("PDFUA-TABLE-"));
    }

    [Fact]
    public void UaTableWithoutScopeGetsARecommendationNotAnError()
    {
        var r = Ua1(Ua("8 0 R",
            "<< /S /Table /K [9 0 R 10 0 R] >>",
            "<< /S /TR /K [11 0 R 12 0 R] >>",
            "<< /S /TR /K [13 0 R 14 0 R] >>",
            "<< /S /TH >>",
            "<< /S /TD >>",
            "<< /S /TD >>",
            "<< /S /TD >>"));

        Assert.Contains(r.Findings, f => f.RuleId == "PDFUA-TABLE-SCOPE-RECOMMENDED" && f.Severity == ValidationSeverity.Info);
        Assert.DoesNotContain(r.Findings, f => f.RuleId == "PDFUA-TABLE-SCOPE" && f.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void UaTableReportsInvalidHeaderScopeAndReferences()
    {
        var r = Ua1(Ua("8 0 R",
            "<< /S /Table /K [9 0 R 10 0 R] >>",
            "<< /S /TR /K [11 0 R 12 0 R] >>",
            "<< /S /TR /K [13 0 R 14 0 R] >>",
            "<< /S /TH /ID (h1) /A << /O /Table /Scope /Diagonal >> >>",
            "<< /S /TH /ID (h1) /A << /O /Table /Scope /Column >> >>",
            "<< /S /TD /A << /O /Table /Headers [(missing)] >> >>",
            "<< /S /TD /A << /O /Table /Headers /h1 >> >>"));

        Assert.Contains(r.Findings, f => f.RuleId == "PDFUA-TABLE-SCOPE");
        Assert.Contains(r.Findings, f => f.RuleId == "PDFUA-TABLE-HEADER-ID-DUPLICATE");
        Assert.Contains(r.Findings, f => f.RuleId == "PDFUA-TABLE-HEADERS-REFERENCE");
        Assert.Contains(r.Findings, f => f.RuleId == "PDFUA-TABLE-HEADERS-FORMAT");
    }

    [Theory]
    [InlineData(1, 1, false)]
    [InlineData(1, 2, false)]
    [InlineData(2, 1, false)]
    [InlineData(2, 2, true)]
    [InlineData(2, 1000, true)]
    public void Ua2TableRejectsSpansBeyondFinalRow(int part, int span, bool invalid)
    {
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(Ua("8 0 R",
            "<< /S /Table /K [9 0 R 10 0 R] >>",
            "<< /S /TR /K [11 0 R 12 0 R] >>",
            "<< /S /TR /K [13 0 R 14 0 R] >>",
            "<< /S /TH /A << /O /Table /Scope /Column >> >>",
            "<< /S /TD >>",
            "<< /S /TD >>",
            $"<< /S /TD /A << /O /Table /RowSpan {span} >> >>")), part);
        Assert.Equal(invalid, report.Has("PDFUA2-TABLE-ROWSPAN-END"));
        Assert.False(report.Has("PDFUA-TABLE-IRREGULAR"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void UaTableRowSpanEndingAtFinalRowIsValid(int part)
    {
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(Ua("8 0 R",
            "<< /S /Table /K [9 0 R 10 0 R] >>",
            "<< /S /TR /K [11 0 R 12 0 R] >>",
            "<< /S /TR /K 13 0 R >>",
            "<< /S /TH /A << /O /Table /Scope /Column /RowSpan 2 >> >>",
            "<< /S /TD >>",
            "<< /S /TD >>")), part);
        Assert.False(report.Has("PDFUA2-TABLE-ROWSPAN-END"));
        Assert.False(report.Has("PDFUA-TABLE-IRREGULAR"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    public static IEnumerable<object[]> TableSpanAttributeCases()
    {
        foreach (var part in new[] { 1, 2 })
        foreach (var kind in new[] { "local-table", "local-layout", "local-omitted", "class-table", "class-layout",
            "local-null-class", "local-one-class-two", "local-two-class-one" })
            yield return [part, kind];
    }

    [Theory]
    [MemberData(nameof(TableSpanAttributeCases))]
    public void Ua2TableSpansUseOwnedLocalAndClassAttributes(int part, string kind)
    {
        var local = kind switch
        {
            "local-layout" => "<< /O /Layout /ColSpan 2 >>",
            "local-omitted" => "<< /ColSpan 2 >>",
            "class-table" or "class-layout" => "null",
            "local-null-class" => "<< /O /Table /ColSpan null >>",
            "local-one-class-two" => "<< /O /Table /ColSpan 1 >>",
            _ => "<< /O /Table /ColSpan 2 >>",
        };
        var classes = kind.Contains("class", StringComparison.Ordinal) ? "/C /Span" : "";
        var owner = kind == "class-layout" ? "Layout" : "Table";
        var span = kind == "local-two-class-one" ? 1 : 2;
        var pdf = UaWithClassMap("8 0 R", $"/Span << /O /{owner} /ColSpan {span} >>",
            "<< /S /Table /K [9 0 R 10 0 R] >>",
            "<< /S /TR /K [11 0 R 12 0 R] >>",
            "<< /S /TR /K [13 0 R 14 0 R] >>",
            $"<< /S /TH /A {local} {classes} >>",
            "<< /S /TD >>",
            "<< /S /TD >>",
            "<< /S /TD >>");
        var report = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), part);
        var irregular = part == 2
            ? kind is "local-table" or "class-table" or "local-null-class" or "local-two-class-one"
            : kind is "local-table" or "local-layout" or "local-omitted" or "local-two-class-one";
        Assert.Equal(irregular, report.Has("PDFUA-TABLE-IRREGULAR"));
        Assert.False(report.Has("PDFUA2-TABLE-ATTRIBUTE-INCOMPLETE"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData("A")]
    [InlineData("C")]
    [InlineData("ClassMap")]
    public void Ua2TableCyclicAttributeReferencesAreIncomplete(string key)
    {
        var doc = PdfDocument.Load(Ua("8 0 R",
            "<< /S /Table /K [9 0 R 10 0 R] >>",
            "<< /S /TR /K [11 0 R 12 0 R] >>",
            "<< /S /TR /K [13 0 R 14 0 R] >>",
            $"<< /S /TH {(key == "A" ? "/A 15 0 R" : key == "C" ? "/C 15 0 R" : "/C /Span")} >>",
            "<< /S /TD >>", "<< /S /TD >>", "<< /S /TD >>", "15 0 R"));
        doc.Structure!.Root.Entries["ClassMap"] = key == "ClassMap" ? new PdfRef(15, 0) : new PdfDictionary();
        var report = PdfValidator.ValidatePdfUA(doc, 2);
        Assert.True(report.Has("PDFUA2-TABLE-ATTRIBUTE-INCOMPLETE"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Theory]
    [InlineData(199999, false)]
    [InlineData(200000, true)]
    public void Ua2TableClassAndAttributeWorkShareOneBound(int count, bool incomplete)
    {
        var doc = PdfDocument.Load(Ua("8 0 R",
            "<< /S /Table /K [9 0 R 10 0 R] >>",
            "<< /S /TR /K [11 0 R 12 0 R] >>",
            "<< /S /TR /K [13 0 R 14 0 R] >>",
            "<< /S /TH /C /Span >>",
            "<< /S /TD >>", "<< /S /TD >>", "<< /S /TD >>"));
        var attributes = new PdfArray(Enumerable.Repeat<PdfObject>(new PdfDictionary(), count - 1)
            .Append(new PdfDictionary { Entries = { ["O"] = new PdfName("Table"), ["ColSpan"] = new PdfInteger(2) } }).ToList());
        doc.Structure!.Root.Entries["ClassMap"] = new PdfDictionary { Entries = { ["Span"] = attributes } };
        var report = PdfValidator.ValidatePdfUA(doc, 2);
        Assert.Equal(incomplete, report.Has("PDFUA2-TABLE-ATTRIBUTE-INCOMPLETE"));
        Assert.Equal(!incomplete, report.Has("PDFUA-TABLE-IRREGULAR"));
    }

    [Fact]
    public void Ua2TableLocalAttributesSkipUnusedCyclicClassMap()
    {
        var doc = PdfDocument.Load(Ua("8 0 R",
            "<< /S /Table /K [9 0 R 10 0 R] >>",
            "<< /S /TR /K [11 0 R 12 0 R] >>",
            "<< /S /TR /K [13 0 R 14 0 R] >>",
            "<< /S /TH /C /Span /A << /O /Table /ColSpan 1 /RowSpan 1 /Scope /Column /Headers [] >> >>",
            "<< /S /TD >>", "<< /S /TD >>", "<< /S /TD >>", "15 0 R"));
        doc.Structure!.Root.Entries["ClassMap"] = new PdfRef(15, 0);
        var report = PdfValidator.ValidatePdfUA(doc, 2);
        Assert.False(report.Has("PDFUA2-TABLE-ATTRIBUTE-INCOMPLETE"));
        Assert.False(report.Has("PDFUA-TABLE-IRREGULAR"));
    }

    public static IEnumerable<object[]> TableNamespaceCases()
    {
        foreach (var kind in new[] { "default", "table", "rows", "headers", "data", "all" })
        foreach (var broken in new[] { false, true })
            yield return [kind, broken, ""];
        foreach (var section in new[] { "THead", "TBody", "TFoot" })
        foreach (var kind in new[] { "default", "table", "rows", "all" })
            yield return [kind, false, section];
    }

    [Theory]
    [MemberData(nameof(TableNamespaceCases))]
    public void Ua2TableRowsAndHeaderAssociationsUseNamespaceRoles(string kind, bool broken, string section)
    {
        var doc = PdfDocument.Load(Ua("8 0 R",
            $"<< /S /Table /K {(section.Length == 0 ? "[9 0 R 10 0 R]" : "15 0 R")} >>",
            "<< /S /TR /K [11 0 R 12 0 R] >>",
            "<< /S /TR /K [13 0 R 14 0 R] >>",
            $"<< /S /TH /ID (h1) /A << /O /Table /Scope /Column /ColSpan {(broken ? 2 : 1)} >> >>",
            "<< /S /TD /A << /O /Table /Headers [(h1)] >> >>",
            "<< /S /TD /A << /O /Table /Headers [(h1)] >> >>",
            "<< /S /TD /A << /O /Table /Headers [(h1)] >> >>",
            section.Length == 0 ? "null" : $"<< /S /{section} /K [9 0 R 10 0 R] >>"));
        var pdf2 = new PdfDictionary { Entries = { ["NS"] = new PdfString(Encoding.ASCII.GetBytes("http://iso.org/pdf2/ssn")) } };
        for (int number = 8; number <= 14; number++)
        {
            var element = Assert.IsType<PdfDictionary>(doc.File.GetObject(number));
            var role = Assert.IsType<PdfName>(element.Get("S")).Value;
            if (kind != "all" && (kind, role) is not (("table", "Table") or ("rows", "TR") or ("headers", "TH") or ("data", "TD")))
                continue;
            var custom = "Custom" + role;
            element.Entries["S"] = new PdfName(custom);
            element.Entries["NS"] = new PdfDictionary
            {
                Entries =
                {
                    ["NS"] = new PdfString(Encoding.ASCII.GetBytes("urn:table-test")),
                    ["RoleMapNS"] = new PdfDictionary { Entries = { [custom] = new PdfArray([new PdfName(role), pdf2]) } },
                },
            };
        }
        var report = PdfValidator.ValidatePdfUA(doc, 2);
        Assert.Equal(broken, report.Has("PDFUA-TABLE-IRREGULAR"));
        Assert.DoesNotContain(report.Findings, finding => finding.RuleId is "PDFUA-TABLE-PARENT" or "PDFUA-TABLE-ROWS"
            or "PDFUA-TABLE-CHILD" or "PDFUA-TABLE-HEADERS" or "PDFUA-ROW-CHILD" or "PDFUA-TABLE-SCOPE"
            or "PDFUA-TABLE-HEADERS-REFERENCE" or "PDFUA-TABLE-HEADERS-CONSISTENCY"
            or "PDFUA-TABLE-SECTION-PARENT" or "PDFUA-TABLE-SECTION-CHILD" or "PDFUA-TABLE-TBODY");
        Assert.False(report.Has("VALIDATION-INTERNAL"));
    }

    [Fact]
    public void Ua2TableRequiresHeadersOnEveryDataCellWhenHeadersAreUsed()
    {
        var pdf = Ua("8 0 R",
            "<< /S /Table /K [9 0 R 10 0 R] >>",
            "<< /S /TR /K [11 0 R 12 0 R] >>",
            "<< /S /TR /K [13 0 R 14 0 R] >>",
            "<< /S /TH /ID (h1) /A << /O /Table /Scope /Column >> >>",
            "<< /S /TD /A << /O /Table /Headers [(h1)] >> >>",
            "<< /S /TD >>",
            "<< /S /TD >>");
        var r = PdfValidator.ValidatePdfUA(PdfDocument.Load(pdf), 2);

        Assert.Equal(2, r.Findings.Count(f => f.RuleId == "PDFUA-TABLE-HEADERS-CONSISTENCY"));
    }

    [Fact]
    public void UaUnmappedTypeAndNonEmbeddedFont()
    {
        var pdf = ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R /StructTreeRoot 5 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> >>",
            ObjPdf.Stream("BT /F1 12 Tf (x) Tj ET"),
            "<< /Type /StructTreeRoot /K 6 0 R >>",
            "<< /S /Weird >>");
        var r = Ua1(pdf);
        Assert.True(r.Has("PDFUA-ROLEMAP-MISSING"));
        Assert.True(r.Has("PDFUA-FONT-EMBEDDED"));
    }

    [Fact]
    public void Ua2UsesPart2Identification()
    {
        var r = PdfValidator.ValidatePdfUA(PdfDocument.Load(Ua("8 0 R", "<< /S /P >>")), 2);
        Assert.True(r.Has("PDFUA-XMP-ID-PART"));
    }

    const string XmpA2 = "<rdf:Description rdf:about='' xmlns:pdfaid='http://www.aiim.org/pdfa/ns/id/' pdfaid:part='2' pdfaid:conformance='B'/>";

    static byte[] PdfA(string resources = "", string pageExtra = "", string xmp = XmpA2, string extraCatalog = "") => ObjPdf.Build(
        $"<< /Type /Catalog /Pages 2 0 R /Metadata 5 0 R {extraCatalog} >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R /Resources << {resources} >> {pageExtra} >>",
        ObjPdf.Stream(""),
        ObjPdf.Xmp(xmp));

    [Fact]
    public void PdfAMinimalDocumentWithoutDeviceColourIsCompliant()
    {
        var r = PdfValidator.Validate(PdfDocument.Load(PdfA()), PdfValidationProfile.PdfA2b);
        Assert.True(r.IsCompliant, string.Join("\n", r.Findings));
    }

    [Fact]
    public void PdfAFlagsMissingXmpFontsAndOutputIntent()
    {
        var pdf = TestPdf.Build("0.5 g 0 0 10 10 re f BT /F1 12 Tf (x) Tj ET", "/Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>");
        var r = PdfValidator.Validate(PdfDocument.Load(pdf), PdfValidationProfile.PdfA2b);
        Assert.True(r.Has("PDFA-XMP-MISSING"));
        Assert.True(r.Has("PDFA-FONT-EMBEDDED"));
        Assert.True(r.Has("PDFA-OUTPUTINTENT-MISSING"));
        Assert.True(r.Has("PDFA-TRAILER-ID"));
    }

    [Fact]
    public void PdfAIdentificationPartMustMatch()
    {
        var r = PdfValidator.Validate(PdfDocument.Load(PdfA()), PdfValidationProfile.PdfA1b);
        Assert.True(r.Has("PDFA-XMP-ID-PART"));
    }

    [Fact]
    public void PdfA1ForbidsTransparencyButA2Allows()
    {
        var pdf = PdfA("/ExtGState << /G1 << /ca 0.5 /BM /Multiply >> >>", "/Group << /S /Transparency >>", XmpA2.Replace("'2'", "'1'"));
        var a1 = PdfValidator.Validate(PdfDocument.Load(pdf), PdfValidationProfile.PdfA1b);
        Assert.True(a1.Has("PDFA1-TRANSPARENCY-ALPHA"));
        Assert.True(a1.Has("PDFA1-TRANSPARENCY-BLEND"));
        Assert.True(a1.Has("PDFA1-TRANSPARENCY-GROUP"));
        var a2 = PdfValidator.Validate(PdfDocument.Load(pdf), PdfValidationProfile.PdfA2b);
        Assert.DoesNotContain(a2.Findings, f => f.RuleId.StartsWith("PDFA1-"));
    }

    [Fact]
    public void PdfAFlagsEncryptionFromFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "encrypted", "aes-128-empty.pdf");
        var r = PdfValidator.Validate(PdfDocument.Open(path), PdfValidationProfile.PdfA3b);
        Assert.True(r.Has("PDFA-ENCRYPT"));
    }

    [Fact]
    public void PdfAOutputIntentRequiresValidProfile()
    {
        var pdf = PdfA("", "", XmpA2, "/OutputIntents [<< /Type /OutputIntent /S /GTS_PDFA1 /DestOutputProfile 6 0 R >>]");
        var withProfile = ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R /Metadata 5 0 R /OutputIntents [<< /Type /OutputIntent /S /GTS_PDFA1 /DestOutputProfile 6 0 R >>] >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>",
            ObjPdf.Stream(""),
            ObjPdf.Xmp(XmpA2),
            ObjPdf.Stream("not an icc profile"));
        var r = PdfValidator.Validate(PdfDocument.Load(withProfile), PdfValidationProfile.PdfA2b);
        Assert.True(r.Has("PDFA-OUTPUTINTENT-ICC"));
        Assert.NotNull(pdf);
    }

    [Fact]
    public void ValidatorsSurviveEveryFixture()
    {
        foreach (var f in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures"), "*.pdf", SearchOption.AllDirectories))
        {
            PdfDocument doc;
            try { doc = PdfDocument.Open(f); } catch (global::PortableDocument.Core.PdfParseException) { continue; }
            foreach (var p in Enum.GetValues<PdfValidationProfile>()) Assert.NotNull(PdfValidator.Validate(doc, p));
        }
    }
}
