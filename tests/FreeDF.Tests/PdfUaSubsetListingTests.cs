using PortableDocument.Core;
using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfUaSubsetListingTests
{
    [Theory]
    [MemberData(nameof(CharSets))]
    public void Ua1UsesTheExistingExactCffListingSemantics(bool subset, bool indirect, string listing, bool rejected)
    {
        var program = CffGlyphNamesTests.Build(4, [0, 0, 1, 0, 34, 0, 35]);
        var doc = PdfAType1CCharSetListingTests.Document(listing, program, subset, indirect);
        var report = PdfValidator.Validate(doc, PdfValidationProfile.PdfUA1);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFUA-FONT-CHARSET-INCOMPLETE"));
        Assert.Equal(subset && rejected, report.Has("PDFUA-FONT-CHARSET-LIST"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFUA-FONT-CHARSET-LIST"), f =>
        {
            Assert.Equal(ValidationSeverity.Error, f.Severity);
            Assert.Equal("ISO 14289-1 7.21.4.2", f.Clause);
        });
        var ua2 = PdfValidator.Validate(doc, PdfValidationProfile.PdfUA2);
        Assert.DoesNotContain(ua2.Findings, f => f.RuleId.StartsWith("PDFUA-FONT-CHARSET", StringComparison.Ordinal));
    }

    public static IEnumerable<object[]> CharSets()
    {
        foreach (var subset in new[] { false, true })
            foreach (var indirect in new[] { false, true })
                foreach (var (listing, rejected) in new[]
                {
                    ("/space/A/B", false), ("/B/space/A", false), ("/space/A/A/B", false),
                    ("", true), ("/space/A", true), ("/space/A/B/extra", true), ("/.notdef/space/A/B", true),
                })
                    yield return [subset, indirect, listing, rejected];
    }

    [Theory]
    [MemberData(nameof(CidSets))]
    public void Ua1UsesExactTrueTypeCidSetSemantics(bool subset, bool indirect, byte[] bitmap, bool rejected)
    {
        var doc = PdfAIdentityCidSetListingTests.Document(PdfAIdentityCidSetListingTests.Program(10), bitmap, subset, indirect);
        var report = PdfValidator.Validate(doc, PdfValidationProfile.PdfUA1);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFUA-FONT-CIDSET-INCOMPLETE"));
        Assert.Equal(subset && rejected, report.Has("PDFUA-FONT-CIDSET-LIST"));
        Assert.All(report.Findings.Where(f => f.RuleId == "PDFUA-FONT-CIDSET-LIST"), f =>
        {
            Assert.Equal(ValidationSeverity.Error, f.Severity);
            Assert.Equal("ISO 14289-1 7.21.4.2", f.Clause);
        });
        Assert.False(PdfValidator.Validate(doc, PdfValidationProfile.PdfUA2).Has("PDFUA-FONT-CIDSET-LIST"));
    }

    public static IEnumerable<object[]> CidSets()
    {
        foreach (var subset in new[] { false, true })
            foreach (var indirect in new[] { false, true })
                foreach (var (bitmap, rejected) in new (byte[], bool)[]
                {
                    ([255, 192], false), ([255, 192, 0], false), ([], true),
                    ([127, 192], true), ([255, 224], true), ([192], true),
                })
                    yield return [subset, indirect, bitmap, rejected];
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void CffCidSetUsesActualCidsIncludingZeroAndUnusedGlyphs(int format, bool missing)
    {
        var bitmap = new byte[8192];
        foreach (var cid in new[] { 0, 1, 41, 65535 }) bitmap[cid / 8] |= (byte)(128 >> (cid % 8));
        if (missing) bitmap[0] &= 191;
        var doc = PdfAIdentityCidSetListingTests.Document(PdfACffCidSetListingTests.CffProgram(format), bitmap, cff: true);
        var report = PdfValidator.Validate(doc, PdfValidationProfile.PdfUA1);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFUA-FONT-CIDSET-INCOMPLETE"));
        Assert.Equal(missing, report.Has("PDFUA-FONT-CIDSET-LIST"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void StreamCidToGidMapIncludesZeroMappings(bool invalidGid, bool missing)
    {
        var doc = PdfAIdentityCidSetListingTests.Document(PdfAIdentityCidSetListingTests.Program(10),
            missing ? [192] : [224], mapping: [0, 0, 0, 1, 0, (byte)(invalidGid ? 10 : 0)]);
        var report = PdfValidator.Validate(doc, PdfValidationProfile.PdfUA1);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.Equal(invalidGid, report.Has("PDFUA-FONT-CIDSET-INCOMPLETE"));
        Assert.Equal(!invalidGid && missing, report.Has("PDFUA-FONT-CIDSET-LIST"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MalformedProgramsAreExplicitlyIncomplete(bool cff)
    {
        var doc = cff ? PdfAType1CCharSetListingTests.Document("/A", [])
            : PdfAIdentityCidSetListingTests.Document([], [255]);
        var report = PdfValidator.Validate(doc, PdfValidationProfile.PdfUA1);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.True(report.Has(cff ? "PDFUA-FONT-CHARSET-INCOMPLETE" : "PDFUA-FONT-CIDSET-INCOMPLETE"));
        Assert.False(report.Has(cff ? "PDFUA-FONT-CHARSET-LIST" : "PDFUA-FONT-CIDSET-LIST"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OptionalAbsentListingsDoNotBecomeMandatory(bool cff)
    {
        var doc = cff ? PdfAType1CCharSetListingTests.Document("/A", [])
            : PdfAIdentityCidSetListingTests.Document([], [255]);
        var font = Assert.IsType<PdfDictionary>(doc.File.Resolve(
            Assert.IsType<PdfDictionary>(doc.File.Resolve(doc.Pages[0].Resources.Get("Font"))).Get("F")));
        if (!cff) font = Assert.IsType<PdfDictionary>(doc.File.Resolve(
            Assert.IsType<PdfArray>(doc.File.Resolve(font.Get("DescendantFonts")))[0]));
        var descriptor = Assert.IsType<PdfDictionary>(doc.File.Resolve(font.Get("FontDescriptor")));
        descriptor.Entries.Remove(cff ? "CharSet" : "CIDSet");
        var report = PdfValidator.Validate(doc, PdfValidationProfile.PdfUA1);
        Assert.DoesNotContain(report.Findings, f => f.RuleId.StartsWith(cff ? "PDFUA-FONT-CHARSET" : "PDFUA-FONT-CIDSET",
            StringComparison.Ordinal));
    }
}
