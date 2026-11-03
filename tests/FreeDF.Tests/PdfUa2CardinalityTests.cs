using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfUa2CardinalityTests
{
    const string Pairs = "Art-Caption Art-H Artifact-Caption Artifact-H Aside-Caption Aside-H BlockQuote-Caption BlockQuote-H Caption-H Code-Caption Document-H DocumentFragment-H Figure-Caption Figure-H Form-Caption Formula-Caption Formula-H H-Sect Hn-Sect Index-Caption L-Caption LBody-Caption LBody-H Note-Caption Sect-H TD-H TH-H TOC-Caption Table-Caption Table-TFoot Table-THead Title-Caption StructTreeRoot-Document";

    public static IEnumerable<object[]> Limits() =>
        Pairs.Split(' ').SelectMany(pair => Enumerable.Range(0, 3).Select(count => new object[] { pair, count }));

    static string Namespace(string type) => type is "Art" or "BlockQuote" or "TOC" or "TOCI" or "Index" or "Private"
        or "Quote" or "Note" or "Reference" or "BibEntry" or "Code"
        ? "8 0 R" : "6 0 R";

    static PdfDocument Load(string parent, string child, int count, string? wrapper = null, bool separate = false,
        string? childNamespace = null, string? childRole = null)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Version /2.0 /Pages 2 0 R /StructTreeRoot 4 0 R /MarkInfo << /Marked true >> /Lang (en) >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>",
            "<< /Type /StructTreeRoot /K 5 0 R >>",
            "<< /S /Document /NS 6 0 R /P 4 0 R /K 7 0 R >>",
            "<< /Type /Namespace /NS (http://iso.org/pdf2/ssn) >>",
            "null",
            "<< /Type /Namespace /NS (http://iso.org/pdf/ssn) >>",
        };
        if (childNamespace is not null)
        {
            objects.Add(childNamespace);
            childNamespace = $"{objects.Count} 0 R";
        }
        var owner = parent == "StructTreeRoot" ? 4 : 7;
        objects[owner - 1] = parent == "StructTreeRoot"
            ? "<< /Type /StructTreeRoot >>"
            : $"<< /S /{(parent == "Hn" ? "H7" : parent)} /NS {Namespace(parent)} /P 5 0 R /Pg 3 0 R >>";
        var children = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var container = owner;
            if (wrapper is not null || separate)
            {
                container = objects.Count + 1;
                objects.Add($"<< /S /{(separate ? parent : wrapper)} /NS {Namespace(separate ? parent : wrapper!)} /P {owner} 0 R /K {container + 1} 0 R >>");
                children.Add(container);
            }
            var number = objects.Count + 1;
            var ns = childNamespace ?? Namespace(child);
            objects.Add($"<< /S /{childRole ?? child} /NS {ns} /P {container} 0 R /Pg 3 0 R >>");
            if (container == owner) children.Add(number);
        }
        objects[owner - 1] = objects[owner - 1][..^2] + $" /K [{string.Join(" ", children.Select(n => $"{n} 0 R"))}] >>";
        return PdfDocument.Load(ObjPdf.Build(objects.ToArray()));
    }

    static List<ValidationFinding> Cardinality(PdfDocument doc)
    {
        var c = new ValidationContext(doc, PdfValidationProfile.PdfUA2);
        PdfUA2NestingChecks.Run(c, doc.Structure!);
        Assert.DoesNotContain(c.Findings, f => f.RuleId == "PDFUA2-NESTING-INCOMPLETE");
        return c.Findings.Where(f => f.RuleId == "PDFUA2-NESTING-CARDINALITY").ToList();
    }

    [Theory]
    [MemberData(nameof(Limits))]
    public void AllUnconditionalOccurrenceLimitsHaveZeroOneAndTwoControls(string pair, int count)
    {
        var types = pair.Split('-');
        var findings = Cardinality(Load(types[0], types[1], count));
        var invalid = pair == "StructTreeRoot-Document" ? count != 1 : count > 1;

        Assert.Equal(invalid ? 1 : 0, findings.Count);
        if (!invalid) return;
        Assert.Equal(ValidationSeverity.Error, findings[0].Severity);
        Assert.Equal("ISO/TS 32005:2023 Table 5", findings[0].Clause);
        Assert.Contains($"Table 5. {pair};", findings[0].Message);
    }

    [Theory]
    [InlineData("Part")]
    [InlineData("Div")]
    [InlineData("NonStruct")]
    public void OccurrencesAcrossTransparentSiblingsBelongToTheirEffectiveParent(string wrapper)
    {
        Assert.Single(Cardinality(Load("Document", "H", 2, wrapper)));
    }

    [Fact]
    public void NontransparentParentsHaveIndependentOccurrenceCounts()
    {
        Assert.Empty(Cardinality(Load("Sect", "H", 2, separate: true)));
    }

    [Theory]
    [InlineData("<< /NS (urn:test) /RoleMapNS << /Custom [/H 6 0 R] >> >>", "Custom", true)]
    [InlineData("<< /NS (http://www.w3.org/1998/Math/MathML) >>", "H", false)]
    public void ChildrenUseNamespaceAwareRoleResolution(string ns, string role, bool invalid)
    {
        Assert.Equal(invalid ? 1 : 0, Cardinality(Load("Document", "H", 2, childNamespace: ns, childRole: role)).Count);
    }

    [Fact]
    public void ParentRoleMappingAndNestedWrappersPreserveTheEffectiveOccurrenceOwner()
    {
        var doc = PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Version /2.0 /Pages 2 0 R /StructTreeRoot 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>",
            "<< /Type /StructTreeRoot /K 5 0 R >>",
            "<< /S /Document /NS 6 0 R /P 4 0 R /K 7 0 R >>",
            "<< /NS (http://iso.org/pdf2/ssn) >>",
            "<< /S /Custom /NS 12 0 R /P 5 0 R /K [8 0 R 11 0 R] >>",
            "<< /S /Div /NS 6 0 R /P 7 0 R /K 9 0 R >>",
            "<< /S /NonStruct /NS 6 0 R /P 8 0 R /K 10 0 R >>",
            "<< /S /Caption /NS 6 0 R /P 9 0 R >>",
            "<< /S /Caption /NS 6 0 R /P 7 0 R >>",
            "<< /Type /Namespace /NS (urn:test) /RoleMapNS << /Custom [/Table 6 0 R] >> >>"));

        var finding = Assert.Single(Cardinality(doc));
        Assert.Contains("Table-Caption;", finding.Message);
    }

    [Theory]
    [InlineData("Annot")]
    [InlineData("FENote")]
    [InlineData("Link")]
    [InlineData("Reference")]
    public void GroupingDependentCaptionLimitsRemainDeferred(string type)
    {
        Assert.Empty(Cardinality(Load(type, "Caption", 2)));
    }

    [Fact]
    public void UnresolvedRootRoleCannotProveTheRequiredDocumentIsAbsent()
    {
        var doc = Load("StructTreeRoot", "Document", 1, childNamespace: "<< /NS (urn:test) >>", childRole: "Custom");
        var c = new ValidationContext(doc, PdfValidationProfile.PdfUA2);
        PdfUA2NestingChecks.Run(c, doc.Structure!);

        Assert.Contains(c.Findings, f => f.RuleId == "PDFUA2-NESTING-INCOMPLETE");
        Assert.DoesNotContain(c.Findings, f => f.RuleId == "PDFUA2-NESTING-CARDINALITY");
    }

    [Fact]
    public void Ua1DoesNotApplyUa2OccurrenceRules()
    {
        var report = PdfValidator.ValidatePdfUA(Load("Table", "THead", 2), 1);

        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFUA2-NESTING-CARDINALITY"));
    }
}
