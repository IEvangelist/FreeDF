using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfA1CidToGidUseTests
{
    public static IEnumerable<object[]> Maps() =>
        PdfACidToGidTests.Maps().Where(row => (PdfValidationProfile)row[0] == PdfValidationProfile.PdfA2b)
            .Select(row => new object[] { row[1], row[2], row[3], row[4], row[5], (bool)row[6] && (int)row[3] != 3 });

    [Theory]
    [MemberData(nameof(Maps))]
    public void OnlyRenderingEmbeddedType2RequiresIdentityOrStream(bool embedded, string subtype, int mode,
        string value, string target, bool invalid)
    {
        var report = PdfValidator.Validate(PdfACidToGidTests.Document(embedded, subtype, mode, value, target),
            PdfValidationProfile.PdfA1b);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-USAGE-INCOMPLETE"));
        Assert.Equal(invalid, report.Has("PDFA-FONT-CIDTOGID"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFA-FONT-CIDTOGID"), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(0, finding.PageIndex);
            Assert.Equal("ISO 19005-1 6.3.3.2", finding.Clause);
        });
    }

    [Theory]
    [InlineData("BT /F1 12 Tf <0001> Tj ET", true)]
    [InlineData("BT /F1 12 Tf 3 Tr <0001> Tj ET", false)]
    [InlineData("BT /F1 12 Tf 7 Tr <0001> Tj ET", true)]
    [InlineData("BT /F1 12 Tf 3 Tr <0001> Tj 0 Tr <0001> Tj ET", true)]
    [InlineData("BT /F1 12 Tf <0001> Tj 3 Tr <0001> Tj ET", true)]
    [InlineData("BT /F1 12 Tf q 3 Tr <0001> Tj Q <0001> Tj ET", true)]
    [InlineData("BT /F1 12 Tf q /F2 12 Tf <01> Tj Q <0001> Tj ET", true)]
    [InlineData("BT /F1 12 Tf 3 Tr q /F2 12 Tf 0 Tr <01> Tj Q <0001> Tj ET", false)]
    [InlineData("BT /F1 12 Tf /F2 12 Tf <01> Tj ET", false)]
    [InlineData("BT /F1 12 Tf ET", false)]
    [InlineData("BT /F1 12 Tf () Tj ET", true)]
    [InlineData("BT /F1 12 Tf [0 () 1 <>] TJ ET", true)]
    [InlineData("BT /F1 12 Tf [] TJ ET", true)]
    [InlineData("BT /F1 12 Tf 3 Tr () Tj ET", false)]
    [InlineData("BT /F1 12 Tf [() 1 <0001>] TJ ET", true)]
    [InlineData("BT /F1 12 Tf <0001> ' ET", true)]
    [InlineData("BT /F1 12 Tf 0 0 <0001> \" ET", true)]
    [InlineData("/GS0 gs BT <0001> Tj ET", true)]
    [InlineData("/GS0 gs BT 3 Tr <0001> Tj ET", false)]
    [InlineData("BT /F2 12 Tf q /GS0 gs <0001> Tj Q <01> Tj ET", true)]
    [InlineData("BT /F2 12 Tf q /GS0 gs Q <01> Tj ET", false)]
    public void TracksTextShowingFontAndGraphicsState(string content, bool invalid)
    {
        var report = PdfValidator.Validate(Document(content), PdfValidationProfile.PdfA1b);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-USAGE-INCOMPLETE"));
        Assert.Equal(invalid, report.Has("PDFA-FONT-CIDTOGID"));
    }

    [Theory]
    [InlineData("BT /F1 12 Tf ET /Fm Do", "BT <0001> Tj ET", true)]
    [InlineData("BT /F1 12 Tf 3 Tr ET /Fm Do", "BT <0001> Tj ET", false)]
    [InlineData("BT /F1 12 Tf 3 Tr ET /Fm Do", "BT 0 Tr <0001> Tj ET", true)]
    [InlineData("BT /F1 12 Tf ET /Fm Do", "BT 3 Tr <0001> Tj ET", false)]
    [InlineData("BT /F2 12 Tf ET /Fm Do BT /F1 12 Tf ET /Fm Do", "BT <0001> Tj ET", true)]
    [InlineData("/Fm Do", "BT /F1 12 Tf <0001> Tj ET", true)]
    public void TracksInheritedAndLocalFormState(string content, string formContent, bool invalid)
    {
        var doc = Document(content);
        var form = Stream(formContent);
        form.Dict.Entries["Subtype"] = new PdfName("Form");
        doc.Pages[0].Resources.Entries["XObject"] = new PdfDictionary
        {
            Entries = { ["Fm"] = form },
        };
        var report = PdfValidator.Validate(doc, PdfValidationProfile.PdfA1b);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-USAGE-INCOMPLETE"));
        Assert.Equal(invalid, report.Has("PDFA-FONT-CIDTOGID"));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(3, false)]
    public void IncludesAnnotationAppearances(int mode, bool invalid)
    {
        var doc = Document("");
        var appearance = Stream($"BT /F1 12 Tf {mode} Tr <0001> Tj ET");
        appearance.Dict.Entries["Subtype"] = new PdfName("Form");
        doc.Pages[0].Dict.Entries["Annots"] = new PdfArray(
        [
            new PdfDictionary
            {
                Entries =
                {
                    ["Subtype"] = new PdfName("Stamp"),
                    ["AP"] = new PdfDictionary { Entries = { ["N"] = appearance } },
                },
            },
        ]);
        var report = PdfValidator.Validate(doc, PdfValidationProfile.PdfA1b);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-USAGE-INCOMPLETE"));
        Assert.Equal(invalid, report.Has("PDFA-FONT-CIDTOGID"));
    }

    [Theory]
    [InlineData("BT /F1 12 Tf (unterminated")]
    [InlineData("BT /Missing 12 Tf <0001> Tj ET")]
    [InlineData("BT /F1 12 Tf /Bad Tr <0001> Tj ET")]
    [InlineData("BT /F1 12 Tf 3.5 Tr <0001> Tj ET")]
    [InlineData("BT <0001> Tj ET")]
    [InlineData("BT /F1 12 Tf [true] TJ ET")]
    public void DoesNotSilentlyExemptAnIncompleteUsageScan(string content)
    {
        var report = PdfValidator.Validate(Document(content), PdfValidationProfile.PdfA1b);
        Assert.Contains(report.Findings, finding => finding.RuleId == "PDFA-FONT-USAGE-INCOMPLETE"
            && finding.Severity == ValidationSeverity.Error && finding.Clause == "ISO 19005-1 6.3.3.2");
    }

    [Fact]
    public void TokenLimitDoesNotSilentlyExemptAnUnexaminedFont()
    {
        var context = new ValidationContext(Document("BT /F1 12 Tf <0001> Tj ET"),
            PdfValidationProfile.PdfA1b, contentScanTokenLimit: 2);
        PdfAChecks.Run(context);
        Assert.Contains(context.Findings, finding => finding.RuleId == "PDFA-FONT-USAGE-INCOMPLETE"
            && finding.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void DiscoversRenderingFontProvidedOnlyByExtGState()
    {
        var doc = Document("/GS0 gs BT <0001> Tj ET");
        Assert.IsType<PdfDictionary>(doc.File.Resolve(doc.Pages[0].Resources.Get("Font"))).Entries.Remove("F1");
        var report = PdfValidator.Validate(doc, PdfValidationProfile.PdfA1b);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-USAGE-INCOMPLETE"));
        Assert.True(report.Has("PDFA-FONT-CIDTOGID"));
    }

    [Fact]
    public void GuardedDecodeFailureDoesNotSilentlyExemptAnUnexaminedFont()
    {
        var doc = Document("");
        var content = new PdfStream(new PdfDictionary { Entries = { ["Filter"] = new PdfName("FlateDecode") } },
            [0xff, 0xff, 0xff]);
        doc.Pages[0].Dict.Entries["Contents"] = content;
        var report = PdfValidator.Validate(doc, PdfValidationProfile.PdfA1b);
        Assert.Contains(report.Findings, finding => finding.RuleId == "PDFA-FONT-USAGE-INCOMPLETE"
            && finding.Severity == ValidationSeverity.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FormDepthAndCycleLimitsDoNotSilentlyExemptAnUnexaminedFont(bool cycle)
    {
        var doc = Document("BT /F1 12 Tf ET /Fm Do");
        var resources = doc.Pages[0].Resources;
        for (var depth = 0; depth < (cycle ? 1 : 18); depth++)
        {
            var form = Stream("/Fm Do");
            form.Dict.Entries["Subtype"] = new PdfName("Form");
            var next = cycle ? resources : new PdfDictionary();
            form.Dict.Entries["Resources"] = next;
            resources.Entries["XObject"] = new PdfDictionary { Entries = { ["Fm"] = form } };
            resources = next;
        }
        var report = PdfValidator.Validate(doc, PdfValidationProfile.PdfA1b);
        Assert.Contains(report.Findings, finding => finding.RuleId == "PDFA-FONT-USAGE-INCOMPLETE"
            && finding.Severity == ValidationSeverity.Error);
        Assert.False(report.Has("PDFA-FONT-CIDTOGID"));
    }

    [Fact]
    public void HeuristicInlineImageSkippingDoesNotProveTheExemption()
    {
        var report = PdfValidator.Validate(Document("BI /W 1 /H 1 /BPC 8 /CS /G ID x EI"),
            PdfValidationProfile.PdfA1b);
        Assert.Contains(report.Findings, finding => finding.RuleId == "PDFA-FONT-USAGE-INCOMPLETE"
            && finding.Severity == ValidationSeverity.Error);
    }

    [Theory]
    [InlineData("valid", false)]
    [InlineData("header", true)]
    [InlineData("checksum", true)]
    [InlineData("truncated", true)]
    public void ExemptionRequiresCompletelyDecodedContent(string mutation, bool incomplete)
    {
        var doc = Document("");
        var bytes = PdfFilters.DeflateEncode(Encoding.ASCII.GetBytes("BT /F1 12 Tf 3 Tr <0001> Tj ET"));
        if (mutation == "header") bytes[0] = 0;
        if (mutation == "checksum") bytes[^1] ^= 1;
        if (mutation == "truncated") bytes = bytes[..^4];
        doc.Pages[0].Dict.Entries["Contents"] = new PdfStream(
            new PdfDictionary { Entries = { ["Filter"] = new PdfName("FlateDecode") } }, bytes);
        var report = PdfValidator.Validate(doc, PdfValidationProfile.PdfA1b);
        Assert.Equal(incomplete, report.Has("PDFA-FONT-USAGE-INCOMPLETE"));
        Assert.False(report.Has("PDFA-FONT-CIDTOGID"));
    }

    [Theory]
    [InlineData("scalar")]
    [InlineData("array-entry")]
    [InlineData("array-length")]
    [InlineData("array-filter-dictionary")]
    public void IgnoredDecodeParametersDoNotProveTheExemption(string shape)
    {
        var doc = Document("");
        var dict = new PdfDictionary { Entries = { ["Filter"] = new PdfName("FlateDecode") } };
        dict.Entries["DecodeParms"] = shape switch
        {
            "scalar" => new PdfInteger(1),
            "array-entry" => new PdfArray([new PdfInteger(1)]),
            "array-length" => new PdfArray([]),
            _ => new PdfDictionary(),
        };
        if (shape == "array-filter-dictionary") dict.Entries["Filter"] = new PdfArray([new PdfName("FlateDecode")]);
        doc.Pages[0].Dict.Entries["Contents"] = new PdfStream(dict,
            PdfFilters.DeflateEncode(Encoding.ASCII.GetBytes("BT /F1 12 Tf 3 Tr <0001> Tj ET")));
        var report = PdfValidator.Validate(doc, PdfValidationProfile.PdfA1b);
        Assert.True(report.Has("PDFA-FONT-USAGE-INCOMPLETE"));
        Assert.False(report.Has("PDFA-FONT-CIDTOGID"));
    }

    [Theory]
    [InlineData("ASCIIHexDecode", false)]
    [InlineData("ASCIIHexDecode", true)]
    [InlineData("ASCII85Decode", false)]
    [InlineData("ASCII85Decode", true)]
    public void CompleteAsciiFilterChainsProveTheInvisibleUseExemption(string filter, bool flate)
    {
        var doc = Document("");
        var text = Encoding.ASCII.GetBytes("BT /F1 12 Tf 3 Tr (A) Tj ET");
        var data = flate ? PdfFilters.DeflateEncode(text) : text;
        var encoded = filter == "ASCIIHexDecode" ? Convert.ToHexString(data) + ">"
            : flate ? "Garg^;:'MC<%p.,#Y@rK<X:-Pcu@K6#g#t`%06H.3!MJ~>" : "6<#'\\7PQ#?1*BP.+>Y->EZcbr.3MT)+@T6~>";
        PdfObject filters = flate ? new PdfArray([new PdfName(filter), new PdfName("FlateDecode")]) : new PdfName(filter);
        doc.Pages[0].Dict.Entries["Contents"] = new PdfStream(
            new PdfDictionary { Entries = { ["Filter"] = filters } }, Encoding.ASCII.GetBytes(encoded));
        var report = PdfValidator.Validate(doc, PdfValidationProfile.PdfA1b);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-CONTENT-SCAN-INCOMPLETE"));
        Assert.False(report.Has("PDFA-FONT-USAGE-INCOMPLETE"));
        Assert.False(report.Has("PDFA-FONT-CIDTOGID"));
    }

    [Theory]
    [InlineData("ASCIIHexDecode", "x>")]
    [InlineData("ASCIIHexDecode", "4254")]
    [InlineData("ASCII85Decode", "uuuuu~>")]
    [InlineData("ASCII85Decode", "!z~>")]
    [InlineData("ASCII85Decode", "!~>")]
    [InlineData("ASCII85Decode", "!!!!!~x")]
    public void MalformedAsciiContentDoesNotProveTheExemption(string filter, string encoded)
    {
        var doc = Document("");
        doc.Pages[0].Dict.Entries["Contents"] = new PdfStream(
            new PdfDictionary { Entries = { ["Filter"] = new PdfName(filter) } }, Encoding.ASCII.GetBytes(encoded));
        var report = PdfValidator.Validate(doc, PdfValidationProfile.PdfA1b);
        Assert.True(report.Has("PDFA-CONTENT-SCAN-INCOMPLETE"));
        Assert.True(report.Has("PDFA-FONT-USAGE-INCOMPLETE"));
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-CIDTOGID"));
    }

    [Theory]
    [InlineData(3, 0, 1)]
    [InlineData(0, 3, 0)]
    [InlineData(3, 3, -1)]
    public void AggregatesSharedFontUseAcrossPages(int firstMode, int secondMode, int expectedPage)
    {
        var doc = Document($"BT /F1 12 Tf {firstMode} Tr <0001> Tj ET");
        doc.Pages.Add(new PdfPage(doc, new PdfDictionary
        {
            Entries =
            {
                ["Resources"] = doc.Pages[0].Resources,
                ["Contents"] = Stream($"BT /F1 12 Tf {secondMode} Tr <0001> Tj ET"),
            },
        }, new()));
        var report = PdfValidator.Validate(doc, PdfValidationProfile.PdfA1b);
        Assert.False(report.Has("PDFA-FONT-USAGE-INCOMPLETE"));
        var findings = report.Findings.Where(f => f.RuleId == "PDFA-FONT-CIDTOGID").ToList();
        if (expectedPage < 0) Assert.Empty(findings);
        else Assert.Equal(expectedPage, Assert.Single(findings).PageIndex);
    }

    [Theory]
    [InlineData("pattern")]
    [InlineData("type3")]
    [InlineData("filter")]
    public void UninspectedExecutionOrDecodingDoesNotProveTheExemption(string surface)
    {
        var doc = Document("");
        var resources = doc.Pages[0].Resources;
        if (surface == "pattern")
        {
            resources.Entries["Pattern"] = new PdfDictionary { Entries = { ["P"] = Stream("") } };
            doc.Pages[0].Dict.Entries["Contents"] = Stream("/Pattern cs /P scn");
        }
        else if (surface == "type3")
        {
            var fonts = Assert.IsType<PdfDictionary>(doc.File.Resolve(resources.Get("Font")));
            Assert.IsType<PdfDictionary>(fonts.Get("F2")).Entries["Subtype"] = new PdfName("Type3");
            doc.Pages[0].Dict.Entries["Contents"] = Stream("BT /F2 12 Tf (A) Tj ET");
        }
        else
            doc.Pages[0].Dict.Entries["Contents"] = new PdfStream(
                new PdfDictionary { Entries = { ["Filter"] = new PdfName("RunLengthDecode") } }, [128]);
        var report = PdfValidator.Validate(doc, PdfValidationProfile.PdfA1b);
        Assert.Contains(report.Findings, finding => finding.RuleId == "PDFA-FONT-USAGE-INCOMPLETE"
            && finding.Severity == ValidationSeverity.Error);
        Assert.False(report.Has("PDFA-FONT-CIDTOGID"));
    }

    static PdfDocument Document(string content)
    {
        var doc = PdfACidToGidTests.Document(true, "CIDFontType2", 0, "/Other", "null");
        var page = doc.Pages[0];
        page.Dict.Entries["Contents"] = Stream(content);
        var fonts = Assert.IsType<PdfDictionary>(doc.File.Resolve(page.Resources.Get("Font")));
        fonts.Entries["F2"] = new PdfDictionary
        {
            Entries = { ["Subtype"] = new PdfName("Type1"), ["BaseFont"] = new PdfName("Helvetica") },
        };
        page.Resources.Entries["ExtGState"] = new PdfDictionary
        {
            Entries =
            {
                ["GS0"] = new PdfDictionary
                {
                    Entries = { ["Font"] = new PdfArray([fonts.Get("F1")!, new PdfInteger(12)]) },
                },
            },
        };
        return doc;
    }

    static PdfStream Stream(string content) => new(new PdfDictionary(), Encoding.ASCII.GetBytes(content));
}
