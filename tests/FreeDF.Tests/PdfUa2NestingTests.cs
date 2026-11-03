using System.Security.Cryptography;
using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfUa2NestingTests
{
    const string Types = "Annot Art Artifact Aside BibEntry BlockQuote Caption Code Div Document DocumentFragment Em FENote Figure Form Formula H Hn Index L LBody LI Lbl Link NonStruct Note P Part Private Quote RB RP RT Reference Ruby Sect Span Strong Sub TBody TD TFoot TH THead TOC TOCI TR Table Title WP WT Warichu";

    static int Namespace(string type) => type is "Art" or "BlockQuote" or "TOC" or "TOCI" or "Index" or "Private"
        or "Quote" or "Note" or "Reference" or "BibEntry" or "Code" ? 10 : 8;

    static PdfDocument Load(string parent, string child, params (int Number, string Body)[] replacements)
    {
        var bodies = new[]
        {
            "<< /Type /Catalog /Version /2.0 /Pages 2 0 R /StructTreeRoot 4 0 R /MarkInfo << /Marked true >> /Lang (en) >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>",
            $"<< /Type /StructTreeRoot /K {(parent == "StructTreeRoot" ? 7 : 5)} 0 R >>",
            "<< /S /Document /NS 8 0 R /P 4 0 R /K 6 0 R >>",
            $"<< /S /{(parent == "Hn" ? "H7" : parent)} /NS {Namespace(parent)} 0 R /P 5 0 R /K 7 0 R >>",
            $"<< /S /{(child == "Hn" ? "H7" : child)} /NS {Namespace(child)} 0 R /P {(parent == "StructTreeRoot" ? 4 : 6)} 0 R /Pg 3 0 R >>",
            "<< /Type /Namespace /NS (http://iso.org/pdf2/ssn) >>",
            "<< /Type /Namespace /NS (urn:test) >>",
            "<< /Type /Namespace /NS (http://iso.org/pdf/ssn) >>",
            "<< /Type /Namespace /NS (http://www.w3.org/1998/Math/MathML) >>",
        };
        foreach (var (number, body) in replacements) bodies[number - 1] = body;
        return PdfDocument.Load(ObjPdf.Build(bodies));
    }

    static List<ValidationFinding> Nesting(PdfDocument doc)
    {
        var c = new ValidationContext(doc, PdfValidationProfile.PdfUA2);
        PdfUA2NestingChecks.Run(c, doc.Structure!);
        return c.Findings;
    }

    [Fact]
    public void EveryUnconditionalPairMatchesThePinnedPublishedFacts()
    {
        var failed = new List<string>();
        var types = Types.Split(' ');
        foreach (var parent in types.Prepend("StructTreeRoot"))
            foreach (var child in types)
            {
                var findings = Nesting(Load(parent, child));
                Assert.DoesNotContain(findings, f => f.RuleId == "PDFUA2-NESTING-INCOMPLETE");
                if (findings.Any(f => f.Message.EndsWith($"Table 5. {parent}-{child})", StringComparison.Ordinal)))
                    failed.Add($"{parent}-{child}");
            }
        Assert.Equal(1534, failed.Count);
        failed.Sort(StringComparer.Ordinal);
        // Independently calculated from the 1504 literal-parent and 30 Hn predicates in v1.30.3.
        Assert.Equal("94170f4be2b860ff2e7f54fa2af4f3248b0e11091861a006129f8b7119b30f87",
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", failed)))));
    }

    [Theory]
    [InlineData("P", "P", true)]
    [InlineData("P", "Span", false)]
    [InlineData("Document", "Span", true)]
    [InlineData("Document", "P", false)]
    [InlineData("L", "LI", false)]
    [InlineData("L", "P", true)]
    [InlineData("LI", "LBody", false)]
    [InlineData("LI", "L", true)]
    [InlineData("Table", "TR", false)]
    [InlineData("Table", "TD", true)]
    [InlineData("TBody", "TR", false)]
    [InlineData("TBody", "TH", true)]
    [InlineData("TR", "TD", false)]
    [InlineData("TR", "P", true)]
    [InlineData("TOC", "TOCI", false)]
    [InlineData("TOC", "P", true)]
    [InlineData("Ruby", "RB", false)]
    [InlineData("Ruby", "Span", true)]
    [InlineData("Warichu", "WT", false)]
    [InlineData("Warichu", "Span", true)]
    [InlineData("Hn", "Span", false)]
    [InlineData("Hn", "Hn", true)]
    [InlineData("Strong", "Span", false)]
    [InlineData("Strong", "P", true)]
    [InlineData("Em", "Span", false)]
    [InlineData("Em", "P", true)]
    public void DirectPairsHaveTheExpectedNestingVerdict(string parent, string child, bool invalid)
    {
        var suffix = $"Table 5. {parent}-{child})";
        var findings = Nesting(Load(parent, child)).Where(f => f.Message.EndsWith(suffix, StringComparison.Ordinal)).ToList();
        Assert.Equal(invalid ? 1 : 0, findings.Count);
        if (!invalid) return;
        Assert.Equal(ValidationSeverity.Error, findings[0].Severity);
        Assert.Equal("ISO/TS 32005:2023 Table 5", findings[0].Clause);
        Assert.Equal(0, findings[0].PageIndex);
    }

    [Theory]
    [InlineData("Art")]
    [InlineData("Document")]
    [InlineData("DocumentFragment")]
    [InlineData("Index")]
    [InlineData("L")]
    [InlineData("Sect")]
    [InlineData("TBody")]
    [InlineData("TFoot")]
    [InlineData("THead")]
    [InlineData("TOC")]
    [InlineData("TOCI")]
    [InlineData("TR")]
    [InlineData("Table")]
    public void AllParsedElementContentBansCoverBothMcrAndObjr(string type)
    {
        foreach (var content in new[] { "0", "<< /Type /MCR /MCID 0 /Pg 3 0 R >>", "<< /Type /OBJR /Obj 3 0 R >>" })
        {
            var doc = Load("Document", type, (7, $"<< /S /{type} /NS {Namespace(type)} 0 R /P 6 0 R /Pg 3 0 R /K {content} >>"));
            Assert.Single(Nesting(doc), f => f.RuleId == "PDFUA2-NESTING-CONTENT");
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("<< /Type /MCR /MCID 0 >>")]
    [InlineData("<< /Type /OBJR /Obj 3 0 R >>")]
    public void RootContentItemsCannotDisappearFromContainmentValidation(string content)
    {
        var doc = Load("P", "Span", (4, $"<< /Type /StructTreeRoot /K [5 0 R {content}] >>"));
        Assert.Single(Nesting(doc), f => f.RuleId == "PDFUA2-NESTING-CONTENT");
    }

    [Theory]
    [InlineData("P")]
    [InlineData("Span")]
    [InlineData("TH")]
    [InlineData("TD")]
    [InlineData("Private")]
    [InlineData("Artifact")]
    public void ContentIsNotBlanketProhibited(string type)
    {
        var doc = Load("P", type, (7, $"<< /S /{type} /NS {Namespace(type)} 0 R /P 6 0 R /Pg 3 0 R /K 0 >>"));
        Assert.DoesNotContain(Nesting(doc), f => f.RuleId == "PDFUA2-NESTING-CONTENT");
    }

    [Theory]
    [InlineData("Div")]
    [InlineData("Part")]
    [InlineData("NonStruct")]
    public void TransparentContainersInheritParentExclusionsAndContentBans(string wrapper)
    {
        var doc = Load(wrapper, "Span");
        Assert.Contains(Nesting(doc), f => f.Message.Contains("Table 5. Document-Span"));
        doc = Load(wrapper, "P");
        Assert.Empty(Nesting(doc));
        doc = Load(wrapper, "P", (6, $"<< /S /{wrapper} /NS 8 0 R /P 5 0 R /Pg 3 0 R /K 0 >>"));
        Assert.Single(Nesting(doc), f => f.RuleId == "PDFUA2-NESTING-CONTENT");
    }

    [Fact]
    public void NamespaceMappingsResolveBothParentAndChildTransitively()
    {
        var doc = Load("Outer", "Inner",
            (6, "<< /S /Outer /NS 9 0 R /P 5 0 R /K 7 0 R >>"),
            (7, "<< /S /Inner /NS 9 0 R /P 6 0 R >>"),
            (9, "<< /NS (urn:test) /RoleMapNS << /Outer [/P 8 0 R] /Inner [/Inner2 10 0 R] >> >>"),
            (10, "<< /NS (urn:second) /RoleMapNS << /Inner2 [/P 8 0 R] >> >>"));
        Assert.Contains(Nesting(doc), f => f.Message.Contains("Table 5. P-P"));
    }

    [Fact]
    public void DefaultAndExplicitPdf17RolesUseTheSameMatrix()
    {
        foreach (var ns in new[] { "", "/NS null", "/NS 10 0 R" })
        {
            var doc = Load("P", "Custom",
                (4, "<< /Type /StructTreeRoot /K 5 0 R /RoleMap << /Custom /P >> >>"),
                (7, $"<< /S /Custom {ns} /P 6 0 R >>"));
            Assert.Contains(Nesting(doc), f => f.Message.Contains("Table 5. P-P"));
        }
    }

    [Theory]
    [InlineData("http://www.w3.org/1998/Math/MathML")]
    [InlineData("urn:unmapped")]
    public void ForeignNamesDoNotImpersonatePdfTypes(string ns)
    {
        var doc = Load("P", "P", (7, "<< /S /P /NS 9 0 R /P 6 0 R >>"), (9, $"<< /NS ({ns}) >>"));
        Assert.DoesNotContain(Nesting(doc), f => f.Message.Contains("Table 5. P-P"));
        Assert.Equal(ns != PdfUA2Checks.MathMlNamespace,
            Nesting(doc).Any(f => f.RuleId == "PDFUA2-NESTING-INCOMPLETE"));
        doc = Load("P", "P", (6, "<< /S /P /NS 9 0 R /P 5 0 R /K 7 0 R >>"), (9, $"<< /NS ({ns}) >>"));
        Assert.DoesNotContain(Nesting(doc), f => f.Message.Contains("Table 5. P-P"));
        Assert.Equal(ns != PdfUA2Checks.MathMlNamespace,
            Nesting(doc).Any(f => f.RuleId == "PDFUA2-NESTING-INCOMPLETE"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/Custom /Missing")]
    [InlineData("/Custom /Other /Other /Custom")]
    public void UnresolvedDefaultRoleChainsReportIncompleteContainment(string mappings)
    {
        foreach (var number in new[] { 6, 7 })
        {
            var doc = Load("P", "P",
                (4, $"<< /Type /StructTreeRoot /K 5 0 R /RoleMap << {mappings} >> >>"),
                (number, $"<< /S /Custom /P {number - 1} 0 R {(number == 6 ? "/K 7 0 R" : "")} >>"));
            Assert.Single(Nesting(doc), f => f.RuleId == "PDFUA2-NESTING-INCOMPLETE"
                && f.Severity == ValidationSeverity.Error && f.Message.Contains("role mapping"));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("/Custom /Missing")]
    [InlineData("/Custom /Other /Other /Custom")]
    [InlineData("/Custom [/P 8 0 R]")]
    [InlineData("/Custom [/math 11 0 R]")]
    public void NamespaceRoleOutcomesDistinguishUnresolvedFromIntentionalExclusion(string mappings)
    {
        foreach (var number in new[] { 6, 7 })
        {
            var doc = Load("P", "P",
                (number, $"<< /S /Custom /NS 9 0 R /P {number - 1} 0 R {(number == 6 ? "/K 7 0 R" : "")} >>"),
                (9, $"<< /NS (urn:test) /RoleMapNS << {mappings} >> >>"));
            var findings = Nesting(doc);
            Assert.Equal(!mappings.Contains('['),
                findings.Any(f => f.RuleId == "PDFUA2-NESTING-INCOMPLETE"));
            Assert.Equal(mappings.Contains("/P 8"),
                findings.Any(f => f.Message.Contains("Table 5. P-P")));
        }
    }

    [Fact]
    public void InvalidNamespacesAndSkippedStructureProduceIncompleteErrors()
    {
        foreach (var ns in new[] { "/NS 99 0 R", "/NS /Bad", "/NS << >>" })
        {
            var doc = Load("P", "P", (7, $"<< /S /P {ns} /P 6 0 R >>"));
            Assert.Single(Nesting(doc), f => f.RuleId == "PDFUA2-NESTING-INCOMPLETE" && f.Severity == ValidationSeverity.Error);
        }
        var cycle = Load("P", "Span", (7, "<< /S /Span /P 6 0 R /K 6 0 R >>"));
        Assert.Single(Nesting(cycle), f => f.RuleId == "PDFUA2-NESTING-INCOMPLETE");
        var namespaceCycle = Load("P", "Span", (7, "<< /S /Span /NS 9 0 R /P 6 0 R >>"), (9, "9 0 R"));
        Assert.Single(Nesting(namespaceCycle), f => f.RuleId == "PDFUA2-NESTING-INCOMPLETE");
    }

    [Fact]
    public void IndirectNullNamespaceIsEquivalentToAnAbsentNamespace()
    {
        var doc = Load("P", "P", (7, "<< /S /P /NS 9 0 R /P 6 0 R >>"), (9, "null"));
        var findings = Nesting(doc);
        Assert.Contains(findings, f => f.Message.Contains("Table 5. P-P"));
        Assert.DoesNotContain(findings, f => f.RuleId == "PDFUA2-NESTING-INCOMPLETE");
    }

    [Fact]
    public void FindingsAreCappedAndOmittedWorkIsNotReportedAsSuccess()
    {
        var children = string.Join(" ", Enumerable.Range(0, 1002).Select(_ => "<< /S /P /NS 8 0 R /P 6 0 R >>"));
        var doc = Load("P", "Span", (6, $"<< /S /P /NS 8 0 R /P 5 0 R /K [{children}] >>"));
        var findings = Nesting(doc);
        Assert.Equal(1000, findings.Count(f => f.RuleId == "PDFUA2-NESTING"));
        Assert.Single(findings, f => f.RuleId == "PDFUA2-NESTING-INCOMPLETE");
    }

    [Fact]
    public void TruncatedTreesProduceIncompleteErrors()
    {
        var bodies = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /StructTreeRoot 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>",
            "<< /Type /StructTreeRoot /K 5 0 R >>",
        };
        for (var i = 0; i < StructureTree.MaxDepth + 3; i++)
            bodies.Add($"<< /S /Div /P {i + 4} 0 R /K {i + 6} 0 R >>");
        var doc = PdfDocument.Load(ObjPdf.Build(bodies.ToArray()));
        Assert.True(doc.Structure!.Truncated);
        Assert.Single(Nesting(doc), f => f.RuleId == "PDFUA2-NESTING-INCOMPLETE");
    }

    [Fact]
    public void Ua1DoesNotApplyTheUa2Matrix()
    {
        var doc = Load("P", "P");
        Assert.Contains(PdfValidator.ValidatePdfUA(doc, 2).Findings, f => f.RuleId == "PDFUA2-NESTING");
        Assert.DoesNotContain(PdfValidator.ValidatePdfUA(doc, 1).Findings, f => f.RuleId.StartsWith("PDFUA2-NESTING"));
    }
}
