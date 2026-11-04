using System.Text;
using PortableDocument.Content;
using PortableDocument.Core;
using PortableDocument.Document;

namespace FreeDF.Tests;

public class PdfAType1CCharSetListingTests
{
    static readonly Lazy<byte[]> Program = new(() =>
    {
        var source = PdfDocument.Open(Path.Combine(AppContext.BaseDirectory, "fixtures", "fonts", "cff.pdf"));
        var fonts = Assert.IsType<PdfDictionary>(source.File.Resolve(source.Pages[0].Resources.Get("Font")));
        foreach (var value in fonts.Entries.Values)
        {
            var font = Assert.IsType<PdfDictionary>(source.File.Resolve(value));
            var descriptor = Assert.IsType<PdfDictionary>(source.File.Resolve(font.Get("FontDescriptor")));
            var stream = Assert.IsType<PdfStream>(source.File.Resolve(descriptor.Get("FontFile3")));
            if (source.File.Resolve(stream.Dict.Get("Subtype")).AsName() != "Type1C") continue;
            var data = source.File.DecodeStream(stream);
            var cff = new CffFont(data);
            Assert.Equal(new[] { ".notdef", "A", "B", "space" },
                Enumerable.Range(0, cff.GlyphCount).Select(cff.GlyphName));
            return data;
        }
        throw new InvalidOperationException("Type1C fixture is missing");
    });

    public static IEnumerable<object[]> Listings()
    {
        foreach (var profile in new[] { PdfValidationProfile.PdfA1b, PdfValidationProfile.PdfA2b, PdfValidationProfile.PdfA3b })
            foreach (var subset in new[] { false, true })
                foreach (var indirect in new[] { false, true })
                    foreach (var (value, rejected) in new (string, bool)[]
                    {
                        ("/A/B/space", false), ("/space/B/A", false), ("/A/A/B/space", false),
                        ("/#41/B/space", false), (" /A /B /space ", false), ("/A% note\n/B/space", false),
                        ("", true), ("/A/space", true), ("/A/B", true), ("/A/B/space/extra", true),
                        ("/A/B/space/.notdef", true), ("bare /A/B/space", true),
                        ("[/A/B/space]", true), ("/A/B/space 12", true),
                    })
                        yield return [profile, subset, indirect, value, subset && rejected];
    }

