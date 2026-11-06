using System.Text;
using PortableDocument.Core;
using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfUaIdentityNotdefTests
{
    public static IEnumerable<object[]> Uses()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfUA1, PdfValidationProfile.PdfUA2 })
            foreach (var (content, rejected) in new[]
            {
                ("BT /F 12 Tf <0000> Tj ET", true),
                ("BT /F 12 Tf <00010000> Tj ET", true),
                ("BT /F 12 Tf [(\\000\\000) 0 <0001>] TJ ET", true),
                ("BT /F 12 Tf <0000> ' ET", true),
                ("BT /F 12 Tf 0 0 <0000> \" ET", true),
                ("BT /F 12 Tf <0000> Tj <0000> Tj ET", true),
                ("BT /F 12 Tf q /Other 12 Tf Q <0000> Tj ET", true),
                ("BT /F 12 Tf q /Other 12 Tf Q <0001> Tj ET", false),
                ("BT /F 12 Tf <0001> Tj ET", false),
                ("BT /F 12 Tf <01000001> Tj ET", false),
                ("BT /F 12 Tf [(\\000) (\\000)] TJ ET", false),
                ("BT /F 12 Tf <0000> pop ET", false),
                ("BT /F 12 Tf [] TJ ET", false),
                ("BT /F 12 Tf () Tj ET", false),
                ("BT /F 12 Tf ET % <0000> Tj", false),
                ("BT /F 12 Tf /Other 12 Tf (\\000\\000) Tj ET", false),
            })
                yield return [profile, content, rejected];
    }

    [Theory]
    [MemberData(nameof(Uses))]
    public void RejectsShownCidZeroNotUnicodeOrArbitraryZeroBytes(PdfValidationProfile profile, string content, bool rejected)
    {
        AssertUse(Document(content), profile, rejected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void EveryRenderingModeIncludingInvisibleAndClippingIsChecked(int mode)
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfUA1, PdfValidationProfile.PdfUA2 })
            AssertUse(Document($"BT /F 12 Tf {mode} Tr <0000> Tj ET"), profile, true);
    }

    [Theory]
    [InlineData("/Identity-H", true)]
    [InlineData("/Identity-V", true)]
    [InlineData("11 0 R", false)]
    [InlineData("/UniJIS-UCS2-H", false)]
    public void RestrictsDetectionToResolvedIdentityEncoding(string encoding, bool rejected)
    {
        var doc = Document("BT /F 12 Tf <0000> Tj ET");
        Font(doc).Entries["Encoding"] = encoding == "11 0 R"
            ? new PdfRef(11, 0) : new PdfName(encoding[1..]);
        foreach (var profile in new[] { PdfValidationProfile.PdfUA1, PdfValidationProfile.PdfUA2 })
            AssertUse(doc, profile, rejected);
    }

    [Theory]
    [InlineData("BT /F 12 Tf ET /Fm Do", "BT <0000> Tj ET", true)]
    [InlineData("BT /Other 12 Tf ET /Fm Do", "BT <0000> Tj ET", false)]
    [InlineData("BT /Other 12 Tf ET /Fm Do BT /F 12 Tf ET /Fm Do", "BT <0000> Tj ET", true)]
    [InlineData("/Fm Do", "BT /F 12 Tf <0000> Tj ET", true)]
    [InlineData("BT /F 12 Tf ET /Fm Do", "BT /Other 12 Tf <0000> Tj ET", false)]
    public void UsesInheritedOrLocalFormFont(string content, string formContent, bool rejected)
    {
        var doc = Document(content);
        var form = Stream(formContent);
        form.Dict.Entries["Subtype"] = new PdfName("Form");
        doc.Pages[0].Resources.Entries["XObject"] = new PdfDictionary { Entries = { ["Fm"] = form } };
        foreach (var profile in new[] { PdfValidationProfile.PdfUA1, PdfValidationProfile.PdfUA2 })
            AssertUse(doc, profile, rejected);
    }

    [Fact]
    public void IncludesAppearanceAndExtGStateOnlyFontUse()
    {
        foreach (var appearance in new[] { false, true })
        {
            var doc = Document(appearance ? "" : "/GS gs BT <0000> Tj ET");
            doc.Pages[0].Resources.Entries["ExtGState"] = new PdfDictionary
            {
                Entries = { ["GS"] = new PdfDictionary { Entries = { ["Font"] = new PdfArray([Font(doc), new PdfInteger(12)]) } } },
            };
            if (appearance)
                doc.Pages[0].Dict.Entries["Annots"] = new PdfArray(
                [
                    new PdfDictionary { Entries = { ["Subtype"] = new PdfName("Stamp"),
                        ["AP"] = new PdfDictionary { Entries = { ["N"] = Stream("/GS gs BT 3 Tr <0000> Tj ET") } } } },
                ]);
            foreach (var profile in new[] { PdfValidationProfile.PdfUA1, PdfValidationProfile.PdfUA2 })
                AssertUse(doc, profile, true);
        }
    }

    static PdfDictionary Font(PdfDocument doc) => (PdfDictionary)doc.File.Resolve(
        ((PdfDictionary)doc.File.Resolve(doc.Pages[0].Resources.Get("Font"))!).Get("F"))!;

    static PdfDocument Document(string content)
    {
        var bitmap = new byte[8192];
        foreach (var cid in new[] { 0, 1, 41, 65535 }) bitmap[cid / 8] |= (byte)(128 >> (cid % 8));
        var doc = PdfAIdentityCidSetListingTests.Document(PdfACffCidSetListingTests.CffProgram(0), bitmap, cff: true);
        var fonts = (PdfDictionary)doc.File.Resolve(doc.Pages[0].Resources.Get("Font"))!;
        fonts.Entries["Other"] = new PdfDictionary { Entries = { ["Subtype"] = new PdfName("Type1"), ["BaseFont"] = new PdfName("Helvetica") } };
        doc.Pages[0].Dict.Entries["Contents"] = Stream(content);
        return doc;
    }

    static PdfStream Stream(string text) => new(new PdfDictionary(), Encoding.Latin1.GetBytes(text));

    static void AssertUse(PdfDocument doc, PdfValidationProfile profile, bool rejected)
    {
        var report = PdfValidator.Validate(doc, profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(rejected, report.Has("PDFUA-FONT-NOTDEF"));
        var findings = report.Findings.Where(f => f.RuleId == "PDFUA-FONT-NOTDEF").ToList();
        Assert.Equal(rejected ? 1 : 0, findings.Count);
        Assert.All(findings, finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(0, finding.PageIndex);
            Assert.Equal(profile == PdfValidationProfile.PdfUA1 ? "ISO 14289-1 7.21.8" : "ISO 14289-2 8.4.5.9", finding.Clause);
        });
    }
}