    [Theory]
    [MemberData(nameof(Listings))]
    public void CharSetListsAllDefinedGlyphsNotOnlyPaintedGlyphs(
        PdfValidationProfile profile, bool subset, bool indirect, string value, bool rejected)
    {
        var report = PdfValidator.Validate(Document(value, Program.Value, subset, indirect), profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-CHARSET-INCOMPLETE"));
        Assert.Equal(rejected, report.Has("PDFA-FONT-CHARSET-LIST"));
        AssertClauses(report, profile);
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b)]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void LiteralNumberSignsInProgramNamesRequirePdfEscapes(PdfValidationProfile profile)
    {
        var program = CffGlyphNamesTests.Build(2, [0, 1, 135], strings: ["A#"]);
        foreach (var (value, rejected) in new[] { ("/A#", true), ("/A#23", false) })
        {
            var report = PdfValidator.Validate(Document(value, program), profile);
            Assert.False(report.Has("VALIDATION-INTERNAL"));
            Assert.False(report.Has("PDFA-FONT-CHARSET-INCOMPLETE"));
            Assert.Equal(rejected, report.Has("PDFA-FONT-CHARSET-LIST"));
        }
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b)]
    [InlineData(PdfValidationProfile.PdfA2b)]
    [InlineData(PdfValidationProfile.PdfA3b)]
    public void MalformedProgramsAreExplicitlyIncomplete(PdfValidationProfile profile)
    {
        foreach (var data in new byte[][] { [], [1, 0, 4, 0], [2, 0, 4, 0] })
        {
            var report = PdfValidator.Validate(Document("/A/B/space", data), profile);
            Assert.False(report.Has("VALIDATION-INTERNAL"));
            Assert.True(report.Has("PDFA-FONT-CHARSET-INCOMPLETE"));
            Assert.False(report.Has("PDFA-FONT-CHARSET-LIST"));
            AssertClauses(report, profile);
        }
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b, 1048576, false)]
    [InlineData(PdfValidationProfile.PdfA1b, 1048577, true)]
    [InlineData(PdfValidationProfile.PdfA2b, 1048576, false)]
    [InlineData(PdfValidationProfile.PdfA2b, 1048577, true)]
    [InlineData(PdfValidationProfile.PdfA3b, 1048576, false)]
    [InlineData(PdfValidationProfile.PdfA3b, 1048577, true)]
    public void CharSetByteLimitIsExplicit(PdfValidationProfile profile, int length, bool incomplete)
    {
        var report = PdfValidator.Validate(Document("/A/B/space".PadRight(length), Program.Value), profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-CHARSET-LIST"));
        Assert.Equal(incomplete, report.Has("PDFA-FONT-CHARSET-INCOMPLETE"));
    }

    [Theory]
    [InlineData(PdfValidationProfile.PdfA1b, 65535, false)]
    [InlineData(PdfValidationProfile.PdfA1b, 65536, true)]
    [InlineData(PdfValidationProfile.PdfA2b, 65535, false)]
    [InlineData(PdfValidationProfile.PdfA2b, 65536, true)]
    [InlineData(PdfValidationProfile.PdfA3b, 65535, false)]
    [InlineData(PdfValidationProfile.PdfA3b, 65536, true)]
    public void NameEntryLimitCountsDuplicates(PdfValidationProfile profile, int count, bool incomplete)
    {
        var value = "/A/B/space" + string.Concat(Enumerable.Repeat("/A", count - 3));
        var report = PdfValidator.Validate(Document(value, Program.Value), profile);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-CHARSET-LIST"));
        Assert.Equal(incomplete, report.Has("PDFA-FONT-CHARSET-INCOMPLETE"));
    }

    [Theory]
    [InlineData(16777216, false)]
    [InlineData(16777217, true)]
    public void DecodedProgramLimitIsExplicit(int length, bool incomplete)
    {
        var data = new byte[length];
        Program.Value.CopyTo(data, 0);
        var report = PdfValidator.Validate(Document("/A/B/space", data), PdfValidationProfile.PdfA1b);
        Assert.False(report.Has("VALIDATION-INTERNAL"));
        Assert.False(report.Has("PDFA-FONT-CHARSET-LIST"));
        Assert.Equal(incomplete, report.Has("PDFA-FONT-CHARSET-INCOMPLETE"));
    }

    static PdfDocument Document(string charset, byte[] program, bool subset = true, bool indirect = false)
    {
        var name = (subset ? "ABCDEF+" : "") + "TestCff";
        return PdfDocument.Load(ObjPdf.Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << /Font << /F 5 0 R >> >> /Contents 4 0 R >>",
            ObjPdf.Stream("BT /F 12 Tf (A) Tj ET"),
            $"<< /Type /Font /Subtype /Type1 /BaseFont /{name} /Encoding /WinAnsiEncoding /FirstChar 65 /LastChar 65 /Widths [600] /FontDescriptor 6 0 R >>",
            $"<< /Type /FontDescriptor /FontName /{name} /Flags 32 /FontBBox [0 0 1000 1000] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 /FontFile3 7 0 R /CharSet {(indirect ? "8 0 R" : "(" + charset + ")")} >>",
            ObjPdf.Stream(Encoding.Latin1.GetString(program), "/Subtype /Type1C"),
            "(" + charset + ")"));
    }

    static void AssertClauses(ValidationReport report, PdfValidationProfile profile)
    {
        Assert.All(report.Findings.Where(f => f.RuleId is "PDFA-FONT-CHARSET-LIST" or "PDFA-FONT-CHARSET-INCOMPLETE"), finding =>
        {
            Assert.Equal(ValidationSeverity.Error, finding.Severity);
            Assert.Equal(0, finding.PageIndex);
            Assert.Equal(profile switch
            {
                PdfValidationProfile.PdfA1b => "ISO 19005-1 6.3.5",
                PdfValidationProfile.PdfA2b => "ISO 19005-2 6.2.11.4.2",
                _ => "ISO 19005-3 6.2.11.4.2",
            }, finding.Clause);
        });
    }
}
